using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Data;
using System.Windows.Media;
using Dynamo.Core;
using Dynamo.Engine.Profiling;
using Dynamo.Graph.Annotations;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Workspaces;
using Dynamo.ViewModels;
using Dynamo.Wpf.Extensions;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace TuneUp
{
    /// <summary>
    /// Enum of possible states of node profiling data
    /// </summary>
    public enum ProfiledNodeState
    {
        [Display(Name = "Executing")]
        Executing = 0,

        [Display(Name = "Latest run")]
        ExecutedOnCurrentRun = 1,

        [Display(Name = "Latest run")]
        ExecutedOnCurrentRunTotal = 2,

        [Display(Name = "Previous run")]
        ExecutedOnPreviousRun = 3,

        [Display(Name = "Previous run")]
        ExecutedOnPreviousRunTotal = 4,

        [Display(Name = "Not executed")]
        NotExecuted = 5,
    }

    /// <summary>
    /// ViewModel for TuneUp. 
    /// Handles profiling setup, workspace events, execution events, etc.
    /// </summary>
    public class TuneUpWindowViewModel : NotificationObject, IDisposable
    {
        #region Internal Properties

        private ViewLoadedParams viewLoadedParams;
        private IProfilingExecutionTimeData executionTimeData;
        private int executedNodesNum;
        private bool isProfilingEnabled = true;
        private bool isRecomputeEnabled = true;
        private HomeWorkspaceModel currentWorkspace;
        private Dictionary<Guid, ProfiledNodeViewModel> nodeDictionary = new Dictionary<Guid, ProfiledNodeViewModel>();
        private Dictionary<Guid, List<ProfiledNodeViewModel>> groupDictionary = new Dictionary<Guid, List<ProfiledNodeViewModel>>();
        private SynchronizationContext uiContext;
        private bool isTuneUpChecked = false;
        private ListSortDirection sortDirection;
        private string sortingOrder = "number";

        private string latestGraphExecutionTime = "N/A";
        private string previousGraphExecutionTime = "N/A";
        private string totalGraphExecutionTime = "N/A";
        private bool showGroups = true;

        /// <summary>
        /// Name of the row to display current execution time
        /// </summary>
        private string CurrentExecutionString = ProfiledNodeViewModel.ExecutionTimelString + " Latest Run";

        /// <summary>
        /// Name of the row to display previous execution time
        /// </summary>
        private string PreviousExecutionString = ProfiledNodeViewModel.ExecutionTimelString + " Previous Run";        

        private HomeWorkspaceModel CurrentWorkspace
        {
            get => currentWorkspace;
            set
            {
                // Unsubscribe from old workspace
                if (currentWorkspace != null && isTuneUpChecked)
                {
                    ManageWorkspaceEvents(currentWorkspace, false);
                }

                // Subscribe to new workspace
                if (value != null)
                {
                    // Set new workspace
                    currentWorkspace = value;
                    if (isTuneUpChecked)
                    {
                        ManageWorkspaceEvents(currentWorkspace, true);
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets the sorting order and toggles the sort direction.
        /// </summary>
        public string SortingOrder
        {
            get => sortingOrder;
            set
            {
                if (sortingOrder != value)
                {
                    sortingOrder = value;
                    SortDirection = ListSortDirection.Ascending;
                }
                else
                {
                    SortDirection = SortDirection == ListSortDirection.Ascending
                        ? ListSortDirection.Descending
                        : ListSortDirection.Ascending;
                }
            }
        }

        /// <summary>
        /// Gets or sets the sort direction and raises property change notification if the value changes.
        /// </summary>
        public ListSortDirection SortDirection
        {
            get => sortDirection;
            set
            {
                if (sortDirection != value)
                {
                    sortDirection = value;
                }
            }
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Is the recomputeAll button enabled in the UI. Users should not be able to force a 
        /// reset of the engine and re-execution of the graph if one is still ongoing. This causes...trouble.
        /// </summary>
        public bool IsRecomputeEnabled
        {
            get => isRecomputeEnabled;
            private set
            {
                if (isRecomputeEnabled != value)
                {
                    isRecomputeEnabled = value;
                    RaisePropertyChanged(nameof(IsRecomputeEnabled));
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the TuneUp extension is active.
        /// When activated, it subscribes to workspace events to enable profiling. 
        /// When deactivated, it unsubscribes from those events.
        /// </summary>
        public bool IsTuneUpChecked
        {
            get => isTuneUpChecked;
            set
            {
                if (isTuneUpChecked != value)
                {
                    isTuneUpChecked = value;
                    RaisePropertyChanged(nameof(IsTuneUpChecked));

                    // Subscribe if activated, unsubscribe if deactivated
                    if (currentWorkspace != null)
                    {
                        ManageWorkspaceEvents(currentWorkspace, isTuneUpChecked);
                    }
                }
            }
        }

        /// <summary>
        /// Collections of profiling data for nodes in the current workspace
        /// </summary>
        public ObservableCollection<ProfiledNodeViewModel> ProfiledNodesLatestRun { get; private set; }
        public ObservableCollection<ProfiledNodeViewModel> ProfiledNodesPreviousRun { get; private set; }
        public ObservableCollection<ProfiledNodeViewModel> ProfiledNodesNotExecuted { get; private set; }

        /// <summary>
        /// Collections of profiling data for nodes in the current workspace
        /// </summary>
        public CollectionViewSource ProfiledNodesCollectionLatestRun { get; set; }
        public CollectionViewSource ProfiledNodesCollectionPreviousRun { get; set; }
        public CollectionViewSource ProfiledNodesCollectionNotExecuted { get; set; }


        /// <summary>
        /// Total graph execution time
        /// </summary>
        public string TotalGraphExecutionTime
        {
            get => totalGraphExecutionTime;
        }
        public string LatestGraphExecutionTime
        {
            get => latestGraphExecutionTime;
        }
        public string PreviousGraphExecutionTime
        {
            get => previousGraphExecutionTime;
        }

        public bool ShowGroups
        {
            get => showGroups;
            set
            {
                if (showGroups != value)
                {
                    showGroups = value;
                    RaisePropertyChanged(nameof(ShowGroups));
                    ProfiledNodesCollectionLatestRun.View.Refresh();
                    ProfiledNodesCollectionPreviousRun.View.Refresh();
                    ProfiledNodesCollectionNotExecuted.View.Refresh();

                    // Refresh all collections and apply group settings
                    UpdateGroupVisibility(ProfiledNodesCollectionLatestRun, ProfiledNodesLatestRun);
                    UpdateGroupVisibility(ProfiledNodesCollectionPreviousRun, ProfiledNodesPreviousRun);
                    UpdateGroupVisibility(ProfiledNodesCollectionNotExecuted, ProfiledNodesNotExecuted);
                }
            }
        }

        #endregion

        #region Constructor

        public TuneUpWindowViewModel(ViewLoadedParams p)
        {
            viewLoadedParams = p;
            // Saving UI context so later when we touch the collection, it is still performed in the same context
            uiContext = SynchronizationContext.Current;
            p.CurrentWorkspaceChanged += OnCurrentWorkspaceChanged;
            p.CurrentWorkspaceCleared += OnCurrentWorkspaceCleared;

            if (p.CurrentWorkspaceModel is HomeWorkspaceModel)
            {
                CurrentWorkspace = p.CurrentWorkspaceModel as HomeWorkspaceModel;
            }
        }

        #endregion

        #region ProfilingMethods

        /// <summary>
        /// Resets the profiling data for all nodes in the current workspace. Clears the existing
        /// profiling data and re-initializes it based on the nodes present in the current workspace.
        /// </summary>
        internal void ResetProfiledNodes()
        {
            if (CurrentWorkspace == null) return;

            // Clear existing collections if they are not null
            ProfiledNodesLatestRun?.Clear();
            ProfiledNodesPreviousRun?.Clear();
            ProfiledNodesNotExecuted?.Clear();

            // Initialize observable collections and dictionaries
            ProfiledNodesLatestRun = ProfiledNodesLatestRun ?? new ObservableCollection<ProfiledNodeViewModel>();
            ProfiledNodesPreviousRun = ProfiledNodesPreviousRun ?? new ObservableCollection<ProfiledNodeViewModel>();
            ProfiledNodesNotExecuted = ProfiledNodesNotExecuted ?? new ObservableCollection<ProfiledNodeViewModel>();
            nodeDictionary = new Dictionary<Guid, ProfiledNodeViewModel>();
            groupDictionary = new Dictionary<Guid, List<ProfiledNodeViewModel>>();

            // Process groups and their nodes
            foreach (var group in CurrentWorkspace.Annotations)
            {
                var groupGUID = group.GUID;
                var groupBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(group.Background));

                // Create and add profiled group node
                var profiledGroup = new ProfiledNodeViewModel(group)
                {
                    BackgroundBrush = groupBackgroundBrush,
                    GroupGUID = groupGUID
                };
                ProfiledNodesNotExecuted.Add(profiledGroup);
                nodeDictionary[group.GUID] = profiledGroup;

                // Initialize group in group dictionary
                groupDictionary[groupGUID] = new List<ProfiledNodeViewModel>();

                // Create and add group total execution time node
                var groupTotalTimeNode = new ProfiledNodeViewModel(ProfiledNodeViewModel.GroupExecutionTimeString, TimeSpan.Zero, ProfiledNodeState.NotExecuted)
                {
                    GroupGUID = groupGUID,
                    GroupName = group.AnnotationText,
                    BackgroundBrush = groupBackgroundBrush,
                    IsGroupExecutionTime = true
                };
                ProfiledNodesNotExecuted.Add(groupTotalTimeNode);
                nodeDictionary[Guid.NewGuid()] = groupTotalTimeNode;
                groupDictionary[groupGUID].Add(groupTotalTimeNode);

                // Add each node in the group
                foreach (var node in group.Nodes.OfType<NodeModel>())
                {
                    var profiledNode = new ProfiledNodeViewModel(node)
                    {
                        GroupGUID = groupGUID,
                        GroupName = group.AnnotationText,
                        BackgroundBrush = groupBackgroundBrush
                    };
                    ProfiledNodesNotExecuted.Add(profiledNode);
                    nodeDictionary[node.GUID] = profiledNode;
                    groupDictionary[groupGUID].Add(profiledNode);
                }
            }

            // Process standalone nodes (those not in groups)
            foreach (var node in CurrentWorkspace.Nodes.Where(n => !nodeDictionary.ContainsKey(n.GUID)))
            {
                var profiledNode = new ProfiledNodeViewModel(node)
                {
                    GroupName = node.Name
                };
                ProfiledNodesNotExecuted.Add(profiledNode);
                nodeDictionary[node.GUID] = profiledNode;
            }

            ProfiledNodesCollectionLatestRun = new CollectionViewSource { Source = ProfiledNodesLatestRun};
            ProfiledNodesCollectionPreviousRun = new CollectionViewSource { Source = ProfiledNodesPreviousRun };
            ProfiledNodesCollectionNotExecuted = new CollectionViewSource { Source = ProfiledNodesNotExecuted };

            ApplyGroupNodeFilter();
            ApplySortingNotExecuted();

            ProfiledNodesCollectionLatestRun.View?.Refresh();
            ProfiledNodesCollectionPreviousRun.View?.Refresh();
            ProfiledNodesCollectionNotExecuted.View?.Refresh();

            RaisePropertyChanged(nameof(ProfiledNodesCollectionNotExecuted));
            RaisePropertyChanged(nameof(ProfiledNodesNotExecuted));
            RaisePropertyChanged(nameof(TotalGraphExecutionTime));
        }

        /// <summary>
        /// The handler for force-recompute the graph
        /// </summary>
        internal void ResetProfiling()
        {
            // Put the graph into manual mode as there is no guarantee that nodes will be marked
            // dirty in topologically sorted order during a reset.
            SwitchToManualMode();
            // TODO: need a way to do this from an extension and not cause a run.
            // DynamoModel interface or a more specific reset command.
            (viewLoadedParams.DynamoWindow.DataContext as DynamoViewModel).Model.ResetEngine(true);
            // Enable profiling on the new engine controller after the reset.
            CurrentWorkspace.EngineController.EnableProfiling(true, currentWorkspace, currentWorkspace.Nodes);
            // run the graph now that profiling is enabled.
            CurrentWorkspace.Run();

            isProfilingEnabled = true;
            executionTimeData = CurrentWorkspace.EngineController.ExecutionTimeData;
        }

        /// <summary>
        /// Switches the current workspace's run mode to manual. Prevents the workspace from 
        /// running automatically and allows for manual control of execution.
        /// </summary>
        internal void SwitchToManualMode()
        {
            CurrentWorkspace.RunSettings.RunType = Dynamo.Models.RunType.Manual;
        }

        /// <summary>
        /// Enable profiling when it is disabled temporarily.
        /// </summary>
        internal void EnableProfiling()
        {
            if (!isProfilingEnabled && CurrentWorkspace != null)
            {
                ResetProfiledNodes();
                CurrentWorkspace.EngineController.EnableProfiling(true, CurrentWorkspace, CurrentWorkspace.Nodes);
                isProfilingEnabled = true;
                executionTimeData = CurrentWorkspace.EngineController.ExecutionTimeData;
            }
            RaisePropertyChanged(nameof(ProfiledNodesCollectionNotExecuted));
        }

        internal void DisableProfiling()
        {
            if (isProfilingEnabled && CurrentWorkspace != null)
            {
                CurrentWorkspace.EngineController.EnableProfiling(false, CurrentWorkspace, CurrentWorkspace.Nodes);
                isProfilingEnabled = false;
            }
        }

        #endregion

        #region ExecutionEvents

        private void CurrentWorkspaceModel_EvaluationStarted(object sender, EventArgs e)
        {
            IsRecomputeEnabled = false;
            foreach (var node in nodeDictionary.Values)
            {
                // Reset Node Execution Order info
                node.ExecutionOrderNumber = null;
                node.GroupExecutionOrderNumber = null;
                node.WasExecutedOnLastRun = false;

                // Update Node state
                if (node.State == ProfiledNodeState.ExecutedOnCurrentRun)
                {
                    node.State = ProfiledNodeState.ExecutedOnPreviousRun;
                }
                // Move to CollectionPreviousRun
                if (node.State == ProfiledNodeState.ExecutedOnPreviousRun)
                {
                    MoveNodeToCollection(node, null);
                    ProfiledNodesPreviousRun.Add(node);
                }
            }
            executedNodesNum = 1;
            EnableProfiling();
        }

        private void CurrentWorkspaceModel_EvaluationCompleted(object sender, Dynamo.Models.EvaluationCompletedEventArgs e)
        {
            IsRecomputeEnabled = true;

            CalculateGroupNodes();
            UpdateExecutionTime();

            RaisePropertyChanged(nameof(ProfiledNodesCollectionLatestRun));
            RaisePropertyChanged(nameof(ProfiledNodesLatestRun));
            RaisePropertyChanged(nameof(ProfiledNodesCollectionPreviousRun));
            RaisePropertyChanged(nameof(ProfiledNodesPreviousRun));
            RaisePropertyChanged(nameof(ProfiledNodesCollectionNotExecuted));
            RaisePropertyChanged(nameof(ProfiledNodesNotExecuted));

            ProfiledNodesCollectionLatestRun.Dispatcher.InvokeAsync(() =>
            {
                ApplyCustomSorting();
                ProfiledNodesCollectionLatestRun.View?.Refresh();
            });
            ProfiledNodesCollectionPreviousRun.Dispatcher.InvokeAsync(() =>
            {
                ProfiledNodesCollectionPreviousRun.View?.Refresh();
            });
            ProfiledNodesCollectionNotExecuted.Dispatcher.InvokeAsync(() =>
            {
                ProfiledNodesCollectionNotExecuted.View?.Refresh();
            });

        }

        /// <summary>
        /// Update execution time rows. These rows are always removed and re-added after each run.
        /// May consider instead, always updating them in the future.
        /// </summary>
        private void UpdateExecutionTime()
        {
            // Reset execution time
            uiContext.Send(
                x =>
                {   // After each evaluation, manually update execution time column(s)
                    var totalSpanLatestRun = new TimeSpan(ProfiledNodesLatestRun
                        .Where(n => n.WasExecutedOnLastRun && !n.IsGroup && !n.IsGroupExecutionTime)
                        .Sum(r => r.ExecutionTime.Ticks));
                    var totalSpanPreviousRun = new TimeSpan(ProfiledNodesPreviousRun
                        .Where(n => !n.WasExecutedOnLastRun && !n.IsGroup && !n.IsGroupExecutionTime)
                        .Sum(r => r.ExecutionTime.Ticks));

                    // Update latest and previous run times
                    latestGraphExecutionTime = Math.Round(totalSpanLatestRun.TotalMilliseconds).ToString();
                    previousGraphExecutionTime = Math.Round(totalSpanPreviousRun.TotalMilliseconds).ToString();
                    totalGraphExecutionTime = Math.Round(totalSpanLatestRun.TotalMilliseconds + totalSpanPreviousRun.TotalMilliseconds).ToString();
                }, null);

            RaisePropertyChanged(nameof(TotalGraphExecutionTime));
            RaisePropertyChanged(nameof(LatestGraphExecutionTime));
            RaisePropertyChanged(nameof(PreviousGraphExecutionTime));

            var c1 = TotalGraphExecutionTime;
            var c2 = LatestGraphExecutionTime;
            var c3 = PreviousGraphExecutionTime;
        }

        /// <summary>
        /// Calculates and assigns execution order numbers for profiled nodes.
        /// Aggregates execution times and updates states for nodes within groups.
        /// Ensures nodes are processed only once and maintains the sorted order of nodes.
        /// </summary>
        private void CalculateGroupNodes()
        {
            int groupExecutionCounter = 1;
            bool groupIsRenamed = false;
            var processedNodes = new HashSet<ProfiledNodeViewModel>();
            //var sortedProfiledNodes = ProfiledNodes.Where(node => node.State == ProfiledNodeState.ExecutedOnCurrentRun).OrderBy(node => node.ExecutionOrderNumber).ToList();

            //ip code:
            var sortedProfiledNodes = ProfiledNodesLatestRun.OrderBy(node => node.ExecutionOrderNumber).ToList();

            foreach (var profiledNode in sortedProfiledNodes)
            {
                // Process nodes that belong to a group and have not been processed yet
                if (!profiledNode.IsGroup && !profiledNode.IsGroupExecutionTime && profiledNode.GroupGUID != Guid.Empty && !processedNodes.Contains(profiledNode))
                {
                    if (nodeDictionary.TryGetValue(profiledNode.GroupGUID, out var profiledGroup) &&
                        groupDictionary.TryGetValue(profiledNode.GroupGUID, out var nodesInGroup))
                    {
                        ProfiledNodeViewModel totalExecTimeNode = null;
                        profiledGroup.State = profiledNode.State;
                        profiledGroup.GroupExecutionTime = TimeSpan.Zero; // Reset execution time
                        MoveNodeToCollection(profiledGroup, ProfiledNodesLatestRun); // Ensure the profiledGroup is in latest run

                        // Check if the group has been renamed
                        var groupModel = CurrentWorkspace.Annotations.FirstOrDefault(g => g.GUID == profiledGroup.GroupGUID);
                        if (groupModel != null && profiledGroup.GroupName != groupModel.AnnotationText)
                        {
                            groupIsRenamed = true;
                            profiledGroup.GroupName = groupModel.AnnotationText;
                            profiledGroup.Name = $"{ProfiledNodeViewModel.GroupNodePrefix}{groupModel.AnnotationText}";
                        }

                        // Iterate through the nodes in the group
                        foreach (var node in nodesInGroup)
                        {
                            // Find the group total execution time node
                            if (node.IsGroupExecutionTime)
                            {
                                totalExecTimeNode = node;
                            }

                            if (processedNodes.Add(node)) // Adds to HashSet and checks if it was added
                            {
                                // Update group state, execution order, and execution time
                                profiledGroup.GroupExecutionTime += node.ExecutionTime;
                                node.GroupExecutionOrderNumber = groupExecutionCounter;
                                if (groupIsRenamed)
                                {
                                    node.GroupName = profiledGroup.GroupName;
                                }                                
                            }
                        }

                        // Update the properties of the group node
                        profiledGroup.GroupExecutionOrderNumber = groupExecutionCounter++;
                        profiledGroup.ExecutionTime = profiledGroup.GroupExecutionTime;
                        profiledGroup.WasExecutedOnLastRun = true;

                        // Update the properties of the group total execution time node
                        if (totalExecTimeNode != null)
                        {
                            totalExecTimeNode.State = profiledGroup.State;
                            totalExecTimeNode.GroupExecutionTime = profiledGroup.GroupExecutionTime;
                            totalExecTimeNode.ExecutionTime = profiledGroup.GroupExecutionTime;
                            totalExecTimeNode.GroupExecutionOrderNumber = profiledGroup.GroupExecutionOrderNumber;
                            totalExecTimeNode.ExecutionOrderNumber = int.MaxValue;
                            totalExecTimeNode.WasExecutedOnLastRun = true;
                        }
                        MoveNodeToCollection(totalExecTimeNode, ProfiledNodesLatestRun);

                        // Update the total groupExecutionTime for the purposes of sorting
                        foreach (var node in nodesInGroup)
                        {
                            node.GroupExecutionTime = profiledGroup.GroupExecutionTime;
                        }
                    }
                }
                // Process standalone nodes
                else if (!profiledNode.IsGroup && processedNodes.Add(profiledNode) &&
                    !profiledNode.Name.Contains(ProfiledNodeViewModel.ExecutionTimelString) &&
                    !profiledNode.IsGroupExecutionTime)
                {
                    profiledNode.ExecutionOrderNumber = groupExecutionCounter;
                    profiledNode.GroupExecutionOrderNumber = groupExecutionCounter++;
                    profiledNode.GroupExecutionTime = profiledNode.ExecutionTime;
                }
            }
        }

        private void ApplyGroupNodeFilter()
        {
            ProfiledNodesCollectionLatestRun.Filter -= GroupNodeFilter;            
            ProfiledNodesCollectionPreviousRun.Filter -= GroupNodeFilter;            
            ProfiledNodesCollectionNotExecuted.Filter -= GroupNodeFilter;

            ProfiledNodesCollectionLatestRun.Filter += GroupNodeFilter;
            ProfiledNodesCollectionPreviousRun.Filter += GroupNodeFilter;
            ProfiledNodesCollectionNotExecuted.Filter += GroupNodeFilter;
        }

        /// <summary>
        /// Filters the collection of profiled nodes based on group and execution time criteria.
        /// If ShowGroups is true, all nodes are accepted. 
        /// Otherwise, nodes where either IsGroup or IsExecutionTime is true are filtered out (not accepted).
        /// </summary>
        private void GroupNodeFilter(object sender, FilterEventArgs e)
        {
            var node = e.Item as ProfiledNodeViewModel;
            if (node == null) return;

            if (ShowGroups) e.Accepted = true;
            else e.Accepted = !(node.IsGroup || node.IsGroupExecutionTime);
        }

        /// <summary>
        /// Applies the sorting logic to all ProfiledNodesCollections.
        /// </summary>
        public void ApplyCustomSorting()
        {
            ApplyCustomSorting(ProfiledNodesCollectionLatestRun);
            ApplyCustomSorting(ProfiledNodesCollectionPreviousRun);
            ApplyCustomSorting(ProfiledNodesCollectionNotExecuted);
        }

        /// <summary>
        /// Applies the sorting logic to a given ProfiledNodesCollection.
        /// </summary>
        public void ApplyCustomSorting(CollectionViewSource collection)
        {
            collection.SortDescriptions.Clear();
            switch (sortingOrder)
            {
                case "time":
                    if (showGroups)
                    {
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.GroupExecutionTime), sortDirection));
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.GroupGUID), sortDirection));
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.IsGroup), ListSortDirection.Descending));
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.IsGroupExecutionTime), ListSortDirection.Ascending));
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.ExecutionTime), sortDirection));
                    }
                    else
                    {
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.ExecutionTime), sortDirection));
                    }
                    break;
                case "name":
                    if (showGroups)
                    {
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.GroupName), sortDirection));
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.GroupGUID), sortDirection));
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.IsGroup), ListSortDirection.Descending));
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.IsGroupExecutionTime), ListSortDirection.Ascending));
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.Name), sortDirection));
                    }
                    else
                    {
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.Name), sortDirection));
                    }
                    break;
                case "number":
                    if (showGroups)
                    {
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.GroupExecutionOrderNumber), sortDirection));
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.GroupName), sortDirection));
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.GroupGUID), sortDirection));
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.IsGroup), ListSortDirection.Descending));
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.IsGroupExecutionTime), ListSortDirection.Ascending));
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.ExecutionOrderNumber), sortDirection));
                    }
                    else
                    {
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.ExecutionOrderNumber), sortDirection));
                    }
                    break;
            }
        }


        // only for when the graph has not been executed yet
        private void ApplySortingNotExecuted()
        {
            ProfiledNodesCollectionNotExecuted.SortDescriptions.Clear();
            // Sort nodes into execution group
            ProfiledNodesCollectionNotExecuted.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.GroupName), ListSortDirection.Ascending));
            ProfiledNodesCollectionNotExecuted.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.GroupGUID), ListSortDirection.Ascending));
            ProfiledNodesCollectionNotExecuted.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.IsGroup), ListSortDirection.Descending));
            ProfiledNodesCollectionNotExecuted.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.IsGroupExecutionTime), ListSortDirection.Ascending));
            ProfiledNodesCollectionNotExecuted.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.Name), ListSortDirection.Ascending));
        }

        /// <summary>
        /// Updates the group visibility, refreshes the collection view, and applies appropriate sorting for the given nodes.
        /// </summary>
        private void UpdateGroupVisibility(CollectionViewSource collectionView, ObservableCollection<ProfiledNodeViewModel> nodes)
        {
            collectionView.View.Refresh();
            foreach (var node in nodes)
            {
                node.ShowGroupIndicator = showGroups;
            }

            if (collectionView == ProfiledNodesCollectionNotExecuted)
            {
                ApplySortingNotExecuted();
            }
            else
            {
                ApplyCustomSorting(collectionView);
            }
        }

        internal void OnNodeExecutionBegin(NodeModel nm)
        {
            var profiledNode = nodeDictionary[nm.GUID];
            profiledNode.Stopwatch.Start();
            profiledNode.State = ProfiledNodeState.Executing;
        }

        internal void OnNodeExecutionEnd(NodeModel nm)
        {
            var profiledNode = nodeDictionary[nm.GUID];
            profiledNode.Stopwatch.Stop();
            var executionTime = profiledNode.Stopwatch.Elapsed;

            if (executionTime > TimeSpan.Zero)
            {
                profiledNode.ExecutionTime = executionTime;

                if (!profiledNode.WasExecutedOnLastRun)
                {
                    profiledNode.ExecutionOrderNumber = executedNodesNum++;
                    // move to CollectionLatestRun
                    MoveNodeToCollection(profiledNode, ProfiledNodesLatestRun);
                }
            }

            profiledNode.Stopwatch.Reset();
            profiledNode.WasExecutedOnLastRun = true;
            profiledNode.State = ProfiledNodeState.ExecutedOnCurrentRun;
        }

        #endregion

        #region Workspace Events

        private void CurrentWorkspaceModel_NodeAdded(NodeModel node)
        {
            var profiledNode = new ProfiledNodeViewModel(node);
            nodeDictionary[node.GUID] = profiledNode;

            node.NodeExecutionBegin += OnNodeExecutionBegin;
            node.NodeExecutionEnd += OnNodeExecutionEnd;

            ProfiledNodesNotExecuted.Add(profiledNode);
            RaisePropertyChanged(nameof(ProfiledNodesCollectionNotExecuted));
        }

        private void CurrentWorkspaceModel_NodeRemoved(NodeModel node)
        {
            var profiledNode = nodeDictionary[node.GUID];
            nodeDictionary.Remove(node.GUID);

            node.NodeExecutionBegin -= OnNodeExecutionBegin;
            node.NodeExecutionEnd -= OnNodeExecutionEnd;

            MoveNodeToCollection(profiledNode, null);
            RaisePropertyChanged(nameof(ProfiledNodesCollectionLatestRun));
            RaisePropertyChanged(nameof(ProfiledNodesCollectionPreviousRun));
            RaisePropertyChanged(nameof(ProfiledNodesCollectionNotExecuted));

            var c = ProfiledNodesLatestRun;
            var c1 = ProfiledNodesPreviousRun;
            var c2 = ProfiledNodesNotExecuted;
        }
        private void MoveNodeToCollection(ProfiledNodeViewModel profiledNode, ObservableCollection<ProfiledNodeViewModel> targetCollection)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var collections = new[]
                { 
                    ProfiledNodesLatestRun, 
                    ProfiledNodesPreviousRun,
                    ProfiledNodesNotExecuted
                };

                foreach (var collection in collections)
                {
                    collection?.Remove(profiledNode);
                }

                targetCollection?.Add(profiledNode);
            });
        }

        private void CurrentWorkspaceModel_GroupAdded(AnnotationModel group)  //TODO: Check if some of that cannot be made into separate method
        {
            var profiledGroup = new ProfiledNodeViewModel(group);
            nodeDictionary[group.GUID] = profiledGroup;
            ProfiledNodesNotExecuted.Add(profiledGroup);
            groupDictionary[group.GUID] = new List<ProfiledNodeViewModel>();

            // Create group total execution time node
            var groupTotalTimeNode = new ProfiledNodeViewModel
                (ProfiledNodeViewModel.GroupExecutionTimeString, TimeSpan.Zero, ProfiledNodeState.NotExecuted)
            {
                GroupGUID = group.GUID,
                GroupName = group.AnnotationText,
                BackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(group.Background)),
                IsGroupExecutionTime = true
            };
            nodeDictionary[Guid.NewGuid()] = groupTotalTimeNode;
            ProfiledNodesNotExecuted.Add(groupTotalTimeNode);
            groupDictionary[group.GUID].Add(groupTotalTimeNode);

            // Create profiledNode for each node in the group
            foreach (var node in group.Nodes)
            {
                if (node is NodeModel nodeModel)
                {
                    ProfiledNodeViewModel profiledNode;
                    if (nodeDictionary.TryGetValue(node.GUID, out profiledNode))
                    {
                        profiledGroup.State = profiledNode.State;
                    }
                    else
                    {
                        profiledNode = new ProfiledNodeViewModel(node as NodeModel);
                        nodeDictionary[node.GUID] = profiledNode;
                        ProfiledNodesNotExecuted.Add(profiledNode);
                    }
                    profiledNode.GroupGUID = group.GUID;
                    profiledNode.GroupName = group.AnnotationText;
                    profiledNode.GroupExecutionOrderNumber = profiledGroup.GroupExecutionOrderNumber;
                    profiledNode.BackgroundBrush = profiledGroup.BackgroundBrush;
                    profiledNode.ShowGroupIndicator = ShowGroups;

                    groupDictionary[group.GUID].Add(profiledNode);
                }                
            }
            // Executes for each group when a graph with groups is open while TuneUp is enabled
            // Ensures that group nodes are sorted properly and do not appear at the bottom of the DataGrid
            //ApplyCustomSorting(); needs to go back just figure out how to sort all?
            ApplySortingNotExecuted();

            RaisePropertyChanged(nameof(ProfiledNodesCollectionNotExecuted));
            RaisePropertyChanged(nameof(ProfiledNodesNotExecuted));
        }

        private void CurrentWorkspaceModel_GroupRemoved(AnnotationModel group)
        {
            var groupGUID = group.GUID;

            // Remove the group from nodeDictionary and ProfiledNodes
            if (nodeDictionary.TryGetValue(groupGUID, out var profiledGroup))
            {
                nodeDictionary.Remove(groupGUID);
                MoveNodeToCollection(profiledGroup, null);
            }

            // Reset grouped nodes' properties and remove them from groupDictionary
            if (groupDictionary.TryGetValue(groupGUID, out var groupedNodes))
            {
                foreach (var profiledNode in groupedNodes)
                {
                    // Remove group total execution time node
                    if (profiledNode.IsGroupExecutionTime)
                    {
                        MoveNodeToCollection(profiledNode, null);
                    }

                    profiledNode.GroupGUID = Guid.Empty;
                    profiledNode.GroupName = string.Empty;
                    // Immediately after the group is removed, the node's execution order should not
                    // be displayed, but the nodes will remain in the same location in the DataGrid.
                    // The execution order will update correctly on the next graph execution.
                    profiledNode.ExecutionOrderNumber = null;
                    profiledNode.GroupExecutionTime = TimeSpan.Zero;
                }
                groupDictionary.Remove(groupGUID);
            }
            
            //RaisePropertyChanged(nameof(ProfiledNodesCollection));// to remove?
            //RaisePropertyChanged(nameof(ProfiledNodes));// to remove?
        }

        private void OnCurrentWorkspaceChanged(IWorkspaceModel workspace)
        {
            // Profiling needs to be enabled per workspace so mark it false after switching
            isProfilingEnabled = false;
            CurrentWorkspace = workspace as HomeWorkspaceModel;
        }

        private void OnCurrentWorkspaceCleared(IWorkspaceModel workspace)
        {
            // Profiling needs to be enabled per workspace so mark it false after closing
            isProfilingEnabled = false;
            CurrentWorkspace = viewLoadedParams.CurrentWorkspaceModel as HomeWorkspaceModel;
        }

        #endregion

        #region Dispose or setup

        /// <summary>
        /// When switching workspaces or closing TuneUp extension,
        /// subscribe (true)/unsubscribe (false) workspace events for profiling
        /// </summary>
        /// <param name="workspace"></param>
        /// <param name="subscribe"></param>
        private void ManageWorkspaceEvents(HomeWorkspaceModel workspace, bool subscribe)
        {
            if (workspace == null) return;

            // Subscribe from workspace events 
            if (subscribe)
            {
                workspace.NodeAdded += CurrentWorkspaceModel_NodeAdded;
                workspace.NodeRemoved += CurrentWorkspaceModel_NodeRemoved;
                workspace.EvaluationStarted += CurrentWorkspaceModel_EvaluationStarted;
                workspace.EvaluationCompleted += CurrentWorkspaceModel_EvaluationCompleted;
                workspace.AnnotationAdded += CurrentWorkspaceModel_GroupAdded;
                workspace.AnnotationRemoved += CurrentWorkspaceModel_GroupRemoved;

                foreach (var node in workspace.Nodes)
                {
                    node.NodeExecutionBegin += OnNodeExecutionBegin;
                    node.NodeExecutionEnd += OnNodeExecutionEnd;
                }
                ResetProfiledNodes();
            }
            // Unsubscribe to workspace events
            else
            {
                workspace.NodeAdded -= CurrentWorkspaceModel_NodeAdded;
                workspace.NodeRemoved -= CurrentWorkspaceModel_NodeRemoved;
                workspace.EvaluationStarted -= CurrentWorkspaceModel_EvaluationStarted;
                workspace.EvaluationCompleted -= CurrentWorkspaceModel_EvaluationCompleted;
                workspace.AnnotationAdded -= CurrentWorkspaceModel_GroupAdded;
                workspace.AnnotationRemoved -= CurrentWorkspaceModel_GroupRemoved;

                foreach (var node in workspace.Nodes)
                {
                    node.NodeExecutionBegin -= OnNodeExecutionBegin;
                    node.NodeExecutionEnd -= OnNodeExecutionEnd;
                }
            }
            executedNodesNum = 1;
        }

        /// <summary>
        /// ViewModel dispose function
        /// </summary>
        public void Dispose()
        {
            ManageWorkspaceEvents(CurrentWorkspace, false);
            viewLoadedParams.CurrentWorkspaceChanged -= OnCurrentWorkspaceChanged;
            viewLoadedParams.CurrentWorkspaceCleared -= OnCurrentWorkspaceCleared;
        }

        #endregion

        #region Execution time exporters

        /// <summary>
        /// Exports the ProfiledNodesCollections to a CSV file.
        /// </summary>
        public void ExportToCsv()
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "CSV file (*.csv)|*.csv|All files (*.*)|*.*";

            if (saveFileDialog.ShowDialog() == true)
            {
                using (var writer = new StreamWriter(saveFileDialog.FileName))
                {
                    writer.WriteLine("Execution Order,Name,Execution Time (ms)");

                    foreach (ProfiledNodeViewModel node in ProfiledNodesCollectionLatestRun.View.Cast<ProfiledNodeViewModel>())
                    {
                        writer.WriteLine($"{node.ExecutionOrderNumber},{node.Name},{node.ExecutionMilliseconds}");
                    }
                }
            }
        }

        /// <summary>
        /// Exports the ProfiledNodesCollections to a JSON file.
        /// </summary>
        public void ExportToJson()
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "JSON file (*.json)|*.json|All files (*.*)|*.*";

            if (saveFileDialog.ShowDialog() == true)
            {
                // Create a list to hold the node data
                var nodeDataList = new List<object>();

                // Loop through the nodes and add the required data to the list
                foreach (ProfiledNodeViewModel node in ProfiledNodesCollectionLatestRun.View.Cast<ProfiledNodeViewModel>())
                {
                    nodeDataList.Add(new
                    {
                        ExecutionOrder = node.ExecutionOrderNumber,
                        Name = node.Name,
                        ExecutionTimeMs = node.ExecutionMilliseconds
                    });
                }

                // Serialize the list and write to JSON
                string json = JsonConvert.SerializeObject(nodeDataList, Formatting.Indented);
                using (var writer = new StreamWriter(saveFileDialog.FileName))
                {
                    writer.Write(json);
                }
            }
        }

        #endregion
    }
}
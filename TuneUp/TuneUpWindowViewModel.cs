using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
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
        private HomeWorkspaceModel currentWorkspace;
        private SynchronizationContext uiContext;
        private int executedNodesNum;
        private bool isProfilingEnabled = true;
        private bool isRecomputeEnabled = true;
        private bool isTuneUpChecked = false;
        private bool showGroups;
        private ListSortDirection sortDirection;
        private const string defaultExecutionTime = "N/A";
        private string defaultSortingOrder = "number";        
        private string latestGraphExecutionTime = defaultExecutionTime;
        private string previousGraphExecutionTime = defaultExecutionTime;
        private string totalGraphExecutionTime = defaultExecutionTime;
        private Dictionary<Guid, ProfiledNodeViewModel> nodeDictionary = new Dictionary<Guid, ProfiledNodeViewModel>();
        private Dictionary<Guid, List<ProfiledNodeViewModel>> groupDictionary = new Dictionary<Guid, List<ProfiledNodeViewModel>>();
        private Dictionary<Guid, Guid> executionTimeNodeDictionary = new Dictionary<Guid, Guid>();
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
        /// Returns visibility status for the latest run, previous run, and not executed node collections,
        /// based on whether each collection contains any nodes.
        /// </summary>
        public Visibility LatestRunTableVisibility
        {
            get => ProfiledNodesLatestRun != null && ProfiledNodesLatestRun.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        public Visibility PreviousRunTableVisibility
        {
            get => ProfiledNodesPreviousRun != null && ProfiledNodesPreviousRun.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        public Visibility NotExecutedTableVisibility
        {
            get => ProfiledNodesNotExecuted != null && ProfiledNodesNotExecuted.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Total graph execution time
        /// </summary>
        public string TotalGraphExecutionTime
        {
            get => totalGraphExecutionTime;
            set
            {
                if (totalGraphExecutionTime != value)
                {
                    totalGraphExecutionTime = value;
                    RaisePropertyChanged(nameof(TotalGraphExecutionTime));
                }
            }
        }
        public string LatestGraphExecutionTime
        {
            get => latestGraphExecutionTime;
            set
            {
                if (latestGraphExecutionTime != value)
                {
                    latestGraphExecutionTime = value;
                    RaisePropertyChanged(nameof(LatestGraphExecutionTime));
                }
            }
        }
        public string PreviousGraphExecutionTime
        {
            get => previousGraphExecutionTime;
            set
            {
                if (previousGraphExecutionTime != value)
                {
                    previousGraphExecutionTime = value;
                    RaisePropertyChanged(nameof(PreviousGraphExecutionTime));
                }
            }
        }

        /// <summary>
        /// Gets or sets whether node groups are displayed, and refreshes node collections based on this setting.
        /// </summary>
        public bool ShowGroups
        {
            get => showGroups;
            set
            {
                if (showGroups != value)
                {
                    showGroups = value;
                    RaisePropertyChanged(nameof(ShowGroups));

                    // Refresh all collections and apply group settings
                    UpdateGroupsVisibility(ProfiledNodesCollectionLatestRun, ProfiledNodesLatestRun);
                    UpdateGroupsVisibility(ProfiledNodesCollectionPreviousRun, ProfiledNodesPreviousRun);
                    UpdateGroupsVisibility(ProfiledNodesCollectionNotExecuted, ProfiledNodesNotExecuted);
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

        /// <summary>
        /// Gets or sets the sorting order and toggles the sort direction.
        /// </summary>
        public string SortingOrder
        {
            get => defaultSortingOrder;
            set
            {
                if (defaultSortingOrder != value)
                {
                    defaultSortingOrder = value;
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

        public const  string SortByName = "name";
        public const string SortByNumber = "number";
        public const string SortByTime = "time";

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

            // Reset total times
            LatestGraphExecutionTime = defaultExecutionTime;
            PreviousGraphExecutionTime = defaultExecutionTime;
            TotalGraphExecutionTime = defaultExecutionTime;

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

            ProfiledNodesCollectionLatestRun = new CollectionViewSource { Source = ProfiledNodesLatestRun };
            ProfiledNodesCollectionPreviousRun = new CollectionViewSource { Source = ProfiledNodesPreviousRun };
            ProfiledNodesCollectionNotExecuted = new CollectionViewSource { Source = ProfiledNodesNotExecuted };

            ApplyGroupNodeFilter();
            ApplyCustomSorting(ProfiledNodesCollectionNotExecuted, SortByName);

            RefreshAllCollectionViews();

            RaisePropertyChanged(nameof(ProfiledNodesCollectionNotExecuted));
            RaisePropertyChanged(nameof(ProfiledNodesNotExecuted));

            RaisePropertyChanged(nameof(NotExecutedTableVisibility));
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

            RaisePropertyChanged(nameof(LatestRunTableVisibility));
            RaisePropertyChanged(nameof(PreviousRunTableVisibility));
            RaisePropertyChanged(nameof(NotExecutedTableVisibility));

            RaisePropertyChanged(nameof(ProfiledNodesCollectionLatestRun));
            RaisePropertyChanged(nameof(ProfiledNodesCollectionPreviousRun));
            RaisePropertyChanged(nameof(ProfiledNodesCollectionNotExecuted));

            ProfiledNodesCollectionLatestRun.Dispatcher.InvokeAsync(() =>
            {
                ApplyCustomSorting(ProfiledNodesCollectionLatestRun);
                ProfiledNodesCollectionLatestRun.View?.Refresh();
                ApplyCustomSorting(ProfiledNodesCollectionPreviousRun);
                ProfiledNodesCollectionPreviousRun.View?.Refresh();
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
                    // Calculate total execution times using rounded node execution times, not exact values.
                    int totalLatestRun = ProfiledNodesLatestRun
                        .Where(n => n.WasExecutedOnLastRun && !n.IsGroup && !n.IsGroupExecutionTime)
                        .Sum(r => r?.ExecutionMilliseconds ?? 0);
                    int previousLatestRun = ProfiledNodesPreviousRun
                        .Where(n => !n.WasExecutedOnLastRun && !n.IsGroup && !n.IsGroupExecutionTime)
                        .Sum(r => r?.ExecutionMilliseconds ?? 0);

                    // Update latest and previous run times
                    latestGraphExecutionTime = totalLatestRun.ToString();
                    previousGraphExecutionTime = previousLatestRun.ToString();
                    totalGraphExecutionTime = (totalLatestRun + previousLatestRun).ToString();
                }, null);
            
            RaisePropertyChanged(nameof(TotalGraphExecutionTime));
            RaisePropertyChanged(nameof(LatestGraphExecutionTime));
            RaisePropertyChanged(nameof(PreviousGraphExecutionTime));
        }

        /// <summary>
        /// Calculates and assigns execution order numbers for profiled nodes.
        /// Aggregates execution times and updates states for nodes within groups.
        /// Ensures nodes are processed only once and maintains the sorted order of nodes.
        /// </summary>
        private void CalculateGroupNodes()
        {
            int groupExecutionCounter = 1;
            var processedNodes = new HashSet<ProfiledNodeViewModel>();
            var sortedProfiledNodes = ProfiledNodesLatestRun.OrderBy(node => node.ExecutionOrderNumber).ToList();

            // Create lookup dictionaries
            var annotationLookup = CurrentWorkspace.Annotations.ToDictionary(g => g.GUID);

            foreach (var profiledNode in sortedProfiledNodes)
            {
                // Process nodes that belong to a group and have not been processed yet
                if (!profiledNode.IsGroup && !profiledNode.IsGroupExecutionTime && profiledNode.GroupGUID != Guid.Empty && !processedNodes.Contains(profiledNode))
                {
                    if (nodeDictionary.TryGetValue(profiledNode.GroupGUID, out var profiledGroup) &&
                        groupDictionary.TryGetValue(profiledNode.GroupGUID, out var nodesInGroup))
                    {
                        ProfiledNodeViewModel groupTotalTimeNode = null;
                        bool groupIsRenamed = false;

                        // Reset group state
                        profiledGroup.State = profiledNode.State;
                        profiledGroup.GroupExecutionTime = TimeSpan.Zero; // Reset execution time
                        profiledGroup.ExecutionMilliseconds = 0; // Reset UI execution time
                        MoveNodeToCollection(profiledGroup, ProfiledNodesLatestRun); // Ensure the profiledGroup is in latest run

                        // Check if the group has been renamed
                        if (annotationLookup.TryGetValue(profiledGroup.GroupGUID, out var groupModel) && profiledGroup.GroupName != groupModel.AnnotationText)
                        {
                            groupIsRenamed = true;
                            profiledGroup.GroupName = groupModel.AnnotationText;
                            profiledGroup.Name = $"{ProfiledNodeViewModel.GroupNodePrefix}{groupModel.AnnotationText}";
                        }

                        // Iterate through the nodes in the group
                        foreach (var node in nodesInGroup)
                        {
                            // Find groupTotalExecutionTime node, if it already exists
                            if (node.IsGroupExecutionTime)
                            {
                                groupTotalTimeNode = node;
                            }
                            else if (processedNodes.Add(node))
                            {
                                // Update group state, execution order, and execution time
                                profiledGroup.GroupExecutionTime += node.ExecutionTime; // accurate, for sorting
                                profiledGroup.ExecutionMilliseconds += node.ExecutionMilliseconds; // rounded, for display in UI
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


                        // Create and add group total execution time node if it doesn't exist
                        groupTotalTimeNode ??= CreateGroupTotalTimeNode(profiledGroup);

                        // Update the properties of the groupTotalTimeNode and move to latestRunCollection
                        UpdateGroupTotalTimeNodeProperties(groupTotalTimeNode, profiledGroup);

                        // Update the groupExecutionTime for all nodes of the group for the purposes of sorting
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
                    profiledNode.GroupExecutionOrderNumber = groupExecutionCounter++;
                    profiledNode.GroupExecutionTime = profiledNode.ExecutionTime;
                }
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
                // Assign execution time and manually set the execution milliseconds value
                // so that group node execution time is based on rounded millisecond values.
                profiledNode.ExecutionMilliseconds = (int)Math.Round(executionTime.TotalMilliseconds);

                if (!profiledNode.WasExecutedOnLastRun)
                {
                    profiledNode.ExecutionOrderNumber = executedNodesNum++;
                    // Move to collection LatestRun
                    MoveNodeToCollection(profiledNode, ProfiledNodesLatestRun);
                }
            }

            profiledNode.Stopwatch.Reset();
            profiledNode.WasExecutedOnLastRun = true;
            profiledNode.State = ProfiledNodeState.ExecutedOnCurrentRun;
        }

        internal void OnNodePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is NodeModel nodeModel && nodeDictionary.TryGetValue(nodeModel.GUID, out var profiledNode))
            {
                bool hasChanges = false;

                // Detect node renaming
                if (e.PropertyName == nameof(nodeModel.Name))
                {
                    profiledNode.Name = nodeModel.Name;
                    hasChanges = true;
                }

                // Refresh UI if any changes were made
                if (hasChanges)
                {
                    RefreshCollectionViewContainingNode(profiledNode);
                }
            }
        }

        internal void OnGroupPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is AnnotationModel groupModel && nodeDictionary.TryGetValue(groupModel.GUID, out var profiledGroup))
            {
                bool hasChanges = false;

                // Detect group renaming
                if (e.PropertyName == nameof(groupModel.AnnotationText))
                {
                    profiledGroup.Name = $"{ProfiledNodeViewModel.GroupNodePrefix}{groupModel.AnnotationText}";
                    profiledGroup.GroupName = groupModel.AnnotationText;

                    // Update the nodes in the group
                    foreach (var profiledNode in groupDictionary[groupModel.GUID])
                    {
                        profiledNode.GroupName = groupModel.AnnotationText;
                    }
                    hasChanges = true;
                }

                // Detect change of color
                if (e.PropertyName == nameof(groupModel.Background))
                {
                    var newBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(groupModel.Background));
                    profiledGroup.BackgroundBrush = newBackgroundBrush;

                    // Update the nodes in the group
                    foreach (var profiledNode in groupDictionary[groupModel.GUID])
                    {
                        profiledNode.BackgroundBrush = newBackgroundBrush;
                    }
                    hasChanges = true;
                }

                // Refresh UI if any changes were made
                if (hasChanges)
                {
                    NotifyProfilingCollectionsChanged();
                    RefreshAllCollectionViews();
                }
            }
        }

        #endregion

        #region Workspace Events

        private void CurrentWorkspaceModel_NodeAdded(NodeModel node)
        {
            var profiledNode = new ProfiledNodeViewModel(node);
            nodeDictionary[node.GUID] = profiledNode;

            node.NodeExecutionBegin += OnNodeExecutionBegin;
            node.NodeExecutionEnd += OnNodeExecutionEnd;
            node.PropertyChanged += OnNodePropertyChanged;

            ProfiledNodesNotExecuted.Add(profiledNode);
            RaisePropertyChanged(nameof(NotExecutedTableVisibility));
        }

        private void CurrentWorkspaceModel_NodeRemoved(NodeModel node)
        {
            var profiledNode = nodeDictionary[node.GUID];
            nodeDictionary.Remove(node.GUID);

            node.NodeExecutionBegin -= OnNodeExecutionBegin;
            node.NodeExecutionEnd -= OnNodeExecutionEnd;
            node.PropertyChanged -= OnNodePropertyChanged;

            MoveNodeToCollection(profiledNode, null);
            //Recalculate the execution times
            UpdateExecutionTime();
        }

        private void CurrentWorkspaceModel_GroupAdded(AnnotationModel group)
        {
            var profiledGroup = new ProfiledNodeViewModel(group);
            nodeDictionary[group.GUID] = profiledGroup;
            ProfiledNodesNotExecuted.Add(profiledGroup);
            groupDictionary[group.GUID] = new List<ProfiledNodeViewModel>();

            group.PropertyChanged += OnGroupPropertyChanged;

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
            ApplyCustomSorting(ProfiledNodesCollectionNotExecuted, SortByName);
        }

        private void CurrentWorkspaceModel_GroupRemoved(AnnotationModel group)
        {
            var groupGUID = group.GUID;

            group.PropertyChanged -= OnGroupPropertyChanged;

            // Remove the group from nodeDictionary and ProfiledNodes
            if (nodeDictionary.Remove(groupGUID, out var profiledGroup))
            {
                MoveNodeToCollection(profiledGroup, null);
            }

            // Reset grouped nodes' properties and remove them from groupDictionary
            if (groupDictionary.Remove(groupGUID, out var groupedNodes))
            {
                foreach (var profiledNode in groupedNodes)
                {
                    // Remove group total execution time node
                    if (profiledNode.IsGroupExecutionTime &&
                        executionTimeNodeDictionary.TryGetValue(groupGUID, out var execTimeNodeGUID))
                    {
                        MoveNodeToCollection(profiledNode, null);
                        nodeDictionary.Remove(execTimeNodeGUID);
                    }

                    // Reset properties for each grouped node
                    profiledNode.GroupGUID = Guid.Empty;
                    profiledNode.GroupName = string.Empty;
                    profiledNode.ExecutionOrderNumber = null;
                    profiledNode.GroupExecutionTime = TimeSpan.Zero;
                }
            }

            //Recalculate the execution times
            UpdateExecutionTime();
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

        #region Helpers

        private ProfiledNodeViewModel CreateGroupTotalTimeNode(ProfiledNodeViewModel profiledGroup)
        {
            var groupTotalTimeNode = new ProfiledNodeViewModel(
                ProfiledNodeViewModel.GroupExecutionTimeString, TimeSpan.Zero, ProfiledNodeState.NotExecuted)
            {
                GroupGUID = profiledGroup.GroupGUID,
                GroupName = profiledGroup.GroupName,
                BackgroundBrush = profiledGroup.BackgroundBrush,
                IsGroupExecutionTime = true,
                ShowGroupIndicator = true
            };

            var totalExecTimeGUID = Guid.NewGuid();
            nodeDictionary[totalExecTimeGUID] = groupTotalTimeNode;
            groupDictionary[profiledGroup.GroupGUID].Add(groupTotalTimeNode);
            executionTimeNodeDictionary[profiledGroup.GroupGUID] = totalExecTimeGUID;

            return groupTotalTimeNode;
        }

        private void UpdateGroupTotalTimeNodeProperties(ProfiledNodeViewModel groupTotalTimeNode, ProfiledNodeViewModel profiledGroup)
        {
            groupTotalTimeNode.State = profiledGroup.State;
            groupTotalTimeNode.GroupExecutionTime = profiledGroup.GroupExecutionTime; // Accurate, for sorting
            groupTotalTimeNode.ExecutionMilliseconds = profiledGroup.ExecutionMilliseconds; // Rounded, for display in UI
            groupTotalTimeNode.GroupExecutionOrderNumber = profiledGroup.GroupExecutionOrderNumber;
            groupTotalTimeNode.WasExecutedOnLastRun = true;

            // Move node to the latest run collection
            MoveNodeToCollection(groupTotalTimeNode, ProfiledNodesLatestRun);
        }

        /// <summary>
        /// Refreshes the profiling node collection that contains a given node and updates the view.
        /// </summary>
        private void RefreshCollectionViewContainingNode(ProfiledNodeViewModel profiledNode)
        {
            if (ProfiledNodesLatestRun.Contains(profiledNode))
            {
                ProfiledNodesCollectionLatestRun.View.Refresh();
            }
            else if (ProfiledNodesPreviousRun.Contains(profiledNode))
            {
                ProfiledNodesCollectionPreviousRun.View.Refresh();
            }
            else if (ProfiledNodesNotExecuted.Contains(profiledNode))
            {
                ProfiledNodesCollectionNotExecuted.View.Refresh();
            }
        }

        /// <summary>
        /// Refreshes all profiling node collections and updates the view.
        /// </summary>
        private void RefreshAllCollectionViews()
        {
            ProfiledNodesCollectionLatestRun?.View?.Refresh();
            ProfiledNodesCollectionPreviousRun?.View?.Refresh();
            ProfiledNodesCollectionNotExecuted?.View?.Refresh();
        }
        /// <summary>
        /// Notifies the system that all profiling node collections have changed,
        /// triggering any necessary updates in the user interface.
        /// </summary>
        private void NotifyProfilingCollectionsChanged()
        {
            RaisePropertyChanged(nameof(ProfiledNodesLatestRun));
            RaisePropertyChanged(nameof(ProfiledNodesPreviousRun));
            RaisePropertyChanged(nameof(ProfiledNodesNotExecuted));
        }

        /// <summary>
        /// Updates the group visibility, refreshes the collection view, and applies appropriate sorting for the given nodes.
        /// </summary>
        private void UpdateGroupsVisibility(CollectionViewSource collectionView, ObservableCollection<ProfiledNodeViewModel> nodes)
        {
            foreach (var node in nodes)
            {
                node.ShowGroupIndicator = showGroups;
            }

            if (collectionView?.View?.Cast<ProfiledNodeViewModel>().Any() == true)
            {
                string sortingOrder = collectionView == ProfiledNodesCollectionNotExecuted ? SortByName : null;
                ApplyCustomSorting(collectionView, sortingOrder);
            }
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
        /// Applies the GroupNodeFilter to all node collections by removing and re-adding the filter.
        /// </summary>
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
        /// Applies the sorting logic to all ProfiledNodesCollections.
        /// </summary>
        public void ApplyCustomSorting()
        {
            ApplyCustomSorting(ProfiledNodesCollectionLatestRun);
            ApplyCustomSorting(ProfiledNodesCollectionPreviousRun);
            // Apply custom sorting to NotExecuted collection only if sortingOrder is "name"
            if (defaultSortingOrder == SortByName)
            {
                ApplyCustomSorting(ProfiledNodesCollectionNotExecuted);
            }
        }

        /// <summary>
        /// Applies the sorting logic to a given ProfiledNodesCollection.
        /// </summary>
        public void ApplyCustomSorting(CollectionViewSource collection, string explicitSortingOrder = null)
        {
            // Use the sorting parameter if specified; otherwise, use the private sortingOrder
            string sortBy = explicitSortingOrder ?? defaultSortingOrder;
            collection.SortDescriptions.Clear();

            switch (sortBy)
            {
                case SortByTime:
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
                case SortByName:
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
                case SortByNumber:
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

        /// <summary>
        /// Moves a node between collections, removing it from all collections and adding it to the target collection if provided.
        /// </summary>
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
                SwitchToManualMode();

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
                    node.PropertyChanged += OnNodePropertyChanged;
                }
                foreach (var group in workspace.Annotations)
                {
                    group.PropertyChanged += OnGroupPropertyChanged;
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
                    node.PropertyChanged -= OnNodePropertyChanged;
                }
                foreach (var group in workspace.Annotations)
                {
                    group.PropertyChanged -= OnGroupPropertyChanged;
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

        #region Exporters

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

                    var collections = new (string Label, CollectionViewSource Collection, string TotalTime)[]
                    {
                        ("Latest Run", ProfiledNodesCollectionLatestRun, LatestGraphExecutionTime),
                        ("Previous Run", ProfiledNodesCollectionPreviousRun, PreviousGraphExecutionTime),
                        ("Not Executed", ProfiledNodesCollectionNotExecuted, null)
                    };

                    foreach (var (label, collection, totalTime) in collections)
                    {
                        var nodes = collection.View.Cast<ProfiledNodeViewModel>().ToList();
                        if (!nodes.Any()) continue;

                        writer.WriteLine(label);

                        foreach (var node in nodes)
                        {
                            if (showGroups)
                            {
                                if (node.IsGroup || node.GroupGUID == Guid.Empty)
                                {
                                    writer.WriteLine($"{node.GroupExecutionOrderNumber},{node.Name},{node.ExecutionMilliseconds}");
                                }
                                else
                                {
                                    writer.WriteLine($",{node.Name},{node.ExecutionMilliseconds}");
                                }
                            }
                            else if (!node.IsGroup || !node.IsGroupExecutionTime)
                            {
                                writer.WriteLine($"{node.ExecutionOrderNumber},{node.Name},{node.ExecutionMilliseconds}");
                            }
                        }

                        // Write total execution time, if applicable
                        if (!string.IsNullOrEmpty(totalTime))
                        {
                            writer.WriteLine($",Total, {totalTime}");
                        }
                        writer.WriteLine();
                    }
                }
            }
        }

        /// <summary>
        /// Exports the ProfiledNodesCollections to a JSON file.
        /// </summary>
        public void ExportToJson()
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "JSON file (*.json)|*.json|All files (*.*)|*.*"
            };

            if (saveFileDialog.ShowDialog() != true) return;

            var exportData = new List<object>();

            var collections = new (string Label, CollectionViewSource Collection, string TotalTime)[]
            {
                ("Latest Run", ProfiledNodesCollectionLatestRun, LatestGraphExecutionTime),
                ("Previous Run", ProfiledNodesCollectionPreviousRun, PreviousGraphExecutionTime),
                ("Not Executed", ProfiledNodesCollectionNotExecuted, null)
            };

            foreach (var (label, collection, totalTime) in collections)
            {
                var nodes = collection.View.Cast<ProfiledNodeViewModel>().ToList();
                if (!nodes.Any()) continue;

                // Create an object for each collection, including label and nodes
                var collectionData = new
                {
                    Label = label,
                    Nodes = new List<object>(),
                    TotalTime = totalTime
                };

                ProfiledNodeViewModel currentGroup = null;
                List<object> currentGroupChildren = null;

                foreach (var node in nodes)
                {
                    if (node.IsGroup)
                    {
                        // If there's an existing group, add its children to it
                        if (currentGroup != null)
                        {
                            collectionData.Nodes.Add(new
                            {
                                ExecutionOrder = currentGroup.GroupExecutionOrderNumber,
                                Name = currentGroup.Name,
                                ExecutionTimeMs = currentGroup.ExecutionMilliseconds,
                                Children = currentGroupChildren
                            });
                        }

                        // Start a new group
                        currentGroup = node;
                        currentGroupChildren = new List<object>();
                    }
                    else
                    {
                        // Add the node either to the current group or directly to the collection
                        if (currentGroup != null && node.GroupGUID == currentGroup.GroupGUID)
                        {
                            currentGroupChildren.Add(new
                            {
                                ExecutionOrder = node.ExecutionOrderNumber,
                                Name = node.Name,
                                ExecutionTimeMs = node.ExecutionMilliseconds
                            });
                        }
                        // Stand-alone node
                        else
                        {
                            collectionData.Nodes.Add(new
                            {
                                ExecutionOrder = showGroups ? node.GroupExecutionOrderNumber : node.ExecutionOrderNumber,
                                Name = node.Name,
                                ExecutionTimeMs = node.ExecutionMilliseconds
                            });
                        }
                    }
                }

                // After the loop, add the last group if it exists
                if (currentGroup != null)
                {
                    collectionData.Nodes.Add(new
                    {
                        ExecutionOrder = currentGroup.GroupExecutionOrderNumber,
                        Name = currentGroup.Name,
                        ExecutionTimeMs = currentGroup.ExecutionMilliseconds,
                        Children = currentGroupChildren
                    });
                }

                // Add the collection data to the export data
                exportData.Add(collectionData);
            }

            // Serialize the data to JSON and write it to a file
            string json = JsonConvert.SerializeObject(exportData, Formatting.Indented);
            using (var writer = new StreamWriter(saveFileDialog.FileName))
            {
                writer.Write(json);
            }
        }

        #endregion
    }
}
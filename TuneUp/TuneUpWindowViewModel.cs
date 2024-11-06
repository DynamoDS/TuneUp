using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Dynamo.Core;
using Dynamo.Engine.Profiling;
using Dynamo.Graph.Annotations;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Workspaces;
using Dynamo.Models;
using Dynamo.ViewModels;
using Dynamo.Wpf.Extensions;
using Dynamo.Wpf.Utilities;
using Microsoft.Win32;
using Newtonsoft.Json;
using TuneUp.Properties;

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
        private bool showGroups = true;
        private ListSortDirection sortDirection;
        private static readonly string defaultExecutionTime = Resources.Label_DefaultExecutionTime;
        private string defaultSortingOrder = "number";        
        private string latestGraphExecutionTime = defaultExecutionTime;
        private string previousGraphExecutionTime = defaultExecutionTime;
        private string totalGraphExecutionTime = defaultExecutionTime;
        private Dictionary<Guid, ProfiledNodeViewModel> nodeDictionary = new Dictionary<Guid, ProfiledNodeViewModel>();
        private Dictionary<Guid, ProfiledNodeViewModel> groupDictionary = new Dictionary<Guid, ProfiledNodeViewModel>();
        // Maps AnnotationModel GUIDs to a list of associated ProfiledNodeViewModel instances.
        private Dictionary<Guid, List<ProfiledNodeViewModel>> groupModelDictionary = new Dictionary<Guid, List<ProfiledNodeViewModel>>();
        private Dictionary<ObservableCollection<ProfiledNodeViewModel>, CollectionViewSource> collectionMapping = new Dictionary<ObservableCollection<ProfiledNodeViewModel>, CollectionViewSource>();

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

        public const string SortByName = "name";
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

            Task.Run(() =>
            {
                uiContext.Post(_ => {
                    // Initialize collections and dictionaries
                    InitializeCollectionsAndDictionaries();

                    // Create a profiled node for each NodeModel
                    foreach (var node in CurrentWorkspace.Nodes)
                    {
                        var profiledNode = new ProfiledNodeViewModel(node) { GroupName = node.Name };
                        ProfiledNodesNotExecuted.Add(profiledNode);
                        nodeDictionary[node.GUID] = profiledNode;
                    }

                    // Create a profiled node for each AnnotationModel
                    foreach (var group in CurrentWorkspace.Annotations)
                    {
                        var pGroup = new ProfiledNodeViewModel(group);
                        ProfiledNodesNotExecuted.Add(pGroup);
                        groupDictionary[pGroup.NodeGUID] = (pGroup);
                        groupModelDictionary[group.GUID] = new List<ProfiledNodeViewModel> { pGroup };

                        var groupedNodeGUIDs = group.Nodes.OfType<NodeModel>().Select(n => n.GUID);

                        foreach (var nodeGuid in groupedNodeGUIDs)
                        {
                            if (nodeDictionary.TryGetValue(nodeGuid, out var pNode))
                            {
                                ApplyGroupPropertiesAndRegisterNode(pNode, pGroup);
                            }
                        }
                    }

                    // Refresh UI after reset
                    RefreshUIAfterReset();
                }, null);
            });            
        }

        /// <summary>
        /// The handler for force-recompute the graph
        /// </summary>
        internal void ResetProfiling()
        {
            // Put the graph into manual mode as there is no guarantee that nodes will be marked
            // dirty in topologically sorted order during a reset.
            SwitchToManualMode();

            // Enable profiling on the new engine controller after the reset.
            CurrentWorkspace.EngineController.EnableProfiling(true, currentWorkspace, currentWorkspace.Nodes);

            // Ensure all nodes are marked as modified
            foreach (var node in viewLoadedParams.CurrentWorkspaceModel.Nodes)
            {
                node.RegisterAllPorts();
                node.MarkNodeAsModified(true);
            }

            // Execute the Run command
            viewLoadedParams.CommandExecutive.ExecuteCommand(
                new DynamoModel.RunCancelCommand(true, false),
                Guid.NewGuid().ToString(),
                "TuneUp Run All");

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
                    MoveNodeToCollection(node, ProfiledNodesPreviousRun);
                }
            }
            executedNodesNum = 1;
            EnableProfiling();
        }

        private void CurrentWorkspaceModel_EvaluationCompleted(object sender, Dynamo.Models.EvaluationCompletedEventArgs e)
        {
            Task.Run(() =>
            {
                IsRecomputeEnabled = true;

                CalculateGroupNodes();
                UpdateExecutionTime();
                UpdateTableVisibility();                

                uiContext.Post(_ =>
                {
                    RaisePropertyChanged(nameof(ProfiledNodesCollectionLatestRun));
                    RaisePropertyChanged(nameof(ProfiledNodesCollectionPreviousRun));
                    RaisePropertyChanged(nameof(ProfiledNodesCollectionNotExecuted));

                    ApplyCustomSorting(ProfiledNodesCollectionLatestRun);
                    ApplyCustomSorting(ProfiledNodesCollectionPreviousRun);

                    ProfiledNodesCollectionLatestRun.View?.Refresh();
                    ProfiledNodesCollectionPreviousRun.View?.Refresh();
                    ProfiledNodesCollectionNotExecuted.View?.Refresh();
                }, null);
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
            Task.Run(() =>
            {
                // Apply all removals and additions on the UI thread
                uiContext.Post(_ =>
                {
                    // Clean the collections from all group and time nodes
                    foreach (var node in groupDictionary.Values)
                    {
                        RemoveNodeFromStateCollection(node, node.State);

                        if (groupModelDictionary.TryGetValue(node.GroupGUID, out var groupNodes))
                        {
                            groupNodes.Remove(node);
                        }
                    }
                    groupDictionary.Clear();

                    // Create group and time nodes for latest and previous runs
                    CreateGroupNodesForCollection(ProfiledNodesLatestRun);
                    CreateGroupNodesForCollection(ProfiledNodesPreviousRun);

                    // Create group nodes for not executed 
                    var processedNodesNotExecuted = new HashSet<ProfiledNodeViewModel>();

                    // Create a copy of ProfiledNodesNotExecuted to iterate over
                    var profiledNodesCopy = ProfiledNodesNotExecuted.ToList();

                    foreach (var pNode in profiledNodesCopy)
                    {
                        if (pNode.GroupGUID != Guid.Empty && !processedNodesNotExecuted.Contains(pNode))
                        {
                            // get the other nodes from this group
                            var nodesInGroup = ProfiledNodesNotExecuted
                                .Where(n => n.GroupGUID == pNode.GroupGUID)
                                .ToList();

                            foreach (var node in nodesInGroup)
                            {
                                processedNodesNotExecuted.Add(node);
                            }

                            // create new group node
                            var pGroup = new ProfiledNodeViewModel(pNode);

                            groupDictionary[pGroup.NodeGUID] = pGroup;
                            groupModelDictionary[pNode.GroupGUID].Add(pGroup);

                            uiContext.Send(_ => ProfiledNodesNotExecuted.Add(pGroup), null);
                        }
                    }

                    RefreshGroupNodeUI();
                }, null);
            });            
        }

        private void CreateGroupNodesForCollection(ObservableCollection<ProfiledNodeViewModel> collection)
        {
            int executionCounter = 1;
            var processedNodes = new HashSet<ProfiledNodeViewModel>();

            var sortedNodes = collection.OrderBy(n => n.ExecutionOrderNumber).ToList();

            foreach (var pNode in sortedNodes)
            {
                // Process the standalone nodes
                if (pNode.GroupGUID == Guid.Empty && !processedNodes.Contains(pNode))
                {
                    pNode.GroupExecutionMilliseconds = pNode.ExecutionMilliseconds;
                    pNode.ExecutionOrderNumber = executionCounter;
                    pNode.GroupExecutionOrderNumber = executionCounter++;

                    processedNodes.Add(pNode);
                }

                // Process the grouped nodes
                else if (pNode.GroupGUID != Guid.Empty && !processedNodes.Contains(pNode))
                {
                    // Get all nodes in the same group and calculate the group execution time
                    int groupExecTime = 0;
                    var nodesInGroup = sortedNodes.Where(n => n.GroupGUID == pNode.GroupGUID).ToList();

                    foreach (var node in nodesInGroup)
                    {
                        processedNodes.Add(node);
                        groupExecTime += node.ExecutionMilliseconds;
                    }

                    // Create and register a new group node using the current profiled node
                    var pGroup = new ProfiledNodeViewModel(pNode)
                    {
                        GroupExecutionOrderNumber = executionCounter++,
                        GroupExecutionMilliseconds = groupExecTime,
                        GroupModel = CurrentWorkspace.Annotations.First(n => n.GUID.Equals(pNode.GroupGUID))
                    };
                    collection.Add(pGroup);

                    groupDictionary[pGroup.NodeGUID] = pGroup;
                    groupModelDictionary[pNode.GroupGUID].Add(pGroup);

                    // Create an register a new time node
                    var timeNode = CreateAndRegisterGroupTimeNode(pGroup);
                    collection.Add(timeNode);

                    // Update group-related properties for all nodes in the group
                    foreach (var node in nodesInGroup)
                    {
                        node.GroupExecutionOrderNumber = pGroup.GroupExecutionOrderNumber;
                        node.GroupExecutionMilliseconds = pGroup.GroupExecutionMilliseconds;
                    }
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
                // Assign execution time and manually set the execution milliseconds value
                // so that group node execution time is based on rounded millisecond values.
                // Nodes should display at least 1ms execution time if they are executed.
                profiledNode.ExecutionMilliseconds = Math.Max(1, (int)Math.Round(executionTime.TotalMilliseconds));

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
            if (sender is AnnotationModel groupModel && groupModelDictionary.TryGetValue(groupModel.GUID, out var nodesInGroup))
            {
                bool isRenamed = false;
                ObservableCollection<ProfiledNodeViewModel> collection = null;

                // Detect group renaming
                if (e.PropertyName == nameof(groupModel.AnnotationText))
                {
                    foreach (var pNode in nodesInGroup)
                    {
                        if (pNode.IsGroup)
                        {
                            pNode.Name = ProfiledNodeViewModel.GetProfiledGroupName(groupModel.AnnotationText);
                        }
                        pNode.GroupName = groupModel.AnnotationText;
                    }
                    isRenamed = true;
                }

                // Detect change of color
                if (e.PropertyName == nameof(groupModel.Background))
                {
                    foreach (var pNode in nodesInGroup)
                    {
                        var newBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(groupModel.Background));
                        pNode.BackgroundBrush = newBackgroundBrush;
                    }
                }

                if (e.PropertyName == nameof(groupModel.Nodes))
                {
                    var allNodesInGroup = groupModelDictionary[groupModel.GUID];

                    var modelNodeGuids = groupModel.Nodes
                        .OfType<NodeModel>()
                        .Select(n => n.GUID)
                        .ToHashSet();

                    // Determine if we adding or removing a node
                    var pNodeToRemove = allNodesInGroup
                        .FirstOrDefault(n => !n.IsGroup && !n.IsGroupExecutionTime && !modelNodeGuids.Contains(n.NodeGUID));

                    var pNodeToAdd = nodeDictionary.FirstOrDefault(kvp => modelNodeGuids.Contains(kvp.Key) && !allNodesInGroup.Contains(kvp.Value)).Value;

                    var (pNodeModified, addNode) = pNodeToRemove == null ? (pNodeToAdd, true) : (pNodeToRemove, false);

                    // Safety check
                    if (pNodeModified == null) return;

                    var state = pNodeModified.State;
                    collection = GetObservableCollectionFromState(state);

                    // Get all nodes for this group in the same state
                    var allNodesInGroupForState = allNodesInGroup.Where(n => n.State == state).ToList();
                    var pGroupToModify = allNodesInGroupForState.FirstOrDefault(n => n.IsGroup);
                    var timeNodeToModify = allNodesInGroupForState.FirstOrDefault(n => n.IsGroupExecutionTime);
                    var pNodesOfSameState = allNodesInGroupForState.Where(n => !n.IsGroupExecutionTime && !n.IsGroup).ToList();

                    // Case REMOVE
                    if (!addNode)
                    {
                        ResetGroupPropertiesAndUnregisterNode(pNodeModified);
                        pNodesOfSameState.Remove(pNodeModified);

                        // Update group execution time
                        if (state != ProfiledNodeState.NotExecuted && pGroupToModify != null && timeNodeToModify != null)
                        {
                            pGroupToModify.GroupExecutionMilliseconds -= pNodeModified.ExecutionMilliseconds;
                            pGroupToModify.ExecutionMilliseconds = pGroupToModify.GroupExecutionMilliseconds;
                            timeNodeToModify.GroupExecutionMilliseconds = pGroupToModify.GroupExecutionMilliseconds;
                            timeNodeToModify.ExecutionMilliseconds = pGroupToModify.GroupExecutionMilliseconds;
                        }
                    }
                    // Case ADD
                    else
                    {
                        // Create a new group node if it doesn't exist for this state
                        if (pGroupToModify == null)
                        {
                            pGroupToModify = new ProfiledNodeViewModel(groupModel) { State = state };
                            collection.Add(pGroupToModify);
                            groupDictionary[pGroupToModify.NodeGUID] = pGroupToModify;
                            groupModelDictionary[groupModel.GUID].Add(pGroupToModify);

                            if (timeNodeToModify == null)
                            {
                                timeNodeToModify = CreateAndRegisterGroupTimeNode(pGroupToModify);
                            }
                        }

                        ApplyGroupPropertiesAndRegisterNode(pNodeModified, pGroupToModify);

                        // Update execution time if necessary
                        if (state != ProfiledNodeState.NotExecuted)
                        {
                            pGroupToModify.GroupExecutionMilliseconds += pNodeModified.ExecutionMilliseconds;
                            pGroupToModify.ExecutionMilliseconds = pGroupToModify.GroupExecutionMilliseconds;
                            timeNodeToModify.GroupExecutionMilliseconds = pGroupToModify.GroupExecutionMilliseconds;
                            timeNodeToModify.ExecutionMilliseconds = pGroupToModify.GroupExecutionMilliseconds;
                        }
                    }

                    // Update execution time for all nodes in the same state
                    if (state != ProfiledNodeState.NotExecuted)
                    {
                        foreach (var pNode in pNodesOfSameState)
                        {
                            pNode.GroupExecutionMilliseconds = pGroupToModify.GroupExecutionMilliseconds;
                            if (pNode.IsGroupExecutionTime)
                            {
                                pNode.ExecutionMilliseconds = pGroupToModify.GroupExecutionMilliseconds;
                            }
                        }
                    }

                    // Reset the group execution order
                    UpdateGroupExecutionOrders(collection);
                }

                // Refresh UI if any changes were made.
                // Changes to the group background do not require a full UI refresh.
                if (isRenamed)
                {
                    RefreshAllCollectionViews();
                }
                if (collection != null)
                {
                    SortCollectionViewForProfiledNodesCollection(collection);
                }
            }
        }

        /// <summary>
        /// Reorders the nodes in the collection by their execution order number,
        /// ensuring that nodes in the same group receive the same execution order.
        /// </summary>
        private void UpdateGroupExecutionOrders(ObservableCollection<ProfiledNodeViewModel> collection)
        {
            var pNodesOfCollection = collection
                .Where(n => !n.IsGroup && !n.IsGroupExecutionTime)
                .OrderBy(n => n.ExecutionOrderNumber);

            int newExecutionCounter = 1;
            var processedNodes = new HashSet<ProfiledNodeViewModel>();

            foreach (var pNode in pNodesOfCollection)
            {
                if (!processedNodes.Contains(pNode))
                {
                    if (pNode.GroupGUID != Guid.Empty)
                    {
                        var pNodesOfGroup = collection.Where(n => n.GroupGUID == pNode.GroupGUID);

                        foreach (var pNodeInGroup in pNodesOfGroup)
                        {
                            pNodeInGroup.GroupExecutionOrderNumber = newExecutionCounter;
                            processedNodes.Add(pNodeInGroup);
                        }
                    }
                    else
                    {
                        pNode.GroupExecutionOrderNumber = newExecutionCounter;
                        processedNodes.Add(pNode);
                    }

                    newExecutionCounter++;
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

            RemoveNodeFromStateCollection(profiledNode, profiledNode.State);

            //Recalculate the execution times
            UpdateExecutionTime();
            UpdateTableVisibility();
        }

        private void CurrentWorkspaceModel_GroupAdded(AnnotationModel group)
        {
            group.PropertyChanged += OnGroupPropertyChanged;

            var groupGUID = group.GUID;
            var pNodesInGroup = new List<ProfiledNodeViewModel>();

            // Initialize the group in the dictionary
            groupModelDictionary[groupGUID] = new List<ProfiledNodeViewModel>();

            // Create or retrieve profiled nodes for each NodeModel in the group
            foreach (var nodeModel in group.Nodes.OfType<NodeModel>())
            {
                if (!nodeDictionary.TryGetValue(nodeModel.GUID, out var pNode))
                {
                    pNode = new ProfiledNodeViewModel(nodeModel);
                    nodeDictionary[nodeModel.GUID] = pNode;
                    ProfiledNodesNotExecuted.Add(pNode);
                }
                pNodesInGroup.Add(pNode);
            }

            // Group profiled nodes by state and sort by execution order
            var groupedNodesInGroup = pNodesInGroup
                .Where(n => !n.IsGroupExecutionTime)
                .GroupBy(n => n.State);

            // Process each group of nodes by state
            foreach (var stateGroup in groupedNodesInGroup)
            {
                var state = stateGroup.Key;
                var collection = GetObservableCollectionFromState(state);

                // Create and log new group node
                var pGroup = new ProfiledNodeViewModel(group) { State = state };
                groupModelDictionary[groupGUID].Add(pGroup);
                groupDictionary[pGroup.NodeGUID] = pGroup;
                collection.Add(pGroup);

                // Accumulate execution times and create a time node
                if (collection != ProfiledNodesNotExecuted)
                {
                    int groupExecutionTime = 0;
                    foreach (var pNode in stateGroup)
                    {
                        groupExecutionTime += pNode.ExecutionMilliseconds;
                        pNode.GroupExecutionOrderNumber = null;
                    }
                    pGroup.GroupExecutionMilliseconds = groupExecutionTime;

                    // Create and register time node
                    var timeNode = CreateAndRegisterGroupTimeNode(pGroup);

                    uiContext.Send(_ => { collection.Add(timeNode); }, null);
                    
                }

                // Apply group properties
                foreach (var pNode in stateGroup)
                {
                    ApplyGroupPropertiesAndRegisterNode(pNode, pGroup);
                }

                // Update the group execution order in the collection
                UpdateGroupExecutionOrders(collection);
            }

            // Ensure new group nodes are sorted properly
            uiContext.Post(_ =>
            {
                ApplyCustomSorting(ProfiledNodesCollectionLatestRun);
                ProfiledNodesCollectionLatestRun.View?.Refresh();
                ApplyCustomSorting(ProfiledNodesCollectionPreviousRun);
                ProfiledNodesCollectionPreviousRun.View?.Refresh();
                ApplyCustomSorting(ProfiledNodesCollectionNotExecuted, SortByName);
                ProfiledNodesCollectionNotExecuted.View?.Refresh();
            }, null);

        }

        private void CurrentWorkspaceModel_GroupRemoved(AnnotationModel group)
        {
            group.PropertyChanged -= OnGroupPropertyChanged;

            var groupGUID = group.GUID;
            groupModelDictionary.TryGetValue(groupGUID, out var allNodes);

            var pNodes = new List<ProfiledNodeViewModel>();
            var gNodes = new List<ProfiledNodeViewModel>();
            var states = new HashSet<ProfiledNodeState>();

            foreach (var node in allNodes)
            {
                if (node.IsGroup || node.IsGroupExecutionTime) gNodes.Add(node);
                else pNodes.Add(node);

                states.Add(node.State);
            }

            // Remove the entire entry from the groupModelDictionary
            groupModelDictionary.Remove(groupGUID);

            // Remove the group and time nodes
            foreach (var node in gNodes)
            {
                RemoveNodeFromStateCollection(node, node.State);
                groupDictionary.Remove(node.NodeGUID);
            }

            // Reset the properties of each pNode
            foreach (var node in pNodes)
            {
                ResetGroupPropertiesAndUnregisterNode(node);
            }

            // Reset the group execution order in the collection based on the affected states
            foreach (var state in states)
            {
                var collection = GetObservableCollectionFromState(state);
                UpdateGroupExecutionOrders(collection);
            }

            RefreshAllCollectionViews();
            UpdateTableVisibility();
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

        /// <summary>
        /// Clears and initializes profiling collections and dictionaries to their default states.
        /// </summary>
        private void InitializeCollectionsAndDictionaries()
        {
            // Clear existing collections
            ProfiledNodesLatestRun?.Clear();
            ProfiledNodesPreviousRun?.Clear();
            ProfiledNodesNotExecuted?.Clear();

            // Reset execution time stats
            LatestGraphExecutionTime = PreviousGraphExecutionTime = TotalGraphExecutionTime = defaultExecutionTime;

            // Initialize observable collections and dictionaries
            ProfiledNodesLatestRun = ProfiledNodesLatestRun ?? new ObservableCollection<ProfiledNodeViewModel>();
            ProfiledNodesPreviousRun = ProfiledNodesPreviousRun ?? new ObservableCollection<ProfiledNodeViewModel>();
            ProfiledNodesNotExecuted = ProfiledNodesNotExecuted ?? new ObservableCollection<ProfiledNodeViewModel>();

            collectionMapping = new Dictionary<ObservableCollection<ProfiledNodeViewModel>, CollectionViewSource>
            {
                { ProfiledNodesLatestRun, ProfiledNodesCollectionLatestRun },
                { ProfiledNodesPreviousRun, ProfiledNodesCollectionPreviousRun },
                { ProfiledNodesNotExecuted, ProfiledNodesCollectionNotExecuted }
            };

            nodeDictionary = new Dictionary<Guid, ProfiledNodeViewModel>();
            groupDictionary = new Dictionary<Guid, ProfiledNodeViewModel>();
            groupModelDictionary = new Dictionary<Guid, List<ProfiledNodeViewModel>>();
        }

        /// <summary>
        /// Resets group-related properties of the node and unregisters it from the group model dictionary.
        /// </summary>
        internal void ResetGroupPropertiesAndUnregisterNode(ProfiledNodeViewModel profiledNode)
        {
            if (groupModelDictionary.TryGetValue(profiledNode.GroupGUID, out var groupNodes))
            {
                groupNodes.Remove(profiledNode);
            }

            profiledNode.GroupGUID = Guid.Empty;
            profiledNode.GroupName = profiledNode.Name;
            profiledNode.GroupExecutionMilliseconds = 0;
            profiledNode.GroupExecutionOrderNumber = null;
            profiledNode.ShowGroupIndicator = false;
        }

        /// <summary>
        /// Applies group properties to the profiled node and registers it in the group model dictionary.
        /// </summary>
        internal void ApplyGroupPropertiesAndRegisterNode(ProfiledNodeViewModel profiledNode, ProfiledNodeViewModel profiledGroup)
        {
            profiledNode.GroupGUID = profiledGroup.GroupGUID;
            profiledNode.GroupName = profiledGroup.GroupName;
            profiledNode.BackgroundBrush = profiledGroup.BackgroundBrush;
            profiledNode.GroupExecutionMilliseconds = profiledGroup.GroupExecutionMilliseconds;
            profiledNode.GroupExecutionOrderNumber = profiledGroup.GroupExecutionOrderNumber;
            profiledNode.ShowGroupIndicator = ShowGroups;

            if (groupModelDictionary.TryGetValue(profiledNode.GroupGUID, out var nodeList) && !nodeList.Contains(profiledNode))
            {
                nodeList.Add(profiledNode);
            }
        }

        /// <summary>
        /// Creates and registers a group time node with execution time details.
        /// </summary>
        private ProfiledNodeViewModel CreateAndRegisterGroupTimeNode(ProfiledNodeViewModel pNode)
        {
            var timeNode = new ProfiledNodeViewModel(pNode)
            {
                Name = ProfiledNodeViewModel.GroupExecutionTimeString,
                IsGroup = false,
                IsGroupExecutionTime = true,
                ExecutionMilliseconds = pNode.GroupExecutionMilliseconds,
                GroupExecutionMilliseconds = pNode.GroupExecutionMilliseconds,
                GroupExecutionOrderNumber = pNode.GroupExecutionOrderNumber
            };

            groupDictionary[timeNode.NodeGUID] = timeNode;
            groupModelDictionary[timeNode.GroupGUID].Add(timeNode);

            return timeNode;
        }

        /// <summary>
        /// Returns the appropriate ObservableCollection based on the node's profiling state.
        /// </summary>
        private ObservableCollection<ProfiledNodeViewModel> GetObservableCollectionFromState(ProfiledNodeState state)
        {
            if (state == ProfiledNodeState.ExecutedOnCurrentRun) return ProfiledNodesLatestRun;
            else if (state == ProfiledNodeState.ExecutedOnPreviousRun) return ProfiledNodesPreviousRun;
            else return ProfiledNodesNotExecuted;
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
        public void ApplyCustomSortingToAllCollections()
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
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.GroupExecutionMilliseconds), sortDirection));
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.GroupGUID), sortDirection));
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.IsGroup), ListSortDirection.Descending));
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.IsGroupExecutionTime), ListSortDirection.Ascending));
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.ExecutionMilliseconds), sortDirection));
                    }
                    else
                    {
                        collection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.ExecutionMilliseconds), sortDirection));
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
        /// Sorts the appropriate collection view based on the provided observable collection of profiled nodes.
        /// </summary>
        private void SortCollectionViewForProfiledNodesCollection(ObservableCollection<ProfiledNodeViewModel> collection)
        {
            if (collection == null) return;

            switch (collection)
            {
                case var _ when collection == ProfiledNodesLatestRun:
                    ApplyCustomSorting(ProfiledNodesCollectionLatestRun);
                    break;
                case var _ when collection == ProfiledNodesPreviousRun:
                    ApplyCustomSorting(ProfiledNodesCollectionPreviousRun);
                    break;
                default:
                    ApplyCustomSorting(ProfiledNodesCollectionNotExecuted, SortByName);
                    break;
            }
        }

        /// <summary>
        /// Moves a node between collections, removing it from all collections and adding it to the target collection if provided.
        /// </summary>
        private void MoveNodeToCollection(ProfiledNodeViewModel profiledNode, ObservableCollection<ProfiledNodeViewModel> targetCollection)
        {
            Task.Run(() =>
            {
                uiContext.Post(_ =>
                {
                    var collections = new[] { ProfiledNodesLatestRun, ProfiledNodesPreviousRun, ProfiledNodesNotExecuted };

                    foreach (var collection in collections)
                    {
                        collection?.Remove(profiledNode);
                    }

                    targetCollection?.Add(profiledNode);
                }, null);
            });            
        }

        /// <summary>
        /// Removes a node from the appropriate collection based on its state.
        /// </summary>
        private void RemoveNodeFromStateCollection(ProfiledNodeViewModel pNode, ProfiledNodeState state)
        {
            var collection = GetObservableCollectionFromState(state);

            collection?.Remove(pNode);
        }

        #endregion

        #region Refresh UI

        /// <summary>
        /// Raises property change notifications for the visibility of the Latest Run, Previous Run, and Not Executed tables.
        /// </summary>
        private void UpdateTableVisibility()
        {
            RaisePropertyChanged(nameof(LatestRunTableVisibility));
            RaisePropertyChanged(nameof(PreviousRunTableVisibility));
            RaisePropertyChanged(nameof(NotExecutedTableVisibility));
        }

        /// <summary>
        /// Refreshes the profiling node collection that contains a given node and updates the view.
        /// </summary>
        private void RefreshCollectionViewContainingNode(ProfiledNodeViewModel profiledNode)
        {
            switch (profiledNode.State)
            {
                case ProfiledNodeState.ExecutedOnCurrentRun:
                    ProfiledNodesCollectionLatestRun.View.Refresh();
                    break;
                case ProfiledNodeState.ExecutedOnPreviousRun:
                    ProfiledNodesCollectionPreviousRun.View.Refresh();
                    break;
                case ProfiledNodeState.NotExecuted:
                    ProfiledNodesCollectionNotExecuted.View.Refresh();
                    break;
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
        /// Refreshes the UI after resetting the profiled nodes
        /// </summary>
        private void RefreshUIAfterReset()
        {
            // Assign CollectionViewSource and set up UI properties
            ProfiledNodesCollectionLatestRun = new CollectionViewSource { Source = ProfiledNodesLatestRun };
            ProfiledNodesCollectionPreviousRun = new CollectionViewSource { Source = ProfiledNodesPreviousRun };
            ProfiledNodesCollectionNotExecuted = new CollectionViewSource { Source = ProfiledNodesNotExecuted };

            // Refresh UI by raising property changes and applying sorting/filtering
            RaisePropertyChanged(nameof(ProfiledNodesCollectionNotExecuted));
            ApplyCustomSorting(ProfiledNodesCollectionNotExecuted, SortByName);
            ApplyGroupNodeFilter();
            UpdateTableVisibility();
        }

        /// <summary>
        /// Refreshes the UI after group nodes are re-calculated
        /// </summary>
        private void RefreshGroupNodeUI()
        {
            ApplyCustomSorting(ProfiledNodesCollectionLatestRun);
            RaisePropertyChanged(nameof(ProfiledNodesCollectionLatestRun));
            ApplyCustomSorting(ProfiledNodesCollectionPreviousRun);
            RaisePropertyChanged(nameof(ProfiledNodesCollectionPreviousRun));
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
                // Check if the .csv file locked or in use
                if (IsFileLocked(new FileInfo(saveFileDialog.FileName)))
                {
                    MessageBoxService.Show(
                        Resources.Message_FileInUse,
                        Resources.Title_FileInUse,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                try
                {
                    using (var writer = new StreamWriter(saveFileDialog.FileName))
                    {
                        writer.WriteLine($"{Resources.Header_ExecutionOrder},{Resources.Header_Name},{Resources.Header_ExecutionTime}");

                        var collections = new (string Label, CollectionViewSource Collection, string TotalTime)[]
                        {
                        (Resources.Label_LatestRun, ProfiledNodesCollectionLatestRun, LatestGraphExecutionTime),
                        (Resources.Label_PreviousRun, ProfiledNodesCollectionPreviousRun, PreviousGraphExecutionTime),
                        (Resources.Label_NotExecuted, ProfiledNodesCollectionNotExecuted, null)
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
                                writer.WriteLine($",{Resources.Label_Total}, {totalTime}");
                            }
                            writer.WriteLine();
                        }
                    }
                }
                catch (IOException ex)
                {
                    string errorMessage = string.Format(Resources.Message_FileWriteError, ex.Message);

                    MessageBoxService.Show(
                        errorMessage,
                        Resources.Title_Error,
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }                
            }
        }

        /// <summary>
        /// Checks if the specified file is locked by another process or application.
        /// </summary>
        private bool IsFileLocked(FileInfo file)
        {
            if (!file.Exists) return false;

            FileStream stream = null;

            try
            {
                stream = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                return true;
            }
            finally
            {
                stream?.Close();
            }

            return false;
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
                (Resources.Label_LatestRun, ProfiledNodesCollectionLatestRun, LatestGraphExecutionTime),
                (Resources.Label_PreviousRun, ProfiledNodesCollectionPreviousRun, PreviousGraphExecutionTime),
                (Resources.Label_NotExecuted, ProfiledNodesCollectionNotExecuted, null)
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
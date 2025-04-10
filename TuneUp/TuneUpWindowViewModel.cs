﻿using System;
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
        // Temporary HashSets used for batch updates. 
        private HashSet<ProfiledNodeViewModel> tempProfiledNodesLatestRun = new HashSet<ProfiledNodeViewModel>();
        private HashSet<ProfiledNodeViewModel> tempProfiledNodesPreviousRun = new HashSet<ProfiledNodeViewModel>();
        private HashSet<ProfiledNodeViewModel> tempProfiledNodesNotExecuted = new HashSet<ProfiledNodeViewModel>();
        private bool suppressNodeReset = false;
        private IWorkspaceModel previousWorkspace;
        private readonly WorkspaceProfilingData cachedData = new WorkspaceProfilingData();

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
                    RaisePropertyChanged(nameof(RunAllTooltipMessage));
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
        public string RunAllTooltipMessage => IsRecomputeEnabled ? Resources.ToolTip_RunAll : Resources.ToolTip_RunAllDisabled;

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
                    if (suppressNodeReset)
                    {
                        // Skip resetting nodes and directly refresh the UI
                        isProfilingEnabled = true;
                        RefreshUIAfterReset();
                        return;
                    }

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
                        // Initialize an empty entry for each group in groupModelDictionary
                        groupModelDictionary[group.GUID] = new List<ProfiledNodeViewModel> ();

                        // Only create and add groups to ProfiledNodesNotExecuted if they contain NodeModel instances
                        if (group.Nodes.Any(n => n is NodeModel))
                        {
                            var pGroup = CreateAndRegisterGroupNode(group, ProfiledNodesNotExecuted);

                            var groupedNodeGUIDs = group.Nodes.OfType<NodeModel>().Select(n => n.GUID);

                            foreach (var nodeGuid in groupedNodeGUIDs)
                            {
                                if (nodeDictionary.TryGetValue(nodeGuid, out var pNode))
                                {
                                    ApplyGroupPropertiesAndRegisterNode(pNode, pGroup);
                                }
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
            // Store nodes in temporary HashSets to batch the updates and avoid immediate UI refreshes.
            tempProfiledNodesLatestRun = ProfiledNodesLatestRun.ToHashSet();
            tempProfiledNodesPreviousRun = ProfiledNodesPreviousRun.ToHashSet();
            tempProfiledNodesNotExecuted = ProfiledNodesNotExecuted.ToHashSet();

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
                    MoveNodeToTempCollection(node, tempProfiledNodesPreviousRun);
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

                uiContext.Post(_ =>
                {
                    // Swap references instead of clearing and re-adding nodes
                    ProfiledNodesLatestRun.Clear();
                    foreach (var node in tempProfiledNodesLatestRun)
                    {
                        ProfiledNodesLatestRun.Add(node);
                    }
                    ProfiledNodesPreviousRun.Clear();
                    foreach (var node in tempProfiledNodesPreviousRun)
                    {
                        ProfiledNodesPreviousRun.Add(node);
                    }
                    ProfiledNodesNotExecuted.Clear();
                    foreach (var node in tempProfiledNodesNotExecuted)
                    {
                        ProfiledNodesNotExecuted.Add(node);
                    }

                    RaisePropertyChanged(nameof(ProfiledNodesCollectionLatestRun));
                    RaisePropertyChanged(nameof(ProfiledNodesCollectionPreviousRun));
                    RaisePropertyChanged(nameof(ProfiledNodesCollectionNotExecuted));

                    ApplyCustomSorting(ProfiledNodesCollectionLatestRun);
                    ApplyCustomSorting(ProfiledNodesCollectionPreviousRun);

                    ProfiledNodesCollectionLatestRun.View?.Refresh();
                    ProfiledNodesCollectionPreviousRun.View?.Refresh();
                    ProfiledNodesCollectionNotExecuted.View?.Refresh();

                    // Update execution time and table visibility
                    UpdateExecutionTime();
                    UpdateTableVisibility();

                    // Clear temporary collections
                    tempProfiledNodesLatestRun = new HashSet<ProfiledNodeViewModel>();
                    tempProfiledNodesPreviousRun = new HashSet<ProfiledNodeViewModel>();
                    tempProfiledNodesNotExecuted = new HashSet<ProfiledNodeViewModel>();
                }, null);
            });            
        }

        /// <summary>
        /// Update execution time rows. These rows are always removed and re-added after each run.
        /// May consider instead, always updating them in the future.
        /// </summary>
        private void UpdateExecutionTime()
        {
            // After each evaluation, manually update execution time column(s)
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
            // Clean the collections from all group and time nodesB
            foreach (var node in groupDictionary.Values)
            {
                RemoveNodeFromState(node, node.State, GetTempCollectionFromState);

                if (groupModelDictionary.TryGetValue(node.GroupGUID, out var groupNodes))
                {
                    groupNodes.Remove(node);
                }
            }
            groupDictionary.Clear();

            // Create group and time nodes for latest and previous runs
            CreateGroupNodesForCollection(tempProfiledNodesLatestRun);
            CreateGroupNodesForCollection(tempProfiledNodesPreviousRun);

            // Create group nodes for not executed 
            var processedNodesNotExecuted = new HashSet<ProfiledNodeViewModel>();

            // Create a copy of ProfiledNodesNotExecuted to iterate over
            var profiledNodesCopy = tempProfiledNodesNotExecuted.ToList();

            foreach (var pNode in profiledNodesCopy)
            {
                if (pNode.GroupGUID != Guid.Empty && !processedNodesNotExecuted.Contains(pNode))
                {
                    // get the other nodes from this group
                    var nodesInGroup = tempProfiledNodesNotExecuted
                        .Where(n => n.GroupGUID == pNode.GroupGUID)
                        .ToList();

                    foreach (var node in nodesInGroup)
                    {
                        processedNodesNotExecuted.Add(node);
                    }

                    // create new group node
                    var pGroup = CreateAndRegisterGroupNode(pNode);
                    tempProfiledNodesNotExecuted.Add(pGroup);
                }
            }
        }

        private void CreateGroupNodesForCollection(HashSet<ProfiledNodeViewModel> collection)
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
                    var pGroup = CreateAndRegisterGroupNode(pNode);
                    pGroup.GroupExecutionOrderNumber = executionCounter++;
                    pGroup.GroupExecutionMilliseconds = groupExecTime;
                    pGroup.GroupModel = CurrentWorkspace.Annotations.First(n => n.GUID.Equals(pNode.GroupGUID));
                    collection.Add(pGroup);

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
                    MoveNodeToTempCollection(profiledNode, tempProfiledNodesLatestRun);
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

                // Detect change of nodes
                if (e.PropertyName == nameof(groupModel.Nodes))
                {
                    var modelNodeGuids = groupModel.Nodes
                        .OfType<NodeModel>()
                        .Select(n => n.GUID)
                        .ToHashSet();

                    // Determine if we adding or removing a node
                    var pNodeToRemove = nodesInGroup
                        .FirstOrDefault(n => !n.IsGroup && !n.IsGroupExecutionTime && !modelNodeGuids.Contains(n.NodeGUID));
                    var pNodeToAdd = nodeDictionary
                        .FirstOrDefault(kvp => modelNodeGuids.Contains(kvp.Key) && !nodesInGroup.Contains(kvp.Value)).Value;

                    var (pNodeModified, addNode) = pNodeToRemove == null ? (pNodeToAdd, true) : (pNodeToRemove, false);

                    // Safety check
                    if (pNodeModified == null) return;

                    var state = pNodeModified.State;
                    collection = GetObservableCollectionFromState(state);

                    // Get all nodes for this group in the same state
                    var allNodesInGroupForState = nodesInGroup.Where(n => n.State == state).ToList();
                    var pGroupToModify = allNodesInGroupForState.FirstOrDefault(n => n.IsGroup);
                    var timeNodeToModify = allNodesInGroupForState.FirstOrDefault(n => n.IsGroupExecutionTime);
                    var pNodesOfSameState = allNodesInGroupForState.Where(n => !n.IsGroupExecutionTime && !n.IsGroup).ToList();

                    // Case REMOVE
                    if (!addNode)
                    {
                        ResetGroupPropertiesAndUnregisterNode(pNodeModified);
                        pNodesOfSameState.Remove(pNodeModified);

                        // check if there are any nodes left of the same state
                        if (!pNodesOfSameState.Any())
                        {
                            // remove the group node and time node from the collection
                            collection.Remove(pGroupToModify);
                            collection.Remove(timeNodeToModify);
                        }
                        else if (state != ProfiledNodeState.NotExecuted && pGroupToModify != null && timeNodeToModify != null)
                        {
                            // Update group execution time
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
                            pGroupToModify = CreateAndRegisterGroupNode(groupModel, collection);
                            pGroupToModify.State = state;

                            timeNodeToModify ??= CreateAndRegisterGroupTimeNode(pGroupToModify);
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
                    UpdateTableVisibility();
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

            RemoveNodeFromState(profiledNode, profiledNode.State, GetObservableCollectionFromState);

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
                var pGroup = CreateAndRegisterGroupNode(group, collection);
                pGroup.State = state;

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
                RemoveNodeFromState(node, node.State, GetObservableCollectionFromState);
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
            // Reset suppression flag
            suppressNodeReset = false;

            // Handle transitions based on the types of the current and previous workspaces
            if (workspace is CustomNodeWorkspaceModel)
            {
                if (previousWorkspace is HomeWorkspaceModel)
                {
                    // Cache data when moving from HomeWorkspace to CustomNodeWorkspace
                    CacheWorkspaceData();
                }
            }
            else if (workspace is HomeWorkspaceModel)
            {
                if (previousWorkspace is CustomNodeWorkspaceModel)
                {
                    // Restore data when moving from CustomNodeWorkspace to HomeWorkspace
                    suppressNodeReset = true;
                    RestoreWorkspaceData();
                }
            }

            // Profiling needs to be enabled per workspace, so mark it false after switching
            isProfilingEnabled = false;

            // Disable IsRecomputeEnabled if the workspace is a CustomNodeWorkspaceModel
            IsRecomputeEnabled = !(workspace is CustomNodeWorkspaceModel);

            // Update the previous and current workspace references
            previousWorkspace = workspace;
            CurrentWorkspace = workspace as HomeWorkspaceModel;
        }

        private void OnCurrentWorkspaceCleared(IWorkspaceModel workspace)
        {
            // Profiling needs to be enabled per workspace so mark it false after closing
            isProfilingEnabled = false;
            suppressNodeReset = false;
            ClearCacheWorkspaceData();
            CurrentWorkspace = viewLoadedParams.CurrentWorkspaceModel as HomeWorkspaceModel;
        }

        #endregion        

        #region Cache

        /// <summary>
        /// Represents cached profiling data for a workspace. Includes node collections and execution times.
        /// </summary>
        private class WorkspaceProfilingData
        {
            /// <summary>
            /// Guid to map graphs with cached data
            /// </summary>
            public Guid GraphGuid { get; set; }
            /// <summary>
            /// Collection to cache nodes executed in the latest run of the graph.
            /// </summary>
            public ObservableCollection<ProfiledNodeViewModel> LatestRunNodes { get; set; } = new();
            /// <summary>
            /// Collection to cache nodes executed in the previous run of the graph.
            /// </summary>
            public ObservableCollection<ProfiledNodeViewModel> PreviousRunNodes { get; set; } = new();
            // <summary>
            /// Collection to cache nodes that were not executed in the graph.
            /// </summary>
            public ObservableCollection<ProfiledNodeViewModel> NotExecutedNodes { get; set; } = new();
            /// <summary>
            /// String to cache the Total execution time for the graph across all runs.
            /// </summary>
            public string TotalGraphExecutionTime { get; set; }
            /// <summary>
            /// String to cache the Execution time for the latest graph run.
            /// </summary>
            public string LatestGraphExecutionTime { get; set; }
            /// <summary>
            /// String to cache the Execution time for the previous graph run.
            /// </summary>
            public string PreviousGraphExecutionTime { get; set; }
        }

        /// <summary>
        /// Caches the current workspace data, including nodes, execution times, and clears old collections.
        /// </summary>
        private void CacheWorkspaceData()
        {
            // Ensure collections are initialized
            if (ProfiledNodesLatestRun == null)
                ProfiledNodesLatestRun = new ObservableCollection<ProfiledNodeViewModel>();
            if (ProfiledNodesPreviousRun == null)
                ProfiledNodesPreviousRun = new ObservableCollection<ProfiledNodeViewModel>();
            if (ProfiledNodesNotExecuted == null)
                ProfiledNodesNotExecuted = new ObservableCollection<ProfiledNodeViewModel>();

            // Save the current data into the cache
            cachedData.GraphGuid = CurrentWorkspace?.Guid ?? Guid.Empty;
            cachedData.LatestRunNodes = new ObservableCollection<ProfiledNodeViewModel>(ProfiledNodesLatestRun);
            cachedData.PreviousRunNodes = new ObservableCollection<ProfiledNodeViewModel>(ProfiledNodesPreviousRun);
            cachedData.NotExecutedNodes = new ObservableCollection<ProfiledNodeViewModel>(ProfiledNodesNotExecuted);
            cachedData.LatestGraphExecutionTime = LatestGraphExecutionTime ?? Resources.Label_DefaultExecutionTime;
            cachedData.PreviousGraphExecutionTime = PreviousGraphExecutionTime ?? Resources.Label_DefaultExecutionTime;
            cachedData.TotalGraphExecutionTime = TotalGraphExecutionTime ?? Resources.Label_DefaultExecutionTime;

            // Clear the old collections
            ProfiledNodesLatestRun.Clear();
            ProfiledNodesPreviousRun.Clear();
            ProfiledNodesNotExecuted.Clear();
            LatestGraphExecutionTime = PreviousGraphExecutionTime = TotalGraphExecutionTime = defaultExecutionTime;

            // Refresh the UI
            RefreshAllCollectionViews();
            UpdateTableVisibility();
        }

        /// <summary>
        /// Restores cached workspace data to the current workspace and updates the UI.
        /// </summary>
        private void RestoreWorkspaceData()
        {
            // Safety check: Ensure cached data is not null
            cachedData.LatestRunNodes ??= new ObservableCollection<ProfiledNodeViewModel>();
            cachedData.PreviousRunNodes ??= new ObservableCollection<ProfiledNodeViewModel>();
            cachedData.NotExecutedNodes ??= new ObservableCollection<ProfiledNodeViewModel>();
            cachedData.LatestGraphExecutionTime ??= Resources.Label_DefaultExecutionTime;
            cachedData.PreviousGraphExecutionTime ??= Resources.Label_DefaultExecutionTime;
            cachedData.TotalGraphExecutionTime ??= Resources.Label_DefaultExecutionTime;

            // Restore cached data
            ProfiledNodesLatestRun = new ObservableCollection<ProfiledNodeViewModel>(cachedData.LatestRunNodes);
            ProfiledNodesPreviousRun = new ObservableCollection<ProfiledNodeViewModel>(cachedData.PreviousRunNodes);
            ProfiledNodesNotExecuted = new ObservableCollection<ProfiledNodeViewModel>(cachedData.NotExecutedNodes);
            LatestGraphExecutionTime = cachedData.LatestGraphExecutionTime;
            PreviousGraphExecutionTime = cachedData.PreviousGraphExecutionTime;
            TotalGraphExecutionTime = cachedData.TotalGraphExecutionTime;

            ClearCacheWorkspaceData();

            // Refresh the UI
            RefreshAllCollectionViews();
            UpdateTableVisibility();
        }

        /// <summary>
        /// Clears the cached workspace data, resetting all collections and execution times to default values.
        /// </summary>
        private void ClearCacheWorkspaceData()
        {
            cachedData.GraphGuid = Guid.Empty;
            cachedData.LatestRunNodes = new ObservableCollection<ProfiledNodeViewModel>();
            cachedData.PreviousRunNodes = new ObservableCollection<ProfiledNodeViewModel>();
            cachedData.NotExecutedNodes = new ObservableCollection<ProfiledNodeViewModel>();
            cachedData.LatestGraphExecutionTime = defaultExecutionTime;
            cachedData.PreviousGraphExecutionTime = defaultExecutionTime;
            cachedData.TotalGraphExecutionTime = defaultExecutionTime;
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

            // Clear temporary collections
            tempProfiledNodesLatestRun = new HashSet<ProfiledNodeViewModel>();
            tempProfiledNodesPreviousRun = new HashSet<ProfiledNodeViewModel>();
            tempProfiledNodesNotExecuted = new HashSet<ProfiledNodeViewModel>();

            // Reset execution time stats
            LatestGraphExecutionTime = PreviousGraphExecutionTime = TotalGraphExecutionTime = defaultExecutionTime;

            // Initialize observable collections and dictionaries
            ProfiledNodesLatestRun = ProfiledNodesLatestRun ?? new ObservableCollection<ProfiledNodeViewModel>();
            ProfiledNodesPreviousRun = ProfiledNodesPreviousRun ?? new ObservableCollection<ProfiledNodeViewModel>();
            ProfiledNodesNotExecuted = ProfiledNodesNotExecuted ?? new ObservableCollection<ProfiledNodeViewModel>();

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
        /// Creates and registers a group node for an AnnotationModel.
        /// Adds it to the specified collection and dictionaries.
        /// </summary>
        private ProfiledNodeViewModel CreateAndRegisterGroupNode(
            AnnotationModel groupModel,
            ObservableCollection<ProfiledNodeViewModel> collection)
        {
            var pGroup = new ProfiledNodeViewModel(groupModel);
            collection.Add(pGroup);
            groupDictionary[pGroup.NodeGUID] = pGroup;
            groupModelDictionary[groupModel.GUID].Add(pGroup);

            return pGroup;
        }

        /// <summary>
        /// Creates and registers a group node based on an existing ProfiledNodeViewModel.
        /// Adding it to group-related dictionaries.
        /// <returns></returns>
        private ProfiledNodeViewModel CreateAndRegisterGroupNode(ProfiledNodeViewModel pNode)
        {
            var pGroup = new ProfiledNodeViewModel(pNode);
            groupDictionary[pGroup.NodeGUID] = pGroup;
            groupModelDictionary[pNode.GroupGUID].Add(pGroup);

            return pGroup;
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
        /// Returns the appropriate ObservableCollection based on the node's profiling state.
        /// </summary>
        private HashSet<ProfiledNodeViewModel> GetTempCollectionFromState(ProfiledNodeState state)
        {
            if (state == ProfiledNodeState.ExecutedOnCurrentRun) return tempProfiledNodesLatestRun;
            else if (state == ProfiledNodeState.ExecutedOnPreviousRun) return tempProfiledNodesPreviousRun;
            else return tempProfiledNodesNotExecuted;
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
            ApplyCustomSorting(ProfiledNodesCollectionNotExecuted, SortByName);
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
        /// Moves a node between HashSets, removing it from all HashSets and adding it to the target HashSet if provided.
        /// </summary>
        private void MoveNodeToTempCollection(ProfiledNodeViewModel profiledNode, HashSet<ProfiledNodeViewModel> targetCollection)
        {
            var collections = new[] { tempProfiledNodesLatestRun, tempProfiledNodesPreviousRun, tempProfiledNodesNotExecuted };

            foreach (var collection in collections)
            {
                collection?.Remove(profiledNode);
            }

            targetCollection?.Add(profiledNode);
        }

        /// <summary>
        /// Removes a node from the appropriate collection based on its state.
        /// </summary>
        private void RemoveNodeFromState<T>(ProfiledNodeViewModel pNode, ProfiledNodeState state, Func<ProfiledNodeState, T> getCollectionFunc) where T : ICollection<ProfiledNodeViewModel>
        {
            var collection = getCollectionFunc(state);
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
            RaisePropertyChanged(nameof(ProfiledNodesCollectionLatestRun));
            RaisePropertyChanged(nameof(ProfiledNodesCollectionPreviousRun));
            RaisePropertyChanged(nameof(ProfiledNodesCollectionNotExecuted));

            ApplyCustomSortingToAllCollections();

            ApplyGroupNodeFilter();
            UpdateTableVisibility();
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
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Windows.Data;
using Dynamo.Core;
using Dynamo.Engine.Profiling;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Workspaces;
using Dynamo.ViewModels;
using Dynamo.Wpf.Extensions;

namespace TuneUp
{
    /// <summary>
    /// Enum of possible states of node profiling data
    /// </summary>
    public enum ProfiledNodeState
    {
        [Display(Name = "Executing")]
        Executing = 0,

        [Display(Name = "Executed On Current Run")]
        ExecutedOnCurrentRun = 1,

        [Display(Name = "Executed On Previous Run")]
        ExecutedOnPreviousRun = 2,

        [Display(Name = "Not Executed")]
        NotExecuted = 3,
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
        private SynchronizationContext uiContext;

        /// <summary>
        /// Name of the row to display current execution time
        /// </summary>
        private string CurrentExecutionString = ProfiledNodeViewModel.ExecutionTimelString + " On Current Run";

        /// <summary>
        /// Name of the row to display previous execution time
        /// </summary>
        private string PreviousExecutionString = ProfiledNodeViewModel.ExecutionTimelString + " On Previous Run";

        /// <summary>
        /// Shortcut to current execution time row
        /// </summary>
        private ProfiledNodeViewModel CurrentExecutionTimeRow => ProfiledNodes.FirstOrDefault(n => n.Name == CurrentExecutionString);

        /// <summary>
        /// Shortcut to previous execution time row
        /// </summary>
        private ProfiledNodeViewModel PreviousExecutionTimeRow => ProfiledNodes.FirstOrDefault(n => n.Name == PreviousExecutionString);

        private HomeWorkspaceModel CurrentWorkspace
        {
            get
            {
                return currentWorkspace;
            }
            set
            {
                // Unsubscribe from old workspace
                if (currentWorkspace != null)
                {
                    UnsubscribeWorkspaceEvents(currentWorkspace);
                }

                // Subscribe to new workspace
                if (value != null)
                {
                    // Set new workspace
                    currentWorkspace = value;
                    SubscribeWorkspaceEvents(currentWorkspace);
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
        /// Collection of profiling data for nodes in the current workspace
        /// </summary>
        public ObservableCollection<ProfiledNodeViewModel> ProfiledNodes { get; set; } = new ObservableCollection<ProfiledNodeViewModel>();

        /// <summary>
        /// Collection of profiling data for nodes in the current workspace.
        /// Profiling data in this collection is grouped by the profiled nodes' states.
        /// </summary>
        public CollectionViewSource ProfiledNodesCollection { get; set; }

        /// <summary>
        /// Total graph execution time
        /// </summary>
        public string TotalGraphExecutiontime
        {
            get
            {
                if (CurrentExecutionTimeRow == null)
                {
                    return "N/A";
                }
                return (PreviousExecutionTimeRow?.ExecutionMilliseconds + CurrentExecutionTimeRow?.ExecutionMilliseconds).ToString() + "ms";
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

        internal void ResetProfiledNodes()
        {
            if (CurrentWorkspace == null)
            {
                return;
            }
            nodeDictionary.Clear();
            ProfiledNodes.Clear();
            foreach (var node in CurrentWorkspace.Nodes)
            {
                var profiledNode = new ProfiledNodeViewModel(node);
                nodeDictionary[node.GUID] = profiledNode;
                ProfiledNodes.Add(profiledNode);
            }

            ProfiledNodesCollection = new CollectionViewSource();
            ProfiledNodesCollection.Source = ProfiledNodes;
            // Sort the data by execution state
            ProfiledNodesCollection.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ProfiledNodeViewModel.StateDescription)));
            ProfiledNodesCollection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.State), ListSortDirection.Ascending));
            ProfiledNodesCollection.View?.Refresh();

            RaisePropertyChanged(nameof(ProfiledNodesCollection));
            RaisePropertyChanged(nameof(ProfiledNodes));
            RaisePropertyChanged(nameof(TotalGraphExecutiontime));
        }

        /// <summary>
        /// The hanlder for force-recompute the graph
        /// </summary>
        internal void ResetProfiling()
        {
            // Put the graph into manual mode as there is no guarantee that nodes will be marked
            // dirty in topologically sorted oreder during a reset.
            CurrentWorkspace.RunSettings.RunType = Dynamo.Models.RunType.Manual;
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
            RaisePropertyChanged(nameof(ProfiledNodesCollection));
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
                node.WasExecutedOnLastRun = false;

                // Update Node state
                if (node.State == ProfiledNodeState.ExecutedOnCurrentRun)
                {
                    node.State = ProfiledNodeState.ExecutedOnPreviousRun;
                }
            }
            executedNodesNum = 0;
            EnableProfiling();
        }

        private void CurrentWorkspaceModel_EvaluationCompleted(object sender, Dynamo.Models.EvaluationCompletedEventArgs e)
        {
            IsRecomputeEnabled = true;
            UpdateExecutionTime();
            RaisePropertyChanged(nameof(ProfiledNodesCollection));
            RaisePropertyChanged(nameof(ProfiledNodes));

            ProfiledNodesCollection.Dispatcher.Invoke(() =>
            {
                ProfiledNodesCollection.SortDescriptions.Clear();
                // Sort nodes into execution group
                ProfiledNodesCollection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.State), ListSortDirection.Ascending));

                // Sort nodes into execution order and make sure Total execution time is always bottom
                ProfiledNodesCollection.SortDescriptions.Add(new SortDescription(nameof(ProfiledNodeViewModel.ExecutionOrderNumber), ListSortDirection.Descending));
                if (ProfiledNodesCollection.View != null)
                    ProfiledNodesCollection.View.Refresh();
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
                {
                    ProfiledNodes.Remove(CurrentExecutionTimeRow);
                    ProfiledNodes.Remove(PreviousExecutionTimeRow);
                    // After each evaluation, manually update execution time column(s)
                    var totalSpanExecuted = new TimeSpan(ProfiledNodes.Where(n => n.WasExecutedOnLastRun).Sum(r => r.ExecutionTime.Ticks));
                    var totalSpanUnexecuted = new TimeSpan(ProfiledNodes.Where(n => !n.WasExecutedOnLastRun).Sum(r => r.ExecutionTime.Ticks));
                    ProfiledNodes.Add(new ProfiledNodeViewModel(
                        CurrentExecutionString, totalSpanExecuted, ProfiledNodeState.ExecutedOnCurrentRun));
                    ProfiledNodes.Add(new ProfiledNodeViewModel(
                        PreviousExecutionString, totalSpanUnexecuted, ProfiledNodeState.ExecutedOnPreviousRun));
                }, null);
            RaisePropertyChanged(nameof(TotalGraphExecutiontime));
        }

        internal void OnNodeExecutionBegin(NodeModel nm)
        {
            var profiledNode = nodeDictionary[nm.GUID];
            profiledNode.State = ProfiledNodeState.Executing;
            RaisePropertyChanged(nameof(ProfiledNodesCollection));
        }

        internal void OnNodeExecutionEnd(NodeModel nm)
        {
            var profiledNode = nodeDictionary[nm.GUID];
            if (executionTimeData != null)
            {
                var executionTime = executionTimeData.NodeExecutionTime(nm);
                if (executionTime != null)
                {
                    profiledNode.ExecutionTime = (TimeSpan)executionTime;
                }
                if (!profiledNode.WasExecutedOnLastRun)
                {
                    profiledNode.ExecutionOrderNumber = executedNodesNum++;
                }
            }
            profiledNode.WasExecutedOnLastRun = true;
            profiledNode.State = ProfiledNodeState.ExecutedOnCurrentRun;
            RaisePropertyChanged(nameof(ProfiledNodesCollection));
        }

        #endregion

        #region Workspace Events

        private void CurrentWorkspaceModel_NodeAdded(NodeModel node)
        {
            var profiledNode = new ProfiledNodeViewModel(node);
            nodeDictionary[node.GUID] = profiledNode;
            node.NodeExecutionBegin += OnNodeExecutionBegin;
            node.NodeExecutionEnd += OnNodeExecutionEnd;
            ProfiledNodes.Add(profiledNode);
            RaisePropertyChanged(nameof(ProfiledNodesCollection));
        }

        private void CurrentWorkspaceModel_NodeRemoved(NodeModel node)
        {
            var profiledNode = nodeDictionary[node.GUID];
            nodeDictionary.Remove(node.GUID);
            node.NodeExecutionBegin -= OnNodeExecutionBegin;
            node.NodeExecutionEnd -= OnNodeExecutionEnd;
            ProfiledNodes.Remove(profiledNode);
            RaisePropertyChanged(nameof(ProfiledNodesCollection));
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
        /// unsubscribe workspace events for profiling
        /// </summary>
        /// <param name="workspace">target workspace</param>
        private void UnsubscribeWorkspaceEvents(HomeWorkspaceModel workspace)
        {
            workspace.NodeAdded -= CurrentWorkspaceModel_NodeAdded;
            workspace.NodeRemoved -= CurrentWorkspaceModel_NodeRemoved;
            workspace.EvaluationStarted -= CurrentWorkspaceModel_EvaluationStarted;
            workspace.EvaluationCompleted -= CurrentWorkspaceModel_EvaluationCompleted;

            foreach (var node in workspace.Nodes)
            {
                node.NodeExecutionBegin -= OnNodeExecutionBegin;
                node.NodeExecutionEnd -= OnNodeExecutionEnd;
            }
            executedNodesNum = 0;
        }

        /// <summary>
        /// When switching workspaces or closing TuneUp extension,
        /// subscribe workspace events for profiling
        /// </summary>
        /// <param name="workspace">target workspace</param>
        private void SubscribeWorkspaceEvents(HomeWorkspaceModel workspace)
        {
            workspace.NodeAdded += CurrentWorkspaceModel_NodeAdded;
            workspace.NodeRemoved += CurrentWorkspaceModel_NodeRemoved;
            workspace.EvaluationStarted += CurrentWorkspaceModel_EvaluationStarted;
            workspace.EvaluationCompleted += CurrentWorkspaceModel_EvaluationCompleted;

            foreach (var node in workspace.Nodes)
            {
                node.NodeExecutionBegin += OnNodeExecutionBegin;
                node.NodeExecutionEnd += OnNodeExecutionEnd;
            }
            ResetProfiledNodes();
            executedNodesNum = 0;
        }

        /// <summary>
        /// ViewModel dispose function
        /// </summary>
        public void Dispose()
        {
            UnsubscribeWorkspaceEvents(CurrentWorkspace);
            viewLoadedParams.CurrentWorkspaceChanged -= OnCurrentWorkspaceChanged;
            viewLoadedParams.CurrentWorkspaceCleared -= OnCurrentWorkspaceCleared;
        }

        #endregion
    }
}

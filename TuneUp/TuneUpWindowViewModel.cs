using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
        Executing = 0,
        ExecutedOnCurrentRun = 1,
        ExecutedOnPreviousRun = 2,
        NotExecuted = 3,
    }

    /// <summary>
    /// ViewModel for TuneUp. 
    /// Handles profiling setup, workspace events, execution events, etc.
    /// </summary>
    public class TuneUpWindowViewModel : NotificationObject, IDisposable
    {
        #region InternalProperties
        private ViewLoadedParams viewLoadedParams;
        private IProfilingExecutionTimeData executionTimeData;
        private int numNodesExecuted;
        private bool profilingEnabled;
        private HomeWorkspaceModel currentWorkspace;
        private Dictionary<Guid, ProfiledNodeViewModel> nodeDictionary;
        internal HomeWorkspaceModel CurrentWorkspace
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
                    currentWorkspace.NodeAdded -= CurrentWorkspaceModel_NodeAdded;
                    currentWorkspace.NodeRemoved -= CurrentWorkspaceModel_NodeRemoved;
                    currentWorkspace.EvaluationStarted -= CurrentWorkspaceModel_EvaluationStarted;
                    currentWorkspace.EvaluationCompleted -= CurrentWorkspaceModel_EvaluationCompleted;

                    foreach (var node in currentWorkspace.Nodes)
                    {
                        node.NodeExecutionBegin -= OnNodeExecutionBegin;
                        node.NodeExecutionEnd -= OnNodeExecutionEnd;
                    }
                }

                // Set new workspace
                currentWorkspace = value;

                // Subscribe to new workspace
                if (currentWorkspace != null)
                {
                    currentWorkspace.NodeAdded += CurrentWorkspaceModel_NodeAdded;
                    currentWorkspace.NodeRemoved += CurrentWorkspaceModel_NodeRemoved;
                    currentWorkspace.EvaluationStarted += CurrentWorkspaceModel_EvaluationStarted;
                    currentWorkspace.EvaluationCompleted += CurrentWorkspaceModel_EvaluationCompleted;

                    foreach (var node in currentWorkspace.Nodes)
                    {
                        node.NodeExecutionBegin += OnNodeExecutionBegin;
                        node.NodeExecutionEnd += OnNodeExecutionEnd;
                    }
                }
            }
        }
        #endregion

        #region PublicProperties

        private bool isRecomputeEnabled = true;
        /// <summary>
        /// Is the recomputeAll button enabled in the UI. Users should not be able to force a 
        /// reset of the engine and rexecution of the graph if one is still ongoing. This causes...trouble.
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
        public ObservableCollection<ProfiledNodeViewModel> ProfiledNodes { get; set; }

        /// <summary>
        /// Collection of profiling data for nodes in the current workspace.
        /// Profiling data in this collection is grouped by the profiled nodes' states.
        /// </summary>
        public CollectionViewSource ProfiledNodesCollection { get; set; }


        #endregion

        #region Constructors

        public TuneUpWindowViewModel(ViewLoadedParams p)
        {
            viewLoadedParams = p;

            p.CurrentWorkspaceChanged += OnCurrentWorkspaceChanged;
            p.CurrentWorkspaceCleared += OnCurrentWorkspaceCleared;

            if (p.CurrentWorkspaceModel is HomeWorkspaceModel)
            {
                CurrentWorkspace = p.CurrentWorkspaceModel as HomeWorkspaceModel;
                ResetProfiledNodes();
            }
        }

        #endregion

        #region ProfilingMethods

        internal void ResetProfiledNodes()
        {
            nodeDictionary = new Dictionary<Guid, ProfiledNodeViewModel>();

            if (CurrentWorkspace == null)
            {
                return;
            }
            ProfiledNodes = new ObservableCollection<ProfiledNodeViewModel>();
            foreach (var node in CurrentWorkspace.Nodes)
            {
                var profiledNode = new ProfiledNodeViewModel(node);
                nodeDictionary[node.GUID] = profiledNode;
                ProfiledNodes.Add(profiledNode);
            }

            ProfiledNodesCollection = new CollectionViewSource();
            ProfiledNodesCollection.Source = ProfiledNodes;

            ProfiledNodesCollection.GroupDescriptions.Add(new PropertyGroupDescription("State"));
            ProfiledNodesCollection.SortDescriptions.Add(new SortDescription("State", ListSortDirection.Ascending));
            ProfiledNodesCollection.LiveSortingProperties.Add("State");
            ProfiledNodesCollection.View.Refresh();

            RaisePropertyChanged(nameof(ProfiledNodesCollection));
            RaisePropertyChanged(nameof(ProfiledNodes));
        }

        internal void ResetProfiling()
        {


            //put the graph into manual mode as there is no guarantee that nodes will be marked dirty in topologically sorted oreder.
            //during a reset.
            CurrentWorkspace.RunSettings.RunType = Dynamo.Models.RunType.Manual;
            //TODO need a way to do this from an extension and not cause a run.//DynamoModel interface or a more specific reset command.
            (viewLoadedParams.DynamoWindow.DataContext as DynamoViewModel).Model.ResetEngine(true);
            // Enable profiling on the new engine controller after the reset.
            CurrentWorkspace.EngineController.EnableProfiling(true, currentWorkspace,currentWorkspace.Nodes);
            //run the graph now that profiling is enabled.
            CurrentWorkspace.Run();

            profilingEnabled = true;
            executionTimeData = CurrentWorkspace.EngineController.ExecutionTimeData;


        }

        internal void EnableProfiling()
        {
            if (!profilingEnabled && CurrentWorkspace != null)
            {
                ResetProfiledNodes();
                CurrentWorkspace.EngineController.EnableProfiling(true, CurrentWorkspace, CurrentWorkspace.Nodes);
                profilingEnabled = true;
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
            numNodesExecuted = 1;
            EnableProfiling();
        }

        private void CurrentWorkspaceModel_EvaluationCompleted(object sender, Dynamo.Models.EvaluationCompletedEventArgs e)
        {
            IsRecomputeEnabled = true;
            RaisePropertyChanged(nameof(ProfiledNodesCollection));
            RaisePropertyChanged(nameof(ProfiledNodes));
            ProfiledNodesCollection.Dispatcher.Invoke(() =>
            {
                ProfiledNodesCollection.SortDescriptions.Clear();
                ProfiledNodesCollection.SortDescriptions.Add(new SortDescription("State", ListSortDirection.Ascending));
                ProfiledNodesCollection.View.Refresh();
            });
            
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
                    profiledNode.ExecutionOrderNumber = numNodesExecuted++;
                }
            }
            profiledNode.WasExecutedOnLastRun = true;
            profiledNode.State = ProfiledNodeState.ExecutedOnCurrentRun;
            RaisePropertyChanged(nameof(ProfiledNodesCollection));
        }

        #endregion

        #region WorkspaceEvents

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
            profilingEnabled = false;
            CurrentWorkspace = workspace as HomeWorkspaceModel;
            ResetProfiledNodes();
        }

        private void OnCurrentWorkspaceCleared(IWorkspaceModel workspace)
        {
            profilingEnabled = false;
            CurrentWorkspace = viewLoadedParams.CurrentWorkspaceModel as HomeWorkspaceModel;
            ResetProfiledNodes();
        }

        #endregion

        #region Dispose

        public void Dispose()
        {
            foreach (var node in CurrentWorkspace.Nodes)
            {
                node.NodeExecutionBegin -= OnNodeExecutionBegin;
                node.NodeExecutionEnd -= OnNodeExecutionEnd;
            }

            CurrentWorkspace.NodeAdded -= CurrentWorkspaceModel_NodeAdded;
            CurrentWorkspace.NodeRemoved -= CurrentWorkspaceModel_NodeRemoved;
            CurrentWorkspace.EvaluationStarted -= CurrentWorkspaceModel_EvaluationStarted;
            CurrentWorkspace.EvaluationCompleted -= CurrentWorkspaceModel_EvaluationCompleted;

            viewLoadedParams.CurrentWorkspaceChanged -= OnCurrentWorkspaceChanged;
            viewLoadedParams.CurrentWorkspaceCleared -= OnCurrentWorkspaceCleared;
        }

        #endregion
    }
}

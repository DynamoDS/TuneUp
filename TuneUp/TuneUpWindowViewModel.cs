using System;
using System.Collections.Generic;
using Dynamo.Core;
using Dynamo.Engine.Profiling;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Workspaces;
using Dynamo.Wpf.Extensions;
using System.Collections.ObjectModel;
using System.Windows.Data;
using System.ComponentModel;

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
                    CurrentWorkspace.EvaluationStarted -= CurrentWorkspaceModel_EvaluationStarted;
                    CurrentWorkspace.EvaluationCompleted -= CurrentWorkspaceModel_EvaluationCompleted;

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
                    CurrentWorkspace.EvaluationStarted += CurrentWorkspaceModel_EvaluationStarted;
                    CurrentWorkspace.EvaluationCompleted += CurrentWorkspaceModel_EvaluationCompleted;

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

        /// <summary>
        /// Collection of profiling data for nodes in the current workspace
        /// </summary>
        public ObservableCollection<ProfiledNodeViewModel> ProfiledNodes
        {
            get
            {
                return new ObservableCollection<ProfiledNodeViewModel>(nodeDictionary.Values);
            }
        }

        /// <summary>
        /// Collection of profiling data for nodes in the current workspace.
        /// Profiling data in this collection is grouped by the profiled nodes' states.
        /// </summary>
        public ListCollectionView ProfiledNodesCollection
        {
            get
            {
                var collection = new ListCollectionView(ProfiledNodes);
                collection.GroupDescriptions.Add(new PropertyGroupDescription("State"));
                collection.SortDescriptions.Add(new SortDescription("StateValue", ListSortDirection.Ascending));
                return collection;
            }
        }

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
            
            foreach (var node in CurrentWorkspace.Nodes)
            {
                var profiledNode = new ProfiledNodeViewModel(node);
                nodeDictionary[node.GUID] = profiledNode;
            }

            RaisePropertyChanged(nameof(ProfiledNodesCollection));
        }

        internal void ResetProfiling()
        {
            // Disable profiling
            CurrentWorkspace.EngineController.EnableProfiling(false, CurrentWorkspace, new List<NodeModel>());
            
            // Enable profiling
            CurrentWorkspace.EngineController.EnableProfiling(true, CurrentWorkspace, CurrentWorkspace.Nodes);
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
            foreach (var node in nodeDictionary.Values)
            {
                // Reset Node Execution Order info
                node.ExecutionOrderNumber = null;
                node.WasExecutedOnLastRun = false;
                node.NumExecutionEndEvents = 0;
                node.NumExecutionStartEvents = 0;

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
            /*foreach (var node in nodeDictionary.Values)
            {
                // Update state of any node that is still in the "Executing" state
                if (node.State == ProfiledNodeState.Executing)
                {
                    node.State = ProfiledNodeState.ExecutedOnCurrentRun;
                }
            }*/
        }

        internal void OnNodeExecutionBegin(NodeModel nm)
        {
            var profiledNode = nodeDictionary[nm.GUID];
            profiledNode.State = ProfiledNodeState.Executing;
            profiledNode.NumExecutionStartEvents++;
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
            profiledNode.NumExecutionEndEvents++;
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
            RaisePropertyChanged(nameof(ProfiledNodesCollection));
        }

        private void CurrentWorkspaceModel_NodeRemoved(NodeModel node)
        {
            var profiledNode = nodeDictionary[node.GUID];
            nodeDictionary.Remove(node.GUID);
            node.NodeExecutionBegin -= OnNodeExecutionBegin;
            node.NodeExecutionEnd -= OnNodeExecutionEnd;
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Timers;
using Dynamo.Core;
using Dynamo.Extensions;
using Dynamo.Engine.Profiling;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Workspaces;
using Dynamo.ViewModels;
using Dynamo.Wpf.Extensions;
using System.Collections.ObjectModel;
using System.Windows.Data;

namespace TuneUp
{
    public enum ProfiledNodeState
    {
        NotProfiled,
        ProfiledOnLastRun,
        ProfiledOnPreviousRun
    }

    public class TuneUpWindowViewModel : NotificationObject, IDisposable
    {
        private ViewLoadedParams viewLoadedParams;
        private IProfilingExecutionTimeData executionTimeData;
        private int numNodesExecuted;
        private bool profilingEnabled;

        private HomeWorkspaceModel currentWorkspace;
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

        public ObservableCollection<ProfiledNodeViewModel> ProfiledNodes
        {
            get
            {
                return new ObservableCollection<ProfiledNodeViewModel>(nodeDictionary.Values);
            }
        }
        
        public ListCollectionView ProfiledNodesCollection
        {
            get
            {
                //return profiledNodesCollection;
                var collection = new ListCollectionView(ProfiledNodes);
                collection.GroupDescriptions.Add(new PropertyGroupDescription("State"));
                return collection;
            }
        }

        private Dictionary<Guid, ProfiledNodeViewModel> nodeDictionary;
        

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

        private void ResetProfiledNodes()
        {
            if (CurrentWorkspace == null)
            {
                return;
            }

            nodeDictionary = new Dictionary<Guid, ProfiledNodeViewModel>();

            foreach (var node in CurrentWorkspace.Nodes)
            {
                var profiledNode = new ProfiledNodeViewModel(node);
                nodeDictionary[node.GUID] = profiledNode;
            }
            RaisePropertyChanged(nameof(ProfiledNodesCollection));
        }

        internal void EnableProfiling()
        {
            /*if (profilingEnabled)
            {
                CurrentWorkspace.EngineController.EnableProfiling(false, CurrentWorkspace, new List<NodeModel>());
            }
            CurrentWorkspace.EngineController.EnableProfiling(true, CurrentWorkspace, CurrentWorkspace.Nodes);*/

            CurrentWorkspace.EngineController.EnableProfiling(true, CurrentWorkspace, new List<NodeModel>());
            profilingEnabled = true;

            executionTimeData = CurrentWorkspace.EngineController.ExecutionTimeData;

            RaisePropertyChanged(nameof(ProfiledNodesCollection));
        }

        private void CurrentWorkspaceModel_EvaluationStarted(object sender, EventArgs e)
        {
            foreach(var node in nodeDictionary.Values)
            {
                node.WasExecutedOnLastRun = false;
                if (node.State == ProfiledNodeState.ProfiledOnLastRun)
                {
                    node.State = ProfiledNodeState.ProfiledOnPreviousRun;
                }
            }
            numNodesExecuted = 1;
            EnableProfiling();
        }

        private void CurrentWorkspaceModel_EvaluationCompleted(object sender, Dynamo.Models.EvaluationCompletedEventArgs e)
        {
        }

        
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
            if (workspace is HomeWorkspaceModel)
            {
                if (profilingEnabled)
                {
                    CurrentWorkspace.EngineController.EnableProfiling(false, CurrentWorkspace, CurrentWorkspace.Nodes);
                }
                CurrentWorkspace = workspace as HomeWorkspaceModel;
                ResetProfiledNodes();
            }
        }

        private void OnCurrentWorkspaceCleared(IWorkspaceModel workspace)
        {
            CurrentWorkspace.EngineController.EnableProfiling(false, CurrentWorkspace, CurrentWorkspace.Nodes);
            CurrentWorkspace = null;
        }

        internal void OnNodeExecutionBegin(NodeModel nm)
        {
        }

        internal void OnNodeExecutionEnd(NodeModel nm)
        {
            var profiledNode = nodeDictionary[nm.GUID];
            if (executionTimeData != null)
            {
                var executionTime = executionTimeData.NodeExecutionTime(nm);
                if (executionTime != null)
                {
                    profiledNode.ExecutionTime = ((TimeSpan)executionTime).ToString("s\\.ffff");
                }
                if (!profiledNode.WasExecutedOnLastRun)
                {
                    profiledNode.ExecutionOrderNumber = numNodesExecuted++;
                }
            }
            profiledNode.WasExecutedOnLastRun = true;
            profiledNode.State = ProfiledNodeState.ProfiledOnLastRun;
            RaisePropertyChanged(nameof(ProfiledNodesCollection));
        }

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
    }
}

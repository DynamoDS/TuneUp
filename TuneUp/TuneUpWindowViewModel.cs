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

namespace TuneUp
{
    public class TuneUpWindowViewModel : NotificationObject, IDisposable
    {
        private ViewLoadedParams viewLoadedParams;
        private IProfilingExecutionTimeData executionTimeData;
        private int numNodesExecuted;

        IWorkspaceModel currentWorkspace;

        public IEnumerable<ProfiledNodeViewModel> ProfiledNodes
        {
            get
            {
                return nodeDictionary.Values;
            }
        }

        private Dictionary<Guid, ProfiledNodeViewModel> nodeDictionary;
        

        public TuneUpWindowViewModel(ViewLoadedParams p)
        {
            viewLoadedParams = p;
            p.CurrentWorkspaceModel.NodeAdded += CurrentWorkspaceModel_NodesChanged;
            p.CurrentWorkspaceModel.NodeRemoved += CurrentWorkspaceModel_NodesChanged;
            p.CurrentWorkspaceChanged += OnCurrentWorkspaceChanged;
            p.CurrentWorkspaceCleared += OnCurrentWorkspaceCleared;
            currentWorkspace = p.CurrentWorkspaceModel;
            nodeDictionary = new Dictionary<Guid, ProfiledNodeViewModel>();
            numNodesExecuted = 0;
        }

        internal void EnableProfiling()
        {
            // Unenable old profiling data
            var nodesToReset = nodeDictionary.Values.Select(v => v.NodeModel);
            if (nodesToReset.Count() > 0)
            {
                (viewLoadedParams.DynamoWindow.DataContext as DynamoViewModel).EngineController.EnableProfiling(false, currentWorkspace as HomeWorkspaceModel, nodesToReset);
            }
            foreach (var node in nodesToReset)
            {
                node.NodeExecutionBegin -= OnNodeExecutionBegin;
                node.NodeExecutionEnd -= OnNodeExecutionEnd;
            }

            // Enable new profiling
            (viewLoadedParams.DynamoWindow.DataContext as DynamoViewModel).EngineController.EnableProfiling(true, currentWorkspace as HomeWorkspaceModel, currentWorkspace.Nodes);
            executionTimeData = (viewLoadedParams.DynamoWindow.DataContext as DynamoViewModel).EngineController.ExecutionTimeData;

            nodeDictionary = new Dictionary<Guid, ProfiledNodeViewModel>();
            numNodesExecuted = 0;
            foreach (var node in currentWorkspace.Nodes)
            {
                node.NodeExecutionBegin += OnNodeExecutionBegin;
                node.NodeExecutionEnd += OnNodeExecutionEnd;
                nodeDictionary[node.GUID] = new ProfiledNodeViewModel(node);
            }
            RaisePropertyChanged(nameof(ProfiledNodes));
        }

        private void CurrentWorkspaceModel_NodesChanged(NodeModel obj)
        {
            EnableProfiling();
        }

        private void OnCurrentWorkspaceChanged(IWorkspaceModel workspace)
        {
            if (currentWorkspace != null)
            {
                currentWorkspace.NodeAdded -= CurrentWorkspaceModel_NodesChanged;
                currentWorkspace.NodeRemoved -= CurrentWorkspaceModel_NodesChanged;

                foreach (var node in currentWorkspace.Nodes)
                {
                    node.NodeExecutionBegin -= OnNodeExecutionBegin;
                    node.NodeExecutionEnd -= OnNodeExecutionEnd;
                }
                nodeDictionary = new Dictionary<Guid, ProfiledNodeViewModel>();
            }
            
            workspace.NodeAdded += CurrentWorkspaceModel_NodesChanged;
            workspace.NodeRemoved += CurrentWorkspaceModel_NodesChanged;
            currentWorkspace = workspace;
            RaisePropertyChanged(nameof(ProfiledNodes));
        }

        private void OnCurrentWorkspaceCleared(IWorkspaceModel workspace)
        {
            workspace.NodeAdded -= CurrentWorkspaceModel_NodesChanged;
            workspace.NodeRemoved -= CurrentWorkspaceModel_NodesChanged;
            RaisePropertyChanged(nameof(ProfiledNodes));
            foreach (var node in currentWorkspace.Nodes)
            {
                node.NodeExecutionBegin -= OnNodeExecutionBegin;
                node.NodeExecutionEnd -= OnNodeExecutionEnd;
            }

            currentWorkspace = null;
            nodeDictionary = new Dictionary<Guid, ProfiledNodeViewModel>();
        }


        internal void OnNodeExecutionBegin(NodeModel nm)
        {
            //Thread.Sleep(500);
        }

        internal void OnNodeExecutionEnd(NodeModel nm)
        {
            if (executionTimeData != null)
            {
                var executionTime = executionTimeData.NodeExecutionTime(nm);
                nodeDictionary[nm.GUID].ExecutionTime = executionTime.ToString();
                nodeDictionary[nm.GUID].ExecutionOrderNumber = numNodesExecuted++;
            }
            
        }

        public void Dispose()
        {
            currentWorkspace.NodeAdded -= CurrentWorkspaceModel_NodesChanged;
            currentWorkspace.NodeRemoved -= CurrentWorkspaceModel_NodesChanged;
            viewLoadedParams.CurrentWorkspaceChanged -= OnCurrentWorkspaceChanged;
        }
    }
}

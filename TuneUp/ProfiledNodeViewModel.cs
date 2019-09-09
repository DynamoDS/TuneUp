using System;
using Dynamo.Core;
using Dynamo.Extensions;
using Dynamo.Graph.Nodes;
namespace TuneUp
{
    public class ProfiledNodeViewModel : NotificationObject
    {
        /// <summary>
        /// The name of this node
        /// </summary>
        public string Name => NodeModel.Name;

        private int executionOrderNumber;
        public int ExecutionOrderNumber
        {
            get
            {
                return executionOrderNumber;
            }
            set
            {
                executionOrderNumber = value;
                RaisePropertyChanged("ExecutionOrderNumber");
            }
        }

        private string executionTime;
        public string ExecutionTime
        {
            get
            {
                return executionTime;
            }
            set
            {
                executionTime = value;
                RaisePropertyChanged("ExecutionTime");
            }
        }

        private bool wasExecutedOnLastRun;
        public bool WasExecutedOnLastRun
        {
            get
            {
                return wasExecutedOnLastRun;
            }
            set
            {
                wasExecutedOnLastRun = value;
                RaisePropertyChanged("WasExecutedOnLastRun");
            }
        }

        private ProfiledNodeState state;
        public ProfiledNodeState State
        {
            get
            {
                return state;
            }
            set
            {
                state = value;
                RaisePropertyChanged("State");
            }
        }

internal NodeModel NodeModel { get; set; }

        /// <summary>
        /// Create a Profiled Node View Model from a NodeModel
        /// </summary>
        /// <param name="node"></param>
        public ProfiledNodeViewModel(NodeModel node)
        {
            NodeModel = node;
            State = ProfiledNodeState.NotProfiled;
        }
        
    }
}

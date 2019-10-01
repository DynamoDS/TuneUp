using System;
using Dynamo.Core;
using Dynamo.Extensions;
using Dynamo.Graph.Nodes;
namespace TuneUp
{
    public class ProfiledNodeViewModel : NotificationObject
    {
        #region Properties

        /// <summary>
        /// The name of this node
        /// </summary>
        public string Name => NodeModel.Name;
        
        /// <summary>
        /// The order number of this node in the most recent graph run.
        /// Returns null if the node was not executed during the most recent graph run.
        /// </summary>
        public int? ExecutionOrderNumber
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
        private int? executionOrderNumber;


        /// <summary>
        /// The most recent execution time of this node
        /// </summary>
        public TimeSpan ExecutionTime
        {
            get
            {
                return executionTime;
            }
            set
            {
                executionTime = value;
                RaisePropertyChanged("ExecutionTime");
                RaisePropertyChanged("ExecutionMilliseconds");
            }
        }
        private TimeSpan executionTime;

        /// <summary>
        /// The most recent execution time of this node in milliseconds
        /// </summary>
        public int ExecutionMilliseconds
        {
            get
            {
                return (int)Math.Round(ExecutionTime.TotalMilliseconds);
            }
        }

        /// <summary>
        /// Indicates whether this node was executed on the most recent graph run
        /// </summary>
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
        private bool wasExecutedOnLastRun;

        /// <summary>
        /// The current profiling state of this node
        /// </summary>
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
                RaisePropertyChanged("StateValue");
            }
        }
        private ProfiledNodeState state;

        /// <summary>
        /// The current profiling state of this node as an integer value
        /// </summary>
        public int StateValue => (int)State;
        
        internal NodeModel NodeModel { get; set; }

        #endregion

        /// <summary>
        /// Create a Profiled Node View Model from a NodeModel
        /// </summary>
        /// <param name="node"></param>
        public ProfiledNodeViewModel(NodeModel node)
        {
            NodeModel = node;
            State = ProfiledNodeState.NotExecuted;
        }
        
    }
}

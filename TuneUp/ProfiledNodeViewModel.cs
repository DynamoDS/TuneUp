using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using Dynamo.Core;
using Dynamo.Graph.Nodes;
namespace TuneUp
{
    public class ProfiledNodeViewModel : NotificationObject
    {
        #region Properties
        /// <summary>
        /// Prefix string of execution time.
        /// </summary>
        public static string ExecutionTimelString = "Execution Time";

        private string name = String.Empty;
        /// <summary>
        /// The name of this profiled node. This value can be either an actual
        /// node name or can be virtually any row you want to append to 
        /// datagrid. See alternative constructor for more details.
        /// </summary>
        public string Name
        {
            get 
            {
                // For virtual row, do not attempt to grab node name
                if (!name.Contains(ExecutionTimelString))
                    name = NodeModel?.Name;
                return name;
            }
            internal set { name = value; }
        }
        
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
                RaisePropertyChanged(nameof(ExecutionOrderNumber));
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
                RaisePropertyChanged(nameof(ExecutionTime));
                RaisePropertyChanged(nameof(ExecutionMilliseconds));
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
                RaisePropertyChanged(nameof(WasExecutedOnLastRun));
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
                RaisePropertyChanged(nameof(State));
            }
        }
        private ProfiledNodeState state;

        /// <summary>
        /// Return the display name of state enum.
        /// Making this identical property because of datagrid binding
        /// </summary>
        public string StateDescription
        {
            get
            {
                return state.GetType()?
                    .GetMember(state.ToString())?
                    .First()?
                    .GetCustomAttribute<DisplayAttribute>()?
                    .Name;
            }
        }

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

        /// <summary>
        /// An alternative constructor which we can customize data for display in Tuneup datagrid
        /// </summary>
        /// <param name="name">row display name</param>
        /// <param name="exTimeSum">execution time in ms</param>
        /// <param name="state">state which determine grouping</param>
        public ProfiledNodeViewModel(string name, TimeSpan exTimeSum, ProfiledNodeState state)
        {
            NodeModel = null;
            this.Name = name;
            this.ExecutionTime = exTimeSum;
            State = state;
        }
    }
}

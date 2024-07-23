using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using Dynamo.Core;
using Dynamo.Graph.Annotations;
using Dynamo.Graph.Nodes;

namespace TuneUp
{
    public class ProfiledNodeViewModel : NotificationObject
    {
        #region Properties
        /// <summary>
        /// Prefix string of execution time.
        /// </summary>
        public static readonly string ExecutionTimelString = "Execution Time";

        public static readonly string GroupNodePrefix = "Group: ";

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
                if (!name.Contains(ExecutionTimelString) && !name.StartsWith(GroupNodePrefix))
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
            get => executionOrderNumber;
            set
            {
                executionOrderNumber = value;
                RaisePropertyChanged(nameof(ExecutionOrderNumber));
            }
        }
        private int? executionOrderNumber;

        /// <summary>
        /// The order number of this group in the most recent graph run.
        /// This number is assigned to each node within the group.
        /// </summary>
        public int? GroupExecutionOrderNumber
        {
            get => groupExecutionOrderNumber;
            set
            {
                groupExecutionOrderNumber = value;
                RaisePropertyChanged(nameof(GroupExecutionOrderNumber));
            }
        }
        private int? groupExecutionOrderNumber;

        /// <summary>
        /// The most recent execution time of this node
        /// </summary>
        public TimeSpan ExecutionTime
        {
            get => executionTime;
            set
            {
                executionTime = value;
                RaisePropertyChanged(nameof(ExecutionTime));
                RaisePropertyChanged(nameof(ExecutionMilliseconds));
            }
        }
        private TimeSpan executionTime;

        /// <summary>
        /// The total execution time of all node in the group.
        /// </summary>
        public TimeSpan GroupExecutionTime
        {
            get => groupExecutionTime;
            set
            {
                groupExecutionTime = value;
                RaisePropertyChanged(nameof(GroupExecutionTime));
            }
        }
        private TimeSpan groupExecutionTime;

        /// <summary>
        /// The most recent execution time of this node in milliseconds
        /// </summary>
        public int ExecutionMilliseconds
        {
            get => (int)Math.Round(ExecutionTime.TotalMilliseconds);
        }

        /// <summary>
        /// Indicates whether this node was executed on the most recent graph run
        /// </summary>
        public bool WasExecutedOnLastRun
        {
            get => wasExecutedOnLastRun;
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
            get => state;
            set
            {
                state = value;
                RaisePropertyChanged(nameof(State));
            }
        }
        private ProfiledNodeState state;

        /// <summary>
        /// The GUID of the group to which this node belongs
        /// </summary>
        public Guid GroupGUID
        {
            get => groupGIUD;
            set
            {
                groupGIUD = value;
                RaisePropertyChanged(nameof(GroupGUID));
            }
        }
        private Guid groupGIUD;

        /// <summary>
        /// The name of the group to which this node belongs
        /// This property is also applied to individual nodes and is used when sorting by name
        /// </summary>
        public string GroupName
        {
            get => groupName;
            set
            {
                groupName = value;
                RaisePropertyChanged(nameof(GroupName));
            }
        }
        private string groupName;

        /// <summary>
        /// Indicates if this node is a group
        /// </summary>
        public bool IsGroup
        {
            get => isGroup;
            set
            {
                isGroup = value;
                RaisePropertyChanged(nameof(IsGroup));
            }
        }
        private bool isGroup;

        /// <summary>
        /// The background brush for this node
        /// If this node represents a group, it inherits the background color from the associated AnnotationModel
        /// </summary>
        public SolidColorBrush BackgroundBrush
        {
            get => backgroundBrush;
            set
            {
                if (value != null)
                {
                    backgroundBrush = value;
                    RaisePropertyChanged(nameof(BackgroundBrush));
                }
            }
        }
        private SolidColorBrush backgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333"));

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

        /// <summary>
        /// The Stopwatch to measure execution time of this node
        /// </summary>
        internal Stopwatch Stopwatch { get; set; }

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
            Stopwatch = new Stopwatch();
        }

        /// <summary>
        /// An alternative constructor which we can customize data for display in TuneUp datagrid
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

        /// <summary>
        /// An alternative constructor to represent an annotation model as a group node.
        /// </summary>
        /// <param name="group">the annotation model</param>
        public ProfiledNodeViewModel(AnnotationModel group)
        {
            NodeModel = null;
            Name = $"{GroupNodePrefix}{group.AnnotationText}";
            GroupName = group.AnnotationText;
            GroupGUID = group.GUID;
            BackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(group.Background));
            IsGroup = true;
            State = ProfiledNodeState.NotExecuted;
        }
    }
}

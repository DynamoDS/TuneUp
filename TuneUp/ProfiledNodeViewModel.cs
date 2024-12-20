﻿using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using Dynamo.Core;
using Dynamo.Graph.Annotations;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Nodes.CustomNodes;
using Dynamo.Graph.Nodes.ZeroTouch;
using TuneUp.Properties;

namespace TuneUp
{
    public class ProfiledNodeViewModel : NotificationObject
    {
        #region Properties

        /// <summary>
        /// Checks if the Node has been Renamed after its creation
        /// </summary>
        public bool IsRenamed
        {
            get
            {
                if (NodeModel == null)
                {
                    return false;
                }
                isRenamed = GetOriginalName(NodeModel) != NodeModel.Name;
                return isRenamed;
            }
            internal set
            {
                if (isRenamed == value) return;
                isRenamed = value;
                RaisePropertyChanged(nameof(IsRenamed));
            }
        }
        private bool isRenamed = false;

        /// <summary>
        /// The original name of the node
        /// </summary>
        public string OriginalName
        {
            get
            {
                return GetOriginalName(NodeModel);
            }
            internal set
            {
                if (originalName == value) return;
                originalName = value;
                RaisePropertyChanged(nameof(OriginalName));
            }
        }
        private string originalName = string.Empty;

        /// <summary>
        /// Indicates whether this node represents the total execution time for its group
        /// </summary>
        public bool IsGroupExecutionTime
        {
            get => isGroupExecutionTime;
            set
            {
                isGroupExecutionTime = value;
                RaisePropertyChanged(nameof(IsGroupExecutionTime));
            }
        }
        private bool isGroupExecutionTime;

        /// <summary>
        /// Prefix string of execution time.
        /// </summary>
        internal static readonly string ExecutionTimelString = Resources.Label_ExecutionTime;
        internal static readonly string GroupNodePrefix = Resources.Label_GroupNodePrefix;
        internal static readonly string GroupExecutionTimeString = Resources.Label_GroupTotalExecutionTime;
        private static readonly string DefaultGroupName = Resources.Label_DefaultGroupName;
        private static readonly string DefaultDisplayGroupName = Resources.Label_DefaultDisplayGroupName;

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
                // For virtual row, do not attempt to grab node or group name if it's already handled
                if (!this.IsGroupExecutionTime)
                {
                    if (NodeModel != null)
                    {
                        return NodeModel.Name;
                    }
                    else if (GroupModel != null)
                    {
                        return GetProfiledGroupName(GroupModel.AnnotationText);
                    }
                }
                return name;
            }
            internal set
            {
                name = value;
                RaisePropertyChanged(nameof(Name));
            }
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
            get => executionMilliseconds;
            set
            {
                executionMilliseconds = value;
                RaisePropertyChanged(nameof(ExecutionMilliseconds));
            }
        }
        private int executionMilliseconds;

        /// <summary>
        /// The total execution time of this group in milliseconds
        /// </summary>
        public int GroupExecutionMilliseconds
        {
            get => groupExecutionMilliseconds;
            set
            {
                groupExecutionMilliseconds = value;
                RaisePropertyChanged(nameof(GroupExecutionMilliseconds));
            }
        }
        private int groupExecutionMilliseconds;

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
        /// The GUID of this node
        /// </summary>
        public Guid NodeGUID
        {
            get => nodeGIUD;
            set
            {
                nodeGIUD = value;
                RaisePropertyChanged(nameof(NodeGUID));
            }
        }
        private Guid nodeGIUD;

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

        public bool ShowGroupIndicator
        {
            get => showGroupIndicator;
            set
            {
                showGroupIndicator = value;
                RaisePropertyChanged(nameof(ShowGroupIndicator));
            }
        }
        private bool showGroupIndicator;

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

        internal AnnotationModel GroupModel { get; set; }

        #endregion

        /// <summary>
        /// Getting the original name before graph author renamed the node
        /// </summary>
        /// <param name="node">target NodeModel</param>
        /// <returns>Original node name as string</returns>
        private static string GetOriginalName(NodeModel node)
        {
            if (node == null) return string.Empty;
            // For dummy node, return the current name so that does not appear to be renamed
            if (node is DummyNode)
            {
                return node.Name;
            }
            if (node.IsCustomFunction)
            {
                // If the custom node is not loaded, return the current name so that does not appear to be renamed
                if ((node as Function).State == ElementState.Error && (node as Function).Definition.IsProxy)
                {
                    return node.Name;
                }
                // If the custom node is loaded, return original name as usual
                var customNodeFunction = node as Function;
                return customNodeFunction?.Definition.DisplayName;
            }

            var function = node as DSFunctionBase;
            if (function != null)
                return function.Controller.Definition.DisplayName;

            var nodeType = node.GetType();
            var elNameAttrib = nodeType.GetCustomAttributes<NodeNameAttribute>(false).FirstOrDefault();
            if (elNameAttrib != null)
                return elNameAttrib.Name;

            return nodeType.FullName;
        }

        /// <summary>
        /// Create a Profiled Node View Model from a NodeModel
        /// </summary>
        /// <param name="node"></param>
        public ProfiledNodeViewModel(NodeModel node)
        {
            NodeModel = node;
            State = ProfiledNodeState.NotExecuted;
            Stopwatch = new Stopwatch();
            NodeGUID = node.GUID;
        }

        /// <summary>
        /// An alternative constructor which we can customize data for display in TuneUp datagrid
        /// </summary>
        /// <param name="name">row display name</param>
        /// <param name="state">state which determine grouping</param>
        public ProfiledNodeViewModel(string name, ProfiledNodeState state)
        {
            this.Name = name;
            State = state;
            NodeGUID = Guid.NewGuid();
            IsGroupExecutionTime = true;
        }

        /// <summary>
        /// An alternative constructor to represent an annotation model as a group node.
        /// </summary>
        /// <param name="group">the annotation model</param>
        public ProfiledNodeViewModel(AnnotationModel group)
        {
            GroupModel = group;
            GroupName = group.AnnotationText;
            GroupGUID = group.GUID;
            BackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(group.Background));
            State = ProfiledNodeState.NotExecuted;
            ShowGroupIndicator = true;
            NodeGUID = Guid.NewGuid();
            IsGroup = true;
        }

        /// <summary>
        /// An alternative constructor to create a group or time node from another profiled node.
        /// </summary>
        /// <param name="group">the annotation model</param>
        public ProfiledNodeViewModel(ProfiledNodeViewModel pNode)
        {
            Name = GetProfiledGroupName(pNode.GroupName);
            GroupName = pNode.GroupName;
            State = pNode.State;
            NodeGUID = Guid.NewGuid();
            GroupGUID = pNode.GroupGUID;
            IsGroup = true;
            BackgroundBrush = pNode.BackgroundBrush;
            ShowGroupIndicator = true;
        }

        /// <summary>
        /// Returns the formatted profiled group name with the group prefix.
        /// Uses a default display name if the group name matches the default.
        /// </summary>
        public static string GetProfiledGroupName(string groupName)
        {
            return groupName == DefaultGroupName
                ? $"{GroupNodePrefix}{DefaultDisplayGroupName}"
                : $"{GroupNodePrefix}{groupName}";
        }
    }
}

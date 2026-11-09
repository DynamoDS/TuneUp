using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Dynamo.Graph.Workspaces;
using Dynamo.Models;
using DynamoCoreWpfTests.Utility;
using NUnit.Framework;
using SystemTestServices;
using TuneUp;

namespace TuneUpTests
{
    public class TuneUpTests : SystemTestBase
    {
        protected override void GetLibrariesToPreload(List<string> libraries)
        {
            libraries.Add("ProtoGeometry.dll");
            libraries.Add("DSCoreNodes.dll");
            libraries.Add("GeometryColor.dll");
            libraries.Add("VMDataBridge.dll");
            base.GetLibrariesToPreload(libraries);
        }

        /// <summary>
        /// Returns the repo's test folder, which holds the test graphs. GetTestDirectory assumes a fixed
        /// depth below it, which no longer matches the bin\Release\net10.0-windows output folder.
        /// </summary>
        private string GetTuneUpTestDirectory()
        {
            for (var dir = new DirectoryInfo(ExecutingDirectory); dir != null; dir = dir.Parent)
            {
                var testDir = Path.Combine(dir.FullName, "test");
                if (File.Exists(Path.Combine(testDir, "CBPointPointLine.dyn"))) return testDir;
            }
            return GetTestDirectory(ExecutingDirectory);
        }

        internal TuneUpViewExtension GetTuneUpViewExtension()
        {
            DispatcherUtil.DoEvents();
            var tuneUpVE = GetViewExtensionsByType<TuneUpViewExtension>().FirstOrDefault();
            return tuneUpVE as TuneUpViewExtension;
        }

        [Test, Apartment(ApartmentState.STA)]
        public void TuneUpCreatesProfilingDataForEveryNodeInWorkspace()
        {
            // Open test graph
            var testDir = GetTuneUpTestDirectory();
            var filepath = Path.Combine(testDir, "CBPointPointLine.dyn");
            OpenDynamoDefinition(filepath);

            // Get TuneUp view extension
            var tuneUpVE = GetTuneUpViewExtension();

            // Open TuneUp
            // The menu item is checkable, and a raised Click doesn't toggle it, so check it first
            tuneUpVE.TuneUpMenuItem.IsChecked = true;
            tuneUpVE.TuneUpMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            DispatcherUtil.DoEvents();

            var homespace = Model.CurrentWorkspace as HomeWorkspaceModel;
            var nodes = homespace.Nodes;

            // Assert there is a ProfiledNodeViewModel for every node in the graph
            var profiledNodes = tuneUpVE.ViewModel.ProfiledNodesNotExecuted;
            foreach (var node in nodes)
            {
                Assert.Contains(node.GUID, profiledNodes.Select(n => n.NodeModel.GUID).ToList());
            }

            RunCurrentModel();
            DispatcherUtil.DoEvents();
            foreach (var node in profiledNodes)
            {
                Assert.IsNotNull(node.ExecutionOrderNumber);
            }
        }

        [Test, Apartment(ApartmentState.STA)]
        public void TuneUpMaintainsProfiledNodeState()
        {
            // Open test graph
            var testDir = GetTuneUpTestDirectory();
            var filepath = Path.Combine(testDir, "CBPointPointLine.dyn");
            OpenDynamoDefinition(filepath);

            // Get TuneUp view extension
            var tuneUpVE = GetTuneUpViewExtension();

            // Open TuneUp
            // The menu item is checkable, and a raised Click doesn't toggle it, so check it first
            tuneUpVE.TuneUpMenuItem.IsChecked = true;
            tuneUpVE.TuneUpMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            DispatcherUtil.DoEvents();

            // Assert all node states are NotExecuted before graph run
            var profiledNodes = tuneUpVE.ViewModel.ProfiledNodesNotExecuted;
            foreach (var node in profiledNodes)
            {
                Assert.AreEqual(ProfiledNodeState.NotExecuted, node.State);
            }

            // Run graph and assert state is ExecutedOnCurrentRun
            RunCurrentModel();
            DispatcherUtil.DoEvents();
            foreach (var node in profiledNodes)
            {
                Assert.AreEqual(ProfiledNodeState.ExecutedOnCurrentRun, node.State);
            }

            // Mark downstream node as modified so that it gets reexecuted on the next graph run
            var modifiedNodeID = new Guid("1e49be233be846688122ac48d70ce961");
            var homespace = Model.CurrentWorkspace as HomeWorkspaceModel;
            homespace.Nodes.Where(n => n.GUID == modifiedNodeID).First().MarkNodeAsModified(true);

            // Run graph, and assert modified node's state is ExecutedOnCurrentRun; assert other nodes are ExecutedOnPreviousRun
            RunCurrentModel();
            DispatcherUtil.DoEvents();
            foreach (var node in profiledNodes)
            {
                if (node.NodeModel.GUID == modifiedNodeID)
                {
                    Assert.AreEqual(ProfiledNodeState.ExecutedOnCurrentRun, node.State);
                }
                else
                {
                    Assert.AreEqual(ProfiledNodeState.ExecutedOnPreviousRun, node.State);
                }
            }

            // Force Re-execute and assert all node states are ExecutedOnCurrentRun
            tuneUpVE.ViewModel.ResetProfiling();
            DispatcherUtil.DoEvents();
            foreach (var node in profiledNodes)
            {
                Assert.AreEqual(ProfiledNodeState.ExecutedOnCurrentRun, node.State);
            }
        }

        [Test, Apartment(ApartmentState.STA)]
        public void TuneUpDeterminesCorrectNodeExecutionOrder()
        {
            // Expected execution order
            var executionOrderDict = new Dictionary<Guid, int>()
            {
                { new Guid("84851304e69f452f8cdabc1be4898880"), 1 }, // CodeBlock
                { new Guid("929a63211eef44639173531bff38f723"), 2 }, // Point.ByCoordinates
                { new Guid("2194fee1c7374aae9694e490854f70f8"), 3 }, // Point.ByCoordinates
                { new Guid("1e49be233be846688122ac48d70ce961"), 4 }, // Line.ByStartPointEndPoint
            };

            // Open test graph
            var testDir = GetTuneUpTestDirectory();
            var filepath = Path.Combine(testDir, "CBPointPointLine.dyn");
            OpenDynamoDefinition(filepath);

            // Get TuneUp view extension
            var tuneUpVE = GetTuneUpViewExtension();

            // Open TuneUp
            // The menu item is checkable, and a raised Click doesn't toggle it, so check it first
            tuneUpVE.TuneUpMenuItem.IsChecked = true;
            tuneUpVE.TuneUpMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            DispatcherUtil.DoEvents();

            // Assert all node execution order numbers are null
            var profiledNodes = tuneUpVE.ViewModel.ProfiledNodesNotExecuted;
            foreach (var node in profiledNodes)
            {
                Assert.IsNull(node.ExecutionOrderNumber);
            }

            // Run graph and assert execution order numbers are correct
            RunCurrentModel();
            DispatcherUtil.DoEvents();
            foreach (var node in profiledNodes)
            {
                var expected = executionOrderDict[node.NodeModel.GUID];
                Assert.AreEqual(expected, node.ExecutionOrderNumber);
            }
        }

        [Test, Apartment(ApartmentState.STA)]
        public void TuneUpCopiedRowsIncludeNodeIds()
        {
            // Open test graph
            var testDir = GetTuneUpTestDirectory();
            var filepath = Path.Combine(testDir, "CBPointPointLine.dyn");
            OpenDynamoDefinition(filepath);

            // Get TuneUp view extension
            var tuneUpVE = GetTuneUpViewExtension();

            // Open TuneUp and run the graph
            // The menu item is checkable, and a raised Click doesn't toggle it, so check it first
            tuneUpVE.TuneUpMenuItem.IsChecked = true;
            tuneUpVE.TuneUpMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            DispatcherUtil.DoEvents();
            RunCurrentModel();
            DispatcherUtil.DoEvents();

            var profiledNodes = tuneUpVE.ViewModel.ProfiledNodesLatestRun.Where(n => n.NodeModel != null).ToList();
            Assert.IsNotEmpty(profiledNodes);

            var lines = tuneUpVE.ViewModel.GetRowsAsTabSeparatedText(profiledNodes)
                .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

            // Header line, then one tab-separated line per row ending with the node's GUID
            Assert.AreEqual(profiledNodes.Count + 1, lines.Length);
            Assert.AreEqual(TuneUp.Properties.Resources.Header_ID, lines[0].Split('	').Last());
            for (var i = 0; i < profiledNodes.Count; i++)
            {
                var fields = lines[i + 1].Split('	');
                Assert.AreEqual(4, fields.Length);
                Assert.AreEqual(profiledNodes[i].NodeModel.Name, fields[1]);
                Assert.AreEqual(profiledNodes[i].NodeModel.GUID.ToString(), fields[3]);
            }
        }

        [Test, Apartment(ApartmentState.STA)]
        public void TuneUpCopiedRowsReplaceTabsAndLineBreaksInNames()
        {
            // Open test graph
            var testDir = GetTuneUpTestDirectory();
            var filepath = Path.Combine(testDir, "CBPointPointLine.dyn");
            OpenDynamoDefinition(filepath);

            // Rename a node so its name contains a tab and line breaks
            var renamedNodeID = new Guid("1e49be233be846688122ac48d70ce961");
            var renamedNode = Model.CurrentWorkspace.Nodes.First(n => n.GUID == renamedNodeID);
            renamedNode.Name = "Line\tfrom\r\ntwo\npoints";

            // Get TuneUp view extension
            var tuneUpVE = GetTuneUpViewExtension();

            // Open TuneUp and run the graph
            // The menu item is checkable, and a raised Click doesn't toggle it, so check it first
            tuneUpVE.TuneUpMenuItem.IsChecked = true;
            tuneUpVE.TuneUpMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            DispatcherUtil.DoEvents();
            RunCurrentModel();
            DispatcherUtil.DoEvents();

            var profiledNodes = tuneUpVE.ViewModel.ProfiledNodesLatestRun.Where(n => n.NodeModel != null).ToList();
            var renamedRow = profiledNodes.IndexOf(profiledNodes.First(n => n.NodeModel.GUID == renamedNodeID));

            var lines = tuneUpVE.ViewModel.GetRowsAsTabSeparatedText(profiledNodes)
                .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

            // The name can't add lines or columns: each becomes a space
            Assert.AreEqual(profiledNodes.Count + 1, lines.Length);
            foreach (var line in lines.Skip(1))
            {
                Assert.AreEqual(4, line.Split('\t').Length);
            }

            var fields = lines[renamedRow + 1].Split('\t');
            Assert.AreEqual("Line from two points", fields[1]);
            Assert.AreEqual(renamedNodeID.ToString(), fields[3]);
        }

        [Test, Apartment(ApartmentState.STA)]
        public void TuneUpCopiedGroupRowsIncludeGroupIds()
        {
            // Open test graph
            var testDir = GetTuneUpTestDirectory();
            var filepath = Path.Combine(testDir, "CBPointPointLine.dyn");
            OpenDynamoDefinition(filepath);

            // Group the two Point.ByCoordinates nodes. TuneUp reads groups when it starts profiling,
            // so the group has to exist before TuneUp opens.
            var pointNodeIDs = new[]
            {
                new Guid("929a63211eef44639173531bff38f723"),
                new Guid("2194fee1c7374aae9694e490854f70f8"),
            };
            Model.ExecuteCommand(new DynamoModel.SelectModelCommand(pointNodeIDs, Dynamo.Utilities.ModifierKeys.None));
            Model.ExecuteCommand(new DynamoModel.CreateAnnotationCommand(Guid.NewGuid(), "Points", string.Empty, 0, 0, true));
            var group = Model.CurrentWorkspace.Annotations.Single();

            // Get TuneUp view extension
            var tuneUpVE = GetTuneUpViewExtension();

            // Open TuneUp with groups shown and run the graph
            // The menu item is checkable, and a raised Click doesn't toggle it, so check it first
            tuneUpVE.TuneUpMenuItem.IsChecked = true;
            tuneUpVE.TuneUpMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            DispatcherUtil.DoEvents();
            tuneUpVE.ViewModel.ShowGroups = true;
            RunCurrentModel();
            DispatcherUtil.DoEvents();

            var groupRows = tuneUpVE.ViewModel.ProfiledNodesLatestRun.Where(n => n.GroupGUID == group.GUID).ToList();
            var groupRow = groupRows.Single(n => n.IsGroup);
            var groupTimeRow = groupRows.Single(n => n.IsGroupExecutionTime);

            var lines = tuneUpVE.ViewModel.GetRowsAsTabSeparatedText(new[] { groupRow, groupTimeRow })
                .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

            // The group row's ID is the group's GUID. The group's execution time row doesn't
            // represent a node or group, so its ID is empty.
            Assert.AreEqual(3, lines.Length);
            Assert.AreEqual(group.GUID.ToString(), lines[1].Split('\t')[3]);
            Assert.AreEqual(string.Empty, lines[2].Split('\t')[3]);
        }
    }
}

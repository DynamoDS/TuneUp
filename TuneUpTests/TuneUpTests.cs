using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Dynamo.Graph.Workspaces;
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
            var testDir = GetTestDirectory(ExecutingDirectory);
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
    }
}

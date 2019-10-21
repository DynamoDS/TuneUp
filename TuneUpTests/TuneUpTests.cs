using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

using Dynamo.Configuration;
using Dynamo.Extensions;
using Dynamo.Models;
using Dynamo.Graph.Workspaces;
using Dynamo.Scheduler;
using SystemTestServices;

using TuneUp;
using TestServices;
using DynamoCoreWpfTests.Utility;
using System.Windows;
using System.Windows.Controls;

namespace TuneUpTests
{
    public class TuneUpTests : SystemTestBase
    {
        protected override void GetLibrariesToPreload(List<string> libraries)
        {
            libraries.Add("ProtoGeometry.dll");
            libraries.Add("DSCoreNodes.dll");
            libraries.Add("GeometryColor.dll");
            base.GetLibrariesToPreload(libraries);
        }

        internal TuneUpViewExtension GetTuneUpViewExtension()
        {
            DispatcherUtil.DoEvents();
            var viewExtensions = GetViewExtensionManager().ViewExtensions;
            var tuneUpVE = viewExtensions.Where(e => e.Name.Equals("TuneUp")).FirstOrDefault() as TuneUpViewExtension;
            return tuneUpVE;
        }

        [Test, RequiresSTA]
        public void TuneUpCreatesProfilingDataForEveryNodeInWorkspace()
        {
            // Open test graph
            var testDir = GetTestDirectory(ExecutingDirectory);
            var filepath = Path.Combine(testDir, "CBPointPointLine.dyn");
            OpenDynamoDefinition(filepath);

            // Get TuneUp view extension
            var tuneUpVE = GetTuneUpViewExtension();

            // Open TuneUp
            tuneUpVE.TuneUpMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            
            var homespace = Model.CurrentWorkspace as HomeWorkspaceModel;
            var nodes = homespace.Nodes;

            // Assert there is a ProfiledNodeViewModel for every node in the graph
            var profiledNodes = tuneUpVE.ViewModel.ProfiledNodes;
            foreach (var node in nodes)
            {
                Assert.Contains(node.GUID, profiledNodes.Select(n => n.NodeModel.GUID).ToList());
            }
        }
    }
}

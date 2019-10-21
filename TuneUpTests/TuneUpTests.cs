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

        protected override void StartDynamo(TestSessionConfiguration testConfig)
        {
            base.StartDynamo(testConfig);
        }

        [Test, RequiresSTA]
        public void TuneUpCreatesProfilingDataForEveryNodeInWorkspace()
        {
            var testDir = GetTestDirectory(ExecutingDirectory);
            var filepath = Path.Combine(testDir, "CBPointPointLine.dyn");
            OpenDynamoDefinition(filepath);

            var homespace = Model.CurrentWorkspace as HomeWorkspaceModel;
            var nodes = homespace.Nodes;

            DispatcherUtil.DoEvents();
            var viewExtensions = GetViewExtensionManager().ViewExtensions;

            //Model.ExtensionManager.ExtensionLoader.Load("C:\\Users\\t_mitcs\\Repos\\TuneUp\\TuneUp\\dist\\TuneUp\\extra\\TuneUp_ViewExtensionDefinition.xml");
            var tuneUpVE = viewExtensions.Where(e => e.Name.Equals("TuneUp")).FirstOrDefault();

            DispatcherUtil.DoEvents();
            (tuneUpVE as TuneUpViewExtension).TuneUpMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            DispatcherUtil.DoEvents();

        }
    }
}

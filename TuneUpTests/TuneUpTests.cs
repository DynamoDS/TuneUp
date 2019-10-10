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

        [Test, RequiresSTA]
        public void TuneUpCreatesProfilingDataForEveryNodeInWorkspace()
        {
            var testDir = GetTestDirectory(ExecutingDirectory);
            var filepath = Path.Combine(testDir, "CBPointPointLine.dyn");
            OpenDynamoDefinition(filepath);

            var homespace = Model.CurrentWorkspace as HomeWorkspaceModel;
            var nodes = homespace.Nodes;

            var viewExtensions = GetViewExtensionManager().ViewExtensions;

            //Model.ExtensionManager.ExtensionLoader.Load("C:\\Users\\t_mitcs\\Repos\\TuneUp\\TuneUp\\dist\\TuneUp\\extra\\TuneUp_ViewExtensionDefinition.xml");
            var tu = Model.ExtensionManager.Extensions;//.Where(e => e is TuneUpViewExtension);
        }
    }
}

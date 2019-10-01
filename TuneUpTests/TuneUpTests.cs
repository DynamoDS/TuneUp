using System;
using System.IO;
using NUnit.Framework;
using SystemTestServices;

using Dynamo.Models;
using Dynamo.Graph.Workspaces;


namespace TuneUpTests
{
    public class TuneUpTests : SystemTestBase
    {
        [Test, RequiresSTA]
        public void TuneUpCreatesProfilingDataForEveryNodeInWorkspace()
        {
            var homespace = Model.CurrentWorkspace as HomeWorkspaceModel;
        }
    }
}

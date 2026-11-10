# TuneUp

[![Build Status](https://c007.cloudbees-ci.autodesk.com/buildStatus/icon?job=DYNCI%2FDynamo%2FTuneUp%2Fmaster)](https://c007.cloudbees-ci.autodesk.com/job/DYNCI/job/Dynamo/job/TuneUp/job/master/)
![Dynamic JSON Badge](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fraw.githubusercontent.com%2FDynamoDS%2FTuneUp%2Frefs%2Fheads%2Fmaster%2FTuneUp%2Fmanifests%2Fpkg.json&query=%24.version&logo=autodesk&label=dpm)
[![Build](https://github.com/DynamoDS/TuneUp/actions/workflows/build.yml/badge.svg)](https://github.com/DynamoDS/TuneUp/actions/workflows/build.yml)

TuneUp is in `beta`.

TuneUp is a view extension for analyzing the performance of Dynamo graphs. TuneUp allows you to see overall graph execution time, per-node execution time, and other helpful information about what's happening under the hood, e.g. nodes run in the current execution v.s. nodes run in the previous execution (which were skipped during the most recent graph run for optimization/ caching).

Here is a short demo of how to utilize it as of now (now loading from the Extensions menu):
![TuneUp](design/gifs/TuneUpScroll.gif)

Here is a mock up of the future design:
![TuneUp](design/images/TuneUp_Mockup_03_SortedByExecutionOrder.jpg?raw=true "TuneUp")

## Building

### Recommended Build Environment

- VisualStudio 2022 or later
- .NET 8.0 Developer Pack
- Dynamo repository cloned and built on the same level of TuneUp repository which means your Dynamo repo and TuneUp repo should exist under the same parent folder.

### Result Binaries

- After a `Debug` build of Tuneup one can expect:
  - Under `TuneUp\dist\TuneUp`, there is a sample package wrapped up ready for publishing and adoption. This would be the un-optimized version.
  - Un-optimized package installed locally for [DynamoVersion] defined in TuneUp/TuneUp.csproj, under DynamoCore and DynamoRevit
- After a `Release` build of Tuneup one can expect:
Under `TuneUp\dist\TuneUp`, there is a sample package wrapped up ready for publishing and adoption. This would be the optimized version.

## Known issues

- TuneUp does not work with .dyfs (custom nodes) yet.
- TuneUp binaries are not semantically versioned and are not intended to be built on top of as an API. Do not treat these binaries like DynamoCore.
- TuneUp requires Dynamo 3.0 or higher for access to new extension APIs.
- When user have TuneUp open, after switching workspace in Dynamo, the first graph run does not give execution time and nodes order.
- Although it's not an issue by itself, TuneUp profiles the execution of graphs even if not showing on the extension bar.
- In some cases TuneUp may calculate incorrect execution times for nodes, we cannot reproduce this consistently, if you see this occur and can reproduce it please file an issue!

## Testing

### Setup

Please check out known issues before trying to setup testing.

- TuneUp targets .NET 10 and builds against the Dynamo 4.3 NuGet packages (`DynamoPackageVersion` in `Directory.Build.props`, the only place it is set), the latest released Dynamo. It ships built in to Dynamo 4.4. Use a Dynamo 4.x build to test it.
- Download a Dynamo 4.x DynamoCoreRuntime from [dynamobuilds.com](https://dynamobuilds.com/), or build Dynamo from the Dynamo repository and use its `bin\AnyCPU\Release` folder.
- Copy all contents of the Dynamo runtime to `TuneUp\TuneUpTests\bin\Release\net10.0-windows\` (or `Debug\net10.0-windows\`), except the `Built-In Packages\packages\TuneUp` folder and the NUnit and test platform files (`nunit*`, `NUnit3*`, `testhost*`, `Microsoft.TestPlatform*`, `Microsoft.VisualStudio.TestPlatform*`), so the copy doesn't replace the TuneUp build or the test runner.
- Copy `TuneUp_ViewExtensionDefinition.xml` from `TuneUp\TuneUp\manifests\` to `TuneUp\TuneUpTests\bin\Release\net10.0-windows\viewExtensions\`
- Open the copied `TuneUp_ViewExtensionDefinition.xml` and change the assembly path to `..\TuneUp.dll`
- Remove `TuneUp` from your Dynamo packages folder if you have it installed from package manager (otherwise `TuneUp.dll` will get loaded twice). This won't work well.
- Launch DynamoSandbox.exe, then click `View-> Open Tune Up` and use while a graph runs.

### Running TuneUp Unit Tests

- Install NUnit3TestAdapter from VisualStudio->Extensions->Manage Extensions->Online.
- Open Test Explorer from VisualStudio->Test->Test Explorer. Now you should see a list of TuneUpTests.
- Click the target test to run or run them all.
- Or from the command line: `dotnet test TuneUpTests\bin\Release\net10.0-windows\TuneUpTests.dll`. All tests run on one STA thread (`TuneUpTests\Properties\AssemblyInfo.cs`), as Dynamo's own WPF tests do.

### How to publish a new version

Releases are built from the internal mirror, and CILibrary opens the version bump PR against this public repository automatically (`create_pr_release_to_public_master` in `pipeline.yml`).

- Make sure the internal repository's master is in sync with public master
- Create a release branch on the internal repository, named for the bump type (e.g. `release/patch-7`, `release/minor-1`)
- Build the branch to publish. CILibrary bumps the version in `pipeline.yml`, publishes that version, then opens a PR on the public repository with the bump
- Review and merge that PR, so public master matches the published version
- Delete the release branch on the internal repository

The version in `pipeline.yml` must always be the latest published version; the release build adds one to it. If the automatic PR is not opened, create it manually with the updated version number in the pipeline file.

![](https://github.com/DynamoDS/TuneUp/workflows/TuneUp-Build/badge.svg)

# TuneUp

TuneUp is in `beta`.

TuneUp is a view extension for analyzing the performance of Dynamo graphs. TuneUp allows you to see overall graph execution time, per-node execution time, and other helpful information about what's happening under the hood, e.g. nodes run in the current execution v.s. nodes run in the previous execution (which were skipped during the most recent graph run for optimization/ caching).

Here is a short demo of how to utilize it as of now:
![TuneUp](design/gifs/TuneUpScroll.gif)

Here is a mock up of the future design:
![Alt text](design/images/TuneUp_Mockup_03_SortedByExecutionOrder.jpg?raw=true "TuneUp")

## Building
### Recommended Build Environment
- VisualStudio 2019
- .Net Framework 4.7 Developer Pack
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
- TuneUp requires Dynamo 2.5 or higher for access to new extension APIs.
- When user have TuneUp open, after switching workspace in Dynamo, the first graph run does not give execution time and nodes order.

## Testing

### Setup
Please check out known issues before trying to setup testing.

- Download DynamoCoreRuntime 2.5.0 (or higher) from https://dynamobuilds.com/. Alternatively, you can build Dynamo from Dynamo repository and use the bin folder equivalently.
- Copy all contents of the DynamoCoreRuntime to `TuneUp\TuneUpTests\bin\Debug\`. If you are building Dynamo locally, copy all contents of Dynamo from `Dynamo/bin/AnyCPU/Debug` to `TuneUp\TuneUpTests\bin\Debug\`
- Copy `TuneUp_ViewExtensionDefinition.xml` from `TuneUp\TuneUp\manifests\` to `TuneUp\TuneUpTests\bin\Debug\viewExtensions\`
- Open the copied `TuneUp_ViewExtensionDefinition.xml` and change the assemply path to `..\TuneUp.dll`
- Remove `TuneUp` from your Dynamo packages folder if you have it installed from package manager (otherwise `TuneUp.dll` will get loaded twice). This won't work well.
- Launch DynamoSandbox.exe, then click `View-> Open Tune Up` and use while a graph runs.

### Running TuneUp Unit Tests
- Install NUnit 2 Test Adapter from VisualStudio->Extensions->Manage Extensions->Online.
- Open Test Explorer from VIsualStudio->Test->Test Explorer. Now you should see a list of TuneUpTests.
- Click the target test to run or run them all.

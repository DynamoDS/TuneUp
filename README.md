![](https://github.com/DynamoDS/TuneUp/workflows/Tuneup-Build/badge.svg)

# TuneUp

TuneUp is a view extension for analyzing the performance of Dynamo graphs. TuneUp allows you to see overall graph execution time, per-node execution time, and other helpful information about what's happening under the hood, e.g. nodes run in the current execution v.s. nodes run in the previous execution (which are skipped this run for optimization/ caching)

Here is a short demo of how to utilize it as of now:
![TuneUp](design/gifs/TuneUpScroll.gif)

Here is a mock up of the future design:
![Alt text](design/images/TuneUp_Mockup_03_SortedByExecutionOrder.jpg?raw=true "TuneUp")

## Building
Recommended build environment
- VisualStudio 2019
- .Net Framework 4.7 Developer Pack
- Dynamo repository cloned and built on the same level of Tuneup repository

## Known issues
- Tuneup does not work with dyfs yet
- Tuneup is required to work with Dynamo 2.5.0 or up because of dependency on certian API which only exists on newer version of Dynamo.

## Testing

### Setup
Please check out known issues before go on testing.

- Download DynamoCoreRuntime 2.5.0 from https://dynamobuilds.com/. Alternatively, you can build Dynamo from Dynamo repository and use the bin folder equivalently. 
- Copy all contents of the DynamoCoreRuntime to `TuneUp\TuneUpTests\bin\Debug\`
- Copy `TuneUp_ViewExtensionDefinition.xml` from `TuneUp\TuneUp\manifests\` to `TuneUp\TuneUpTests\bin\Debug\viewExtensions\`
- Open the copied `TuneUp_ViewExtensionDefinition.xml` and change the assemply path to `..\TuneUp.dll`
- Remove `TuneUp` from your Dynamo packages folder if you have it installed from package manager (otherwise `TuneUp.dll` will get loaded twice)
- Launch DynamoSandbox.exe make click `View-> Open Tune Up` and enjoy it while having graph runs

### Running Tuneup Unit Tests
- Install NUnit 2 Test Adapter from VisualStudio->Extensions->Manage Extensions->Online
- Open Test Explorer from VIsualStudio->Test->Test Explorer. Now you should see a list of TuneUpTests
- Click the target test to run or run them all

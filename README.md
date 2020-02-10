# TuneUp

TuneUp is a view extension for analyzing the performance of Dynamo graphs. TuneUp allows you to see overall graph execution time, per-node execution time, and other helpful information about what's happening under the hood.

![Alt text](design/images/TuneUp_Mockup_03_SortedByExecutionOrder.jpg?raw=true "TuneUp")

## Building
Recommend build environment
- VisualStudio 2019
- .Net Framework 4.7 Developer Pack which should come with VS2019
- Dynamo repository cloned and built on the same level of Tuneup repository

### Setup

- Download DynamoCoreRuntime 2.5.0 from https://dynamobuilds.com/
- Copy all contents of the DynamoCoreRuntime to `TuneUp\TuneUpTests\bin\Debug\`
- Copy `TuneUp_ViewExtensionDefinition.xml` from `TuneUp\TuneUp\manifests\` to `TuneUp\TuneUpTests\bin\Debug\viewExtensions\`
- Open the copied `TuneUp_ViewExtensionDefinition.xml` and change the assemply path to `..\TuneUp.dll`
- Remove TuneUp from your Dynamo packages folder (otherwise TuneUp.dll will get loaded twice)

## Testing
- Install NUnit 2 Test Adapter from VisualStudio->Extensions->Manage Extensions->Online
- Open Test Explorer from VIsualStudio->Test->Test Explorer. Now you should see a list of TuneUpTests
- Click the target test to run or run them all
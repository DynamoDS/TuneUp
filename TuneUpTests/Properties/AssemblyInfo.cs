using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using NUnit.Framework;

[assembly: AssemblyTitle("TuneUpTests")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("TuneUpTests")]
[assembly: AssemblyCopyright("Copyright ©  2019")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]

// Run every test on one STA thread, as Dynamo's own WPF test assemblies do. With a new thread per test,
// the second test to start Dynamo fails on WPF objects created on the first test's thread.
[assembly: RequiresThread(ApartmentState.STA)]

[assembly: Guid("b6817785-afd9-4af5-9562-5808e74b53e7")]

// [assembly: AssemblyVersion("1.0.*")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

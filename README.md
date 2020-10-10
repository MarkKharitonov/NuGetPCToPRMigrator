# Introduction
This tool migrates all the non Asp.Net projects in the given solution from packages.config (PC) to PackageReferences (PR) using the [Migration tool][3] built-in in Visual Studio 2017 and up.

That [Migration tool][3] is fantastic if you need to convert one or two projects, but totally useless when the solution contains a hundred or so of projects. I was unable to find any good way to [migrate en masse][6] and hence wrote my own something, which still leverages the built-in facility.

Given a solution file path it proceeds like this:
 1. Locates the Visual Studio instance where the solution is open. If not found a new VS instance is open with the solution in question.
 2. Obtains the [DTE2][2] object with the help of [RunningVSInstanceFinder][1].
 3. Using the [DTE2][2] object it enumerates all the non Asp.Net projects and for each of them runs the [Migration tool][3].
 4. At the end when all the relevant projects were processed and if the tool has previously open a new VS instance - the tool will close that VS instance.

The code is smart enough to:
 - Close the modal dialog (open by the migration tool) using the [SendKeys.SendWait][5] API.
 - Properly initialize the NuGet Package Manager to address the [known issue][4].
 - Absorb `COMExceptions` occurring when trying to automate the IDE while it is busy doing something.
 - Retry operations failed due to IDE being busy.
 - Identify when a project migration is done by checking the presence of the migration report html.

The code is NOT smart enough to:
 - Figure out the devenv path. Right now it hard codes it to **C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\Common7\IDE\devenv.exe**
 - Figure out the default browser. Right now it hard codes it to `chrome.exe`. Why does it care in the first place? See [below](#kill_browser).

# Building
The code consumes [RunningVSInstanceFinder][1] as a NuGet package, which does not exist publicly. So, trying to clone and build would not work. Alas, I do not have time to setup an external build pipeline for [RunningVSInstanceFinder][1] and so if you wish to use this tool you need to do the following:
 1. Clone [RunningVSInstanceFinder][1]
 2. Clone this repository
 3. Include the RunningVSInstanceFinder project into the solution of this tool.
 4. Modify the project of this tool to reference the `RunningVSInstanceFinder` as a project rather than as a Nuget library.
 5. Update the DEVENV constant in Program.cs to point to your devenv.exe
 6. Update the DEFAULT_BROWSER constant in Program.cs to point to your default browser.

# Running
Run the tool with a single argument - the solution file path. The tool may open a new VS instance, if it could not find an already running one with the solution in question already open there. Note, that a VS instance that presents a modal dialog cannot be automated.

The tool is going to bring forward the relevant VS instance and you will not be able to do anything while it is running, because it would constantly steal the mouse focus from you.

I tried it on a solution containing more than a hundred projects, seems to work fine. It is possible that the tool would exhaust it retry capacity (should VS become really busy) and abort. It is safe to restart, optionally killing the VS instance.

Here how it looks like on a small solution:
```powershell
C:\tools\NuGetPCToPRMigrator> .\NuGetPCToPRMigrator\bin\Debug\net472\NuGetPCToPRMigrator.exe C:\dayforce\tip\build\deployer.sln
[1/5] C:\Dayforce\tip\Build\2010\Activities\ActivityPack.csproj
[2/5] C:\Dayforce\tip\Build\2010\Deployer\Deployer.csproj
[3/5] C:\Dayforce\tip\Build\2010\Configurator\Configurator.csproj
[4/5] C:\Dayforce\tip\Build\2010\DeploymentEngine\DeploymentEngine.csproj
[5/5] C:\Dayforce\tip\Build\2010\DeployerTests\DeployerTests.csproj
C:\tools\NuGetPCToPRMigrator>
```
The solution contains more than 5 projects, but only 5 contain packages.config and are non Asp.Net projects.

## <a name="kill_browser"></a>Killing all the default browser instances
The [Migration tool][3] opens an HTML report at the end of a migration. For a solution with 100 eligible projects that would result in a 100 tabs in the default browser. That is why the code kills the default browser at the end of each migration.

# Potential Issues
 - VS automation is fragile. It may sometimes fail on a large solution somewhere midway. Just restart the tool.
 - The tool saves all files in the solution and waits 5 seconds. May not be enough for a big solution.

[1]: https://github.com/MarkKharitonov/RunningVSInstanceFinder
[2]: https://docs.microsoft.com/en-us/dotnet/api/envdte80.dte2?view=visualstudiosdk-2019
[3]: https://devblogs.microsoft.com/nuget/migrate-packages-config-to-package-reference/
[4]: https://docs.microsoft.com/en-us/nuget/consume-packages/migrate-packages-config-to-package-reference#issue
[5]: https://docs.microsoft.com/en-us/dotnet/api/system.windows.forms.sendkeys.sendwait?view=netcore-3.1
[6]: https://stackoverflow.com/a/64200862/80002

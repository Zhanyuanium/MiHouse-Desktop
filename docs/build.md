# Build, run and package

## Prerequisites

- .NET 10 SDK (the solution targets `net10.0` and `net10.0-windows10.0.26100.0`).
- Windows 10 version 1809 or later (Windows 11 recommended for the full WinUI experience).
- Windows App SDK runtime. The project references `Microsoft.WindowsAppSDK` and
  `Microsoft.Windows.SDK.BuildTools.WinApp`, so a restore brings in the build integration.
- The `winapp` CLI, version 0.7.1 or later, on `PATH`. Verify with `winapp --version`.

### Target machine requirements for the MSIX

The package produced below is **framework-dependent**, not self-contained. The target machine
needs:

- **.NET 10 Desktop Runtime** (x64). Check with `dotnet --list-runtimes`; a line such as
  `Microsoft.WindowsDesktop.App 10.x.x` must be present.
- **Windows App Runtime 2.5.1 or later** (x64). Check with
  `Get-AppxPackage *WindowsAppRuntime*`, or `winget list "Windows App Runtime"`.

Install them with:

```powershell
winget install Microsoft.DotNet.DesktopRuntime.10
winget install Microsoft.WindowsAppRuntime.2.5
```

If the Windows App Runtime winget id is not available in your feed, install the matching
Windows App SDK 2.5 runtime from Microsoft's Windows App SDK downloads page. Package with
`--self-contained` if you want the Windows App SDK runtime bundled into the MSIX instead.

## Build and test

```powershell
dotnet build MiHouse.slnx -c Release -warnaserror
dotnet test MiHouse.slnx -c Release
```

Warnings are errors. Run `dotnet format MiHouse.slnx --verify-no-changes` before sending a change.

## Run

```powershell
winapp run src\MiHouse.App\MiHouse.App.csproj -c Release
```

`winapp run` builds the project, registers a development package identity and launches the app.
Use `--no-launch` to register without starting it, or `--unregister-on-exit` to clean up after
the process exits.

## Package (MSIX)

```powershell
winapp package src\MiHouse.App\MiHouse.App.csproj -c Release --generate-cert
```

This builds and publishes the Release x64 app, creates a development certificate, signs the
package and writes the MSIX into the current directory:

```
MiHouse.App_1.0.0.0_x64.msix
MiHouse.App_1.0.0.0_x64_cert.pfx
```

The version and architecture in the file names follow `Package.appxmanifest` and the build
target; the certificate is for local testing only. If a certificate already exists, pass it
instead of generating a new one:

```powershell
winapp package src\MiHouse.App\MiHouse.App.csproj -c Release --cert .\path\to\cert.pfx
```

### Trust the development certificate

`Add-AppxPackage` only accepts a package whose signing certificate is trusted on the machine.
Install the generated certificate once, from an elevated shell:

```powershell
winapp cert install .\MiHouse.App_1.0.0.0_x64_cert.pfx
```

The generated PFX password is `password`. You can also import it manually into
**Local Machine > Trusted People** (or **Trusted Root Certification Authorities** for a
self-signed development certificate). Never distribute this certificate or its private key.

Then install the package:

```powershell
Add-AppxPackage .\MiHouse.App_1.0.0.0_x64.msix
```

For a production build, sign with a certificate from a trusted CA whose subject matches the
manifest `Publisher` (`CN=AppPublisher`), or submit through the Microsoft Store.

## Logs

Crash reports are written to:

```
%LOCALAPPDATA%\MiHouse\logs\crash-yyyyMMdd-HHmmss.log
```

Each report contains the timestamp, app informational version, exception type, message, stack
trace and whether the failure was handled. Query strings are stripped and token/cookie-style
values are redacted. The app keeps the newest 20 reports and deletes older ones at startup.

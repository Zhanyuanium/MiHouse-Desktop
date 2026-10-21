# Build, run and package

## Prerequisites

- .NET 10 SDK (the solution targets `net10.0` and `net10.0-windows10.0.26100.0`).
- Windows 11 24H2 (build 26100) or later.
- Windows App SDK runtime. The project references `Microsoft.WindowsAppSDK` and
  `Microsoft.Windows.SDK.BuildTools.WinApp`, so a restore brings in the build integration.
- The `winapp` CLI, version 0.7.1 or later, on `PATH`. Verify with `winapp --version`.

### Target machine requirements for the MSIX bundle

The release build is **framework-dependent**: `SelfContained` is `false` in
`Properties/PublishProfiles/win-x64.pubxml`, `win-arm64.pubxml` and `win-x86.pubxml`, and
neither runtime is bundled. The target machine needs the runtimes matching its architecture
(x64 or ARM64; x86 only if an x86 slice is included):

- **.NET 10 Desktop Runtime**. Check with `dotnet --list-runtimes`; a line such as
  `Microsoft.WindowsDesktop.App 10.x.x` must be present.
- **Windows App Runtime 2.5.1 or later**. Check with
  `Get-AppxPackage *WindowsAppRuntime*`, or `winget list "Windows App Runtime"`.

Install them with:

```powershell
winget install Microsoft.DotNet.DesktopRuntime.10
winget install Microsoft.WindowsAppRuntime.2.5
```

If the Windows App Runtime winget id is not available in your feed, install the matching
Windows App SDK 2.5 runtime from Microsoft's Windows App SDK downloads page.

`winapp package --self-contained` bundles only the Windows App SDK runtime; it does **not**
bundle the .NET runtime. This release does not use that option, so the .NET 10 Desktop
Runtime is always required.

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

## Package (MSIX bundle)

```powershell
winapp package src\MiHouse.App\MiHouse.App.csproj -c Release --arch x64 --arch arm64 --generate-cert
```

This publishes the Release x64 and ARM64 slices, creates a self-signed development
certificate, signs the bundle and writes it plus the certificate into the current directory:

```
E7E58AE2-FE69-4041-B3CB-BF61B9EC1A47_1.0.0.0_arm64_x64.msixbundle
E7E58AE2-FE69-4041-B3CB-BF61B9EC1A47_cert.pfx
```

The bundle name follows `Package.appxmanifest` and the bundled architectures; the PFX is
named after the package identity. Add `--arch x86` to include an x86 slice. The certificate
is self-signed and not publicly trusted, so every target machine must trust it before
installing the bundle. If a certificate already exists, pass it instead of generating a new
one:

```powershell
winapp package src\MiHouse.App\MiHouse.App.csproj -c Release --arch x64 --arch arm64 --cert .\path\to\cert.pfx
```

### Trust the self-signed certificate

`Add-AppxPackage` only accepts a bundle whose signing certificate is trusted on the machine.
Install the generated certificate once, from an elevated shell:

```powershell
winapp cert install .\E7E58AE2-FE69-4041-B3CB-BF61B9EC1A47_cert.pfx
```

The generated PFX password is `password`. You can also import its public certificate manually
into **Local Machine > Trusted People** (or **Trusted Root Certification Authorities** for a
self-signed certificate). Keep the PFX and its private key private; if you distribute the
bundle, distribute only the public `.cer`, never the PFX or its password.

Then install the bundle:

```powershell
Add-AppxPackage .\E7E58AE2-FE69-4041-B3CB-BF61B9EC1A47_1.0.0.0_arm64_x64.msixbundle
```

For a future production release, sign with a certificate from a trusted CA whose subject
matches the manifest `Publisher` (`CN=AppPublisher`), or submit through the Microsoft Store.

## Logs

Crash reports are written to:

```
%LOCALAPPDATA%\MiHouse\logs\crash-yyyyMMdd-HHmmss.log
```

Each report contains the timestamp, app informational version, source, handled flag, exception
type, inner-exception type names and a reflection-built stack. Exception messages are never
recorded; the message field reads `<not recorded>`. The app keeps the newest 20 reports and
deletes older ones at startup.

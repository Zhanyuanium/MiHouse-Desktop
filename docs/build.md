# Build, run and package

## Prerequisites

- .NET 10 SDK (the solution targets `net10.0` and `net10.0-windows10.0.26100.0`).
- Windows 11 24H2 (build 26100) or later.
- Windows App SDK runtime. The project references `Microsoft.WindowsAppSDK` and
  `Microsoft.Windows.SDK.BuildTools.WinApp`, so a restore brings in the build integration.
- The `winapp` CLI, version 0.7.1 or later, on `PATH`. Verify with `winapp --version`.
- PowerShell 7 or later for the scripts in `scripts\`.

### Target machine requirements for the MSIX package

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

The package is registered with `appContainer` trust, and `winapp run` launches it as such; a
registered package shows only `internetClient` and `privateNetworkClientServer` under the app's
capabilities.

## Package (MSIX)

### Signing certificate

Releases are signed with a self-signed personal developer certificate whose subject is
`CN=Zhanyuanium`, the same value as the manifest `Publisher`. The friendly publisher name comes
from `PublisherDisplayName`, `浪迹星海@Zhanyuanium`, which is what Windows Settings and the About
dialog show. The subject is ASCII on purpose: MSIX compares the manifest publisher and the
certificate subject as exact strings, so the Chinese name stays only in `PublisherDisplayName`
and the About dialog, while the certificate remains ASCII. The certificate is valid for
1095 days (three years) and carries a code-signing extended key usage. The project ships
outside the Store and has no CA-issued identity, so the certificate signs itself: creating it
costs nothing, but every target machine has to trust it once before Windows accepts the package.

Create it once from the repository root:

```powershell
scripts\New-SigningCertificate.ps1
```

The script reads the publisher from `Package.appxmanifest` and writes:

```
artifacts\signing\MiHouse-Signing.pfx   private key, never commit or publish it
artifacts\signing\MiHouse-Signing.cer   development copy for local trust, not shipped
```

`artifacts/` and `*.pfx` are already ignored by Git. Back up the `.pfx`: if every copy of its
private key is lost, it cannot be reconstructed from the public `.cer`, and a replacement
certificate requires every user to trust the new `.cer` before updating again.

#### Signing password

The scripts never fall back to a publicly known default. On first use,
`New-SigningCertificate.ps1` generates a 48-character random secret and stores it
DPAPI-protected (`DataProtectionScope.CurrentUser`) at:

```
%LOCALAPPDATA%\MiHouse-Release\signing-password.dpapi
```

Later runs of `scripts\Package-Release.ps1` read that file, so no arguments are needed on the
machine that created it.

Without `-Password`, certificate creation reuses the stored secret and generates a random one
only when none exists. To resume signing on another machine, restore the `.pfx` to
`artifacts\signing\MiHouse-Signing.pfx` and pass the backed-up password to
`Package-Release.ps1 -Password`; do not regenerate the certificate merely because the DPAPI file
is unavailable.

The DPAPI file is a local convenience, not a portable backup. Keep the password in a password
manager, in a record **separate from the `.pfx` backup**: anyone holding both the private key
and its password can sign packages that your users already trust. To copy the secret out on this
machine, run:

```powershell
scripts\Show-SigningPassword.ps1
```

The password store is shared by all checkouts under the same Windows user. With no `-Password`,
`New-SigningCertificate.ps1` reuses it instead of silently replacing it. A different explicit
password to `New-SigningCertificate.ps1` is refused when this checkout has no `.pfx` unless
`-Force` is passed. `Package-Release.ps1 -Password` overrides only that invocation and does not
update the stored secret.

- `-AllowInsecurePassword` permits the literal `password` for throwaway local testing only,
  never for a published certificate.
- `-ValidDays` changes the certificate lifetime.
- `-Force` replaces the certificate; users must then trust the new `.cer`.

### Build the package

```powershell
scripts\Package-Release.ps1
```

This publishes and signs the x64 and ARM64 slices and writes:

```
artifacts\MiHouse_<version>_x64_arm64.msixbundle
artifacts\MiHouse_<version>.cer
```

`<version>` is the manifest version with a trailing `.0` component removed, so `1.0.0.0` becomes
`1.0.0`. The package is framework-dependent, and the script never passes `--self-contained`:
in winapp that option bundles only the Windows App SDK runtime, not .NET. A missing signing
certificate is generated first, with a random secret if none is stored; `-ForceCertificate`
regenerates it, after which every installation must trust the new `.cer` before it can update.
Certificate generation stages and validates a temporary certificate before replacing existing
files. On a caught failure it attempts to restore the previous state and keeps backups if
restoration fails; abrupt termination or power loss may require manual recovery.

For a single architecture use `-Arch x64`, which writes `MiHouse_<version>_x64.msix`; `-Arch`
also accepts `arm64` and `x86`. One architecture produces a `.msix`, two or more produce a
`.msixbundle`.

The release certificate is exported from the exact `.pfx` used to sign the package, not copied
from an earlier file, and the script checks the certificate subject against the manifest
`Publisher` before packaging, so a stale or mismatched signing certificate fails instead of
shipping. The exported public certificate is `artifacts\MiHouse_<version>.cer`.

The equivalent manual command:

```powershell
winapp package src\MiHouse.App\MiHouse.App.csproj -c Release --arch x64 --arch arm64 --cert artifacts\signing\MiHouse-Signing.pfx --cert-password <password> --output artifacts\MiHouse_<version>_x64_arm64.msixbundle
```

When packaging without the script, export the public certificate from the same `.pfx`; the
script does that for you.

### Trust the certificate

`Add-AppxPackage` only accepts a package whose signing certificate is trusted on the machine,
so install the public certificate once per target machine from an **elevated** shell;
administrator rights are required:

```powershell
winapp cert install .\MiHouse_<version>.cer
```

Without the `winapp` CLI, write the same store directly:

```powershell
Import-Certificate -FilePath .\MiHouse_<version>.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

Or open the `.cer` and choose **Install Certificate > Local Machine > Place all certificates
in the following store > Trusted People**. Then install the package:

```powershell
Add-AppxPackage .\MiHouse_<version>_x64_arm64.msixbundle
```

For a single-architecture build substitute `MiHouse_<version>_x64.msix`. A build signed with a
different certificate needs the new `.cer` trusted before it can replace an installed copy.

### Verify the signature

```powershell
winapp cert info artifacts\MiHouse_<version>.cer
Get-AuthenticodeSignature artifacts\MiHouse_<version>_x64_arm64.msixbundle
```

For a single-architecture build, check `artifacts\MiHouse_<version>_x64.msix` instead. The
release `.cer` named here is the file users install, exported by `Package-Release.ps1` from the
same `.pfx` used to sign the package. `artifacts\signing\MiHouse-Signing.cer` is a development
export of that same certificate, for local trust only.

The certificate reports the `CN=Zhanyuanium` subject, matching the manifest `Publisher`.
`Package-Release.ps1` verifies that match before packaging and exports the release `.cer` from
the same `.pfx` used to sign the package. Until the certificate is trusted,
`Get-AuthenticodeSignature` and `signtool verify /pa` report a non-Valid status whose message
names the untrusted root (PowerShell may report `UnknownError` for an MSIX container rather
than `NotTrusted`). That means the signature is present but the trust step is missing; after
installing the matching `.cer`, verify again. A non-Valid result can also indicate an expired
certificate, a damaged signature or another validation failure, so inspect the reported error
instead of assuming trust is the only cause.

### Upgrading from a build before the publisher rename

Changing the publisher changed the package family name. Uninstall the old package before
adding a current release, either from **Settings > Apps > Installed apps**, or by listing the
identity and removing the matching entry:

```powershell
Get-AppxPackage -Name E7E58AE2-FE69-4041-B3CB-BF61B9EC1A47
```

Remove the stale one with `Remove-AppxPackage <PackageFullName>`. Settings kept under the old
package data folder are not migrated, so the new build starts signed out with the default
appearance.

## Logs

Crash reports are written to the package state directory, which the system removes when the
package is uninstalled:

```
%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState\logs\crash-yyyyMMdd-HHmmss.log
```

An unpackaged run has no package identity and writes to
`%LOCALAPPDATA%\MiHouse-Unpackaged\logs` instead.

Each report contains the timestamp, app informational version, source, handled flag, exception
type, inner-exception type names and a reflection-built stack. Exception messages are never
recorded; the message field reads `<not recorded>`. The app keeps the newest 20 reports and
deletes older ones at startup.

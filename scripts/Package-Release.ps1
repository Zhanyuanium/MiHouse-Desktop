#Requires -Version 7.0
<#
.SYNOPSIS
    Publishes, signs and packages MiHouse for sideloading.

.DESCRIPTION
    Builds the requested architectures, signs them with the personal self-signed certificate
    created by New-SigningCertificate.ps1, and writes to artifacts/:

      MiHouse_<version>_<archs>.msixbundle   two or more architectures
      MiHouse_<version>_<arch>.msix          a single architecture
      MiHouse_<version>.cer                  public certificate exported from the signing .pfx

    The .cer is the public half users must trust before adding the package. It is exported from
    the exact .pfx used to sign, and its subject is checked against the Publisher in
    Package.appxmanifest so a stale certificate cannot silently ship next to the package.

    The signing password never falls back to a public default. Pass -Password, or let the
    script use the DPAPI-protected secret that New-SigningCertificate.ps1 stores for the
    current Windows user. When a certificate must be created and no password is available,
    New-SigningCertificate.ps1 generates one and stores it, after which this script re-resolves
    it and continues.

.PARAMETER Arch
    Architectures to publish. Repeat or pass an array; the default matches the release bundle.
    One architecture produces a .msix, several produce a .msixbundle.

.PARAMETER Password
    Password of the signing .pfx. Omit it to use the DPAPI-protected secret stored on this
    machine by New-SigningCertificate.ps1.

.PARAMETER AllowInsecurePassword
    Permits the publicly known password 'password'. Intended only for throwaway local testing;
    never use it for a certificate whose .cer is published to users.

.PARAMETER ForceCertificate
    Regenerate the signing certificate. Every installed copy must trust the new .cer afterwards.
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64', 'x86')]
    [string[]]$Arch = @('x64', 'arm64'),
    [string]$Password,
    [switch]$AllowInsecurePassword,
    [switch]$ForceCertificate
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'Signing.Secret.ps1')

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repoRoot 'src\MiHouse.App\MiHouse.App.csproj'
$manifestPath = Join-Path $repoRoot 'src\MiHouse.App\Package.appxmanifest'
$artifactsDirectory = Join-Path $repoRoot 'artifacts'
$pfxPath = Join-Path $artifactsDirectory 'signing\MiHouse-Signing.pfx'

if (-not (Get-Command winapp -ErrorAction SilentlyContinue)) {
    throw 'The winapp CLI is required. Install it and verify with: winapp --version'
}

if (-not (Test-Path -LiteralPath $pfxPath) -or $ForceCertificate) {
    $certificateArguments = @{}

    if ([string]::IsNullOrEmpty($Password)) {
        $storedPassword = $null
        try {
            $storedPassword = Unprotect-SigningPassword
        }
        catch {
            Write-Host 'No usable stored signing password was found; the new certificate will get a random one.'
        }

        if (-not [string]::IsNullOrEmpty($storedPassword)) {
            $certificateArguments['Password'] = $storedPassword
            if ($AllowInsecurePassword) {
                $certificateArguments['AllowInsecurePassword'] = $true
            }
        }
    }
    else {
        $certificateArguments['Password'] = $Password
        if ($AllowInsecurePassword) {
            $certificateArguments['AllowInsecurePassword'] = $true
        }
    }

    if ($ForceCertificate) {
        $certificateArguments['Force'] = $true
    }

    & (Join-Path $PSScriptRoot 'New-SigningCertificate.ps1') @certificateArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Signing certificate generation failed with exit code $LASTEXITCODE"
    }
}

$Password = Resolve-SigningPassword -Password $Password -AllowInsecurePassword:$AllowInsecurePassword

[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
$identity = $manifest.SelectSingleNode('/*[local-name()="Package"]/*[local-name()="Identity"]')
$version = $identity.Version -replace '\.0+$', ''
$archSuffix = $Arch -join '_'
$extension = if ($Arch.Count -eq 1) { '.msix' } else { '.msixbundle' }
$packagePath = Join-Path $artifactsDirectory "MiHouse_${version}_${archSuffix}$extension"

try {
    $signingCertificate = New-Object -TypeName System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList @(
        $pfxPath,
        $Password,
        [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
    )
}
catch {
    throw "Could not open the signing certificate at $pfxPath with the resolved password. Pass -Password with the password of the existing .pfx, or run with -ForceCertificate to replace it. Details: $($_.Exception.Message)"
}

try {
    if ($signingCertificate.Subject -ne $identity.Publisher) {
        throw "The signing certificate subject '$($signingCertificate.Subject)' does not match the manifest Publisher '$($identity.Publisher)'. Recreate the certificate with scripts\New-SigningCertificate.ps1 -Force."
    }

    $certificateBytes = $signingCertificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
}
finally {
    $signingCertificate.Dispose()
}

New-Item -ItemType Directory -Path $artifactsDirectory -Force | Out-Null

$arguments = @(
    'package', $projectPath
    '-c', 'Release'
    '--cert', $pfxPath
    '--cert-password', $Password
    '--output', $packagePath
)
foreach ($architecture in $Arch) {
    $arguments += @('--arch', $architecture)
}

Write-Host "Packaging MiHouse $version for $archSuffix..."
& winapp @arguments
if ($LASTEXITCODE -ne 0) {
    throw "winapp package failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path -LiteralPath $packagePath)) {
    throw "Expected package was not produced: $packagePath"
}

$releaseCertificatePath = Join-Path $artifactsDirectory "MiHouse_${version}.cer"
[System.IO.File]::WriteAllBytes($releaseCertificatePath, $certificateBytes)

Write-Host ''
Write-Host 'Release artifacts:'
Write-Host "  Package:     $packagePath"
Write-Host "  Certificate: $releaseCertificatePath"
Write-Host ''
Write-Host 'Users must trust the certificate before adding the package (elevated shell, once):'
Write-Host "  winapp cert install `"$releaseCertificatePath`""

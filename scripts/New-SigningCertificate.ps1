#Requires -Version 7.0
<#
.SYNOPSIS
    Creates the self-signed personal signing certificate used to sign MiHouse packages.

.DESCRIPTION
    Reads the publisher distinguished name from Package.appxmanifest, generates a code-signing
    certificate with that exact subject, and exports:

      artifacts/signing/MiHouse-Signing.pfx   private key, kept out of source control
      artifacts/signing/MiHouse-Signing.cer   development copy for local trust, not published

    The .cer beside the .pfx is a development convenience for installing local trust with
    winapp cert install. Package-Release.ps1 exports the release certificate users install to
    artifacts/MiHouse_<version>.cer from the exact .pfx that signed the package. Regenerating
    the certificate produces a different key pair, so every existing installation must trust
    the new certificate before it can update: back up the .pfx and reuse it for every release.

    The certificate password is never a public default. With no -Password, the script reuses the
    DPAPI-protected secret stored for the current Windows user, or generates and stores a
    48-character random secret on the first run, under
    %LOCALAPPDATA%\MiHouse-Release\signing-password.dpapi, so scripts\Package-Release.ps1 can
    run without arguments on this machine. Retrieve it for a password-manager backup with
    scripts\Show-SigningPassword.ps1, and keep that backup separate from the .pfx backup.

.PARAMETER ValidDays
    Certificate lifetime in days. The default of 1095 (three years) keeps the key-rotation
    window bounded while avoiding yearly re-trusting; renew before it expires and publish the
    new .cer so users can trust it.

.PARAMETER Password
    Password for the exported .pfx. Omit it to reuse the stored secret, or to generate a strong
    random secret when none is stored. A supplied password is stored DPAPI-protected for the
    current Windows user so later releases can sign without repeating it. The value 'password'
    is rejected.

.PARAMETER AllowInsecurePassword
    Permits the publicly known password 'password'. Intended only for throwaway local testing;
    never use it for a certificate whose .cer is published to users.

.PARAMETER Force
    Replace an existing .pfx and .cer.
#>
[CmdletBinding()]
param(
    [int]$ValidDays = 1095,
    [string]$Password,
    [switch]$AllowInsecurePassword,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'Signing.Secret.ps1')

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$manifestPath = Join-Path $repoRoot 'src\MiHouse.App\Package.appxmanifest'
$outputDirectory = Join-Path $repoRoot 'artifacts\signing'
$pfxPath = Join-Path $outputDirectory 'MiHouse-Signing.pfx'
$cerPath = Join-Path $outputDirectory 'MiHouse-Signing.cer'
$passwordStorePath = Get-SigningPasswordPath

if (-not (Get-Command winapp -ErrorAction SilentlyContinue)) {
    throw 'The winapp CLI is required. Install it and verify with: winapp --version'
}

if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "Package manifest not found: $manifestPath"
}

[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
$identity = $manifest.SelectSingleNode('/*[local-name()="Package"]/*[local-name()="Identity"]')
if ($null -eq $identity -or [string]::IsNullOrWhiteSpace($identity.Publisher)) {
    throw "No Identity/@Publisher found in $manifestPath"
}

$publisher = $identity.Publisher
Write-Host "Publisher: $publisher"

if ((Test-Path -LiteralPath $pfxPath) -and -not $Force) {
    throw "A signing certificate already exists at $pfxPath. Reuse it, or pass -Force to replace it (users must then trust the new .cer)."
}

$generatedPassword = $false
$reusedStoredPassword = $false
$hasStoredPassword = Test-Path -LiteralPath $passwordStorePath -PathType Leaf

if ([string]::IsNullOrEmpty($Password)) {
    if ($hasStoredPassword) {
        try {
            $Password = Unprotect-SigningPassword
        }
        catch {
            if (-not $Force) {
                throw "The stored signing password at $passwordStorePath could not be read. Pass -Password with the password of the existing .pfx, or pass -Force to replace the certificate and its stored password. Details: $($_.Exception.Message)"
            }

            $Password = ''
        }

        if (-not [string]::IsNullOrEmpty($Password)) {
            $Password = Resolve-SigningPassword -Password $Password -AllowInsecurePassword:$AllowInsecurePassword
            $reusedStoredPassword = $true
        }
    }

    if ([string]::IsNullOrEmpty($Password)) {
        $passwordBytes = [byte[]]::new(24)
        [System.Security.Cryptography.RandomNumberGenerator]::Fill($passwordBytes)
        $Password = [System.BitConverter]::ToString($passwordBytes).Replace('-', '').ToLowerInvariant()
        $generatedPassword = $true
    }
}
else {
    $Password = Resolve-SigningPassword -Password $Password -AllowInsecurePassword:$AllowInsecurePassword

    $existingStoredPassword = $null
    if ($hasStoredPassword) {
        try {
            $existingStoredPassword = Unprotect-SigningPassword
        }
        catch {
            $existingStoredPassword = $null
        }
    }

    if ($null -ne $existingStoredPassword -and
        -not [string]::Equals($existingStoredPassword, $Password, [System.StringComparison]::Ordinal) -and
        -not $Force -and
        -not (Test-Path -LiteralPath $pfxPath)) {
        throw "A different signing password is already stored for this Windows user at $passwordStorePath, and this checkout has no signing certificate. Refusing to replace the shared secret; pass -Force to do so deliberately."
    }
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$suffix = [guid]::NewGuid().ToString('N')
$temporaryPfxPath = Join-Path $outputDirectory "MiHouse-Signing.$suffix.tmp.pfx"
$temporaryCerPath = [System.IO.Path]::ChangeExtension($temporaryPfxPath, '.cer')
$backupPfxPath = Join-Path $outputDirectory "MiHouse-Signing.$suffix.bak.pfx"
$backupCerPath = Join-Path $outputDirectory "MiHouse-Signing.$suffix.bak.cer"
$passwordDirectory = Split-Path -Parent $passwordStorePath
$temporaryBlobPath = Join-Path $passwordDirectory "signing-password.$suffix.tmp"
$backupBlobPath = Join-Path $passwordDirectory "signing-password.$suffix.bak"

$hadPfx = Test-Path -LiteralPath $pfxPath -PathType Leaf
$hadCer = Test-Path -LiteralPath $cerPath -PathType Leaf
$blobReplaced = $false
$pfxReplaced = $false
$cerReplaced = $false
$keepBackups = $false

try {
    $arguments = @(
        'cert', 'generate'
        '--publisher', $publisher
        '--output', $temporaryPfxPath
        '--password', $Password
        '--valid-days', $ValidDays
        '--export-cer'
    )

    & winapp @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "winapp cert generate failed with exit code $LASTEXITCODE"
    }

    if (-not (Test-Path -LiteralPath $temporaryPfxPath -PathType Leaf)) {
        throw "Expected temporary signing certificate was not created: $temporaryPfxPath"
    }

    if (-not (Test-Path -LiteralPath $temporaryCerPath -PathType Leaf)) {
        throw "Expected temporary public certificate was not exported: $temporaryCerPath"
    }

    try {
        $temporaryCertificate = New-Object -TypeName System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList @(
            $temporaryPfxPath,
            $Password,
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
        )
    }
    catch {
        throw "The generated signing certificate could not be opened with its password. Details: $($_.Exception.Message)"
    }

    try {
        if ($null -eq $temporaryCertificate) {
            throw 'The generated signing certificate could not be opened.'
        }

        if (-not $temporaryCertificate.HasPrivateKey) {
            throw 'The generated signing certificate has no private key.'
        }

        if ($temporaryCertificate.Subject -ne $publisher) {
            throw "The generated certificate subject '$($temporaryCertificate.Subject)' does not match the manifest Publisher '$publisher'."
        }
    }
    finally {
        if ($null -ne $temporaryCertificate) {
            $temporaryCertificate.Dispose()
        }
    }

    # Snapshot the current state before changing anything. A failed copy aborts with the real files untouched.
    if ($hadPfx) {
        Copy-Item -LiteralPath $pfxPath -Destination $backupPfxPath -ErrorAction Stop
    }

    if ($hadCer) {
        Copy-Item -LiteralPath $cerPath -Destination $backupCerPath -ErrorAction Stop
    }

    if ($hasStoredPassword) {
        Copy-Item -LiteralPath $passwordStorePath -Destination $backupBlobPath -ErrorAction Stop
    }

    # Persist the new password to a sibling temp file, then swap it in atomically.
    Protect-SigningPassword -Password $Password -Path $temporaryBlobPath | Out-Null
    [System.IO.File]::Move($temporaryBlobPath, $passwordStorePath, $true)
    $blobReplaced = $true

    [System.IO.File]::Move($temporaryPfxPath, $pfxPath, $true)
    $pfxReplaced = $true

    [System.IO.File]::Move($temporaryCerPath, $cerPath, $true)
    $cerReplaced = $true
}
catch {
    $generationError = $_
    $restoreMessages = [System.Collections.Generic.List[string]]::new()

    if ($cerReplaced) {
        try {
            if ($hadCer) {
                Copy-Item -LiteralPath $backupCerPath -Destination $cerPath -Force -ErrorAction Stop
            }
            else {
                Remove-Item -LiteralPath $cerPath -Force -ErrorAction Stop
            }
        }
        catch {
            $restoreMessages.Add("the .cer could not be restored: $($_.Exception.Message)")
        }
    }

    if ($pfxReplaced) {
        try {
            if ($hadPfx) {
                Copy-Item -LiteralPath $backupPfxPath -Destination $pfxPath -Force -ErrorAction Stop
            }
            else {
                Remove-Item -LiteralPath $pfxPath -Force -ErrorAction Stop
            }
        }
        catch {
            $restoreMessages.Add("the .pfx could not be restored: $($_.Exception.Message)")
        }
    }

    if ($blobReplaced) {
        try {
            if ($hasStoredPassword) {
                Copy-Item -LiteralPath $backupBlobPath -Destination $passwordStorePath -Force -ErrorAction Stop
            }
            else {
                Remove-Item -LiteralPath $passwordStorePath -Force -ErrorAction Stop
            }
        }
        catch {
            $restoreMessages.Add("the stored password could not be restored: $($_.Exception.Message)")
        }
    }

    if ($restoreMessages.Count -gt 0) {
        $keepBackups = $true
        throw "Certificate generation failed: $($generationError.Exception.Message) Restoring the previous state also failed: $($restoreMessages -join '; '). Backup files were kept in $outputDirectory and $passwordDirectory."
    }

    if ($blobReplaced -or $pfxReplaced -or $cerReplaced) {
        throw "Certificate generation failed: $($generationError.Exception.Message) The previous state was restored."
    }

    throw "Certificate generation failed: $($generationError.Exception.Message) No existing certificate or stored password was changed."
}
finally {
    foreach ($path in @($temporaryPfxPath, $temporaryCerPath, $temporaryBlobPath)) {
        Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    }

    if (-not $keepBackups) {
        foreach ($path in @($backupPfxPath, $backupCerPath, $backupBlobPath)) {
            Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host ''
Write-Host 'Signing certificate created.'
Write-Host "  Private key: $pfxPath   (never commit or publish this file)"
Write-Host "  Public cert: $cerPath   (development copy for local trust; releases get MiHouse_<version>.cer)"
Write-Host "  Password:    stored DPAPI-protected at $passwordStorePath"
Write-Host ''
if ($generatedPassword) {
    Write-Host 'No -Password was supplied and no stored secret existed, so a random signing password was generated.'
}
elseif ($reusedStoredPassword) {
    Write-Host 'No -Password was supplied, so the existing DPAPI-protected signing password was reused.'
}
Write-Host 'Store the password in a password manager now, separately from the .pfx backup;'
Write-Host 'anyone holding both can sign packages that your users already trust.'
Write-Host 'To read it back on this machine:'
Write-Host '  scripts\Show-SigningPassword.ps1'
Write-Host ''
Write-Host 'To build and sign a package, run:'
Write-Host '  scripts\Package-Release.ps1'
Write-Host ''
Write-Host 'To trust the certificate on this machine (elevated shell, once per certificate):'
Write-Host "  winapp cert install `"$cerPath`""

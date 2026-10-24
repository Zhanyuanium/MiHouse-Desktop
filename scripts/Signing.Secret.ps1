#Requires -Version 7.0
<#
.SYNOPSIS
    Shared helper for the MiHouse signing certificate password.

.DESCRIPTION
    Dot-source this file from a script that creates or uses the signing certificate:

        . (Join-Path $PSScriptRoot 'Signing.Secret.ps1')

    The password is stored per Windows user with DPAPI (CurrentUser) under
    %LOCALAPPDATA%\MiHouse-Release\signing-password.dpapi. The blob is bound to that user and
    machine, so it is a convenience for local release builds, not a backup: keep the password
    itself in a password manager, separately from the .pfx.
#>

function Get-SigningPasswordPath {
    <#
    .SYNOPSIS
        Returns the full path of the DPAPI-protected signing password file.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param()

    $localAppData = $env:LOCALAPPDATA
    if ([string]::IsNullOrWhiteSpace($localAppData)) {
        throw 'The LOCALAPPDATA environment variable is empty or not set, so the signing password cannot be stored or read.'
    }

    return Join-Path $localAppData 'MiHouse-Release\signing-password.dpapi'
}

function Protect-SigningPassword {
    <#
    .SYNOPSIS
        Stores the signing password DPAPI-protected for the current Windows user.

    .PARAMETER Path
        Destination file. Defaults to the shared signing password path; callers can pass a
        temporary path and replace the shared file only after the rest of their work succeeds.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$Password,
        [string]$Path
    )

    Add-Type -AssemblyName System.Security.Cryptography.ProtectedData

    if ([string]::IsNullOrEmpty($Path)) {
        $Path = Get-SigningPasswordPath
    }

    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $directory -Force | Out-Null

    $plainBytes = [System.Text.Encoding]::UTF8.GetBytes($Password)
    $blob = [System.Security.Cryptography.ProtectedData]::Protect(
        $plainBytes,
        $null,
        [System.Security.Cryptography.DataProtectionScope]::CurrentUser
    )
    [System.IO.File]::WriteAllBytes($Path, $blob)

    return $Path
}

function Unprotect-SigningPassword {
    <#
    .SYNOPSIS
        Reads and decrypts the signing password stored for the current Windows user.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param()

    Add-Type -AssemblyName System.Security.Cryptography.ProtectedData

    $path = Get-SigningPasswordPath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "No stored signing password was found at $path. Run scripts\New-SigningCertificate.ps1 to create a certificate and store a password, or pass -Password explicitly."
    }

    try {
        $blob = [System.IO.File]::ReadAllBytes($path)
        $plainBytes = [System.Security.Cryptography.ProtectedData]::Unprotect(
            $blob,
            $null,
            [System.Security.Cryptography.DataProtectionScope]::CurrentUser
        )
    }
    catch {
        throw "The stored signing password at $path could not be decrypted. The file is bound to the Windows user and machine that created it; restore the password from your password manager and pass -Password, or regenerate the certificate. Details: $($_.Exception.Message)"
    }

    return [System.Text.Encoding]::UTF8.GetString($plainBytes)
}

function Resolve-SigningPassword {
    <#
    .SYNOPSIS
        Returns the signing password to use, rejecting the publicly known winapp default.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [string]$Password,
        [switch]$AllowInsecurePassword
    )

    if ([string]::IsNullOrEmpty($Password)) {
        $Password = Unprotect-SigningPassword
    }

    if ([string]::IsNullOrWhiteSpace($Password)) {
        throw 'The signing password is empty. Pass -Password with the password used for the .pfx, or store one with scripts\New-SigningCertificate.ps1.'
    }

    if (-not $AllowInsecurePassword -and [string]::Equals($Password, 'password', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to use the publicly known password 'password'. Pass a strong -Password, or add -AllowInsecurePassword only for throwaway local testing."
    }

    return $Password
}

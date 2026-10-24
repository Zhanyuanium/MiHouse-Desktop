#Requires -Version 7.0
<#
.SYNOPSIS
    Prints the stored MiHouse signing certificate password.

.DESCRIPTION
    Reads the DPAPI-protected password that New-SigningCertificate.ps1 stores for the current
    Windows user and prints it to the console. Use it to copy the password into a password
    manager.

    The DPAPI blob lives at %LOCALAPPDATA%\MiHouse-Release\signing-password.dpapi and is bound
    to the current user and machine: it is not a backup and does not survive a reinstall or a
    new machine. Keep a copy of the password in a password manager, separately from the .pfx
    backup, because anyone holding both can sign packages that your users already trust.

.EXAMPLE
    scripts\Show-SigningPassword.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'Signing.Secret.ps1')

$password = Unprotect-SigningPassword
$path = Get-SigningPasswordPath

Write-Host ''
Write-Host 'Signing password:'
Write-Host "  $password"
Write-Host ''
Write-Host "Stored at: $path (DPAPI, current Windows user and machine)"
Write-Host 'Put this value in a password manager now, in a record separate from the .pfx backup.'
Write-Host 'The DPAPI file is a local convenience: it is lost on reinstall, Windows profile reset,'
Write-Host 'or another machine, and a lost password means generating a new certificate and asking'
Write-Host 'every user to trust the new .cer.'

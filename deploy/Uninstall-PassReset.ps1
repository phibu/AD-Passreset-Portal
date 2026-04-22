#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Removes a PassReset installation from IIS and the file system.

.DESCRIPTION
    This script reverses what Install-PassReset.ps1 created:
      1. Stops and removes the IIS site.
      2. Stops and removes the IIS application pool.
      3. Removes the physical deployment folder and its contents.
      4. Optionally removes dated backup folders left by previous upgrades.

    Nothing else is touched: IIS, IIS features, the .NET Hosting Bundle,
    certificates, and all other sites/pools are left completely intact.

.PARAMETER SiteName
    Name of the IIS site to remove. Default: PassReset

.PARAMETER AppPoolName
    Name of the IIS application pool to remove. Default: PassResetPool

.PARAMETER PhysicalPath
    Deployment folder to delete. Default: C:\inetpub\PassReset

.PARAMETER KeepFiles
    Remove the IIS site and app pool but leave the physical folder on disk.
    Useful when you want to preserve appsettings.Production.json for a reinstall.

.PARAMETER RemoveBackups
    Also remove dated backup folders created by the installer during upgrades
    (folders matching ${PhysicalPath}_backup_*).

.PARAMETER Force
    Skip the confirmation prompt. Use for unattended / scripted uninstalls.

.EXAMPLE
    # Interactive — prompts for confirmation before removing anything:
    .\Uninstall-PassReset.ps1

.EXAMPLE
    # Unattended — remove everything including upgrade backups, no prompt:
    .\Uninstall-PassReset.ps1 -Force -RemoveBackups

.EXAMPLE
    # Remove IIS config only — keep files on disk for a reinstall:
    .\Uninstall-PassReset.ps1 -KeepFiles
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $SiteName     = 'PassReset',
    [string] $AppPoolName  = 'PassResetPool',
    [string] $PhysicalPath = 'C:\inetpub\PassReset',

    [switch] $KeepFiles,
    [switch] $RemoveBackups,
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Helpers ------------------------------------------------------------------

function Write-Step  { param([string]$Msg) Write-Host "`n[>>] $Msg" -ForegroundColor Cyan }
function Write-Ok    { param([string]$Msg) Write-Host "  [OK] $Msg" -ForegroundColor Green }
function Write-Warn  { param([string]$Msg) Write-Host "  [!!] $Msg" -ForegroundColor Yellow }
function Abort       { param([string]$Msg) Write-Host "`n[ERR] $Msg`n" -ForegroundColor Red; exit 1 }

# --- Detect what exists -------------------------------------------------------

Import-Module WebAdministration -ErrorAction SilentlyContinue

$siteExists = Test-Path "IIS:\Sites\$SiteName"
$poolExists = Test-Path "IIS:\AppPools\$AppPoolName"
$pathExists = Test-Path $PhysicalPath

# Windows Service hosting mode (Phase 14): the installer may have registered a
# service with the same name as the site (default: 'PassReset') instead of an IIS site.
$svc        = Get-Service -Name $SiteName -ErrorAction SilentlyContinue
$svcExists  = $null -ne $svc

if ($svcExists -and $siteExists) {
    Write-Warn "Both a Windows Service and an IIS site named '$SiteName' exist. Removing both."
}

# Find backup folders created by the upgrade path of Install-PassReset.ps1
$backupFolders = @(Get-Item "${PhysicalPath}_backup_*" -ErrorAction SilentlyContinue)

# --- Nothing to do? -----------------------------------------------------------

if (-not $siteExists -and -not $poolExists -and -not $pathExists -and -not $svcExists) {
    Write-Warn "Nothing found to remove:"
    Write-Warn "  Service '$SiteName'    : not installed"
    Write-Warn "  Site    '$SiteName'    : not present in IIS"
    Write-Warn "  Pool    '$AppPoolName' : not present in IIS"
    Write-Warn "  Path    '$PhysicalPath': does not exist"
    exit 0
}

# --- Show what will be removed ------------------------------------------------

Write-Host ''
Write-Host '  The following will be removed:' -ForegroundColor Yellow
if ($svcExists)   { Write-Host "    Windows service : $SiteName"     -ForegroundColor Yellow }
if ($siteExists)  { Write-Host "    IIS site        : $SiteName"     -ForegroundColor Yellow }
if ($poolExists)  { Write-Host "    IIS app pool    : $AppPoolName"  -ForegroundColor Yellow }
if ($pathExists -and -not $KeepFiles) {
                    Write-Host "    Deployment path : $PhysicalPath" -ForegroundColor Yellow }
if ($pathExists -and $KeepFiles) {
                    Write-Host "    Deployment path : $PhysicalPath  (kept — -KeepFiles)" -ForegroundColor DarkYellow }
if ($RemoveBackups -and $backupFolders.Count -gt 0) {
    foreach ($b in $backupFolders) {
        Write-Host "    Backup folder   : $($b.FullName)" -ForegroundColor Yellow
    }
}
Write-Host ''
Write-Host '  IIS, IIS features, .NET Hosting Bundle, and certificates are NOT affected.' -ForegroundColor DarkGray
Write-Host ''

# --- Confirm ------------------------------------------------------------------

if (-not $Force) {
    $confirm = Read-Host '  Proceed with uninstall? [Y/N]'
    if ($confirm -notmatch '^[Yy]') {
        Write-Host "`n  Uninstall cancelled." -ForegroundColor Yellow
        exit 0
    }
}

# --- 1. Stop and remove Windows service (Phase 14 hosting mode) ---------------

Write-Step "Removing Windows service: $SiteName"

if ($svcExists) {
    if ($svc.Status -eq 'Running') {
        Stop-Service -Name $SiteName -Force
        Write-Ok "Stopped service $SiteName"
    }
    sc.exe delete $SiteName | Out-Null
    Start-Sleep -Seconds 2
    Write-Ok "Removed service $SiteName"
} else {
    Write-Warn "Windows service '$SiteName' not found — skipping"
}

# --- 2. Stop and remove IIS site ----------------------------------------------

Write-Step "Removing IIS site: $SiteName"

if ($siteExists) {
    $siteState = (Get-WebsiteState -Name $SiteName).Value
    if ($siteState -eq 'Started') {
        Stop-Website -Name $SiteName
        Write-Ok "Stopped site $SiteName"
    }
    Remove-Website -Name $SiteName
    Write-Ok "Removed IIS site $SiteName"
} else {
    Write-Warn "IIS site '$SiteName' not found — skipping"
}

# --- 3. Stop and remove app pool ----------------------------------------------

Write-Step "Removing app pool: $AppPoolName"

if ($poolExists) {
    $poolState = (Get-WebAppPoolState -Name $AppPoolName).Value
    if ($poolState -eq 'Started') {
        Stop-WebAppPool -Name $AppPoolName
        Write-Ok "Stopped app pool $AppPoolName"
    }
    Remove-WebAppPool -Name $AppPoolName
    Write-Ok "Removed app pool $AppPoolName"
} else {
    Write-Warn "App pool '$AppPoolName' not found — skipping"
}

# --- 4. Remove deployment folder ----------------------------------------------

if (-not $KeepFiles) {
    Write-Step "Removing deployment folder: $PhysicalPath"

    if ($pathExists) {
        Remove-Item -Path $PhysicalPath -Recurse -Force
        Write-Ok "Removed $PhysicalPath"
    } else {
        Write-Warn "Path '$PhysicalPath' not found — skipping"
    }
} else {
    Write-Step 'Skipping file removal (-KeepFiles)'
    Write-Warn "Files retained at $PhysicalPath"
}

# --- 5. Remove upgrade backup folders (optional) ------------------------------

if ($RemoveBackups) {
    Write-Step 'Removing upgrade backup folders'

    if ($backupFolders.Count -gt 0) {
        foreach ($b in $backupFolders) {
            Remove-Item -Path $b.FullName -Recurse -Force
            Write-Ok "Removed $($b.FullName)"
        }
    } else {
        Write-Warn 'No backup folders found'
    }
} elseif ($backupFolders.Count -gt 0) {
    Write-Host ''
    Write-Warn "$($backupFolders.Count) upgrade backup folder(s) were left on disk:"
    foreach ($b in $backupFolders) {
        Write-Warn "  $($b.FullName)"
    }
    Write-Warn "Re-run with -RemoveBackups to delete them."
}

# --- Done ---------------------------------------------------------------------

Write-Host ''
Write-Host '======================================================' -ForegroundColor Cyan
Write-Host '  PassReset uninstalled successfully.' -ForegroundColor Green
Write-Host ''
if ($KeepFiles -and $pathExists) {
    Write-Host '  Files retained (use -KeepFiles was set):' -ForegroundColor Yellow
    Write-Host "    $PhysicalPath"
    Write-Host '  Delete the folder manually when no longer needed.'
    Write-Host ''
}
Write-Host '  IIS, IIS features, .NET Hosting Bundle,' -ForegroundColor DarkGray
Write-Host '  certificates, and other sites were not modified.' -ForegroundColor DarkGray
Write-Host '======================================================' -ForegroundColor Cyan
Write-Host ''

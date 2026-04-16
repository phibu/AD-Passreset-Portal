#Requires -Version 7.0
<#
.SYNOPSIS
    Builds the frontend, publishes the .NET app, and packages a release zip.

.DESCRIPTION
    Run this from the repo root before running Install-PassReset.ps1.
    Output:
      deploy\publish\            — raw publish output (used by Install-PassReset.ps1)
      deploy\PassReset-<ver>.zip — release package ready for distribution

    Release workflow (run on every version bump):
      1. Tag the commit: git tag v1.2.0
      2. Run: .\deploy\Publish-PassReset.ps1
      3. Upload deploy\PassReset-v1.2.0.zip to the GitHub release as an asset.
         The zip includes Install-PassReset.ps1, Uninstall-PassReset.ps1,
         and the published app so that users can deploy without building from source.

.PARAMETER Configuration
    Build configuration. Default: Release

.PARAMETER Version
    Version string embedded in the zip filename.
    Defaults to the latest git tag (e.g. v1.0.0), or 'dev' if no tag exists.
#>
param(
    [string] $Configuration = 'Release',
    [string] $Version       = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $PSScriptRoot   # one level up from deploy\
$clientApp  = Join-Path $repoRoot 'src\PassReset.Web\ClientApp'
$publishOut = Join-Path $PSScriptRoot 'publish'

# ── Resolve version ───────────────────────────────────────────────────────────
if (-not $Version) {
    $Version = git -C $repoRoot describe --tags --abbrev=0 2>$null
    if (-not $Version) { $Version = 'dev' }
}

# Strip 'v' prefix for MSBuild /p:Version (semver only, e.g. 1.2.0)
$semver = if ($Version -match '^v?(\d+\.\d+\.\d+.*)$') { $Matches[1] } else { '0.0.0-dev' }

# ── Validate production template against schema (Phase 8 / D-02) ─────────────
# Schema is the single source of truth. CI enforces this on every push; we
# re-check here as a belt-and-braces gate before cutting a release zip.

$webProject   = Join-Path $repoRoot 'src\PassReset.Web'
$templatePath = Join-Path $webProject 'appsettings.Production.template.json'
$schemaPath   = Join-Path $webProject 'appsettings.schema.json'

if (-not (Test-Path $schemaPath)) {
    throw "appsettings.schema.json not found at $schemaPath - publish cannot proceed without the schema."
}

Write-Host "`n[>>] Validating template against schema (pre-publish)..." -ForegroundColor Cyan
$validationErrors = @()
try {
    $valid = Test-Json `
        -Path $templatePath `
        -SchemaFile $schemaPath `
        -ErrorVariable validationErrors `
        -ErrorAction SilentlyContinue
} catch {
    $valid = $false
    $validationErrors = @($_.Exception.Message)
}
if (-not $valid) {
    Write-Host "`n  [ERR] Template validation failed against schema:" -ForegroundColor Red
    foreach ($e in $validationErrors) { Write-Host "        - $e" -ForegroundColor Red }
    throw "Pre-publish validation failed. Fix template or schema and retry."
}
Write-Host "  [OK] Template conforms to schema" -ForegroundColor Green

# ── Frontend ──────────────────────────────────────────────────────────────────
Write-Host "`n[>>] Building React frontend..." -ForegroundColor Cyan
Push-Location $clientApp
try {
    npm ci --silent
    npm run build
} finally {
    Pop-Location
}
Write-Host "  [OK] Frontend built → src\PassReset.Web\wwwroot\" -ForegroundColor Green

# ── .NET publish ──────────────────────────────────────────────────────────────
Write-Host "`n[>>] Publishing .NET app ($Configuration)..." -ForegroundColor Cyan
dotnet publish "$repoRoot\src\PassReset.Web\PassReset.Web.csproj" `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained false `
    --output $publishOut `
    /p:Version=$semver
Write-Host "  [OK] Published to $publishOut" -ForegroundColor Green

# Copy the production config template into publish output so the installer finds it
Copy-Item $templatePath -Destination $publishOut

# Copy the JSON Schema alongside the template — the installer (Phase 8) uses it
# for pre-flight Test-Json validation and additive-merge sync on upgrade.
Copy-Item $schemaPath -Destination $publishOut
Write-Host "  [OK] Copied appsettings.schema.json to $publishOut" -ForegroundColor Green

# ── Package release zip ───────────────────────────────────────────────────────
$zipName    = "PassReset-$Version.zip"
$zipPath    = Join-Path $PSScriptRoot $zipName
$stagingDir = Join-Path $PSScriptRoot '_staging'

Write-Host "`n[>>] Packaging $zipName..." -ForegroundColor Cyan

if (Test-Path $zipPath)    { Remove-Item $zipPath    -Force }
if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }

# Build staging layout:
#   _staging\Install-PassReset.ps1
#   _staging\Uninstall-PassReset.ps1
#   _staging\publish\*
$stagingPublish = Join-Path $stagingDir 'publish'
New-Item -ItemType Directory -Path $stagingPublish | Out-Null
Copy-Item "$PSScriptRoot\Install-PassReset.ps1"   -Destination $stagingDir
Copy-Item "$PSScriptRoot\Uninstall-PassReset.ps1" -Destination $stagingDir
Copy-Item "$publishOut\*" -Destination $stagingPublish -Recurse

try {
    Compress-Archive -Path "$stagingDir\*" -DestinationPath $zipPath -CompressionLevel Optimal
} finally {
    Remove-Item $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
}

$sizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "  [OK] $zipName ($sizeMb MB) → $zipPath" -ForegroundColor Green

Write-Host @"

Release package ready:
  $zipPath

Next steps:
  1. Copy the zip to the IIS server and extract it to a staging folder
  2. Run .\Install-PassReset.ps1 (it reads from deploy\publish\ on the build machine,
     or point -PublishFolder at the extracted folder on the server)
  3. Edit C:\inetpub\PassReset\appsettings.Production.json

"@ -ForegroundColor Yellow

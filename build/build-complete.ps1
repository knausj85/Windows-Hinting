param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$ExeOnly
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir
$ProjectDir = Join-Path $RepoRoot "Windows-Hinting"

Write-Host "=========================================="
Write-Host "Windows-Hinting Complete Build Script"
Write-Host "=========================================="
Write-Host "Configuration: $Configuration"
Write-Host "Build Installer: $(if ($ExeOnly) { 'False' } else { 'True' })"
Write-Host "Repository Root: $RepoRoot"
Write-Host ""

# By default, build both app and MSI (unless -ExeOnly specified)
$IsBuildingMsi = (-not $ExeOnly -and $Configuration -eq "Release")
$StepCount = if ($IsBuildingMsi) { '3' } else { '1' }

# Step 1: Build the executable (with signing if Release)
Write-Host "[1/$StepCount] Building Windows-Hinting executable..."
Write-Host ""

$BuildArgs = @(
    "$ProjectDir\Windows-Hinting.csproj"
    "/p:Configuration=$Configuration"
    "/nologo"
    "/v:minimal"
)

msbuild @BuildArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "ERROR: Windows-Hinting build failed with exit code $LASTEXITCODE"
    exit 1
}

Write-Host ""
Write-Host "[OK] Executable build completed successfully"
Write-Host ""

# Build installer if not explicitly skipped
if (-not $ExeOnly) {
    Write-Host "[2/$StepCount] Building MSI installer..."
    Write-Host ""

    $InstallerProject = "$RepoRoot\Windows-Hinting.Installer\Windows-Hinting.Installer.wixproj"

    if (-not (Test-Path $InstallerProject)) {
        Write-Host "ERROR: WiX installer project not found at: $InstallerProject"
        Write-Host "Please ensure the installer project is set up with a valid .wixproj file"
        exit 1
    }

    $InstallerArgs = @(
        $InstallerProject
        "/p:Configuration=$Configuration"
        "/p:SignOutput=false"
        "/nologo"
        "/v:minimal"
    )
    msbuild @InstallerArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "ERROR: Installer build failed with exit code $LASTEXITCODE"
        exit 1
    }

    Write-Host ""
    Write-Host "[OK] Installer build completed successfully"
    Write-Host ""

    # Verify MSI contents
    Write-Host "[3/$StepCount] Verifying installer contents..."
    Write-Host ""

    $MsiPath = "$RepoRoot\Windows-Hinting.Installer\bin\$Configuration\en-US\Windows-Hinting.msi"

    if (Test-Path $MsiPath) {
        $MsiSize = (Get-Item $MsiPath).Length / 1MB
        Write-Host "[OK] MSI created successfully"
        Write-Host "  Path: $MsiPath"
        Write-Host "  Size: $($MsiSize.ToString('0.0')) MB"
    }
    else {
        Write-Host ""
        Write-Host "ERROR: MSI file not found at expected location: $MsiPath"
        exit 1
    }

    Write-Host ""
}

Write-Host "=========================================="
Write-Host "[OK] Build completed successfully!"
Write-Host "=========================================="
Write-Host ""
Write-Host "Build Summary:"
Write-Host "  Configuration: $Configuration"
Write-Host "  Executable: bin\$Configuration\net8.0-windows\Windows-Hinting.exe"

if (-not $ExeOnly) {
    $InstallerPathFriendly = "Windows-Hinting.Installer\bin\$Configuration\en-US\Windows-Hinting.msi"
    Write-Host "  Installer: $InstallerPathFriendly"

    $ActualMsiPath = "$RepoRoot\Windows-Hinting.Installer\bin\$Configuration\en-US\Windows-Hinting.msi"
    if (Test-Path $ActualMsiPath) {
        $MsiSize = (Get-Item $ActualMsiPath).Length / 1MB
        Write-Host "  Installer Size: $($MsiSize.ToString('0.0')) MB"
    }
}

Write-Host ""

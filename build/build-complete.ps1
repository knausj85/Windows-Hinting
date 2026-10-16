param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$ExeOnly,
    # Package the MSI from the existing app build output without rebuilding the app.
    [switch]$InstallerOnly,
    [switch]$Portable,
    [ValidateSet("win-x64", "win-x86", "all")]
    [string]$Runtime = "all",
    # Selects the update channel baked into the build. "Stable" (default) uses
    # the GitHub "latest" release; "Beta" uses the "beta" tag and the
    # appcast-*-beta.xml naming. See UpdateService.cs and Windows-Hinting.csproj.
    [ValidateSet("Stable", "Beta")]
    [string]$UpdateChannel = "Stable"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir
$ProjectDir = Join-Path $RepoRoot "Windows-Hinting"

if ($ExeOnly -and $InstallerOnly) {
    Write-Host "ERROR: -ExeOnly and -InstallerOnly are mutually exclusive"
    exit 1
}

Write-Host "=========================================="
Write-Host "Windows-Hinting Complete Build Script"
Write-Host "=========================================="
Write-Host "Configuration: $Configuration"
Write-Host "Build Executable: $(if ($InstallerOnly -or $Portable) { 'False' } else { 'True' })"
Write-Host "Build Installer: $(if ($ExeOnly -or $Portable) { 'False' } else { 'True' })"
Write-Host "Portable Mode: $(if ($Portable) { 'True' } else { 'False' })"
if ($Portable) {
    Write-Host "Runtime: $Runtime"
}
Write-Host "Update Channel: $UpdateChannel"
Write-Host "Repository Root: $RepoRoot"
Write-Host ""

# Handle Portable self-contained publish path
if ($Portable) {
    $Runtimes = if ($Runtime -eq "all") { @("win-x64", "win-x86") } else { @($Runtime) }

    Write-Host "Building self-contained portable single-file executables..."
    Write-Host ""

    $StepCount = $Runtimes.Count
    $StepIndex = 0

    foreach ($Rid in $Runtimes) {
        $StepIndex++
        Write-Host "[$StepIndex/$StepCount] Publishing self-contained build for $Rid..."
        Write-Host ""

        # Restore, build and publish in one step. The single-file / self-contained /
        # no-UIAccess settings live in Properties\PublishProfiles\Portable-<rid>.pubxml
        # (shared with CI and Visual Studio's Publish).
        Write-Host "  Publishing self-contained single-file package..."
        $PublishDir = "$ProjectDir\bin-portable\publish\$Rid"

        # Clean publish output directory
        if (Test-Path $PublishDir) {
            Remove-Item $PublishDir -Recurse -Force
        }

        $PublishArgs = @(
            "publish"
            "$ProjectDir\Windows-Hinting.csproj"
            "-c", $Configuration
            "-p:PublishProfile=Portable-$Rid"
            "-p:UpdateChannel=$UpdateChannel"
            "-nologo"
            "-v:minimal"
        )

        dotnet @PublishArgs

        if ($LASTEXITCODE -ne 0) {
            Write-Host ""
            Write-Host "ERROR: Self-contained publish failed for $Rid with exit code $LASTEXITCODE"
            exit 1
        }

        $ExePath = Join-Path $PublishDir "Windows-Hinting.exe"
        if (Test-Path $ExePath) {
            $ExeSize = (Get-Item $ExePath).Length / 1MB
            Write-Host ""
            Write-Host "[OK] Self-contained executable published for $Rid"
            Write-Host "  Path: $ExePath"
            Write-Host "  Size: $($ExeSize.ToString('0.0')) MB"
            Write-Host ""
        } else {
            Write-Host ""
            Write-Host "ERROR: Executable not found at expected location: $ExePath"
            exit 1
        }
    }

    Write-Host "=========================================="
    Write-Host "[OK] Portable build completed successfully!"
    Write-Host "=========================================="
    Write-Host ""
    Write-Host "Build Summary:"
    foreach ($Rid in $Runtimes) {
        $ExePath = "$ProjectDir\bin-portable\publish\$Rid\Windows-Hinting.exe"
        if (Test-Path $ExePath) {
            $ExeSize = (Get-Item $ExePath).Length / 1MB
            Write-Host "  $Rid : $ExePath ($($ExeSize.ToString('0.0')) MB)"
        }
    }
    Write-Host ""
    exit 0
}

# Default: build the app, then the MSI. -ExeOnly skips the MSI; -InstallerOnly packages
# the existing app output without rebuilding it (CI signs the app binaries in between,
# and rebuilding through the ProjectReference would overwrite them).
$BuildExe = -not $InstallerOnly
$BuildMsi = -not $ExeOnly
$StepCount = @($BuildExe, $BuildMsi, $BuildMsi).Where({ $_ }).Count
$Step = 0
$MsiPath = "$RepoRoot\Windows-Hinting.Installer\bin\$Configuration\en-US\Windows-Hinting.msi"

if ($BuildExe) {
    $Step++
    Write-Host "[$Step/$StepCount] Building Windows-Hinting executable..."
    Write-Host ""

    $BuildArgs = @(
        "build"
        "$ProjectDir\Windows-Hinting.csproj"
        "-c", $Configuration
        "-p:UpdateChannel=$UpdateChannel"
        "-nologo"
        "-v:minimal"
    )
    dotnet @BuildArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "ERROR: Windows-Hinting build failed with exit code $LASTEXITCODE"
        exit 1
    }

    Write-Host ""
    Write-Host "[OK] Executable build completed successfully"
    Write-Host ""
}

if ($BuildMsi) {
    $Step++
    Write-Host "[$Step/$StepCount] Building MSI installer..."
    Write-Host ""

    $InstallerProject = "$RepoRoot\Windows-Hinting.Installer\Windows-Hinting.Installer.wixproj"

    $InstallerArgs = @(
        "build"
        $InstallerProject
        "-c", $Configuration
        "-nologo"
        "-v:minimal"
    )
    if ($InstallerOnly) {
        $InstallerArgs += "-p:BuildProjectReferences=false"
    }
    dotnet @InstallerArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "ERROR: Installer build failed with exit code $LASTEXITCODE"
        exit 1
    }

    Write-Host ""
    Write-Host "[OK] Installer build completed successfully"
    Write-Host ""

    $Step++
    Write-Host "[$Step/$StepCount] Verifying installer output..."
    Write-Host ""

    if (-not (Test-Path $MsiPath)) {
        Write-Host "ERROR: MSI file not found at expected location: $MsiPath"
        exit 1
    }
    Write-Host "[OK] MSI created successfully"
    Write-Host ""
}

Write-Host "=========================================="
Write-Host "[OK] Build completed successfully!"
Write-Host "=========================================="
Write-Host ""
Write-Host "Build Summary:"
Write-Host "  Configuration: $Configuration"
if ($BuildExe) {
    Write-Host "  Executable: $ProjectDir\bin\$Configuration\net10.0-windows\Windows-Hinting.exe"
}
if ($BuildMsi) {
    $MsiSize = (Get-Item $MsiPath).Length / 1MB
    Write-Host "  Installer: $MsiPath ($($MsiSize.ToString('0.0')) MB)"
}
Write-Host ""
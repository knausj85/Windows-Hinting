param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$ExeOnly,
    [switch]$Portable,
    [ValidateSet("win-x64", "win-x86", "all")]
    [string]$Runtime = "all"
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
Write-Host "Portable Mode: $(if ($Portable) { 'True' } else { 'False' })"
if ($Portable) {
    Write-Host "Runtime: $Runtime"
}
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

        # Step 1: Restore with the specific RID (SelfContained so runtime packs are fetched)
        Write-Host "  Restoring packages for $Rid..."
        $RestoreArgs = @(
            "restore"
            "$ProjectDir\Windows-Hinting.csproj"
            "-r", $Rid
            "-p:SelfContained=true"
            "-p:PublishReadyToRun=true"
        )
        dotnet @RestoreArgs

        if ($LASTEXITCODE -ne 0) {
            Write-Host ""
            Write-Host "ERROR: dotnet restore failed for $Rid with exit code $LASTEXITCODE"
            exit 1
        }

        # Step 2: Use msbuild (Framework) to compile and generate COM interop
        # Override ApplicationManifest to use UIAccess-disabled manifest for portable builds
        # SelfContained=true on build so runtime packs are resolved for the publish step
        Write-Host "  Building with msbuild to generate COM interop..."
        $BuildArgs = @(
            "$ProjectDir\Windows-Hinting.csproj"
            "/p:Configuration=$Configuration"
            "/p:RuntimeIdentifier=$Rid"
            "/p:SelfContained=true"
            "/p:ApplicationManifest=app.debug.manifest"
            "/p:PortableBuild=true"
            "/nologo"
            "/v:minimal"
        )
        msbuild @BuildArgs

        if ($LASTEXITCODE -ne 0) {
            Write-Host ""
            Write-Host "ERROR: msbuild compile failed for $Rid with exit code $LASTEXITCODE"
            exit 1
        }

        # Step 3: Use msbuild /t:Publish for self-contained single-file (reuses COM interop from step 2)
        Write-Host "  Publishing self-contained single-file package..."
        $PublishDir = "$ProjectDir\bin-portable\publish\$Rid"

        # Clean publish output directory
        if (Test-Path $PublishDir) {
            Remove-Item $PublishDir -Recurse -Force
        }

        $PublishArgs = @(
            "$ProjectDir\Windows-Hinting.csproj"
            "/t:Publish"
            "/p:Configuration=$Configuration"
            "/p:RuntimeIdentifier=$Rid"
            "/p:ApplicationManifest=app.debug.manifest"
            "/p:SelfContained=true"
            "/p:PublishSingleFile=true"
            "/p:IncludeNativeLibrariesForSelfExtract=true"
            "/p:EnableCompressionInSingleFile=true"
            "/p:PublishReadyToRun=true"
            "/p:DebugType=embedded"
            "/p:PublishDir=$PublishDir\"
            "/p:PortableBuild=true"
            "/p:NoBuild=true"
            "/nologo"
            "/v:minimal"
        )

        msbuild @PublishArgs

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

# By default, build both app and MSI (unless -ExeOnly specified)
$IsBuildingMsi = (-not $ExeOnly -and $Configuration -eq "Release")
$StepCount = if ($IsBuildingMsi) { '3' } else { '1' }

# Step 1: Build the executable
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
Write-Host "  Executable: bin\$Configuration\net10.0-windows\Windows-Hinting.exe"

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

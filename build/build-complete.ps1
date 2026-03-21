param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$ExeOnly,
    [switch]$SkipSigning,
    [string]$CertPath = "",
    [string]$CertPassword = "WindowsHinting_BuildCert_2024"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir

if ($CertPath -eq "" -and -not $SkipSigning -and $Configuration -eq "Release") {
    $CertPath = "$RepoRoot\certs\WindowsHinting_CodeSign.pfx"
}

Write-Host "=========================================="
Write-Host "Windows-Hinting Complete Build Script"
Write-Host "=========================================="
Write-Host "Configuration: $Configuration"
Write-Host "Build Installer: $(if ($ExeOnly) { 'False' } else { 'True' })"
Write-Host "Skip Signing: $SkipSigning"
Write-Host "Repository Root: $RepoRoot"
Write-Host ""
Write-Host "--- Parameters (debug) ---"
Write-Host "  -Configuration : $Configuration"
Write-Host "  -ExeOnly       : $ExeOnly"
Write-Host "  -SkipSigning   : $SkipSigning"
Write-Host "  -CertPath      : $(if ($CertPath) { $CertPath } else { '(empty)' })"
Write-Host "  -CertPassword  : $(if ($CertPassword) { '(set)' } else { '(empty)' })"
Write-Host "  Raw args       : $($args -join ' ')"
Write-Host "--------------------------"
Write-Host ""

# By default, build both app and MSI (unless -ExeOnly specified)
$IsBuildingMsi = (-not $ExeOnly -and $Configuration -eq "Release")
$StepCount = if ($IsBuildingMsi) { '4' } elseif (-not $ExeOnly) { '3' } else { '1' }

# Step 1: Build the executable (with signing if Release)
Write-Host "[1/$StepCount] Building Windows-Hinting executable..."
Write-Host ""

$BuildArgs = @(
    "$RepoRoot\Windows-Hinting.csproj"
    "/p:Configuration=$Configuration"
    "/nologo"
    "/v:minimal"
)

if ($SkipSigning) {
    # Disable both signing MSBuild targets via the SkipCodeSigning property.
    # Also clear CodeSigningCertPath as a belt-and-suspenders guard.
    $BuildArgs += "/p:SkipCodeSigning=true"
    $BuildArgs += "/p:CodeSigningCertPath="
} elseif ($Configuration -eq "Release") {
    $BuildArgs += "/p:CodeSigningCertPath=$CertPath"
    $BuildArgs += "/p:CodeSigningPassword=$CertPassword"
}

msbuild @BuildArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "ERROR: Windows-Hinting build failed with exit code $LASTEXITCODE"
    exit 1
}

Write-Host ""
Write-Host "[OK] Executable build completed successfully"
Write-Host ""

# Verify signature if Release build
if (($Configuration -eq "Release") -and (-not $SkipSigning)) {
    Write-Host "[$(if ($IsBuildingMsi) { '2' } else { 'Verify' })/$StepCount] Verifying executable signature..."
    $ExePath = "$RepoRoot\bin\$Configuration\net8.0-windows\Windows-Hinting.exe"

    if (Test-Path $ExePath) {
        $sig = Get-AuthenticodeSignature -FilePath $ExePath
        if ($sig.SignerCertificate) {
            Write-Host "[OK] Executable is signed"
            Write-Host "  Subject: $($sig.SignerCertificate.Subject)"
            Write-Host "  Valid until: $($sig.SignerCertificate.NotAfter)"
        }
        else {
            Write-Host "WARNING: Executable does not appear to be signed"
        }
    }
    Write-Host ""
}

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
        "/nologo"
        "/v:minimal"
    )
    if ($SkipSigning) {
        # Disable WiX's built-in MSI signing when signing is skipped.
        $InstallerArgs += "/p:SignOutput=false"
    }
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

    # If Release config with signing, verify MSI contains signed executable
    if (($Configuration -eq "Release") -and (-not $SkipSigning)) {
        Write-Host "[4/$StepCount] Verifying signed executable in MSI..."
        Write-Host ""

        # Extract executable from MSI for verification
        $TempDir = "$RepoRoot\temp_msi_verify_$([System.IO.Path]::GetRandomFileName())"
        New-Item -ItemType Directory -Path $TempDir -Force | Out-Null

        try {
            # Use lessmsi to extract if available, otherwise inform user
            $LessMsi = Get-Command lessmsi -ErrorAction SilentlyContinue

            if ($LessMsi) {
                & lessmsi x $MsiPath "$TempDir\" | Out-Null

                $ExtractedExePath = Get-ChildItem -Path $TempDir -Recurse -Filter "Windows-Hinting.exe" | Select-Object -First 1

                if ($ExtractedExePath) {
                    $sig = Get-AuthenticodeSignature -FilePath $ExtractedExePath.FullName

                    if ($sig.SignerCertificate) {
                        Write-Host "[OK] MSI contains signed executable"
                        Write-Host "  Subject: $($sig.SignerCertificate.Subject)"
                        Write-Host "  Valid until: $($sig.SignerCertificate.NotAfter)"
                    }
                    else {
                        Write-Host "WARNING: Executable in MSI does not appear to be signed"
                    }
                }
            }
            else {
                Write-Host "[OK] MSI created (lessmsi not available for detailed verification)"
                Write-Host "  To verify signed executable in MSI, install 'lessmsi' tool"
            }
        }
        finally {
            if (Test-Path $TempDir) {
                Remove-Item -Path $TempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        Write-Host ""
    }
}

Write-Host "=========================================="
Write-Host "[OK] Build completed successfully!"
Write-Host "=========================================="
Write-Host ""
Write-Host "Build Summary:"
Write-Host "  Configuration: $Configuration"
Write-Host "  Executable: bin\$Configuration\net8.0-windows\Windows-Hinting.exe"

if (($Configuration -eq "Release") -and (-not $SkipSigning)) {
    Write-Host "  Signing: Enabled"
}

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

if (-not $ExeOnly) {
    Write-Host "Next Steps:"
    Write-Host "  1. Test the installer by running the MSI file"
    Write-Host "  2. Verify the signed executable is included in the installation"
    Write-Host "  3. Test UIAccess functionality in the installed application"
    Write-Host "  4. Verify that the installed executable maintains its signature"
    Write-Host ""
}

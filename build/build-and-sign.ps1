param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$RegenerateCert,
    [switch]$SkipSigning,
    [string]$CertPath = "",
    [string]$CertPassword = "HintOverlay_BuildCert_2024"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir

if ($CertPath -eq "") {
    $CertPath = "$RepoRoot\certs\HintOverlay_CodeSign.pfx"
}

Write-Host "=========================================="
Write-Host "HintOverlay Build and Sign Script"
Write-Host "=========================================="
Write-Host "Configuration: $Configuration"
Write-Host "Repository Root: $RepoRoot"
Write-Host ""

# Step 1: Generate or verify certificate
if (-not $SkipSigning) {
    Write-Host "[1/3] Ensuring code signing certificate exists..."
    $CertScript = Join-Path $ScriptDir "generate-signing-cert.ps1"

    if ($RegenerateCert) {
        Write-Host "  (Regenerating certificate due to -RegenerateCert flag)"
        & $CertScript -CertPassword $CertPassword -Force
    } else {
        & $CertScript -CertPassword $CertPassword
    }

    Write-Host ""
}

# Step 2: Build the project
Write-Host "[2/3] Building HintOverlay ($Configuration)..."
$ProjectPath = Join-Path $RepoRoot "HintOverlay.csproj"

if ($SkipSigning) {
    msbuild $ProjectPath /p:Configuration=$Configuration /nologo /v:minimal
} else {
    msbuild $ProjectPath /p:Configuration=$Configuration /p:CodeSigningCertPath="$CertPath" /p:CodeSigningPassword="$CertPassword" /nologo /v:minimal
}

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "Build failed"
    exit 1
}

Write-Host "✓ Build completed successfully"
Write-Host ""

# Step 3: Verify signature (if not skipped)
if (-not $SkipSigning) {
    Write-Host "[3/3] Verifying executable signature..."

    $ExePath = Join-Path $RepoRoot "bin\$Configuration\net8.0-windows\HintOverlay.exe"

    if (-not (Test-Path $ExePath)) {
        Write-Host "Executable not found at: $ExePath"
        exit 1
    }

    # Get signature details with PowerShell
    $AuthSig = Get-AuthenticodeSignature -FilePath $ExePath
    Write-Host ""
    Write-Host "Signature Details:"
    Write-Host "  Status: $($AuthSig.Status)"
    if ($AuthSig.SignerCertificate) {
        Write-Host "  Signer: $($AuthSig.SignerCertificate.Subject)"
        Write-Host "  Thumbprint: $($AuthSig.SignerCertificate.Thumbprint)"
    }
    Write-Host ""
}

Write-Host "=========================================="
Write-Host "✓ Build process completed successfully!"
Write-Host "=========================================="
Write-Host ""
Write-Host "Output:"
Write-Host "  Executable: bin\$Configuration\net8.0-windows\HintOverlay.exe"
if (-not $SkipSigning) {
    Write-Host "  Certificate: $CertPath"
}

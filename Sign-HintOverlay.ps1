# Sign-WindowsHinting.ps1
# Code signing script for Windows-Hinting executable

<#
.SYNOPSIS
    Signs the Windows-Hinting executable with a code signing certificate
.DESCRIPTION
    This script signs the Windows-Hinting.exe file with a code signing certificate.
    Supports both self-signed (development) and commercial (production) certificates.
.PARAMETER BuildConfiguration
    Build configuration: Debug or Release (default: Release)
.PARAMETER CertificatePath
    Path to the PFX certificate file
.PARAMETER CertificatePassword
    Password for the PFX certificate (prompted if not provided)
.PARAMETER TimestampServer
    URL of timestamp server (default: Sectigo public server)
.PARAMETER ProjectRoot
    Root directory of the project (default: current directory)
.EXAMPLE
    .\Sign-WindowsHinting.ps1 -BuildConfiguration Release

    Prompts for certificate password, then signs the Release executable.
.EXAMPLE
    .\Sign-WindowsHinting.ps1 -CertificatePassword "MyPassword123"

    Signs using provided password.
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$BuildConfiguration = "Release",

    [string]$CertificatePath = "$env:USERPROFILE\WindowsHinting_CodeSign.pfx",

    [string]$CertificatePassword = "",

    [string]$TimestampServer = "http://timestamp.sectigo.com",

    [string]$ProjectRoot = (Get-Location).Path
)

# ============================================================================
# Configuration
# ============================================================================

$exePath = Join-Path $ProjectRoot "bin\$BuildConfiguration\net8.0-windows\Windows-Hinting.exe"
$signtool = $null

# Try to find signtool.exe in common locations
$possibleLocations = @(
    "C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages\microsoft.windows.sdk.buildtools\10.0.26100.1742\bin\10.0.26100.0\x64\signtool.exe",
    "C:\Program Files (x86)\Microsoft SDKs\ClickOnce\SignTool\signtool.exe",
    "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe",
    "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\signtool.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\signtool.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\Tools\signtool.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\Tools\signtool.exe"
)

foreach ($location in $possibleLocations) {
    if (Test-Path $location) {
        $signtool = $location
        break
    }
}

# ============================================================================
# Validation
# ============================================================================

Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "Windows-Hinting Code Signing Tool" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Validate executable exists
if (-not (Test-Path $exePath)) {
    Write-Host "✗ ERROR: Executable not found" -ForegroundColor Red
    Write-Host "  Expected: $exePath" -ForegroundColor Red
    Write-Host ""
    Write-Host "Make sure you have:"
    Write-Host "  1. Built the project in $BuildConfiguration configuration"
    Write-Host "  2. Set the correct -ProjectRoot parameter"
    exit 1
}

# Validate certificate exists
if (-not (Test-Path $CertificatePath)) {
    Write-Host "✗ ERROR: Certificate not found" -ForegroundColor Red
    Write-Host "  Expected: $CertificatePath" -ForegroundColor Red
    Write-Host ""
    Write-Host "To create a self-signed certificate, run:"
    Write-Host "  .\Create-CodeSigningCert.ps1" -ForegroundColor Yellow
    exit 1
}

# Validate signtool.exe
if (-not $signtool -or -not (Test-Path $signtool)) {
    Write-Host "✗ ERROR: signtool.exe not found" -ForegroundColor Red
    Write-Host ""
    Write-Host "signtool.exe is included in:"
    Write-Host "  1. Windows SDK (https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/)"
    Write-Host "  2. Visual Studio (with C++ tools or SDK selection)"
    Write-Host ""
    Write-Host "After installing, you can add it to PATH or update this script."
    exit 1
}

# Get certificate password if not provided
if (-not $CertificatePassword) {
    $securePassword = Read-Host -Prompt "Enter certificate password" -AsSecureString
    $CertificatePassword = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [System.Runtime.InteropServices.Marshal]::SecureStringToCoTaskMemUnicode($securePassword)
    )
}

# ============================================================================
# Display Configuration
# ============================================================================

Write-Host "Configuration:" -ForegroundColor Green
Write-Host "  Build Configuration: $BuildConfiguration"
Write-Host "  Executable: $exePath"
Write-Host "  Certificate: $CertificatePath"
Write-Host "  Timestamp Server: $TimestampServer"
Write-Host "  signtool Location: $signtool"
Write-Host ""

# ============================================================================
# Sign the Executable
# ============================================================================

Write-Host "Signing executable..." -ForegroundColor Yellow

$signCommand = @(
    $signtool,
    "sign",
    "/f", "`"$CertificatePath`"",
    "/p", "`"$CertificatePassword`"",
    "/t", $TimestampServer,
    "/fd", "SHA256",
    "/v",
    "`"$exePath`""
)

$signOutput = & cmd /c ($signCommand -join " ") 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Executable signed successfully" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "✗ Signing failed" -ForegroundColor Red
    Write-Host ""
    Write-Host "Output:" -ForegroundColor Yellow
    Write-Host $signOutput
    Write-Host ""
    exit 1
}

# ============================================================================
# Verify Signature
# ============================================================================

Write-Host "Verifying signature..." -ForegroundColor Yellow

$verifyCommand = @(
    $signtool,
    "verify",
    "/pa",
    "`"$exePath`""
)

$verifyOutput = & cmd /c ($verifyCommand -join " ") 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Signature verified successfully" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "✗ Signature verification failed" -ForegroundColor Red
    Write-Host ""
    Write-Host "Output:" -ForegroundColor Yellow
    Write-Host $verifyOutput
    Write-Host ""
    exit 1
}

# ============================================================================
# Display Certificate Details
# ============================================================================

Write-Host "Certificate Details:" -ForegroundColor Green

$authSig = Get-AuthenticodeSignature $exePath

Write-Host "  Status: $($authSig.Status)" -ForegroundColor $(
    if ($authSig.Status -eq "Valid") { "Green" } else { "Red" }
)
Write-Host "  Subject: $($authSig.SignerCertificate.Subject)"
Write-Host "  Issuer: $($authSig.SignerCertificate.Issuer)"
Write-Host "  Thumbprint: $($authSig.SignerCertificate.Thumbprint)"
Write-Host "  Valid From: $($authSig.SignerCertificate.NotBefore)"
Write-Host "  Valid Until: $($authSig.SignerCertificate.NotAfter)"
Write-Host "  Algorithm: $($authSig.SignatureAlgorithm)"
Write-Host "  Hash Algorithm: $($authSig.HashAlgorithm)"
Write-Host ""

# ============================================================================
# File Information
# ============================================================================

Write-Host "File Information:" -ForegroundColor Green
$fileInfo = Get-Item $exePath
Write-Host "  File: $($fileInfo.Name)"
Write-Host "  Size: $([Math]::Round($fileInfo.Length / 1MB, 2)) MB"
Write-Host "  Created: $($fileInfo.CreationTime)"
Write-Host "  Modified: $($fileInfo.LastWriteTime)"
Write-Host ""

# ============================================================================
# Summary
# ============================================================================

Write-Host "===================================================================" -ForegroundColor Green
Write-Host "OK Signing Complete!" -ForegroundColor Green
Write-Host "===================================================================" -ForegroundColor Green
Write-Host ""
Write-Host "The executable is now signed and ready for distribution."
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Test the signed executable: .\bin\$BuildConfiguration\net8.0-windows\Windows-Hinting.exe"
Write-Host "  2. Package for distribution"
Write-Host "  3. Share with users"
Write-Host ""

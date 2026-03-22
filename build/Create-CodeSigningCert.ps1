# Create-CodeSigningCert.ps1
# Creates a self-signed code signing certificate for Windows-Hinting

<#
.SYNOPSIS
    Creates a self-signed code signing certificate for Windows-Hinting
.DESCRIPTION
    This script creates a self-signed certificate suitable for code signing.
    The certificate is stored in the current user's certificate store and
    exported as a PFX file for use with signtool.exe.

    For production/distribution, use a commercial certificate instead.
.PARAMETER CertificateName
    Subject name for the certificate (default: "Windows-Hinting Development")
.PARAMETER FriendlyName
    Friendly name for the certificate (default: "Windows-Hinting Code Signing")
.PARAMETER ValidityYears
    Number of years the certificate is valid (default: 10)
.PARAMETER OutputPath
    Directory to export the certificate files (default: $env:USERPROFILE)
.PARAMETER ExportPassword
    Password to protect the PFX file (prompted if not provided)
.EXAMPLE
    .\Create-CodeSigningCert.ps1

    Creates a certificate with default settings and prompts for password.
.EXAMPLE
    .\Create-CodeSigningCert.ps1 -ValidityYears 5 -ExportPassword "MySecurePassword"

    Creates a 5-year certificate with the specified password.
#>

param(
    [string]$CertificateName = "Windows-Hinting Development",
    [string]$FriendlyName = "Windows-Hinting Code Signing",
    [int]$ValidityYears = 10,
    [string]$OutputPath = $env:USERPROFILE,
    [SecureString]$ExportPassword = ""
)

# ============================================================================
# Validation & Setup
# ============================================================================

Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "Self-Signed Code Signing Certificate Creator" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Check if running as Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")

if (-not $isAdmin) {
    Write-Host "⚠ WARNING: Not running as Administrator" -ForegroundColor Yellow
    Write-Host "  Some certificate operations may fail."
    Write-Host "  Recommend: Right-click PowerShell and select 'Run as administrator'"
    Write-Host ""
}

# Validate output path
if (-not (Test-Path $OutputPath)) {
    Write-Host "✗ ERROR: Output path not found: $OutputPath" -ForegroundColor Red
    exit 1
}

Write-Host "Configuration:" -ForegroundColor Green
Write-Host "  Subject: CN=$CertificateName"
Write-Host "  Friendly Name: $FriendlyName"
Write-Host "  Valid For: $ValidityYears years"
Write-Host "  Output Path: $OutputPath"
Write-Host ""

# Get password if not provided
if (-not $ExportPassword) {
    $securePassword = Read-Host -Prompt "Enter a password to protect the certificate" -AsSecureString
    $ExportPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [System.Runtime.InteropServices.Marshal]::SecureStringToCoTaskMemUnicode($securePassword)
    )
}

if ([string]::IsNullOrWhiteSpace($ExportPassword)) {
    Write-Host "✗ ERROR: Password cannot be empty" -ForegroundColor Red
    exit 1
}

# ============================================================================
# Create Self-Signed Certificate
# ============================================================================

Write-Host "Creating self-signed certificate..." -ForegroundColor Yellow

try {
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject "CN=$CertificateName" `
        -FriendlyName $FriendlyName `
        -NotAfter (Get-Date).AddYears($ValidityYears) `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -KeyUsageNotPresent @() `
        -KeyUsage DigitalSignature `
        -ErrorAction Stop

    Write-Host "✓ Certificate created successfully" -ForegroundColor Green
    Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor Green
    Write-Host ""
} catch {
    Write-Host "✗ ERROR: Failed to create certificate" -ForegroundColor Red
    Write-Host "  $_" -ForegroundColor Red
    exit 1
}

# ============================================================================
# Export to PFX File
# ============================================================================

$pfxPath = Join-Path $OutputPath "WindowsHinting_CodeSign.pfx"

Write-Host "Exporting certificate to PFX file..." -ForegroundColor Yellow

try {
    $securePassword = ConvertTo-SecureString -String $ExportPassword -AsPlainText -Force

    Export-PfxCertificate `
        -Cert $cert `
        -FilePath $pfxPath `
        -Password $securePassword `
        -ErrorAction Stop | Out-Null

    Write-Host "✓ PFX file created" -ForegroundColor Green
    Write-Host "  Location: $pfxPath" -ForegroundColor Green
    Write-Host ""
} catch {
    Write-Host "✗ ERROR: Failed to export PFX file" -ForegroundColor Red
    Write-Host "  $_" -ForegroundColor Red
    exit 1
}

# ============================================================================
# Export Certificate File (for adding to Trusted Publishers)
# ============================================================================

$cerPath = Join-Path $OutputPath "WindowsHinting_CodeSign.cer"

Write-Host "Exporting certificate to CER file..." -ForegroundColor Yellow

try {
    Export-Certificate `
        -Cert $cert `
        -FilePath $cerPath `
        -ErrorAction Stop | Out-Null

    Write-Host "✓ CER file created" -ForegroundColor Green
    Write-Host "  Location: $cerPath" -ForegroundColor Green
    Write-Host ""
} catch {
    Write-Host "✗ ERROR: Failed to export CER file" -ForegroundColor Red
    Write-Host "  $_" -ForegroundColor Red
    exit 1
}

# ============================================================================
# Display Certificate Details
# ============================================================================

Write-Host "Certificate Details:" -ForegroundColor Green
Write-Host "  Subject: $($cert.Subject)"
Write-Host "  Thumbprint: $($cert.Thumbprint)"
Write-Host "  Valid From: $($cert.NotBefore)"
Write-Host "  Valid Until: $($cert.NotAfter)"
Write-Host "  Serial Number: $($cert.SerialNumber)"
Write-Host ""

# ============================================================================
# Optional: Add to Trusted Publishers (for testing)
# ============================================================================

Write-Host "Optional: Add certificate to Trusted Publishers?" -ForegroundColor Yellow
Write-Host "This is useful for testing but NOT recommended for production." -ForegroundColor Yellow
Write-Host "For production, use a commercial certificate from a trusted CA." -ForegroundColor Yellow
$addToTrusted = Read-Host "Add to Trusted Publishers now? (y/N)"

if ($addToTrusted -eq "y" -or $addToTrusted -eq "Y") {
    Write-Host ""
    Write-Host "Adding certificate to Trusted Publishers..." -ForegroundColor Yellow

    if ($isAdmin) {
        try {
            Import-Certificate `
                -FilePath $cerPath `
                -CertStoreLocation "Cert:\LocalMachine\TrustedPublisher" `
                -ErrorAction Stop | Out-Null

            Write-Host "✓ Certificate added to Trusted Publishers" -ForegroundColor Green
            Write-Host "  Note: This only works for the current user's applications."
            Write-Host ""
        } catch {
            Write-Host "✗ ERROR: Failed to import to Trusted Publishers" -ForegroundColor Red
            Write-Host "  $_" -ForegroundColor Red
            Write-Host ""
        }
    } else {
        Write-Host "✗ Cannot add to Trusted Publishers without Administrator rights" -ForegroundColor Yellow
        Write-Host "  Run PowerShell as Administrator to enable this." -ForegroundColor Yellow
        Write-Host ""
    }
}

# ============================================================================
# Summary
# ============================================================================

Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host "✓ Certificate Creation Complete!" -ForegroundColor Green
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host ""

Write-Host "Files Created:" -ForegroundColor Green
Write-Host "  1. PFX (for signing): $pfxPath" -ForegroundColor Green
Write-Host "  2. CER (for distribution): $cerPath" -ForegroundColor Green
Write-Host ""

Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "  1. Build your application:" -ForegroundColor Cyan
Write-Host "     dotnet build -c Release" -ForegroundColor Yellow
Write-Host ""
Write-Host "  2. Sign the executable:" -ForegroundColor Cyan
Write-Host "     .\Sign-WindowsHinting.ps1 -BuildConfiguration Release" -ForegroundColor Yellow
Write-Host ""
Write-Host "  3. Distribute the signed executable" -ForegroundColor Cyan
Write-Host ""

Write-Host "Important Notes:" -ForegroundColor Yellow
Write-Host "  ⚠ This is a SELF-SIGNED certificate for DEVELOPMENT only" -ForegroundColor Yellow
Write-Host "  ⚠ For production/distribution, purchase a certificate from:" -ForegroundColor Yellow
Write-Host "    • Sectigo (formerly Comodo)" -ForegroundColor Yellow
Write-Host "    • DigiCert" -ForegroundColor Yellow
Write-Host "    • GlobalSign" -ForegroundColor Yellow
Write-Host "    • Other trusted Certificate Authorities" -ForegroundColor Yellow
Write-Host ""

Write-Host "Certificate Password:" -ForegroundColor Yellow
Write-Host "  Store this password securely!" -ForegroundColor Yellow
Write-Host "  You'll need it to sign executables and in CI/CD pipelines." -ForegroundColor Yellow
Write-Host ""

Write-Host "For CI/CD (GitHub Actions, Azure DevOps, etc.):" -ForegroundColor Cyan
Write-Host "  1. Keep the PFX file secure (use secrets/vault)" -ForegroundColor Cyan
Write-Host "  2. Keep the password secure (use secrets/vault)" -ForegroundColor Cyan
Write-Host "  3. Use the signing script in your pipeline" -ForegroundColor Cyan
Write-Host ""

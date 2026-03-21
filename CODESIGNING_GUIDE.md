# Code Signing Guide for HintOverlay

## Overview

Code signing your executable allows users to verify the authenticity and integrity of your application. This is especially important since HintOverlay requires `uiAccess` which makes it sensitive for security.

## Prerequisites

- **Windows 10/11** with development tools
- **signtool.exe** (included with Visual Studio or Windows SDK)
- **Certificate** (self-signed for development, purchased for distribution)

## Two Approaches

### Option A: Self-Signed Certificate (Development)

Best for testing and internal use.

#### Step 1: Create Self-Signed Certificate

```powershell
# Run PowerShell as Administrator

# Create a self-signed certificate valid for 10 years
$cert = New-SelfSignedCertificate `
  -Type CodeSigningCert `
  -Subject "CN=HintOverlay Development" `
  -FriendlyName "HintOverlay Code Signing" `
  -NotAfter (Get-Date).AddYears(10) `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -KeyUsageNotPresent @() `
  -KeyUsage DigitalSignature

# Export to PFX file (with password protection)
$pfxPassword = ConvertTo-SecureString -String "YourPassword123" -AsPlainText -Force
Export-PfxCertificate `
  -Cert $cert `
  -FilePath "$env:USERPROFILE\HintOverlay_CodeSign.pfx" `
  -Password $pfxPassword

# Export certificate to trusted store (optional, for testing)
Export-Certificate `
  -Cert $cert `
  -FilePath "$env:USERPROFILE\HintOverlay_CodeSign.cer"

Write-Host "✓ Certificate created: $env:USERPROFILE\HintOverlay_CodeSign.pfx"
Write-Host "✓ Thumbprint: $($cert.Thumbprint)"
```

#### Step 2: Add Certificate to Trusted Publishers (Testing Only)

```powershell
# Run PowerShell as Administrator
# This makes Windows trust your self-signed certificate

Import-Certificate `
  -FilePath "$env:USERPROFILE\HintOverlay_CodeSign.cer" `
  -CertStoreLocation "Cert:\LocalMachine\TrustedPublisher"

Write-Host "✓ Certificate added to Trusted Publishers"
```

### Option B: Commercial Certificate (Production)

For distributing to end users, use a commercial certificate from:
- **Sectigo** (~$99/year)
- **DigiCert** (~$229/year)
- **GlobalSign** (~$200/year)
- **Comodo** (~$99/year)

Once you have a commercial certificate:
1. Export it as a PFX file
2. Follow the signing steps below

## Signing Your Executable

### Step 1: Locate signtool.exe

```powershell
# Find signtool.exe location
$signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\signtool.exe"

# Or use from Visual Studio
$signtool = "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\signtool.exe"

# Verify it exists
if (Test-Path $signtool) {
    Write-Host "✓ Found signtool at: $signtool"
} else {
    Write-Host "✗ signtool not found. Install Windows SDK or Visual Studio"
}
```

### Step 2: Sign Your Executable

After building your application:

```powershell
# Variables
$pfxPath = "$env:USERPROFILE\HintOverlay_CodeSign.pfx"
$pfxPassword = "YourPassword123"  # Or prompt for it
$exePath = "C:\Users\knausj\git\Windows-Hinting\bin\Release\net8.0-windows\HintOverlay.exe"
$signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\signtool.exe"
$timestampServer = "http://timestamp.sectigo.com"

# Sign the executable
& $signtool sign `
  /f $pfxPath `
  /p $pfxPassword `
  /t $timestampServer `
  /fd SHA256 `
  $exePath

Write-Host "✓ Executable signed successfully"
```

### Step 3: Verify Signature

```powershell
$signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\signtool.exe"
$exePath = "C:\Users\knausj\git\Windows-Hinting\bin\Release\net8.0-windows\HintOverlay.exe"

& $signtool verify /pa $exePath

# Alternative: Check certificate properties
certutil -hashfile $exePath SHA256
```

## Automation Script

Create `sign-executable.ps1`:

```powershell
<#
.SYNOPSIS
    Signs the HintOverlay executable with a code signing certificate
.PARAMETER BuildConfiguration
    Build configuration (Debug or Release)
.PARAMETER CertificatePath
    Path to the PFX certificate file
.PARAMETER CertificatePassword
    Password for the PFX certificate
#>

param(
    [string]$BuildConfiguration = "Release",
    [string]$CertificatePath = "$env:USERPROFILE\HintOverlay_CodeSign.pfx",
    [string]$CertificatePassword,
    [string]$TimestampServer = "http://timestamp.sectigo.com"
)

# Configuration
$projectRoot = "C:\Users\knausj\git\Windows-Hinting"
$exePath = Join-Path $projectRoot "bin\$BuildConfiguration\net8.0-windows\HintOverlay.exe"
$signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\signtool.exe"

# Validation
if (-not (Test-Path $exePath)) {
    Write-Error "Executable not found: $exePath"
    exit 1
}

if (-not (Test-Path $CertificatePath)) {
    Write-Error "Certificate not found: $CertificatePath"
    exit 1
}

if (-not (Test-Path $signtool)) {
    Write-Error "signtool.exe not found: $signtool"
    exit 1
}

if (-not $CertificatePassword) {
    $CertificatePassword = Read-Host "Enter certificate password" -AsSecureString
    $CertificatePassword = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [System.Runtime.InteropServices.Marshal]::SecureStringToCoTaskMemUnicode($CertificatePassword)
    )
}

Write-Host "Signing executable..."
Write-Host "  Executable: $exePath"
Write-Host "  Certificate: $CertificatePath"
Write-Host "  Timestamp Server: $TimestampServer"

# Sign the executable
$signOutput = & $signtool sign `
  /f $CertificatePath `
  /p $CertificatePassword `
  /t $TimestampServer `
  /fd SHA256 `
  /v `
  $exePath 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Executable signed successfully" -ForegroundColor Green

    # Verify signature
    Write-Host "Verifying signature..."
    & $signtool verify /pa $exePath

    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ Signature verified" -ForegroundColor Green
    } else {
        Write-Host "✗ Signature verification failed" -ForegroundColor Red
    }
} else {
    Write-Host "✗ Signing failed" -ForegroundColor Red
    Write-Host $signOutput
    exit 1
}
```

### Usage:

```powershell
.\sign-executable.ps1 -BuildConfiguration Release
```

## CI/CD Integration

### GitHub Actions Example

Create `.github/workflows/sign-and-release.yml`:

```yaml
name: Build, Sign, and Release

on:
  push:
    tags:
      - 'v*'

jobs:
  build-and-sign:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v3

      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'

      - name: Build Release
        run: dotnet build -c Release

      - name: Import Code Signing Certificate
        run: |
          $pfxPath = "cert.pfx"
          $certPassword = "${{ secrets.CODE_SIGNING_PASSWORD }}"

          # Import certificate (GitHub Actions has the cert in secrets)
          $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2
          $cert.Import("${{ secrets.CODE_SIGNING_CERT }}", $certPassword, 'PersistKeySet')

      - name: Sign Executable
        run: |
          $signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22000.0\x64\signtool.exe"
          & $signtool sign `
            /f cert.pfx `
            /p "${{ secrets.CODE_SIGNING_PASSWORD }}" `
            /t "http://timestamp.sectigo.com" `
            /fd SHA256 `
            "bin\Release\net8.0-windows\HintOverlay.exe"

      - name: Create Release
        uses: softprops/action-gh-release@v1
        with:
          files: bin/Release/net8.0-windows/HintOverlay.exe
```

## Verification

After signing, users can verify your executable:

```powershell
# Check certificate details
Get-AuthenticodeSignature "C:\path\to\HintOverlay.exe"

# Expected output:
# Status: Valid
# SignerCertificate: ...
# SignatureAlgorithm: sha256RSA
```

## Troubleshooting

### "Certificate not found in store"
- Ensure PFX file is valid
- Check password is correct
- Try: `certutil -f -p $password -importpfx cert.pfx`

### "signtool.exe not found"
- Install Windows SDK from: https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/
- Or use Visual Studio installer to add Windows SDK

### "uiAccess broken after signing"
- Ensure manifest has `uiAccess="true"` BEFORE signing
- Re-build and re-sign if manifest was modified
- Verify manifest is embedded correctly: `mt.exe -inputresource:HintOverlay.exe;3 -out:HintOverlay.manifest`

### "Invalid timestamp server"
- Verify timestamp server URL is correct
- Try alternative: `http://time.certum.pl` or `http://timestamp.globalsign.com`
- Offline signing (no /t parameter) also works for testing

## Security Best Practices

✅ **Do:**
- Store certificate password securely (use GitHub Secrets, Azure Key Vault, etc.)
- Use official timestamp servers (prevent certificate expiration issues)
- Verify signatures before release
- Rotate certificates periodically
- Keep private keys secure and backed up

❌ **Don't:**
- Hardcode passwords in scripts
- Share private keys
- Use self-signed certificates for public distribution
- Skip timestamp servers (allows certificate to expire and break signature)
- Modify executable after signing (breaks signature)

## References

- [Microsoft Authenticode Documentation](https://docs.microsoft.com/en-us/windows-hardware/drivers/install/authenticode)
- [signtool.exe Reference](https://docs.microsoft.com/en-us/dotnet/framework/tools/signtool-exe-code-signing-tool)
- [UI Access Security Considerations](https://docs.microsoft.com/en-us/windows/win32/winauto/uiaccessforw3c)

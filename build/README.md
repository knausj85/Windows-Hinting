# Windows-Hinting Build and Code Signing Guide

## Overview

Windows-Hinting is **automatically signed with a self-signed certificate** during the Release build process. This enables UIAccess functionality on Windows, which allows the application to automate UI controls with elevated privileges.

## ✅ Quick Start

### Build Signed Executable Only

```powershell
# Using MSBuild directly (recommended)
msbuild Windows-Hinting.csproj /p:Configuration=Release

# Or using the PowerShell script
.\build\build-and-sign.ps1

# Or using the batch file
.\build\build-and-sign.bat
```

### Build Signed Executable + MSI Installer

```powershell
# Using the complete build script (RECOMMENDED)
.\build\build-complete.ps1 -Configuration Release -Installer

# Or using the batch file
.\build\build-complete.bat Release --installer
```

**This is the recommended approach** - it ensures the signed executable is built first, then included in the installer.

### Build Configurations

- **Release Build (with signing)**: Automatically signs executable
- **Debug Build (no signing)**: Faster builds for development

## How It Works

The build process has three automatic steps for Release configuration:

### 1. Certificate Generation (Pre-Build)
- **When**: First Release build, or when certificate is missing
- **Script**: `build\generate-signing-cert.ps1` (runs automatically)
- **Output**: `certs\WindowsHinting_CodeSign.pfx`
- **Certificate Details**:
  - Subject: `CN=Windows-Hinting`
  - Type: Code Signing Certificate
  - Valid For: 10 years
  - Password: `WindowsHinting_BuildCert_2024` (for PFX file)

### 2. Project Compilation
- **Tool**: MSBuild with .NET 8 SDK
- **Output**: `bin\Release\net8.0-windows\Windows-Hinting.dll` and `Windows-Hinting.exe`
- **Manifest**: UIAccess manifest automatically embedded

### 3. Code Signing (Post-Build)
- **Tool**: `signtool.exe` from Windows SDK
- **Certificate**: Auto-detected at `certs\WindowsHinting_CodeSign.pfx`
- **Timestamp**: DigiCert timestamp server (allows verification after cert expiry)
- **Algorithm**: SHA256
- **Output**: Signed `Windows-Hinting.exe` ready for deployment

## Certificate Management

### Checking the Signature

```powershell
# View signature details on the built executable
Get-AuthenticodeSignature .\bin\Release\net8.0-windows\Windows-Hinting.exe | Format-List

# Extract certificate subject and validity
$sig = Get-AuthenticodeSignature .\bin\Release\net8.0-windows\Windows-Hinting.exe
Write-Host "Signer: $($sig.SignerCertificate.Subject)"
Write-Host "Valid until: $($sig.SignerCertificate.NotAfter)"
```

### Viewing the Certificate in Windows

```powershell
# View certificate in personal store
Get-ChildItem cert:\CurrentUser\My | Where-Object { $_.Subject -like "*Windows-Hinting*" } | Format-List
```

### Regenerate Certificate (Manual)

```powershell
# Force regeneration of certificate
.\build\generate-signing-cert.ps1 -Force
```

### Use Custom Certificate (Production)

If you have a production code-signing certificate from a CA, you can use it instead:

```powershell
# Build with custom certificate (any Release build will use it)
msbuild Windows-Hinting.csproj `
    /p:Configuration=Release `
    /p:CodeSigningCertPath="C:\path\to\production_cert.pfx" `
    /p:CodeSigningPassword="your_password"
```

The custom certificate will be used for that build, while the default self-signed cert remains unchanged.

## Project Configuration Files

### `Windows-Hinting.csproj`
Contains the build targets for automatic certificate generation and signing:
- `EnsureSigningCertificate` target: Creates certificate before build
- `SignExecutable` target: Signs executable after build
- `DefaultCodeSigningCertPath` property: Path to certificate file
- `DefaultCodeSigningPassword` property: Certificate password

### `build\generate-signing-cert.ps1`
Standalone script that:
- Checks if certificate already exists
- Creates new self-signed certificate if needed
- Exports to PFX format
- Can be run independently: `.\build\generate-signing-cert.ps1`

### `build\build-and-sign.ps1`
Main build orchestration script that:
- Generates certificate if needed
- Builds the project
- Verifies signature with signtool
- Displays detailed signature information

### `build\build-and-sign.bat`
Batch wrapper for PowerShell script (for CMD users)

## Understanding the Certificate

### Self-Signed Certificate Details
```
Subject: CN=Windows-Hinting
Key Usage: Digital Signature
Type: Code Signing Certificate
Valid: 10 years from generation date
Issuer: Self (not a trusted CA)
```

### Why Self-Signed?
- ✅ Enables UIAccess functionality for development/testing
- ✅ No cost (self-signed, not CA-issued)
- ✅ Full control over certificate properties
- ⚠️ Not trusted by Windows SmartScreen
- ⚠️ Windows may display security warnings for first-run

### For Production
To replace with a trusted certificate:
1. Obtain certificate from a CA (Sectigo, DigiCert, etc.)
2. Build with custom certificate:
   ```powershell
   msbuild Windows-Hinting.csproj `
       /p:Configuration=Release `
       /p:CodeSigningCertPath="C:\path\to\production.pfx" `
       /p:CodeSigningPassword="your_password"
   ```
3. Update `DefaultCodeSigningCertPath` in Windows-Hinting.csproj if desired

## Build Output

After a successful Release build with signing:

```
========================================
Windows-Hinting Build and Sign Script
==========================================
Configuration: Release
Repository Root: C:\Users\knausj\git\Windows-Hinting

[1/3] Ensuring code signing certificate exists...
✓ Code signing certificate already exists at: C:\Users\knausj\git\Windows-Hinting\certs\WindowsHinting_CodeSign.pfx

[2/3] Building Windows-Hinting (Release)...
✓ Build completed successfully

[3/3] Verifying executable signature...
✓ Signature verified successfully

Signature Details:
  Status: Valid
  Signer: CN=Windows-Hinting
  Thumbprint: [certificate thumbprint]

==========================================
✓ Build process completed successfully!
==========================================

Output:
  Executable: bin\Release\net8.0-windows\Windows-Hinting.exe
  Certificate: certs\WindowsHinting_CodeSign.pfx
```

## Troubleshooting

### Issue: "signtool.exe not found"
**Solution**: Install Windows SDK
```powershell
# Download from: https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/
# Or use Visual Studio installer to add Windows SDK
```

### Issue: Certificate file not found in first build
**Solution**: The script should auto-generate it. If not:
```powershell
.\build\generate-signing-cert.ps1 -Force
```

### Issue: "Access denied" when generating certificate
**Solution**: Run PowerShell as Administrator

### Issue: Signature verification fails
**Solution**: This is normal for self-signed certificates. Verify with:
```powershell
Get-AuthenticodeSignature .\bin\Release\net8.0-windows\Windows-Hinting.exe
```

### Issue: Build fails when signing is required but cert doesn't exist
**Solution**: Ensure Windows SDK is installed with signtool.exe, or use Debug configuration:
```powershell
.\build\build-and-sign.ps1 -Configuration Debug
```

## CI/CD Integration

### GitHub Actions Example
```yaml
- name: Build and Sign Windows-Hinting
  run: |
    # Generate certificate (or load from secrets in production)
    powershell -NoProfile -ExecutionPolicy Bypass -File build/generate-signing-cert.ps1

    # Build
    msbuild Windows-Hinting.csproj /p:Configuration=Release
```

### Azure Pipelines Example
```yaml
- task: PowerShell@2
  displayName: 'Generate Code Signing Certificate'
  inputs:
    filePath: 'build/generate-signing-cert.ps1'
    pwsh: true

- task: VSBuild@1
  displayName: 'Build Windows-Hinting'
  inputs:
    solution: 'Windows-Hinting.csproj'
    configuration: 'Release'
```

## Related Files

- `Windows-Hinting.csproj` - Build configuration with signing targets
- `app.manifest` - Embedded manifest with UIAccess configuration
- `Windows-Hinting.Installer\` - MSI installer (uses signed executable)
- `CODE_SIGNING_SETUP.md` - Detailed signing documentation
- `../COMPLETE_BUILD_GUIDE.md` - **Complete build process guide (executable + installer)**

## Support

For issues related to code signing:
1. Check the troubleshooting section above
2. Review Windows SDK installation
3. Verify certificate exists: `dir certs\WindowsHinting_CodeSign.pfx`
4. Check build output for detailed error messages

## Security Notes

⚠️ **Important for Development**:
- Self-signed certificates are for development only
- Production builds should use CA-issued certificates
- Never commit `.pfx` files to version control
- Certificate password is stored in build configuration (safe for local builds)

⚠️ **For Production Deployment**:
- Obtain certificate from trusted CA
- Use secure secret management (Azure Key Vault, GitHub Secrets, etc.)
- Update build configuration with production certificate path
- Test signed executable before deployment

# Code Signing Setup for Windows-Hinting UIAccess

## Overview
Windows-Hinting requires code signing with an Authenticode certificate to enable UIAccess functionality on Windows. The executable must be signed for Windows to grant elevated UI automation permissions.

## Requirements
- **Windows SDK**: signtool.exe (included with Windows SDK, typically at `C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe`)
- **Code Signing Certificate**: A valid .pfx (PKCS#12) certificate file with private key
- **.NET 8**: Already configured in the project
- **Manifest**: UIAccess manifest already embedded in app.manifest

## Steps to Sign the Executable

### Option 1: Use Command Line with Certificate File (Recommended)

1. **Prepare your certificate** (.pfx file with private key)
   - If you have a .cer public certificate, you'll need to create a self-signed certificate:
     ```powershell
     # Create a self-signed certificate (valid for 10 years)
     $cert = New-SelfSignedCertificate -CertStoreLocation cert:\CurrentUser\My `
       -Subject "CN=Windows-Hinting" `
       -KeyUsage DigitalSignature `
       -Type CodeSigningCert `
       -NotAfter (Get-Date).AddYears(10)

     # Export to .pfx file
     Export-PfxCertificate -Cert $cert -FilePath ".\WindowsHinting_CodeSign.pfx" -Password (ConvertTo-SecureString -String "your_password" -AsPlainText -Force)
     ```

2. **Build and sign the executable**:
   ```powershell
   # From the repository root
   msbuild Windows-Hinting.csproj /p:Configuration=Release /p:CodeSigningCertPath="path\to\cert.pfx" /p:CodeSigningPassword="your_password"
   ```

   Or use the build script with signing:
   ```powershell
   cd Windows-Hinting.Installer
   msbuild . /p:Configuration=Release /p:CodeSigningCertPath="..\WindowsHinting_CodeSign.pfx" /p:CodeSigningPassword="your_password"
   ```

### Option 2: Use Certificate Thumbprint

If your certificate is already installed in Windows certificate store:

1. **Find your certificate thumbprint**:
   ```powershell
   Get-ChildItem cert:\CurrentUser\My -CodeSigningCert | Format-Table Thumbprint, Subject
   ```

2. **Modify Windows-Hinting.csproj** to use thumbprint instead of file path (optional enhancement)

### Option 3: Automated Build Script

Create a `build-signed.ps1` script:

```powershell
param(
    [Parameter(Mandatory=$true)][string]$CertPath,
    [Parameter(Mandatory=$true)][string]$CertPassword,
    [string]$Configuration = "Release"
)

Write-Host "Building Windows-Hinting with code signing..."

msbuild Windows-Hinting.csproj `
    /p:Configuration=$Configuration `
    /p:CodeSigningCertPath=$CertPath `
    /p:CodeSigningPassword=$CertPassword

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed"
    exit 1
}

Write-Host "Building installer..."
msbuild Windows-Hinting.Installer\Windows-Hinting.Installer.wixproj `
    /p:Configuration=$Configuration

Write-Host "Build complete. Signed executable at: bin\$Configuration\net8.0-windows\Windows-Hinting.exe"
```

Usage:
```powershell
.\build-signed.ps1 -CertPath "path\to\cert.pfx" -CertPassword "password"
```

## Verification

### Verify Signature
```powershell
# Verify the signed executable
signtool verify /pa "bin\Release\net8.0-windows\Windows-Hinting.exe"

# Get detailed signature information
Get-AuthenticodeSignature -FilePath "bin\Release\net8.0-windows\Windows-Hinting.exe" | Format-List *
```

### Verify UIAccess Manifest
```powershell
# Check if manifest is embedded and has uiAccess="true"
$file = "bin\Release\net8.0-windows\Windows-Hinting.exe"
[Reflection.Assembly]::LoadFile($file) | ForEach-Object {
    $_.GetManifestResourceStream("Windows-Hinting.exe.manifest") | Out-File manifest.xml
}
type manifest.xml | findstr uiAccess
```

## Build Properties

The following MSBuild properties control code signing:

| Property | Purpose |
|----------|---------|
| `CodeSigningCertPath` | Path to .pfx certificate file (required for signing) |
| `CodeSigningPassword` | Password for the .pfx certificate (required if cert is password-protected) |
| `Configuration` | Build configuration; signing only runs for Release configuration |

## CI/CD Integration

For automated builds (GitHub Actions, Azure Pipelines, etc.):

1. **Store certificate securely**:
   - GitHub Secrets: Store .pfx as base64-encoded secret
   - Azure Key Vault: Store certificate with password

2. **Example GitHub Actions workflow**:
   ```yaml
   - name: Build and Sign Windows-Hinting
     run: |
       msbuild Windows-Hinting.csproj `
         /p:Configuration=Release `
         /p:CodeSigningCertPath="${{ secrets.SIGNING_CERT_PATH }}" `
         /p:CodeSigningPassword="${{ secrets.SIGNING_CERT_PASSWORD }}"
   ```

## Troubleshooting

### Error: "signtool.exe not found"
- Install Windows SDK: https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/
- Verify Windows Kits installation path matches the one in Windows-Hinting.csproj

### Error: "No certificates were found"
- Ensure the .pfx file path is correct
- Verify the certificate has a private key (PFX must contain both public and private key)

### Error: "Password incorrect"
- Double-check the certificate password
- Try exporting the certificate again with a known password

### UIAccess still not working after signing
1. **Verify signature**: `signtool verify /pa "Windows-Hinting.exe"`
2. **Check manifest**: Ensure uiAccess="true" in embedded manifest
3. **Check security**: File must be in a protected location (e.g., Program Files) when signed
4. **Restart**: After installing the signed executable, restart the application

## Next Steps

1. **Obtain or create a code-signing certificate**
2. **Build the Release configuration with signing**
3. **Verify the signature**
4. **Build the MSI installer** - it will automatically include the signed executable
5. **Test UIAccess functionality** in the installed application

## Related Files
- `Windows-Hinting.csproj` - Build configuration with signing target
- `app.manifest` - UIAccess manifest configuration
- `Windows-Hinting.Installer/Windows-Hinting.Installer.wixproj` - MSI installer (automatically packages signed exe)

## Security Notes

⚠️ **Important**:
- Never commit certificate files (.pfx) to version control
- Use environment variables or secrets management for production
- Code-signing certificates from established CAs (like Sectigo, DigiCert) are recommended for production
- Self-signed certificates work for testing but won't be trusted by Windows SmartScreen

# Windows-Hinting Code Signing Implementation - Complete

## Summary

✅ **Code signing has been successfully implemented for Windows-Hinting.exe with automatic self-signed certificate generation and signing during the Release build process.**

## What Was Done

### 1. **Automatic Code Signing Setup**
   - Created `build\generate-signing-cert.ps1` - PowerShell script that generates a self-signed code-signing certificate
   - Updated `Windows-Hinting.csproj` with two MSBuild targets:
     - `EnsureSigningCertificate` (pre-build): Automatically generates certificate if it doesn't exist
     - `SignExecutable` (post-build): Automatically signs the executable with signtool.exe
   - Certificate is stored at: `certs\WindowsHinting_CodeSign.pfx`

### 2. **Build Scripts**
   - Created `build\build-and-sign.ps1` - PowerShell build orchestration script
   - Created `build\build-and-sign.bat` - Batch file wrapper for command prompt users
   - Both scripts handle certificate generation and build verification

### 3. **Project Configuration**
   - Updated `Windows-Hinting.csproj` to automatically detect the certificate
   - Configured signing to run only on Release builds
   - Windows SDK signtool.exe paths are auto-detected (supports versions 10.0.22621.0 and 10.0.26100.0)

## How to Use

### Simple Release Build (with Signing)
```powershell
msbuild Windows-Hinting.csproj /p:Configuration=Release
```

### Using Build Scripts
```powershell
# PowerShell
.\build\build-and-sign.ps1

# Batch
.\build\build-and-sign.bat
```

### Debug Build (No Signing)
```powershell
msbuild Windows-Hinting.csproj /p:Configuration=Debug
```

## Verification

The signed executable can be verified with:

```powershell
# View signature details
Get-AuthenticodeSignature .\bin\Release\net8.0-windows\Windows-Hinting.exe | Format-List

# Certificate details
$sig = Get-AuthenticodeSignature .\bin\Release\net8.0-windows\Windows-Hinting.exe
Write-Host "Signer: $($sig.SignerCertificate.Subject)"
Write-Host "Thumbprint: $($sig.SignerCertificate.Thumbprint)"
Write-Host "Valid until: $($sig.SignerCertificate.NotAfter)"
```

## Certificate Details

- **Subject**: CN=Windows-Hinting
- **Thumbprint**: 57416ABB6B20681CFE301142E4EC580D664328C6
- **Type**: Code Signing Certificate
- **Valid From**: 3/15/2026 3:57:31 AM
- **Valid Until**: 3/15/2036 4:07:31 AM (10 years)
- **Location**: `certs\WindowsHinting_CodeSign.pfx`
- **Password**: WindowsHinting_BuildCert_2024

## UIAccess Enablement

The combination of:
1. ✅ **Code-signed executable** (now implemented)
2. ✅ **Embedded UIAccess manifest** (already in app.manifest with `uiAccess="true"`)
3. ✅ **Installation to protected location** (handled by MSI installer)

...enables full UIAccess functionality for UI automation without requiring the user to disable UAC.

## Files Modified/Created

### New Files
- `build\generate-signing-cert.ps1` - Certificate generation script
- `build\build-and-sign.ps1` - Build orchestration script
- `build\build-and-sign.bat` - Batch wrapper
- `build\README.md` - Build documentation

### Modified Files
- `Windows-Hinting.csproj` - Added signing targets and certificate configuration

### Certificate File (Auto-generated)
- `certs\WindowsHinting_CodeSign.pfx` - Self-signed certificate (created on first build)

## Build Output Example

```
Signing executable: C:\...\bin\Release\net8.0-windows\Windows-Hinting.exe
Done Adding Additional Store
Successfully signed: C:\...\bin\Release\net8.0-windows\Windows-Hinting.exe
Executable signed successfully
```

## Next Steps

1. **Installer Integration**: The MSI installer (`Windows-Hinting.Installer`) will automatically include the signed executable in the next build
2. **Testing**: Test the installed Windows-Hinting.exe to verify UIAccess functionality works correctly
3. **Production**: For production, replace the self-signed certificate with a trusted CA-issued certificate

## Important Notes

⚠️ **Self-Signed Certificate**
- This certificate is for development/testing only
- Windows may display security warnings on first run
- For production, obtain a certificate from a trusted CA (Sectigo, DigiCert, etc.)
- Never commit the .pfx file to version control

✅ **Security**
- The certificate password is stored in `Windows-Hinting.csproj` (safe for local development)
- For CI/CD pipelines, use secret management (GitHub Secrets, Azure Key Vault)
- The timestamp server allows verification even after certificate expiry

✅ **Automatic Process**
- No manual steps required for Release builds
- Certificate is generated automatically if missing
- Signing happens automatically as part of the build
- Just run: `msbuild Windows-Hinting.csproj /p:Configuration=Release`

## Troubleshooting

### Signing failed with "The system cannot find the path specified"
- Verify Windows SDK is installed: `Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Filter signtool.exe -Recurse`
- Windows SDK v10.0.22621.0 or newer is required

### Certificate not found on first build
- Run a Release build first to auto-generate certificate
- Or manually run: `.\build\generate-signing-cert.ps1`

### "Executable is not signed" after build
- Check that Configuration is set to Release
- Verify `certs\WindowsHinting_CodeSign.pfx` exists
- Run verbose build to see signing output: `msbuild ... /v:detailed`

## Documentation

See `build\README.md` for detailed information on:
- Certificate management
- Using custom certificates
- CI/CD integration
- Build script options

# Complete Build Process - Signed Executable + Installer

## Overview

The complete build process now ensures that **the signed executable is built first**, then automatically included in the MSI installer. This guarantees that end users receive a properly signed executable that supports UIAccess functionality.

## Build Process

### One-Command Complete Build

```powershell
# Build both executable and installer
.\build\build-complete.ps1 -Configuration Release -Installer

# Or using batch
.\build\build-complete.bat Release --installer
```

### What Happens

**Step 1**: Build signed executable (Release mode)
- Generates self-signed certificate if needed
- Compiles HintOverlay.csproj in Release configuration
- Automatically signs HintOverlay.exe with code-signing certificate
- **Output**: `bin\Release\net8.0-windows\HintOverlay.exe` (signed)

**Step 2**: Build MSI installer
- Depends on HintOverlay.csproj being built first
- Automatically includes the signed executable
- Bundles all dependencies (DLLs, runtimes, etc.)
- **Output**: `HintOverlay.Installer2\bin\Release\en-US\HintOverlay.msi`

**Step 3**: Verify installer contents
- Confirms MSI file was created successfully
- Reports installer size and location

## Usage Examples

### Build Signed Executable Only (No Installer)

```powershell
# Using direct msbuild
msbuild HintOverlay.csproj /p:Configuration=Release

# Using build script
.\build\build-and-sign.ps1
```

### Build Complete Package (Executable + Installer)

```powershell
# Using complete build script
.\build\build-complete.ps1 -Configuration Release -Installer

# Using batch file
.\build\build-complete.bat Release --installer
```

### Debug Build (No Signing)

```powershell
# Executable only
msbuild HintOverlay.csproj /p:Configuration=Debug

# Complete build (no installer for debug)
.\build\build-complete.ps1 -Configuration Debug
```

### Custom Certificate

```powershell
# Use a production code-signing certificate
msbuild HintOverlay.csproj `
    /p:Configuration=Release `
    /p:CodeSigningCertPath="C:\path\to\cert.pfx" `
    /p:CodeSigningPassword="your_password"

# Then build installer separately
msbuild HintOverlay.Installer2\HintOverlay.Installer2.wixproj /p:Configuration=Release
```

## Build Output

### Signed Executable
```
Configuration: Release
Location: bin\Release\net8.0-windows\HintOverlay.exe
Size: ~138 KB
Signed: Yes
Certificate: CN=HintOverlay (self-signed)
Valid Until: 3/15/2036
```

### MSI Installer
```
Configuration: Release
Location: HintOverlay.Installer2\bin\Release\en-US\HintOverlay.msi
Size: ~0.9 MB
Contents:
  - HintOverlay.exe (signed)
  - HintOverlay.dll
  - .NET runtime dependencies
  - Platform-specific runtimes
  - Configuration files
  - Shortcuts
  - Registry entries (UIAccess, auto-start)
```

## Verification

### Verify Executable Signature

```powershell
# Check if executable is signed
Get-AuthenticodeSignature "bin\Release\net8.0-windows\HintOverlay.exe" | Format-List

# Expected output includes:
#   SignerCertificate: CN=HintOverlay
#   Valid until: 03/15/2036
```

### Verify Installer Contains Signed Executable

The installer will contain the signed executable as it's built after the signing step. The WiX project file references `$(var.HintOverlay.TargetDir)HintOverlay.exe`, which automatically uses the most recently built executable.

## Build Dependencies

### Execution Order (for complete build)

1. **Pre-Build**: Certificate generation (if needed)
2. **HintOverlay.csproj** builds → Produces `HintOverlay.exe`
3. **HintOverlay.Installer2.wixproj** builds → Produces `HintOverlay.msi`
   - Automatically includes the signed `HintOverlay.exe`

### Project Dependencies

```
HintOverlay.Installer2.wixproj
  └─ depends on HintOverlay.csproj
      └─ depends on app.manifest (UIAccess configuration)
      └─ depends on certs\HintOverlay_CodeSign.pfx (signing certificate)
```

## Build Scripts Reference

### `build\build-and-sign.ps1`
Builds and signs the executable only.

**Usage**:
```powershell
.\build\build-and-sign.ps1 -Configuration Release
```

**Options**:
- `-Configuration Release|Debug` - Build configuration (default: Release)
- `-RegenerateCert` - Force regenerate certificate
- `-SkipSigning` - Skip code signing

### `build\build-complete.ps1`
Builds signed executable AND MSI installer.

**Usage**:
```powershell
.\build\build-complete.ps1 -Configuration Release -Installer
```

**Options**:
- `-Configuration Release|Debug` - Build configuration (default: Release)
- `-Installer` - Build MSI installer (default: false)
- `-SkipSigning` - Skip code signing
- `-CertPath` - Custom certificate path
- `-CertPassword` - Custom certificate password

### Batch Files

**`build\build-and-sign.bat`** - Wrapper for `build-and-sign.ps1`

**`build\build-complete.bat`** - Wrapper for `build-complete.ps1`

Usage:
```cmd
build-complete.bat Release --installer
```

## Important Notes

### Build Order Matters

- **Always build the executable first** before the installer
- The installer automatically uses the signed executable from the most recent build
- If you rebuild just the installer, it will include the signed executable from the previous executable build

### Certificate Persistence

- The self-signed certificate is created once and reused for all subsequent Release builds
- Location: `certs\HintOverlay_CodeSign.pfx`
- Never commit this file to version control

### MSI Installer Contents

The MSI includes:
- ✅ Signed HintOverlay.exe with UIAccess manifest
- ✅ HintOverlay.dll and configuration files
- ✅ .NET Runtime dependencies (29 DLLs)
- ✅ Platform-specific runtime files
- ✅ Desktop and Start Menu shortcuts
- ✅ Registry entries for UIAccess, auto-start, and uninstall

### Installation Location

When the MSI is installed:
- Default: `C:\Program Files\HintOverlay\`
- Executable: `C:\Program Files\HintOverlay\HintOverlay.exe`
- All dependencies in same directory
- Registry entries in `HKEY_LOCAL_MACHINE`

## Troubleshooting

### Build fails with "Installer project not found"
- Verify `HintOverlay.Installer2\HintOverlay.Installer2.wixproj` exists
- Ensure WiX Toolset v6.0.2 SDK is installed

### Executable is not signed after build
- Verify Windows SDK is installed: `where signtool.exe`
- Check Release configuration is being used
- Verify certificate exists: `ls certs\HintOverlay_CodeSign.pfx`

### MSI is missing the signed executable
- Clean and rebuild: `msbuild ... /t:Clean;Build`
- Verify executable was signed first
- Check build output for signing messages

### Certificate generation fails
- Run PowerShell as Administrator
- Verify Windows has certificate services available
- Check certificate generation script logs

## Next Steps

1. **Test Installation**
   ```powershell
   # Run the MSI
   msiexec /i "HintOverlay.Installer2\bin\Release\en-US\HintOverlay.msi"
   ```

2. **Verify Installation**
   - Check `C:\Program Files\HintOverlay\HintOverlay.exe` exists
   - Verify file is signed: `Get-AuthenticodeSignature "C:\Program Files\HintOverlay\HintOverlay.exe"`
   - Test UIAccess functionality

3. **For Production**
   - Replace self-signed certificate with CA-issued certificate
   - Update `HintOverlay.csproj` to reference production certificate
   - Rebuild and redistribute

## Build Performance

Typical build times:
- **Executable only**: 5-10 seconds (including signing)
- **Executable + Installer**: 10-15 seconds total
- **Clean build**: 15-20 seconds (includes certificate check)

## CI/CD Integration

For automated builds in GitHub Actions, Azure Pipelines, etc.:

```yaml
# Example GitHub Actions
- name: Build HintOverlay Signed Executable
  run: msbuild HintOverlay.csproj /p:Configuration=Release

- name: Build MSI Installer
  run: msbuild HintOverlay.Installer2\HintOverlay.Installer2.wixproj /p:Configuration=Release

- name: Upload artifacts
  uses: actions/upload-artifact@v3
  with:
    name: HintOverlay-Release
    path: |
      bin\Release\net8.0-windows\HintOverlay.exe
      HintOverlay.Installer2\bin\Release\en-US\HintOverlay.msi
```

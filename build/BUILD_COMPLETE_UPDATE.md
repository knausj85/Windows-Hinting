# Build Complete Script Update Summary

## 📋 Overview

The `build\build-complete.ps1` script has been enhanced to automatically build and sign the MSI installer after successfully building the signed executable.

## ✨ What Changed

### Before
- Built executable only
- Optional separate MSI build step
- Manual coordination between executable and installer

### After
- **Single command** builds executable AND MSI
- **Automatic signing** verification for both
- **4-step build pipeline** in Release+Installer mode:
  1. Build executable (with signing)
  2. Verify executable signature
  3. Build MSI installer
  4. Verify signed executable is in MSI

## 🎯 Key Features

### Smart Step Counting
The script adjusts step numbers based on your configuration:
```
-Configuration Release -Installer        → 4 steps (build + sign + MSI + verify)
-Configuration Release                   → 1 step  (build + sign)
-Configuration Debug -Installer          → 3 steps (build + MSI + verify)
-Configuration Debug                     → 1 step  (build)
```

### Automatic Signature Verification
- Checks executable signature after build
- Displays certificate subject and expiration
- Verifies signed executable is packaged in MSI (with lessmsi)

### Error Handling
- Validates WiX installer project exists
- Checks certificate file before signing
- Validates MSI was created at expected location
- Provides helpful error messages with solutions

### Optional MSI Content Verification
If `lessmsi` tool is installed:
- Extracts executable from MSI
- Verifies it maintains code signature
- Displays embedded certificate details

Otherwise:
- Completes build successfully
- Provides instructions to install lessmsi for detailed verification

## 📝 Usage Examples

### Complete Release Build with MSI (Recommended)
```powershell
cd C:\Users\knausj\git\Windows-Hinting
.\build\build-complete.ps1 -Installer
```

### Build Only (No MSI)
```powershell
.\build\build-complete.ps1
```

### Debug Build
```powershell
.\build\build-complete.ps1 -Configuration Debug
```

### Release without Signing
```powershell
.\build\build-complete.ps1 -Installer -SkipSigning
```

### Custom Certificate
```powershell
.\build\build-complete.ps1 -Installer `
  -CertPath "C:\custom\cert.pfx" `
  -CertPassword "password"
```

## 📦 Build Output

### Executable
```
bin\Release\net8.0-windows\HintOverlay.exe
  - Signed with code signing certificate
  - Ready for distribution
```

### Installer
```
HintOverlay.Installer2\bin\Release\en-US\HintOverlay.msi
  - Contains signed executable
  - Ready for deployment
  - Shows file size in output
```

## 🔐 Code Signing Flow

```
1. Build HintOverlay.csproj
   ↓
2. MSBuild signs executable automatically
   (if -SkipSigning not set AND Configuration=Release)
   ↓
3. Verify executable signature
   (displays subject, expiration)
   ↓
4. Build WiX installer project
   ↓
5. Extract exe from MSI & verify signature
   (requires lessmsi, optional)
```

## 📊 Sample Output

```
==========================================
HintOverlay Complete Build Script
==========================================
Configuration: Release
Build Installer: True
Skip Signing: False
Repository Root: C:\Users\knausj\git\Windows-Hinting

[1/4] Building HintOverlay executable...
[OK] Executable build completed successfully

[2/4] Verifying executable signature...
[OK] Executable is signed
  Subject: CN=HintOverlay
  Valid until: 12/31/2025

[3/4] Building MSI installer...
[OK] Installer build completed successfully

[4/4] Verifying signed executable in MSI...
[OK] MSI contains signed executable
  Subject: CN=HintOverlay
  Valid until: 12/31/2025

==========================================
[OK] Build completed successfully!
==========================================

Build Summary:
  Configuration: Release
  Executable: bin\Release\net8.0-windows\HintOverlay.exe
  Signing: Enabled
  Installer: HintOverlay.Installer2\bin\Release\en-US\HintOverlay.msi
  Installer Size: 5.2 MB

Next Steps:
  1. Test the installer by running the MSI file
  2. Verify the signed executable is included in the installation
  3. Test UIAccess functionality in the installed application
  4. Verify that the installed executable maintains its signature
```

## ⚠️ Requirements

1. **Visual Studio/MSBuild** - for building projects
2. **WiX Toolset** - for MSI creation (when using -Installer)
3. **Code Signing Certificate** - `certs\HintOverlay_CodeSign.pfx` (for Release builds)
4. **lessmsi** (optional) - for MSI content verification
   ```powershell
   choco install lessmsi -y
   ```

## 🔧 Batch File Wrapper

Also updated: `build\build-complete.bat`

Usage:
```batch
build\build-complete.bat Release --installer
build\build-complete.bat Debug
build\build-complete.bat Release --skip-signing
```

## 📚 Documentation

New documentation files added:
- `build\BUILD_COMPLETE_MSI_GUIDE.md` - Detailed usage guide
- `build\BUILD_COMPLETE_QUICK_REF.md` - Quick reference card

## ✅ Testing the Changes

### Test 1: Build with MSI
```powershell
.\build\build-complete.ps1 -Installer
# Should produce 4-step build with signature verification
```

### Test 2: Build Release only
```powershell
.\build\build-complete.ps1
# Should produce 1-step build with signature verification
```

### Test 3: Debug Build
```powershell
.\build\build-complete.ps1 -Configuration Debug
# Should build without signing
```

### Test 4: Skip Signing
```powershell
.\build\build-complete.ps1 -Installer -SkipSigning
# Should build MSI without signing verification
```

## 🎉 Benefits

✅ **Single Command** - No need to manually run separate build/sign/installer steps
✅ **Automatic Verification** - Confirms signatures at every stage
✅ **Better Traceability** - Clear step numbers and status messages
✅ **Error Prevention** - Validates requirements and catches issues early
✅ **CI/CD Ready** - Easy to integrate into automated build pipelines
✅ **Flexible** - Works with Release, Debug, custom certs, skipping signing, etc.

## 🚀 Next Steps

1. Review the updated script at `build\build-complete.ps1`
2. Read `build\BUILD_COMPLETE_QUICK_REF.md` for quick usage
3. Run a test build: `.\build\build-complete.ps1 -Installer`
4. Verify MSI creation and signed executable
5. Test installer installation and functionality

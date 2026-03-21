# Build Complete Script - MSI Integration Guide

## Overview

The updated `build-complete.ps1` script now automatically builds the MSI installer after successfully building and signing the executable in Release configuration.

## Features

### Single Command Build & MSI
```powershell
# Build executable AND create MSI installer
.\build\build-complete.ps1 -Installer

# Same as above, explicit Release configuration
.\build\build-complete.ps1 -Configuration Release -Installer
```

### Build-Only (No Installer)
```powershell
# Just build the executable, no MSI
.\build\build-complete.ps1

# Build in Debug mode
.\build\build-complete.ps1 -Configuration Debug
```

### Skip Code Signing (if needed)
```powershell
# Build Release executable without signing
.\build\build-complete.ps1 -SkipSigning

# Build MSI without signing the executable
.\build\build-complete.ps1 -Installer -SkipSigning
```

### Custom Certificate Path
```powershell
# Use custom signing certificate
.\build\build-complete.ps1 -Installer -CertPath "C:\path\to\cert.pfx" -CertPassword "password"
```

## Build Steps

When building with `-Installer` flag in Release mode:

### Step 1: Build Executable
- Compiles Windows-Hinting.csproj in Release configuration
- Automatically signs executable if certificate is available
- Validates that the executable is properly signed

### Step 2: Verify Executable Signature
- Checks that the executable has a valid code signature
- Displays certificate subject and expiration date
- Warns if signing failed

### Step 3: Build MSI Installer
- Builds the WiX installer project (Windows-Hinting.Installer.wixproj)
- Creates MSI package in `Windows-Hinting.Installer\bin\Release\en-US\Windows-Hinting.msi`
- Verifies MSI file was created successfully

### Step 4: Verify Signed Executable in MSI (Release only)
- Extracts executable from MSI using `lessmsi` tool (if available)
- Verifies that the packaged executable is signed
- Displays certificate details

## Output Structure

```
Build Summary:
  Configuration: Release
  Executable: bin\Release\net8.0-windows\Windows-Hinting.exe
  Signing: Enabled
  Installer: Windows-Hinting.Installer\bin\Release\en-US\Windows-Hinting.msi
  Installer Size: X.X MB
```

## Requirements for MSI Build

1. **WiX Toolset** - Must be installed for MSI creation
2. **Visual Studio Build Tools or MSBuild** - For compiling the WiX project
3. **Code Signing Certificate** (Release builds) - Located at `certs\WindowsHinting_CodeSign.pfx` by default
4. **lessmsi** (Optional) - For verifying signed executable in MSI
   ```powershell
   choco install lessmsi -y
   # or
   winget install lessmsi
   ```

## Certificate Management

### Default Certificate Location
```
certs\WindowsHinting_CodeSign.pfx
```

### Generate New Certificate
```powershell
.\build\generate-signing-cert.ps1
```

### Custom Certificate
```powershell
.\build\build-complete.ps1 -Installer `
  -CertPath "C:\my-certs\custom.pfx" `
  -CertPassword "my-password"
```

## Troubleshooting

### MSI Not Found
- Ensure `Windows-Hinting.Installer` directory exists
- Verify `.wixproj` file is present and valid
- Check that WiX Toolset is installed

### Signing Failed
- Verify certificate file exists at specified path
- Check certificate password is correct
- Ensure certificate has valid key for code signing
- Use `-SkipSigning` to build without signing (testing only)

### lessmsi Not Available
- The script will continue without detailed MSI verification
- Install lessmsi to enable MSI content verification:
  ```powershell
  choco install lessmsi
  ```

## Batch File Usage

The `.bat` wrapper simplifies command-line usage:

```batch
# Build with installer
build\build-complete.bat Release --installer

# Build Debug executable only
build\build-complete.bat Debug

# Build Release without signing
build\build-complete.bat Release --skip-signing
```

## CI/CD Integration

For automated builds:

```powershell
# Full Release build with MSI
& .\build\build-complete.ps1 `
  -Configuration Release `
  -Installer `
  -CertPath $env:CERT_PATH `
  -CertPassword $env:CERT_PASSWORD
```

## Next Steps After Build

1. **Test the MSI installer**
   ```powershell
   msiexec /i "Windows-Hinting.Installer\bin\Release\en-US\Windows-Hinting.msi"
   ```

2. **Verify installed executable signature**
   ```powershell
   Get-AuthenticodeSignature -FilePath "C:\Program Files\Windows-Hinting\Windows-Hinting.exe"
   ```

3. **Test UIAccess functionality**
   - Verify the application can interact with UI elements from privileged processes
   - Test hint overlay functionality with administrator windows

4. **Sign MSI** (optional but recommended)
   - Use signtool to code-sign the MSI file itself
   - Adds additional trust chain to the installer package

## Success Indicators

✅ All 3-4 build steps complete without errors
✅ Executable shows "[OK] Executable is signed"
✅ MSI file created with expected size
✅ "[OK] MSI contains signed executable" message appears (if lessmsi available)

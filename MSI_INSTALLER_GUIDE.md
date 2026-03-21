# MSI Installer Setup Guide

## Overview

This guide explains how to build and deploy Windows-Hinting using Windows Installer (MSI) format. The MSI:

- ✅ Installs to Program Files (required for uiAccess)
- ✅ Registers shortcuts in Start Menu and Desktop
- ✅ Handles uninstall/removal cleanly
- ✅ Integrates with Windows Add/Remove Programs
- ✅ Supports both per-machine and per-user installation

## Prerequisites

### Required
- **Visual Studio 2026** (or later) - with C# and installer components
- **.NET 8** SDK
- **WiX Toolset** (included with Visual Studio or install separately)
- **Windows SDK** (for signtool, included with Visual Studio)

### Optional
- Code signing certificate (for production MSIs)

### Verify Installation

```powershell
# Check if heat.exe exists (WiX tool)
where heat.exe

# Check if candle.exe exists (WiX compiler)
where candle.exe

# Check if light.exe exists (WiX linker)
where light.exe
```

If any are missing, install WiX Toolset:
https://github.com/wixtoolset/wix3/releases

## Project Structure

```
Windows-Hinting/
├── Windows-Hinting.csproj              (Main application)
├── Windows-Hinting.sln                 (Solution file)
├── Build-InstallerMSI.ps1          (Builder script)
└── Windows-Hinting.Installer/
    ├── Windows-Hinting.Installer.wixproj  (WiX project)
    ├── Product.wxs                 (WiX source file)
    ├── License.rtf                 (License for installer)
    └── bin/Release/
        └── Windows-Hinting.msi         (Output)
```

## Quick Start

### One-Command Build

```powershell
cd C:\Users\knausj\git\Windows-Hinting
.\Build-InstallerMSI.ps1
```

This will:
1. Build the application (Release)
2. Sign the executable
3. Build the MSI installer
4. Output to: `Windows-Hinting.Installer\bin\Release\Windows-Hinting.msi`

### Build Without Signing

If you just want to test the MSI without signing:

```powershell
.\Build-InstallerMSI.ps1 -SkipSign
```

### Build Without Rebuilding App

If you already built the app and just want to rebuild the MSI:

```powershell
.\Build-InstallerMSI.ps1 -SkipBuild
```

## Installation & Testing

### Install the MSI

```powershell
# Method 1: Using msiexec
msiexec /i "Windows-Hinting.Installer\bin\Release\Windows-Hinting.msi"

# Method 2: Double-click the MSI file
& "Windows-Hinting.Installer\bin\Release\Windows-Hinting.msi"

# Method 3: From Explorer
explorer Windows-Hinting.Installer\bin\Release\
# Then double-click Windows-Hinting.msi
```

### Verify Installation

The installer creates:

```
C:\Program Files\Windows-Hinting\
├── Windows-Hinting.exe       (Signed with uiAccess)
└── [Other dependencies]

C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Windows-Hinting\
└── Windows-Hinting.lnk

C:\Users\[YourUser]\Desktop\
└── Windows-Hinting.lnk
```

### Test uiAccess

After installation:

```powershell
# Run from Program Files
& "C:\Program Files\Windows-Hinting\Windows-Hinting.exe"

# Should:
# - Start successfully
# - Have uiAccess privileges
# - Can interact with elevated windows
```

### Uninstall

```powershell
# Method 1: Using msiexec (quiet)
msiexec /x "Windows-Hinting.Installer\bin\Release\Windows-Hinting.msi" /quiet

# Method 2: Settings → Apps → Installed apps → Windows-Hinting → Uninstall

# Method 3: Control Panel → Programs → Programs and Features → Windows-Hinting → Uninstall
```

## Customizing the MSI

### Change Installation Directory

Edit `Windows-Hinting.Installer\Product.wxs`:

```xml
<Directory Id="INSTALLFOLDER" Name="Windows-Hinting" />
```

Change to:

```xml
<Directory Id="INSTALLFOLDER" Name="MyApp" />
```

### Add More Files

In `Product.wxs`, add to the `INSTALLFOLDER`:

```xml
<DirectoryRef Id="INSTALLFOLDER">
  <Component Id="MainExecutable" Guid="...">
    <File Id="WindowsHintingExe" Source="..." KeyPath="yes" />
    <File Id="ConfigFile" Source="config.json" />
    <File Id="ReadmeFile" Source="README.txt" />
  </Component>
</DirectoryRef>
```

### Add Registry Entries

```xml
<RegistryValue Root="HKLM"
               Key="Software\Windows-Hinting"
               Name="Version"
               Value="1.0.0"
               Type="string" />
```

### Change License

Replace `Windows-Hinting.Installer\License.rtf` with your own license file.

## Building MSI Manually (Without Script)

### Step 1: Build the Application

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe" `
  Windows-Hinting.sln /p:Configuration=Release
```

### Step 2: Sign the Executable

```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages\microsoft.windows.sdk.buildtools\10.0.26100.1742\bin\10.0.26100.0\x64\signtool.exe" `
  sign /f "C:\Users\knausj\WindowsHinting_CodeSign.pfx" /p "test123" /fd SHA256 /v `
  "bin\Release\net8.0-windows\Windows-Hinting.exe"
```

### Step 3: Build the MSI

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe" `
  Windows-Hinting.Installer\Windows-Hinting.Installer.wixproj /p:Configuration=Release
```

## Signing the MSI (Optional)

For production distributions, you can sign the MSI itself:

```powershell
$signtool = "C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages\microsoft.windows.sdk.buildtools\10.0.26100.1742\bin\10.0.26100.0\x64\signtool.exe"

& $signtool sign `
  /f "C:\Users\knausj\WindowsHinting_CodeSign.pfx" `
  /p "test123" `
  /fd SHA256 `
  /v `
  "Windows-Hinting.Installer\bin\Release\Windows-Hinting.msi"
```

## Command-Line Installation Options

```powershell
# Quiet installation (no UI)
msiexec /i Windows-Hinting.msi /quiet

# Quiet with basic UI
msiexec /i Windows-Hinting.msi /qb

# With logging
msiexec /i Windows-Hinting.msi /l*v install.log

# Uninstall silently
msiexec /x Windows-Hinting.msi /quiet

# Set installation directory
msiexec /i Windows-Hinting.msi INSTALLFOLDER="C:\CustomPath\"

# Administrative install (for deployment)
msiexec /a Windows-Hinting.msi
```

## WiX File Structure Explained

### Product.wxs Components

**File Component:**
```xml
<Component Id="MainExecutable" Guid="UNIQUE-GUID">
  <File Id="WindowsHintingExe" Source="bin/Release/.../Windows-Hinting.exe" />
</Component>
```
- Installs the executable
- Each file needs a unique GUID
- Source path is relative to .wxs file

**Shortcut Component:**
```xml
<Component Id="ProgramMenuShortcut" Guid="UNIQUE-GUID">
  <Shortcut Id="ApplicationStartMenuShortcut"
            Name="Windows-Hinting"
            Target="[INSTALLFOLDER]Windows-Hinting.exe" />
</Component>
```
- Creates Start Menu shortcut
- [INSTALLFOLDER] is a WiX variable pointing to installation directory

**Registry Component:**
```xml
<RegistryValue Root="HKCU"
               Key="Software\Windows-Hinting"
               Name="InstallPath"
               Value="[INSTALLFOLDER]"
               Type="string" />
```
- Stores installation path in registry
- Useful for applications to find themselves

## Troubleshooting

### "WiX Toolset not installed"

Install from: https://github.com/wixtoolset/wix3/releases

Or via Visual Studio Installer:
1. Open Visual Studio Installer
2. Click "Modify"
3. Go to "Installation details"
4. Search for "WiX"
5. Check "WiX Toolset"
6. Click "Modify"

### "Light.exe not found"

Light.exe is the WiX linker. If missing:
1. Install WiX Toolset (see above)
2. Or add to PATH:
   ```powershell
   $env:PATH += ";C:\Program Files (x86)\WiX Toolset v3.14\bin"
   ```

### "Heat.exe not found"

Heat.exe is the WiX harvester (optional). Only needed if using auto-generation of file lists.

### "MSI installation fails"

Check the log:
```powershell
msiexec /i Windows-Hinting.msi /l*v msi.log
notepad msi.log
```

### "Program Files installation fails"

Ensure:
1. Running as Administrator (for per-machine install)
2. Sufficient disk space
3. User has write permissions to Program Files

### "Shortcuts not appearing"

Check:
1. Component GUIDs are unique (each needs different GUID)
2. Registry entries are correct
3. Paths are valid

## Advanced: Bundle with .NET Runtime

To bundle .NET 8 with your installer:

1. Download .NET 8 Runtime installer
2. Reference it in a Bundle:

```xml
<Bundle Name="Windows-Hinting" Version="1.0.0">
  <BootstrapperApplicationRef Id="WixStandardBootstrapperApplication.RtfLicense" />

  <Chain>
    <!-- .NET 8 Runtime -->
    <ExePackage Id="DotNetRuntime"
                SourceFile="dotnet-runtime-8.0.0-win-x64.exe"
                InstallCommand="/install /quiet"
                DetectCondition="EXISTS [WindowsFolder]..." />

    <!-- Your MSI -->
    <MsiPackage Id="WindowsHintingMsi"
                SourceFile="Windows-Hinting.msi" />
  </Chain>
</Bundle>
```

This creates an `.exe` bundle that installs .NET first, then your app.

## Next Steps

1. **Test Installation**: Run `.\Build-InstallerMSI.ps1`
2. **Install the MSI**: `msiexec /i Windows-Hinting.Installer\bin\Release\Windows-Hinting.msi`
3. **Verify**: Check `C:\Program Files\Windows-Hinting\`
4. **Test Application**: Run from Program Files
5. **Customize**: Edit Product.wxs as needed
6. **Distribute**: Share the .msi file with users

## References

- [WiX Toolset Documentation](https://wixtoolset.org/docs/)
- [MSI Command Line Options](https://docs.microsoft.com/en-us/windows/win32/msi/command-line-options)
- [Program Files Installation](https://docs.microsoft.com/en-us/windows/win32/msi/installation-context)
- [uiAccess Requirements](https://docs.microsoft.com/en-us/windows/win32/winauto/uiaccessforw3c)

## Files Created

- `Windows-Hinting.Installer/Windows-Hinting.Installer.wixproj` - WiX project file
- `Windows-Hinting.Installer/Product.wxs` - WiX source (installer definition)
- `Windows-Hinting.Installer/License.rtf` - License file for installer
- `Build-InstallerMSI.ps1` - Build script (build + sign + create MSI)

## Summary

✅ MSI-based installation system fully configured
✅ Installs to Program Files (required for uiAccess)
✅ Creates shortcuts and registry entries
✅ One-command build: `.\Build-InstallerMSI.ps1`
✅ Customizable via WiX source files
✅ Ready for distribution

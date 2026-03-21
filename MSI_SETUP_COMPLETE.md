# MSI Installer Setup - Complete Guide

## Overview

I've set up a complete MSI installer system for Windows-Hinting that:

- ✅ Builds the signed application
- ✅ Creates a Windows Installer (.msi) package
- ✅ Installs to `C:\Program Files\Windows-Hinting\`
- ✅ Creates Start Menu and Desktop shortcuts
- ✅ Registers for Add/Remove Programs
- ✅ Handles uninstall cleanly

## What's New

### Files Created

```
Project Root/
├── Windows-Hinting.Installer/
│   ├── Windows-Hinting.Installer.wixproj    ← WiX project file
│   ├── Product.wxs                      ← MSI definition (XML)
│   ├── License.rtf                      ← License file
│   └── bin/Release/
│       └── Windows-Hinting.msi              ← Output (generated)
│
├── Build-InstallerMSI.ps1               ← Main build script
├── New-WindowsHintingMSI.ps1               ← Alternative builder
├── Install-WiX.ps1                      ← WiX installer
├── MSI_INSTALLER_GUIDE.md               ← Detailed documentation
└── INSTALL_WIX_GUIDE.md                 ← WiX setup guide
```

## Quick Start (2 Steps)

### Step 1: Install WiX Toolset

WiX is required to build the MSI. Install it once:

```powershell
cd C:\Users\knausj\git\Windows-Hinting
.\Install-WiX.ps1
```

This will:
- Download WiX v3.14 from GitHub
- Install to `C:\Program Files\WiX Toolset v3.14\`
- Make WiX tools available system-wide

**Note**: Installation takes 2-3 minutes. Be patient.

### Step 2: Build and Create MSI

```powershell
# From project root
.\Build-InstallerMSI.ps1
```

This will:
1. Build the application (Release)
2. Sign the executable with your certificate
3. Create the MSI installer
4. Output to: `Windows-Hinting.Installer\bin\Release\Windows-Hinting.msi`

**Done!** Your MSI is ready to install and distribute.

## Testing the MSI

### Install

```powershell
# Method 1: Using msiexec (recommended)
msiexec /i "Windows-Hinting.Installer\bin\Release\Windows-Hinting.msi"

# Method 2: Double-click the file in Explorer
explorer Windows-Hinting.Installer\bin\Release\
# Then double-click Windows-Hinting.msi
```

### Verify Installation

```powershell
# Check if installed
Get-Item "C:\Program Files\Windows-Hinting\Windows-Hinting.exe"

# Check shortcuts
Get-Item "C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Windows-Hinting"

# Check registry
Get-ItemProperty "HKCU:\Software\Windows-Hinting"
```

### Test Application

```powershell
# Run from Program Files
& "C:\Program Files\Windows-Hinting\Windows-Hinting.exe"

# Should have uiAccess privileges ✓
```

### Uninstall

```powershell
# Method 1: Quiet uninstall
msiexec /x "Windows-Hinting.Installer\bin\Release\Windows-Hinting.msi" /quiet

# Method 2: Settings → Apps → Installed apps → Windows-Hinting → Uninstall

# Method 3: Control Panel → Programs and Features → Windows-Hinting → Uninstall
```

## How It Works

### Build Process

```
Source Code (C#)
       ↓
   dotnet build (via MSBuild)
       ↓
   Windows-Hinting.exe (unsigned)
       ↓
   signtool sign (with certificate)
       ↓
   Windows-Hinting.exe (signed with uiAccess)
       ↓
   WiX Toolset (candle + light)
       ↓
   Windows-Hinting.msi (MSI package)
       ↓
   Ready for distribution!
```

### Installation Process

```
User downloads Windows-Hinting.msi
       ↓
   Double-click or msiexec /i
       ↓
   Windows Installer service starts
       ↓
   Copy files to C:\Program Files\Windows-Hinting\
       ↓
   Create shortcuts
       ↓
   Register in Add/Remove Programs
       ↓
   Application ready to use
```

## MSI Contents

The MSI installs the following to `C:\Program Files\Windows-Hinting\`:

```
C:\Program Files\Windows-Hinting\
├── Windows-Hinting.exe              (Signed with uiAccess)
└── [Dependencies if bundled]
```

The MSI also creates:

```
C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Windows-Hinting\
└── Windows-Hinting.lnk

C:\Users\[YourUser]\Desktop\
└── Windows-Hinting.lnk
```

## Customization

### Change Installation Directory

Edit `Windows-Hinting.Installer\Product.wxs`:

```xml
<!-- Line 73: Change from "Windows-Hinting" to your name -->
<Directory Id="INSTALLFOLDER" Name="Windows-Hinting" />
```

### Add Files to MSI

Edit `Windows-Hinting.Installer\Product.wxs`:

```xml
<DirectoryRef Id="INSTALLFOLDER">
  <Component Id="MainExecutable" Guid="...">
    <File Id="WindowsHintingExe" Source="..." KeyPath="yes" />
    <!-- Add more files here -->
    <File Id="ConfigFile" Source="config.json" />
    <File Id="DataFile" Source="data\important.dat" />
  </Component>
</DirectoryRef>
```

### Update License

Replace `Windows-Hinting.Installer\License.rtf` with your own license file.

## Advanced Options

### Command-Line Installation

```powershell
# Quiet install
msiexec /i Windows-Hinting.msi /quiet

# Quiet with basic UI
msiexec /i Windows-Hinting.msi /qb

# With logging
msiexec /i Windows-Hinting.msi /l*v install.log

# Custom install directory
msiexec /i Windows-Hinting.msi INSTALLFOLDER="C:\MyApp\"

# Uninstall silently
msiexec /x Windows-Hinting.msi /quiet
```

### Sign the MSI Itself

For production distributions, you can sign the MSI:

```powershell
$signtool = "C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages\microsoft.windows.sdk.buildtools\10.0.26100.1742\bin\10.0.26100.0\x64\signtool.exe"

& $signtool sign `
  /f "C:\Users\knausj\WindowsHinting_CodeSign.pfx" `
  /p "test123" `
  /fd SHA256 `
  /v `
  "Windows-Hinting.Installer\bin\Release\Windows-Hinting.msi"
```

### Bundle .NET Runtime

To include .NET 8 in the MSI, use WiX Bundle (not covered here, but advanced option).

## Troubleshooting

### "WiX not found"

Install WiX Toolset:
```powershell
.\Install-WiX.ps1
```

### "MSI build failed"

Check MSBuild output:
```powershell
.\Build-InstallerMSI.ps1 2>&1 | Tee-Object build.log
```

Look in `build.log` for errors.

### "Installation fails"

Check installation log:
```powershell
msiexec /i Windows-Hinting.msi /l*v msi-install.log
notepad msi-install.log
```

### "Program Files permission denied"

Ensure:
1. Running as Administrator
2. User has write permissions to Program Files
3. No file is locked by another process

### "Shortcuts not appearing"

Run as Administrator and check:
1. Start Menu location: `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Windows-Hinting\`
2. Desktop for shortcut file
3. Reinstall: `msiexec /x Windows-Hinting.msi && msiexec /i Windows-Hinting.msi`

## File Structure Explained

### Product.wxs (WiX Source File)

This XML file defines:

```xml
<Product>                    <!-- Overall installer -->
  <Package>                  <!-- MSI metadata -->
  <Feature>                  <!-- What gets installed -->
  <Directory>                <!-- Where to install -->
    <File>                   <!-- Files to include -->
    <Component>              <!-- Logical grouping -->
      <Shortcut>             <!-- Menu shortcuts -->
```

Key sections:
- **Product**: Installer name, version, GUID
- **Feature**: What to install (executable, shortcuts, etc.)
- **Directory**: Installation path (Program Files, etc.)
- **Component**: Files and settings (each needs unique GUID)

### GUIDs Explained

Each component has a GUID (Globally Unique ID):

```xml
<Component Id="MainExecutable" Guid="12345678-1234-1234-1234-123456789001">
```

These must be unique. If you copy components, **change the GUIDs**!

Generate new GUIDs:
```powershell
[guid]::NewGuid()
```

## Next Steps

### Immediate
1. ✅ Files created and documented
2. Install WiX: `.\Install-WiX.ps1`
3. Build MSI: `.\Build-InstallerMSI.ps1`
4. Test installation

### After Testing
1. Sign MSI (optional): Use signtool command above
2. Distribute .msi file to users
3. Users can install with double-click or `msiexec /i`

### For Production
1. Purchase code signing certificate
2. Sign both .exe and .msi
3. Host on website or internal server
4. Users download and install

## Related Commands

```powershell
# Build only (skip everything)
& "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe" `
  Windows-Hinting.Installer\Windows-Hinting.Installer.wixproj /p:Configuration=Release

# Sign MSI only
$signtool = "..."
& $signtool sign /f "C:\Users\knausj\WindowsHinting_CodeSign.pfx" /p "test123" /fd SHA256 `
  "Windows-Hinting.Installer\bin\Release\Windows-Hinting.msi"

# Clean build artifacts
rmdir "Windows-Hinting.Installer\bin" -Recurse -Force
rmdir "Windows-Hinting.Installer\obj" -Recurse -Force

# Check installed applications via registry
Get-ItemProperty "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*" | 
  Where-Object DisplayName -match "Windows-Hinting"
```

## Summary

✅ **MSI Installer System Complete**
- WiX project files created
- Build scripts ready
- Installation system configured
- All tools integrated

**Status**: Ready to build MSI!

**Next action**: 
1. Run `.\Install-WiX.ps1` (first time only)
2. Run `.\Build-InstallerMSI.ps1` (every build)

Your signed application + MSI installer = professional distribution package! 🎉

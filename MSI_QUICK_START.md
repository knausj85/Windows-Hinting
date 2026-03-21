# Quick Reference: MSI Builder

## One-Time Setup

```powershell
# Install WiX Toolset (required once)
.\Install-WiX.ps1
# Takes 2-3 minutes, downloads ~200 MB from GitHub
```

## Build MSI

```powershell
# Build app → sign → create MSI
.\Build-InstallerMSI.ps1

# Output: Windows-Hinting.Installer\bin\Release\Windows-Hinting.msi
```

## Options

```powershell
# Build without signing
.\Build-InstallerMSI.ps1 -SkipSign

# Build without rebuilding app
.\Build-InstallerMSI.ps1 -SkipBuild

# Alternative builder (fallback if WiX fails)
.\New-WindowsHintingMSI.ps1
```

## Install & Test

```powershell
# Install MSI
msiexec /i "Windows-Hinting.Installer\bin\Release\Windows-Hinting.msi"

# Install silently
msiexec /i "Windows-Hinting.Installer\bin\Release\Windows-Hinting.msi" /quiet

# Uninstall
msiexec /x "Windows-Hinting.Installer\bin\Release\Windows-Hinting.msi"

# Run installed application
& "C:\Program Files\Windows-Hinting\Windows-Hinting.exe"

# Check installation
Get-Item "C:\Program Files\Windows-Hinting\Windows-Hinting.exe"
```

## Production Setup

```powershell
# Sign the MSI itself
$signtool = "C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages\microsoft.windows.sdk.buildtools\10.0.26100.1742\bin\10.0.26100.0\x64\signtool.exe"

& $signtool sign `
  /f "C:\Users\knausj\WindowsHinting_CodeSign.pfx" `
  /p "test123" `
  /fd SHA256 `
  "Windows-Hinting.Installer\bin\Release\Windows-Hinting.msi"

# Verify MSI signature
Get-AuthenticodeSignature "Windows-Hinting.Installer\bin\Release\Windows-Hinting.msi"
```

## Customize MSI

Edit: `Windows-Hinting.Installer\Product.wxs`

**Change install folder:**
```xml
<Directory Id="INSTALLFOLDER" Name="Windows-Hinting" />
→
<Directory Id="INSTALLFOLDER" Name="MyAppName" />
```

**Add license:**
Replace: `Windows-Hinting.Installer\License.rtf`

**Add files:**
```xml
<File Id="MyFile" Source="path\to\file.ext" />
```

## Troubleshooting

| Issue | Solution |
|-------|----------|
| "WiX not found" | Run `.\Install-WiX.ps1` |
| "Build failed" | Check error in console output |
| "Installation fails" | Run as Administrator |
| "Need custom installer" | Edit `Product.wxs` |

## File Locations

| Item | Path |
|------|------|
| Build Script | `Build-InstallerMSI.ps1` |
| WiX Project | `Windows-Hinting.Installer\Windows-Hinting.Installer.wixproj` |
| MSI Definition | `Windows-Hinting.Installer\Product.wxs` |
| License | `Windows-Hinting.Installer\License.rtf` |
| Output MSI | `Windows-Hinting.Installer\bin\Release\Windows-Hinting.msi` |
| Install Path | `C:\Program Files\Windows-Hinting\` |

## Typical Workflow

```
1. Make code changes in Visual Studio
2. Run: .\Build-InstallerMSI.ps1
3. Wait for build + sign + MSI creation
4. Install for testing: msiexec /i ...
5. Test application
6. Ready for distribution!
```

## Fastest Path

```powershell
# First time
.\Install-WiX.ps1

# Then, every time you want to build:
.\Build-InstallerMSI.ps1
```

## What's Installed

```
C:\Program Files\Windows-Hinting\
├── Windows-Hinting.exe          (Signed, uiAccess enabled)

Start Menu:
└── Windows-Hinting → Windows-Hinting

Desktop:
└── Windows-Hinting (Shortcut)

Registry:
└── HKCU:\Software\Windows-Hinting
```

## Next Step

```powershell
# Setup (first time)
.\Install-WiX.ps1

# Build MSI
.\Build-InstallerMSI.ps1

# Go! ✓
```

---

**Status**: ✅ MSI system ready!

For detailed info: See `MSI_SETUP_COMPLETE.md` or `MSI_INSTALLER_GUIDE.md`

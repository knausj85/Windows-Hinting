# Windows-Hinting.Installer - Configuration Summary

**Status**: ✅ Successfully Built and Configured
**Build Date**: 2026-03-15
**Installer Version**: 1.0.0.0
**Target Platform**: Windows 10/11 (x64)
**WiX Toolset**: v6.0.2

---

## 📦 Build Artifacts

| File | Location | Size | Status |
|------|----------|------|--------|
| **Windows-Hinting.msi** | `bin/Release/en-US/` | 72 KB | ✅ Ready |
| **Windows-Hinting.wixpdb** | `bin/Release/en-US/` | 11 KB | ✅ Debug Symbols |

---

## 🎯 Installation Configuration

### Application Details
- **Application Name**: Windows-Hinting
- **Publisher**: Windows-Hinting
- **Version**: 1.0.0.0
- **Installation Directory**: `C:\Program Files\Windows-Hinting\`
- **Architecture**: 64-bit (x64)
- **Language**: English (US)

### Installation Components
- ✅ Windows-Hinting.exe (main application)
- ✅ Desktop Shortcut
- ✅ Start Menu Shortcut
- ✅ Registry Entries for UIAccess
- ✅ Uninstall Support

### Registry Entries Created
```
HKLM\Software\Windows-Hinting\Windows-Hinting\
  InstallPath = C:\Program Files\Windows-Hinting\
  Version = 1.0.0.0
  UIAccessEnabled = 1

HKLM\Software\Microsoft\Windows\CurrentVersion\Run\
  Windows-Hinting = [points to executable]

HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\Windows-Hinting\
  DisplayName = Windows-Hinting
  DisplayVersion = 1.0.0.0
  Publisher = Windows-Hinting
  InstallLocation = C:\Program Files\Windows-Hinting\
```

---

## 🔐 UIAccess Configuration

### Manifest Status
```xml
<requestedExecutionLevel level="asInvoker" uiAccess="true" />
```
**Status**: ✅ Correctly embedded in Windows-Hinting.exe

### Installation Location
**Status**: ✅ Program Files (required for UIAccess)

### Code Signing
**Status**: ⏳ **PENDING** - Executable must be signed with a code-signing certificate

### What UIAccess Does
- Allows Windows-Hinting to interact with system UI elements
- Bypasses UIPI (User Interface Privilege Isolation)
- Enables detection of hints from elevated processes
- **Requires code signing** to function

---

## 📁 Project File Structure

```
Windows-Hinting.Installer/
├── WiX Source Files
│   ├── Package.wxs              - Main package definition
│   ├── ExampleComponents.wxs    - Application components & shortcuts
│   ├── UI.wxs                   - UI configuration
│   ├── Folders.wxs              - Directory structure
│   └── Package.en-us.wxl        - Localization strings
│
├── Configuration Files
│   ├── Windows-Hinting.Installer.wixproj - Project file
│   └── UIAccessConfig.wixproj.user     - Build configuration
│
├── Documentation
│   ├── QUICK_START.md           - Quick reference guide
│   ├── INSTALLATION_GUIDE.md    - Detailed setup guide
│   ├── UIACCESS_SETUP.md        - UIAccess technical reference
│   └── License.rtf              - End-user license agreement
│
└── Build Output
    └── bin/Release/en-US/
        ├── Windows-Hinting.msi      - Ready-to-deploy installer
        └── Windows-Hinting.wixpdb   - Debug symbols
```

---

## 🔧 Building the Installer

### Quick Build
```powershell
msbuild Windows-Hinting.Installer/Windows-Hinting.Installer.wixproj -p:Configuration=Release
```

### Build with Code Signing
```powershell
# 1. Sign the executable
signtool sign /f cert.pfx /p password /fd SHA256 bin\Release\net8.0-windows\Windows-Hinting.exe

# 2. Build installer
msbuild Windows-Hinting.Installer/Windows-Hinting.Installer.wixproj -p:Configuration=Release
```

### Build Entire Solution
```powershell
msbuild Windows-Hinting.sln -p:Configuration=Release
```

---

## 📋 Installation Instructions

### End User
```cmd
# Standard installation
msiexec /i Windows-Hinting.msi

# Silent installation
msiexec /i Windows-Hinting.msi /qn

# With progress display
msiexec /i Windows-Hinting.msi /qb
```

### Uninstall
```cmd
msiexec /x Windows-Hinting.msi
```

---

## ✅ Verification Checklist

After Building:
- [ ] MSI file exists: `bin/Release/en-US/Windows-Hinting.msi`
- [ ] File size ~70 KB (appropriate for simple installer)
- [ ] WiX project file includes all .wxs files
- [ ] License.rtf is present

After Installation:
- [ ] Application installed to Program Files
- [ ] Shortcuts appear on Desktop and Start Menu
- [ ] Registry entries created in correct locations
- [ ] Uninstall information available in Control Panel
- [ ] Auto-start entry in Run key (if enabled)

For UIAccess:
- [ ] Windows-Hinting.exe has valid code signature
- [ ] Manifest embedded with uiAccess="true"
- [ ] Application can interact with elevated UI elements
- [ ] No SmartScreen/security warnings

---

## ⚙️ Customization Guide

### Change Version Number
Edit `Windows-Hinting.Installer.wixproj`:
```xml
<PropertyVersion>2.0.0.0</PropertyVersion>
```

### Disable Auto-Start
Remove from `ExampleComponents.wxs`:
```xml
<RegistryValue Root="HKLM" Key="Software\Microsoft\Windows\CurrentVersion\Run" ... />
```

### Update License
Replace `License.rtf` with your own RTF file

### Add Additional Files
Add `<File>` elements to `ExampleComponents.wxs` ComponentGroup

### Change Installation Folder Name
Modify `Package.wxs`:
```xml
<Directory Id="INSTALLFOLDER" Name="YourCustomName" />
```

---

## 🚨 Critical Requirements

### For Standalone Installation
1. ✅ Installer created and ready
2. ✅ Configured for Program Files (x64)
3. ✅ License agreement included
4. ⏳ **Code-signing certificate needed for UIAccess to work**

### For Enterprise Deployment
1. ✅ MSI format (compatible with SCCM, Intune, GPO)
2. ✅ Silent installation support
3. ✅ Uninstall available
4. ⏳ Recommended: Sign MSI with certificate

---

## 📚 Documentation Files

| File | Purpose |
|------|---------|
| **QUICK_START.md** | Fast reference for building and deploying |
| **INSTALLATION_GUIDE.md** | Comprehensive setup and configuration guide |
| **UIACCESS_SETUP.md** | Technical details about UIAccess requirements |
| **License.rtf** | End-user license agreement |

---

## 🔐 Security Notes

### Code Signing
- **Required for**: UIAccess functionality
- **Purpose**: Prevents malware abuse of UIAccess privileges
- **Tools**: signtool.exe (Windows SDK)
- **Certificates**: Use trusted CAs for production

### Installation Scope
- **per-Machine**: All users on computer (current configuration)
- **Location**: Program Files (required for UIAccess)
- **Permissions**: Administrator privileges required for installation

### Registry Permissions
- HKLM entries: Requires admin access
- HKCU entries: Per-user installation tracking
- UIAccessEnabled flag: Identifies signed application

---

## 🛠️ Troubleshooting

| Issue | Solution |
|-------|----------|
| Build fails | Run `dotnet restore` first |
| MSI not found | Check `bin/Release/en-US/` directory |
| UIAccess not working | Sign Windows-Hinting.exe with certificate |
| Installation fails | Run as Administrator |
| File in use | Close Windows-Hinting.exe before upgrading |
| SmartScreen warning | Sign executable with trusted certificate |

---

## 📞 Support Resources

- [WiX Toolset Documentation](https://wixtoolset.org/)
- [Microsoft Code Signing Guide](https://learn.microsoft.com/windows/win32/seccrypto/signtool)
- [UIAccess Security Overview](https://learn.microsoft.com/windows/win32/winauto/uiauto-securityoverview)
- [Windows Installer Documentation](https://learn.microsoft.com/windows/win32/msi/windows-installer-portal)

---

**Next Action Items**:
1. ✅ Configure and build installer (COMPLETE)
2. ⏳ Obtain code-signing certificate
3. ⏳ Sign Windows-Hinting.exe
4. ⏳ Test installer on clean Windows environment
5. ⏳ Deploy to users

---

*Configuration completed: 2026-03-15*
*Installer version: 1.0.0.0*
*WiX Toolset: 6.0.2*

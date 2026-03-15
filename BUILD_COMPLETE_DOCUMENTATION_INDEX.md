# 📚 Build Complete Script Update - Complete Documentation Index

## 🎉 Update Summary

The `build\build-complete.ps1` script has been successfully updated to automatically build the MSI installer with the signed executable.

**Status:** ✅ Complete and Ready to Use

---

## 📖 Documentation Files

### Quick Start (Read These First!)
1. **`build\COMMAND_REFERENCE.md`** ⭐ START HERE
   - Common command examples
   - Parameter reference
   - Verification commands
   - 15+ ready-to-use examples

2. **`build\BUILD_COMPLETE_QUICK_REF.md`**
   - Quick reference card
   - Most common commands
   - Troubleshooting table
   - Parameter matrix

### Detailed Guides
3. **`build\BUILD_COMPLETE_MSI_GUIDE.md`**
   - Comprehensive usage guide
   - Build step explanations
   - Requirements & setup
   - Detailed troubleshooting
   - Certificate management

4. **`BUILD_COMPLETE_SCRIPT_UPDATE.md`** (Root level)
   - Full change summary
   - Before/After comparison
   - Feature overview
   - Testing checklist

5. **`build\BUILD_COMPLETE_UPDATE.md`**
   - Technical details of changes
   - Code signing flow diagram
   - Sample output examples
   - CI/CD integration guide

---

## 🚀 Quick Start (60 seconds)

### Build with MSI (Recommended)
```powershell
cd C:\Users\knausj\git\Windows-Hinting
.\build\build-complete.ps1 -Installer
```

**Done!** You now have:
- ✅ Signed executable: `bin\Release\net8.0-windows\HintOverlay.exe`
- ✅ MSI installer: `HintOverlay.Installer2\bin\Release\en-US\HintOverlay.msi`

---

## 📋 What Changed

### Updated File
- `build\build-complete.ps1` - Enhanced with MSI integration

### New Documentation (5 files)
- `build\BUILD_COMPLETE_UPDATE.md` - Change summary
- `build\BUILD_COMPLETE_MSI_GUIDE.md` - Full guide
- `build\BUILD_COMPLETE_QUICK_REF.md` - Quick reference
- `build\COMMAND_REFERENCE.md` - Command examples
- `BUILD_COMPLETE_SCRIPT_UPDATE.md` - Overview

### Also Updated
- `build\build-complete.bat` - Batch wrapper (compatible)

---

## 🎯 Key Features

| Feature | Details |
|---------|---------|
| **Single Command** | Build exe + MSI with one command |
| **Auto Signing** | Code-signs executable automatically |
| **Verification** | Verifies signatures at every stage |
| **Smart Steps** | 1-4 step pipeline based on configuration |
| **MSI Verification** | Confirms signed exe is in MSI |
| **Error Handling** | Validates requirements upfront |
| **Flexible** | Works with Debug/Release, custom certs |

---

## 🔧 Build Pipeline

### Standard Release + MSI Build (4 Steps)

```
┌─────────────────────────────────────┐
│  [1/4] Build Executable             │
│        Compile HintOverlay.csproj    │
│        Apply code signing            │
└────────────────┬────────────────────┘
                 │
┌────────────────▼────────────────────┐
│  [2/4] Verify Executable Signature  │
│        Check certificate validity    │
│        Display signing details       │
└────────────────┬────────────────────┘
                 │
┌────────────────▼────────────────────┐
│  [3/4] Build MSI Installer          │
│        Compile .wixproj             │
│        Package signed executable    │
└────────────────┬────────────────────┘
                 │
┌────────────────▼────────────────────┐
│  [4/4] Verify MSI Contents          │
│        Extract exe from MSI         │
│        Verify signature maintained  │
└─────────────────────────────────────┘
```

---

## 📚 Documentation Guide

### For Different Needs

**"I just want to build!"**
→ Read: `build\COMMAND_REFERENCE.md` (first example)

**"What are all the available commands?"**
→ Read: `build\COMMAND_REFERENCE.md` (all examples)

**"I need a quick reference while building"**
→ Read: `build\BUILD_COMPLETE_QUICK_REF.md`

**"I want to understand everything"**
→ Read: `build\BUILD_COMPLETE_MSI_GUIDE.md`

**"What was changed in this update?"**
→ Read: `BUILD_COMPLETE_SCRIPT_UPDATE.md`

**"How does this integrate with CI/CD?"**
→ Read: `build\BUILD_COMPLETE_MSI_GUIDE.md` (CI/CD section)

---

## ✨ Usage Examples

### Most Common
```powershell
# Full Release build with signed exe and MSI
.\build\build-complete.ps1 -Installer
```

### Other Scenarios
```powershell
# Release executable only (no MSI)
.\build\build-complete.ps1

# Debug build
.\build\build-complete.ps1 -Configuration Debug

# Release without signing (testing)
.\build\build-complete.ps1 -Installer -SkipSigning

# Custom certificate
.\build\build-complete.ps1 -Installer `
  -CertPath "C:\my\cert.pfx" `
  -CertPassword "password"
```

See `build\COMMAND_REFERENCE.md` for 15+ more examples.

---

## 📦 Output Files

After running with `-Installer`:

```
HintOverlay/
├── bin/Release/net8.0-windows/
│   └── HintOverlay.exe               ← Signed executable
│
└── HintOverlay.Installer2/bin/Release/en-US/
    └── HintOverlay.msi               ← MSI with signed exe
```

---

## 🧪 Testing

### Test the Build
```powershell
.\build\build-complete.ps1 -Installer
# Should complete with "[OK]" messages
```

### Test the Installer
```powershell
msiexec /i HintOverlay.Installer2\bin\Release\en-US\HintOverlay.msi
```

### Verify Signature
```powershell
Get-AuthenticodeSignature "C:\Program Files\HintOverlay\HintOverlay.exe"
# Should show certificate details
```

---

## 🔐 Code Signing

### Default Certificate
- Location: `certs\HintOverlay_CodeSign.pfx`
- Password: `HintOverlay_BuildCert_2024`

### Generate New Certificate
```powershell
.\build\generate-signing-cert.ps1
```

### Custom Certificate
```powershell
.\build\build-complete.ps1 -Installer `
  -CertPath "C:\my-cert.pfx" `
  -CertPassword "my-password"
```

---

## ⚠️ Requirements

✅ **Visual Studio / MSBuild**
- For compiling C# projects

✅ **WiX Toolset** (for MSI builds)
- For creating installer packages
- Install from: https://wixtoolset.org/

✅ **Code Signing Certificate** (optional, Release only)
- Automatically generated if missing
- Can be custom or self-signed

✅ **lessmsi** (optional, for verification)
```powershell
choco install lessmsi
# or
winget install lessmsi
```

---

## 🚨 Troubleshooting

### Issue: "WiX installer project not found"
**Solution:** Install WiX Toolset or check `HintOverlay.Installer2` exists

### Issue: "Certificate not found"
**Solution:** 
```powershell
.\build\generate-signing-cert.ps1    # Generate new
# OR
-SkipSigning                         # Skip for testing
```

### Issue: "MSI file not found"
**Solution:** 
- Check WiX build output for errors
- Ensure Visual Studio build tools installed

### Issue: Executable not signing
**Solution:**
```powershell
-SkipSigning          # Skip for testing
# Then verify cert:
Test-Path "certs\HintOverlay_CodeSign.pfx"
```

See `build\BUILD_COMPLETE_MSI_GUIDE.md` for detailed troubleshooting.

---

## 📊 Configuration Reference

| Config | Command | Steps | Output |
|--------|---------|-------|--------|
| Release + MSI | `.\build\build-complete.ps1 -Installer` | 4 | exe + MSI (signed) |
| Release only | `.\build\build-complete.ps1` | 1 | exe (signed) |
| Debug + MSI | `-Configuration Debug -Installer` | 3 | exe + MSI (unsigned) |
| Debug only | `-Configuration Debug` | 1 | exe (unsigned) |

---

## 🎯 Next Steps

1. **Read Quick Reference**
   ```
   → build\COMMAND_REFERENCE.md
   ```

2. **Run Your First Build**
   ```powershell
   .\build\build-complete.ps1 -Installer
   ```

3. **Test the Installer**
   ```powershell
   msiexec /i HintOverlay.Installer2\bin\Release\en-US\HintOverlay.msi
   ```

4. **Verify Signature**
   ```powershell
   Get-AuthenticodeSignature "C:\Program Files\HintOverlay\HintOverlay.exe"
   ```

5. **Read Detailed Guide** (if needed)
   ```
   → build\BUILD_COMPLETE_MSI_GUIDE.md
   ```

---

## 📞 Support

For specific scenarios, consult:

| Question | Read |
|----------|------|
| "How do I run this?" | `build\COMMAND_REFERENCE.md` |
| "What are the options?" | `build\BUILD_COMPLETE_QUICK_REF.md` |
| "How does it work?" | `build\BUILD_COMPLETE_MSI_GUIDE.md` |
| "What changed?" | `BUILD_COMPLETE_SCRIPT_UPDATE.md` |
| "How do I troubleshoot?" | `build\BUILD_COMPLETE_MSI_GUIDE.md` |

---

## ✅ Checklist

- ✅ Script updated with MSI integration
- ✅ Automatic code signing
- ✅ Signature verification at each stage
- ✅ MSI content verification
- ✅ Comprehensive documentation
- ✅ Quick reference guides
- ✅ Command examples (15+)
- ✅ Troubleshooting guide
- ✅ CI/CD integration examples
- ✅ Ready for production

---

## 🎉 Status

**Build Complete Script Update: COMPLETE ✅**

The script is ready to use. Start with:
```powershell
.\build\build-complete.ps1 -Installer
```

Questions? Check `build\COMMAND_REFERENCE.md` for examples.

---

**Last Updated:** 2024
**Status:** Production Ready ✅

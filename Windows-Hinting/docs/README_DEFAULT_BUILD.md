# ✅ BUILD SCRIPT DEFAULT UPDATE - SUMMARY

## Quick Overview

The `build-complete.ps1` script now builds **both the signed executable AND MSI installer by default**.

### Before vs After

```powershell
# BEFORE
.\build\build-complete.ps1                    # Built exe only
.\build\build-complete.ps1 -Installer         # Built exe + MSI

# AFTER  
.\build\build-complete.ps1                    # Builds exe + MSI ✨ (DEFAULT!)
.\build\build-complete.ps1 -ExeOnly           # Builds exe only
```

---

## 🚀 Start Here

### Default (Full Release Build)
```powershell
.\build\build-complete.ps1
```
✅ Builds signed Release exe + MSI installer + verifies both

### Just the Executable
```powershell
.\build\build-complete.ps1 -ExeOnly
```
✅ Builds signed exe, skips MSI

### Debug Build
```powershell
.\build\build-complete.ps1 -Configuration Debug
```
✅ Builds debug exe + MSI (unsigned)

---

## 📊 What Gets Built (Default)

```
✅ bin\Release\net8.0-windows\Windows-Hinting.exe
   └─ Code-signed
   └─ Verified

✅ Windows-Hinting.Installer\bin\Release\en-US\Windows-Hinting.msi
   └─ Contains signed executable
   └─ Verified
```

---

## 🔧 Parameter Changes

### Removed Parameter
- `-Installer` → Now **default behavior** (no flag needed)

### New Parameter
- `-ExeOnly` → Skip MSI build

### Still Available
- `-Configuration` → Release or Debug
- `-SkipSigning` → Skip code signing
- `-CertPath` → Custom certificate
- `-CertPassword` → Custom password

---

## 📝 All Common Commands

```powershell
# Full Release (DEFAULT)
.\build\build-complete.ps1

# Release exe only
.\build\build-complete.ps1 -ExeOnly

# Debug (includes MSI by default)
.\build\build-complete.ps1 -Configuration Debug

# Debug exe only
.\build\build-complete.ps1 -Configuration Debug -ExeOnly

# Release without signing (testing)
.\build\build-complete.ps1 -SkipSigning

# Custom certificate
.\build\build-complete.ps1 -CertPath "C:\cert.pfx" -CertPassword "pwd"
```

---

## 📋 4-Step Build Process (Default)

```
[1/4] Build Executable
      └─ Compiles Windows-Hinting.csproj
      └─ Signs with certificate

[2/4] Verify Executable Signature
      └─ Checks certificate validity
      └─ Displays signing details

[3/4] Build MSI Installer
      └─ Compiles WiX project
      └─ Packages signed executable

[4/4] Verify MSI Contents
      └─ Extracts exe from MSI
      └─ Confirms signature maintained
```

---

## ✨ Key Benefits

✅ **Simpler** - No need for flags, just run it
✅ **Faster** - Single command does everything
✅ **Safer** - Automatic verification at each stage
✅ **Cleaner** - Clear step numbers show progress
✅ **Flexible** - Still can skip MSI with `-ExeOnly`

---

## 📚 Documentation

All documentation updated to reflect new defaults:

| File | Purpose |
|------|---------|
| `build\COMMAND_REFERENCE.md` | All command examples |
| `build\BUILD_COMPLETE_QUICK_REF.md` | Quick reference card |
| `build\BUILD_COMPLETE_MSI_GUIDE.md` | Detailed guide |
| `BUILD_DEFAULT_INSTALLER.md` | This update explained |
| `BUILD_COMPLETE_DOCUMENTATION_INDEX.md` | Master index |

---

## 🆕 Updated Files

### Modified
- `build\build-complete.ps1` - Changed parameters & defaults
- `build\build-complete.bat` - Updated batch wrapper

### Recreated (With New Info)
- `build\COMMAND_REFERENCE.md`
- `build\BUILD_COMPLETE_QUICK_REF.md`

### New
- `BUILD_DEFAULT_INSTALLER.md` - This update

---

## 🧪 Quick Verification

### Test the new default
```powershell
.\build\build-complete.ps1
```
Expected: 4-step build creating both exe and MSI ✅

### Test exe-only mode
```powershell
.\build\build-complete.ps1 -ExeOnly
```
Expected: 1-step build creating exe only ✅

### Test batch wrapper
```batch
build\build-complete.bat
```
Expected: Same as PowerShell, builds exe + MSI ✅

---

## 💡 Examples

### CI/CD Pipeline
```powershell
# Just run, no extra flags needed
& .\build\build-complete.ps1 -Configuration Release

# Archive outputs
Copy-Item "bin\Release\net8.0-windows\Windows-Hinting.exe" "artifacts\"
Copy-Item "Windows-Hinting.Installer\bin\Release\en-US\Windows-Hinting.msi" "artifacts\"
```

### Local Development
```powershell
# Just run, get everything
.\build\build-complete.ps1

# Test installer
msiexec /i Windows-Hinting.Installer\bin\Release\en-US\Windows-Hinting.msi
```

### Quick Exe Build Only
```powershell
# When you just need the executable
.\build\build-complete.ps1 -ExeOnly
```

---

## ✅ Compatibility

### Migration Path

If you have scripts using the old way:
```powershell
# OLD (still works, but different behavior)
.\build\build-complete.ps1 -Installer

# This will error because -Installer is removed
# NEW (use instead)
.\build\build-complete.ps1
# (No flag needed, MSI is now default)
```

To build exe-only:
```powershell
# OLD (no flag)
.\build\build-complete.ps1

# NEW (use -ExeOnly)
.\build\build-complete.ps1 -ExeOnly
```

---

## 🎯 Next Steps

1. **Use the script:**
   ```powershell
   .\build\build-complete.ps1
   ```

2. **Verify outputs:**
   - Check `bin\Release\net8.0-windows\Windows-Hinting.exe`
   - Check `Windows-Hinting.Installer\bin\Release\en-US\Windows-Hinting.msi`

3. **Test installer:**
   ```powershell
   msiexec /i Windows-Hinting.Installer\bin\Release\en-US\Windows-Hinting.msi
   ```

4. **Verify signature:**
   ```powershell
   Get-AuthenticodeSignature "C:\Program Files\Windows-Hinting\Windows-Hinting.exe"
   ```

---

## 📞 Questions?

- **How do I build exe-only?** → `.\build\build-complete.ps1 -ExeOnly`
- **How do I use a custom cert?** → `.\build\build-complete.ps1 -CertPath "..." -CertPassword "..."`
- **How do I skip signing?** → `.\build\build-complete.ps1 -SkipSigning`
- **What are all the commands?** → See `build\COMMAND_REFERENCE.md`

---

**Status:** ✅ Complete - Ready to use!

Now just run:
```powershell
.\build\build-complete.ps1
```

Done! You have a signed executable and MSI installer. 🎉

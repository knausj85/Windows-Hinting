# ✅ Build Script Default Behavior Update - COMPLETED

## 📝 What Changed

The `build-complete.ps1` script now builds both the executable AND MSI installer by default.

### Before
```powershell
.\build\build-complete.ps1              # Built exe only
.\build\build-complete.ps1 -Installer   # Built exe + MSI
```

### After
```powershell
.\build\build-complete.ps1              # Builds exe + MSI (DEFAULT!)
.\build\build-complete.ps1 -ExeOnly     # Builds exe only
```

---

## 🎯 Key Changes

### Parameter Changes
| Old Parameter | New Parameter | Behavior |
|---|---|---|
| `-Installer` (optional) | Removed | **Now default behavior** |
| N/A | `-ExeOnly` (new flag) | Skip MSI build |

### Default Behavior
- **Release mode:** Builds signed exe + MSI
- **Debug mode:** Builds unsigned exe + MSI
- **With `-ExeOnly`:** Skips MSI, exe only
- **With `-SkipSigning`:** Exe unsigned, MSI included

---

## 🚀 Quick Start

### Default: Full Release Build with MSI
```powershell
.\build\build-complete.ps1
```
✅ Builds signed exe + MSI installer + verifies both

### Exe Only (No MSI)
```powershell
.\build\build-complete.ps1 -ExeOnly
```
✅ Builds signed exe, skips MSI

### Debug Build (Includes MSI)
```powershell
.\build\build-complete.ps1 -Configuration Debug
```
✅ Builds unsigned exe + MSI

### Custom Certificate
```powershell
.\build\build-complete.ps1 `
  -CertPath "C:\certs\cert.pfx" `
  -CertPassword "password"
```
✅ Uses custom cert, includes MSI

---

## 📊 Build Pipeline (Default)

```
┌─────────────────────────────────────┐
│  [1/4] Build Executable             │
│        Compile & Sign               │
└────────────────┬────────────────────┘
                 │
┌────────────────▼────────────────────┐
│  [2/4] Verify Executable Signature  │
│        Check certificate            │
└────────────────┬────────────────────┘
                 │
┌────────────────▼────────────────────┐
│  [3/4] Build MSI Installer          │
│        Package with signed exe      │
└────────────────┬────────────────────┘
                 │
┌────────────────▼────────────────────┐
│  [4/4] Verify MSI Contents          │
│        Confirm signed exe inside    │
└─────────────────────────────────────┘
```

---

## 📚 Updated Documentation

All documentation has been updated to reflect the new defaults:

| File | Purpose |
|------|---------|
| `build\COMMAND_REFERENCE.md` | Command examples |
| `build\BUILD_COMPLETE_QUICK_REF.md` | Quick reference |
| `build\BUILD_COMPLETE_MSI_GUIDE.md` | Full guide |
| `BUILD_COMPLETE_DOCUMENTATION_INDEX.md` | Master index |

---

## ✨ Usage Examples

### Simplest Case (Full Build)
```powershell
.\build\build-complete.ps1
```

### Still Want Just Exe?
```powershell
.\build\build-complete.ps1 -ExeOnly
```

### Debug with MSI
```powershell
.\build\build-complete.ps1 -Configuration Debug
```

### Release without Signing
```powershell
.\build\build-complete.ps1 -SkipSigning
```

### All Variations
```powershell
# Release exe + MSI, signed (DEFAULT)
.\build\build-complete.ps1

# Release exe only, signed
.\build\build-complete.ps1 -ExeOnly

# Debug exe + MSI, unsigned
.\build\build-complete.ps1 -Configuration Debug

# Debug exe only, unsigned
.\build\build-complete.ps1 -Configuration Debug -ExeOnly

# Release exe + MSI, unsigned
.\build\build-complete.ps1 -SkipSigning

# Custom certificate
.\build\build-complete.ps1 -CertPath "..." -CertPassword "..."
```

---

## 🔄 Batch File Wrapper

Also updated: `build\build-complete.bat`

```batch
# Full build (DEFAULT)
build\build-complete.bat

# Release exe only
build\build-complete.bat Release --exe-only

# Debug with MSI
build\build-complete.bat Debug

# Release without signing
build\build-complete.bat Release --skip-signing
```

---

## 🧪 Verify the Changes

### Test 1: Full Release Build
```powershell
.\build\build-complete.ps1
```
Expected output:
- `[1/4]` Build executable ✅
- `[2/4]` Verify signature ✅
- `[3/4]` Build MSI ✅
- `[4/4]` Verify MSI ✅

### Test 2: Release Exe Only
```powershell
.\build\build-complete.ps1 -ExeOnly
```
Expected output:
- `[1/1]` Build executable ✅
- No MSI build steps

### Test 3: Debug Build
```powershell
.\build\build-complete.ps1 -Configuration Debug
```
Expected output:
- `[1/3]` Build executable ✅
- `[2/3]` Build MSI ✅
- `[3/3]` Verify MSI ✅

---

## 📦 Output Files

After running `.\build\build-complete.ps1`:

```
Windows-Hinting/
├── bin/Release/net8.0-windows/
│   └── Windows-Hinting.exe               ← Signed
│
└── Windows-Hinting.Installer/bin/Release/en-US/
    └── Windows-Hinting.msi               ← With signed exe
```

---

## ✅ Updated Files

### Modified
- `build\build-complete.ps1` - Changed defaults and parameters
- `build\build-complete.bat` - Updated parameter handling

### Documentation (Recreated)
- `build\COMMAND_REFERENCE.md` - New examples with updated defaults
- `build\BUILD_COMPLETE_QUICK_REF.md` - New reference with updated commands

### Still Valid
- `build\BUILD_COMPLETE_MSI_GUIDE.md` - Concepts still apply
- `build\BUILD_COMPLETE_UPDATE.md` - Technical details
- `BUILD_COMPLETE_DOCUMENTATION_INDEX.md` - Master index

---

## 🎉 Benefits

✅ **Simpler Default** - Just run the script, get everything
✅ **Less Typing** - No need for `-Installer` flag
✅ **Intuitive** - Most people want both exe + MSI
✅ **Flexible** - Still can build exe-only with `-ExeOnly`
✅ **Backwards Compatible** - All options still work
✅ **Clear Status** - Step numbers show what's happening

---

## 🚨 Migration Notes

### If You Use CI/CD
Update your scripts:
```powershell
# Old
.\build\build-complete.ps1 -Installer

# New (same behavior, simpler)
.\build\build-complete.ps1
```

### If You Only Want Exe
```powershell
# Old (no flag, just exe)
.\build\build-complete.ps1

# New (use -ExeOnly)
.\build\build-complete.ps1 -ExeOnly
```

---

## 📋 Parameter Summary

| Parameter | Default | Purpose |
|-----------|---------|---------|
| `-Configuration` | Release | Debug or Release build |
| `-ExeOnly` | False | Skip MSI build |
| `-SkipSigning` | False | Skip code signing |
| `-CertPath` | `certs\WindowsHinting_CodeSign.pfx` | Certificate file path |
| `-CertPassword` | `WindowsHinting_BuildCert_2024` | Certificate password |

---

## 🎯 Next Steps

1. Run the updated script:
   ```powershell
   .\build\build-complete.ps1
   ```

2. Verify both exe and MSI are created

3. Test the installer:
   ```powershell
   msiexec /i Windows-Hinting.Installer\bin\Release\en-US\Windows-Hinting.msi
   ```

4. Check signature:
   ```powershell
   Get-AuthenticodeSignature "C:\Program Files\Windows-Hinting\Windows-Hinting.exe"
   ```

---

## 📞 Reference

- **Command examples:** `build\COMMAND_REFERENCE.md`
- **Quick reference:** `build\BUILD_COMPLETE_QUICK_REF.md`
- **Full guide:** `build\BUILD_COMPLETE_MSI_GUIDE.md`
- **Master index:** `BUILD_COMPLETE_DOCUMENTATION_INDEX.md`

---

**Status:** ✅ Update Complete - Ready to use!

The build script now builds both the signed executable and MSI installer by default. Just run:
```powershell
.\build\build-complete.ps1
```

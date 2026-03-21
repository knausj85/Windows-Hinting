# 📚 BUILD SCRIPT UPDATES - COMPLETE INDEX

## 🎯 What Was Done

The `build-complete.ps1` script has been updated to **build both the signed executable and MSI installer by default**.

**Before:** `.\build\build-complete.ps1` → exe only (need `-Installer` flag for MSI)
**After:** `.\build\build-complete.ps1` → exe + MSI (default!) 🎉

---

## 📖 Documentation Overview

### Start Here ⭐
| File | Purpose | Read If |
|------|---------|---------|
| `README_DEFAULT_BUILD.md` | Quick summary of changes | You want a 2-minute overview |
| `build\COMMAND_REFERENCE.md` | All command examples | You want to see how to use it |
| `build\BUILD_COMPLETE_QUICK_REF.md` | Quick reference card | You need quick lookup during building |

### Detailed Guides
| File | Purpose | Read If |
|------|---------|---------|
| `BUILD_DEFAULT_INSTALLER.md` | Detailed change explanation | You want to understand what changed |
| `build\BUILD_COMPLETE_MSI_GUIDE.md` | Full guide with troubleshooting | You hit an issue or need deep knowledge |
| `BUILD_COMPLETE_DOCUMENTATION_INDEX.md` | Master index of all docs | You're looking for something specific |

---

## 🚀 Quick Start (30 seconds)

### Run the default build
```powershell
.\build\build-complete.ps1
```

**That's it!** You now have:
- ✅ Signed Release executable: `bin\Release\net8.0-windows\Windows-Hinting.exe`
- ✅ MSI installer: `Windows-Hinting.Installer\bin\Release\en-US\Windows-Hinting.msi`

---

## 📋 Common Commands

```powershell
# Full Release (signed exe + MSI) — DEFAULT
.\build\build-complete.ps1

# Release exe only (no MSI)
.\build\build-complete.ps1 -ExeOnly

# Debug build (exe + MSI, unsigned)
.\build\build-complete.ps1 -Configuration Debug

# Release without signing
.\build\build-complete.ps1 -SkipSigning

# Custom certificate
.\build\build-complete.ps1 -CertPath "C:\cert.pfx" -CertPassword "pwd"
```

---

## ✨ Key Changes

### Parameters
| Old | New | Note |
|-----|-----|------|
| `-Installer` (optional) | Removed | MSI now default |
| N/A | `-ExeOnly` (new) | Skip MSI if needed |
| `-Configuration` | Unchanged | Still Release/Debug |
| `-SkipSigning` | Unchanged | Still works |
| `-CertPath` | Unchanged | Still works |
| `-CertPassword` | Unchanged | Still works |

### Default Behavior
- **Release mode:** Builds signed exe + MSI
- **Debug mode:** Builds unsigned exe + MSI
- **With `-ExeOnly`:** Skips MSI, exe only
- **With `-SkipSigning`:** Exe unsigned, MSI included

---

## 📊 Build Steps (Default)

```
[1/4] Build Executable
      └─ Compiles and signs

[2/4] Verify Signature
      └─ Checks certificate

[3/4] Build MSI
      └─ Creates installer

[4/4] Verify MSI Contents
      └─ Confirms signed exe inside
```

---

## 📂 Files Modified/Created

### Modified
- `build\build-complete.ps1` - Updated parameters & logic
- `build\build-complete.bat` - Updated batch wrapper

### Updated Documentation
- `build\COMMAND_REFERENCE.md` - New command examples
- `build\BUILD_COMPLETE_QUICK_REF.md` - Updated reference

### New Documentation
- `BUILD_DEFAULT_INSTALLER.md` - Explains the update
- `README_DEFAULT_BUILD.md` - Quick summary
- This file: `BUILD_SCRIPT_UPDATES_INDEX.md`

### Still Valid
- `build\BUILD_COMPLETE_MSI_GUIDE.md` - Full guide
- `build\BUILD_COMPLETE_UPDATE.md` - Technical details
- `BUILD_COMPLETE_DOCUMENTATION_INDEX.md` - Master index

---

## 🔍 Need Help?

### "How do I use the script?"
→ Read: `README_DEFAULT_BUILD.md` (5 min read)

### "What are all the commands?"
→ Read: `build\COMMAND_REFERENCE.md` (reference style)

### "I need quick lookup"
→ Read: `build\BUILD_COMPLETE_QUICK_REF.md` (card style)

### "I want to understand everything"
→ Read: `build\BUILD_COMPLETE_MSI_GUIDE.md` (full guide)

### "What was changed?"
→ Read: `BUILD_DEFAULT_INSTALLER.md` (change details)

### "I hit an error"
→ Check: `build\BUILD_COMPLETE_MSI_GUIDE.md` → Troubleshooting section

---

## ✅ Verification Checklist

- ✅ Script parameter changed from `-Installer` to `-ExeOnly`
- ✅ Default behavior now builds both exe + MSI
- ✅ MSI only built in Release mode by default
- ✅ Batch wrapper updated
- ✅ All documentation updated
- ✅ Quick reference guides created
- ✅ New summary documents created
- ✅ Ready for immediate use

---

## 🎉 Current Status

**The script is ready to use!**

### Just run:
```powershell
.\build\build-complete.ps1
```

### Done! You have:
- Signed Release executable
- MSI installer with signed executable
- Full verification output
- Ready to distribute

---

## 📚 Documentation Map

```
Documentation/
├── Quick Start
│   ├── README_DEFAULT_BUILD.md ⭐ Start here
│   └── build\COMMAND_REFERENCE.md
│
├── Quick Reference
│   └── build\BUILD_COMPLETE_QUICK_REF.md
│
├── Detailed Guides
│   ├── BUILD_DEFAULT_INSTALLER.md
│   ├── build\BUILD_COMPLETE_MSI_GUIDE.md
│   └── build\BUILD_COMPLETE_UPDATE.md
│
└── Master Indexes
    ├── BUILD_COMPLETE_DOCUMENTATION_INDEX.md
    └── BUILD_SCRIPT_UPDATES_INDEX.md (this file)
```

---

## 🚀 Next Steps

1. **Read:** `README_DEFAULT_BUILD.md` (2 min)
2. **Run:** `.\build\build-complete.ps1` (1-5 min)
3. **Verify:** Check exe and MSI created (1 min)
4. **Test:** Run MSI installer (5 min)
5. **Reference:** Bookmark `build\COMMAND_REFERENCE.md`

---

## 💡 Pro Tips

### For CI/CD
Just use the default, MSI is automatic:
```powershell
& .\build\build-complete.ps1
```

### For Local Dev
- Full build: `.\build\build-complete.ps1`
- Quick exe only: `.\build\build-complete.ps1 -ExeOnly`

### For Testing
- No signing: `.\build\build-complete.ps1 -SkipSigning`
- Debug mode: `.\build\build-complete.ps1 -Configuration Debug`

### For Custom Certs
```powershell
.\build\build-complete.ps1 -CertPath "..." -CertPassword "..."
```

---

## 📞 Quick Reference

| Need | Command | Doc |
|------|---------|-----|
| Full build | `.\build\build-complete.ps1` | This page |
| Exe only | `.\build\build-complete.ps1 -ExeOnly` | This page |
| All commands | See `build\COMMAND_REFERENCE.md` | Detailed |
| Troubleshooting | See `build\BUILD_COMPLETE_MSI_GUIDE.md` | Full guide |
| Technical details | See `build\BUILD_COMPLETE_UPDATE.md` | Dev info |

---

**Status:** ✅ Complete and Ready!

The build script now intelligently builds everything you need by default.

Just run:
```powershell
.\build\build-complete.ps1
```

🎉 Done!

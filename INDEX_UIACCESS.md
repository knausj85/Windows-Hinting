# uiAccess & Code Signing - Complete Index

## 📖 Documentation Overview

| Document | Purpose | Read Time | Audience |
|----------|---------|-----------|----------|
| **README_UIACCESS_SETUP.md** | Start here! Quick summary | 5 min | Everyone |
| **VISUAL_GUIDE.md** | Diagrams and flowcharts | 10 min | Visual learners |
| **UIACCESS_QUICKSTART.md** | Quick reference | 5 min | Fast reference |
| **CODESIGNING_GUIDE.md** | Complete reference | 30 min | Deep dive |
| **UIACCESS_IMPLEMENTATION_COMPLETE.md** | Status & details | 15 min | Technical details |

## 🎯 Start Here Based on Your Needs

### "I just want to get started"
1. Read: **README_UIACCESS_SETUP.md** (5 min)
2. Run: `.\Create-CodeSigningCert.ps1`
3. Build & Sign: `.\Sign-WindowsHinting.ps1 -BuildConfiguration Release`
4. Done!

### "I want to understand the architecture"
1. Look at: **VISUAL_GUIDE.md** (diagrams)
2. Read: **UIACCESS_IMPLEMENTATION_COMPLETE.md** (details)
3. Reference: **CODESIGNING_GUIDE.md** (full info)

### "I want to set up CI/CD automation"
1. Read: **CODESIGNING_GUIDE.md** → "CI/CD Integration"
2. Copy: GitHub Actions example
3. Configure: Your pipeline

### "I need a detailed reference"
1. Check: **CODESIGNING_GUIDE.md** (comprehensive)
2. Search: Ctrl+F for your topic

## 📁 Files Created

### Configuration Files
```
app.manifest                      ← Manifest with uiAccess="true"
Windows-Hinting.csproj               ← Updated to reference manifest
```

### Script Files
```
Create-CodeSigningCert.ps1        ← Create certificates (one-time)
Sign-WindowsHinting.ps1             ← Sign executables (after each build)
```

### Documentation Files
```
README_UIACCESS_SETUP.md                    ← Summary & overview
VISUAL_GUIDE.md                             ← Diagrams and flowcharts
UIACCESS_QUICKSTART.md                      ← 5-minute reference
CODESIGNING_GUIDE.md                        ← Complete comprehensive guide
UIACCESS_IMPLEMENTATION_COMPLETE.md         ← Implementation status
```

## 🚀 Quick Reference

### Create Certificate (One-time)
```powershell
.\Create-CodeSigningCert.ps1
# Answer: Enter a password (save it!)
# Creates: WindowsHinting_CodeSign.pfx
```

### Build Application
```powershell
# Option A: Visual Studio
Ctrl+Shift+B (then set to Release mode)

# Option B: Command line (once build issue is fixed)
msbuild Windows-Hinting.sln /p:Configuration=Release
```

### Sign Executable
```powershell
.\Sign-WindowsHinting.ps1 -BuildConfiguration Release
# Answer: Enter certificate password from Step 1
# Creates: Signed Windows-Hinting.exe with uiAccess
```

### Verify Signature
```powershell
Get-AuthenticodeSignature "bin\Release\net8.0-windows\Windows-Hinting.exe"
# Expected: Status = "Valid"
```

## ✅ Implementation Checklist

### Phase 1: Setup (First Time)
- [ ] Review: README_UIACCESS_SETUP.md
- [ ] Create certificate: .\Create-CodeSigningCert.ps1
- [ ] Save password securely
- [ ] Verify PFX file: C:\Users\knausj\WindowsHinting_CodeSign.pfx

### Phase 2: First Build
- [ ] Build application: Ctrl+Shift+B (Release)
- [ ] Sign: .\Sign-WindowsHinting.ps1 -BuildConfiguration Release
- [ ] Verify: Get-AuthenticodeSignature
- [ ] Test: Run the executable

### Phase 3: Ongoing
- [ ] Build → Sign → Test (repeat)
- [ ] Optionally automate (see CODESIGNING_GUIDE.md)

## 🎓 Learning Paths

### Path A: Quick Setup (15 minutes)
```
README_UIACCESS_SETUP.md (5 min)
    ↓
Run scripts (5 min)
    ↓
Test (5 min)
    ↓
Done!
```

### Path B: Complete Understanding (1 hour)
```
VISUAL_GUIDE.md (10 min)
    ↓
README_UIACCESS_SETUP.md (5 min)
    ↓
UIACCESS_IMPLEMENTATION_COMPLETE.md (15 min)
    ↓
CODESIGNING_GUIDE.md (30 min)
    ↓
Ready for production!
```

### Path C: Production Setup (2+ hours)
```
All documentation (1 hour)
    ↓
CODESIGNING_GUIDE.md - Commercial Certs (30 min)
    ↓
CODESIGNING_GUIDE.md - CI/CD Integration (30 min)
    ↓
Setup pipeline & automation
    ↓
Ready for scale!
```

## 💡 Key Concepts

### uiAccess
- Allows application to interact with elevated UI
- Requires manifest declaration
- Requires digital signature
- Only works on Windows

### Code Signing
- Creates unforgeable proof of authenticity
- Certificate + private key = signature
- Windows verifies before granting uiAccess
- Self-signed OK for dev, commercial for prod

### Manifest
- XML file declaring application intent
- Embedded in executable (auto by .csproj)
- Declares: uiAccess="true", compatibility, DPI
- Windows reads before granting privileges

## 🔗 Cross-References

| Concept | Where to Learn |
|---------|----------------|
| Quick start | README_UIACCESS_SETUP.md |
| Architecture | VISUAL_GUIDE.md |
| How uiAccess works | UIACCESS_IMPLEMENTATION_COMPLETE.md |
| Certificates | CODESIGNING_GUIDE.md |
| Commercial certs | CODESIGNING_GUIDE.md → "Option B" |
| CI/CD setup | CODESIGNING_GUIDE.md → "CI/CD Integration" |
| Troubleshooting | CODESIGNING_GUIDE.md → "Troubleshooting" |
| Best practices | CODESIGNING_GUIDE.md → "Security" |

## 📊 Decision Tree

```
Do you want uiAccess?
├─ YES → Follow Quick Setup (README_UIACCESS_SETUP.md)
└─ NO → Don't add manifest

Do you need commercial certificate?
├─ YES (for public distribution)
│  └─ See CODESIGNING_GUIDE.md → "Option B: Commercial"
└─ NO (for testing/internal use)
   └─ Use self-signed (already set up)

Do you want to automate signing?
├─ YES (for CI/CD)
│  └─ See CODESIGNING_GUIDE.md → "CI/CD Integration"
└─ NO (manual signing is fine)
   └─ Just use Sign-WindowsHinting.ps1 after each build

Need more info?
├─ General questions → README_UIACCESS_SETUP.md
├─ Visual learner → VISUAL_GUIDE.md
├─ Technical details → UIACCESS_IMPLEMENTATION_COMPLETE.md
└─ Everything → CODESIGNING_GUIDE.md
```

## 🛠️ Script Reference

### Create-CodeSigningCert.ps1
```
Usage: .\Create-CodeSigningCert.ps1
Params: Optional (cert name, friendly name, validity, output path, password)
Output: PFX file (for signing), CER file (for distribution)
Time: ~5 seconds
When: Run once at setup
```

### Sign-WindowsHinting.ps1
```
Usage: .\Sign-WindowsHinting.ps1 -BuildConfiguration Release
Params: -BuildConfiguration, -CertificatePath, -CertificatePassword
Output: Signed Windows-Hinting.exe
Time: ~2 seconds
When: After each build
```

## 📝 Common Tasks

### "I want to sign my executable"
```powershell
.\Sign-WindowsHinting.ps1 -BuildConfiguration Release
```
See: README_UIACCESS_SETUP.md

### "I want to verify the signature"
```powershell
Get-AuthenticodeSignature "bin\Release\net8.0-windows\Windows-Hinting.exe"
```
See: UIACCESS_QUICKSTART.md

### "I want to use a commercial certificate"
See: CODESIGNING_GUIDE.md → "Option B: Commercial Certificate"

### "I want to automate this in GitHub Actions"
See: CODESIGNING_GUIDE.md → "CI/CD Integration"

### "Something's not working"
See: CODESIGNING_GUIDE.md → "Troubleshooting"

### "I want to deploy to Program Files"
See: UIACCESS_IMPLEMENTATION_COMPLETE.md → "Production Checklist"

## ⚠️ Known Issues

### Build Issue (Pre-existing)
```
Error: ResolveComReference task not supported
```
**Workaround**: Use Visual Studio GUI to build
- Ctrl+Shift+B (works perfectly)
- No command-line build needed

See: UIACCESS_IMPLEMENTATION_COMPLETE.md → "Build Issue"

## ✅ Success Criteria

```
Phase 1: Setup
  ✓ Certificate created (PFX file exists)
  ✓ Password saved securely

Phase 2: Build & Sign
  ✓ Executable built (file exists)
  ✓ Signed (signature verification succeeds)
  ✓ Manifest embedded (uiAccess declared)

Phase 3: Deployment
  ✓ Executable runs
  ✓ Has uiAccess privileges
  ✓ Can interact with elevated UI
```

## 📱 File Locations

```
Your Computer
├─ Certificate: C:\Users\knausj\WindowsHinting_CodeSign.pfx
├─ Executable: C:\Users\knausj\git\Windows-Hinting\
│               bin\Release\net8.0-windows\Windows-Hinting.exe
└─ Documentation: C:\Users\knausj\git\Windows-Hinting\
                  [various .md files]
```

## 🎯 Next Steps

1. **Read**: README_UIACCESS_SETUP.md (5 min)
2. **Run**: .\Create-CodeSigningCert.ps1 (5 min)
3. **Build**: Ctrl+Shift+B (5 min)
4. **Sign**: .\Sign-WindowsHinting.ps1 -BuildConfiguration Release (1 min)
5. **Verify**: Get-AuthenticodeSignature (1 min)
6. **Done**: Signed executable with uiAccess! ✨

**Total time**: ~17 minutes (first time)
**Time per rebuild**: ~2 minutes (sign + verify)

---

## 📞 Quick Help

| Question | Answer |
|----------|--------|
| Where do I start? | README_UIACCESS_SETUP.md |
| How does this work? | VISUAL_GUIDE.md |
| I'm stuck | CODESIGNING_GUIDE.md → Troubleshooting |
| I want commercial certs | CODESIGNING_GUIDE.md → Option B |
| I want CI/CD | CODESIGNING_GUIDE.md → CI/CD Integration |
| Everything! | CODESIGNING_GUIDE.md (comprehensive) |

---

## Summary

✅ **What you have**:
- Manifest with uiAccess
- Scripts to create certificates
- Scripts to sign executables
- Complete documentation

🚀 **What to do now**:
- Read README_UIACCESS_SETUP.md
- Run Create-CodeSigningCert.ps1
- Build & sign your executable

🎉 **Result**:
- Signed executable with uiAccess enabled
- Can interact with elevated UI
- Ready to distribute (with self-signed cert)

---

**Last Updated**: 2024
**Status**: ✅ Complete & Ready to Use
**Time to First Signature**: ~15 minutes

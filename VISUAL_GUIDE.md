# Visual Guide: uiAccess & Code Signing

## What You Have Now

```
Your Application
      ↓
app.manifest (declares uiAccess="true")
      ↓
HintOverlay.csproj (embeds manifest)
      ↓
dotnet build / Visual Studio
      ↓
HintOverlay.exe (with manifest inside)
      ↓
Sign-HintOverlay.ps1 (signs with certificate)
      ↓
SIGNED HintOverlay.exe (with uiAccess enabled)
      ↓
Windows trusts it → grants uiAccess privileges
      ↓
Can interact with elevated UI elements ✨
```

---

## Workflow Diagram

```
┌─────────────────────────────────┐
│  You write C# code              │
│  (same as always)               │
└──────────────┬──────────────────┘
               │
               ▼
    ┌──────────────────────┐
    │  Build (Ctrl+Shift+B)│ ← Use Visual Studio GUI
    │  or msbuild          │   (avoids COM issue)
    └──────────────────────┘
               │
               ▼
   ┌───────────────────────────────┐
   │  HintOverlay.exe (unsigned)   │
   │  Contains embedded manifest   │
   │  with uiAccess="true"         │
   └──────────────┬────────────────┘
                  │
     ┌────────────▼────────────┐
     │ Sign-HintOverlay.ps1    │ ← One command:
     │ .\Sign-HintOverlay.ps1  │   .\Sign-HintOverlay.ps1
     │ -BuildConfiguration     │      -BuildConfiguration Release
     │ Release                 │
     └────────────┬────────────┘
                  │
                  ▼
    ┌─────────────────────────────┐
    │  HintOverlay.exe (signed)   │
    │  ✅ Digital signature       │
    │  ✅ Certificate chain       │
    │  ✅ Manifest embedded       │
    │  ✅ uiAccess enabled        │
    └──────────────┬──────────────┘
                   │
                   ▼
        ┌──────────────────────┐
        │  Windows validates:  │
        │  • Signature valid?  │
        │  • Trusted cert?     │
        │  • uiAccess declared?│
        └──────────────────────┘
                   │
                   ▼
        ┌──────────────────────┐
        │  ✅ Grant uiAccess   │
        │  ✅ Can interact     │
        │  ✅ with elevated UI │
        └──────────────────────┘
```

---

## File Organization

```
Project Root
│
├── 📄 app.manifest                      ← What: Manifest with uiAccess
│                                          When: Use: Always (embedded in .exe)
│
├── 📄 HintOverlay.csproj                ← What: References manifest
│                                          When: Use: Always (build config)
│
├── 🔧 Create-CodeSigningCert.ps1        ← What: Create certificates
│                                          When: Use: Once at setup
│
├── 🔧 Sign-HintOverlay.ps1              ← What: Sign executables
│                                          When: Use: After each build
│
├── 📚 README_UIACCESS_SETUP.md           ← You are here! Quick summary
├── 📚 UIACCESS_QUICKSTART.md             ← 5-minute guide
├── 📚 CODESIGNING_GUIDE.md               ← Complete reference
└── 📚 UIACCESS_IMPLEMENTATION_COMPLETE.md ← Implementation details
```

---

## Implementation Timeline

```
NOW                          FIRST BUILD                    ONGOING
│                            │                              │
│  Step 1: Create Cert      │  Step 2: Build               │  Step 4: Sign
│  ┌──────────────────────┐ │  ┌──────────────────┐        │  ┌─────────────┐
│  │ Create-CodeSigningCert│ │  │ Ctrl+Shift+B     │        │  │ Sign-Hint   │
│  │ .ps1                 │ │  │ (Visual Studio)  │ ──────┐ │  │ Overlay.ps1 │
│  │ [Enter password]     │ │  │ or msbuild       │       │ │  │ [password]  │
│  │ ✓ Cert created      │ │  │ ✓ .exe built     │       └─┼─│ ✓ Signed    │
│  └──────────────────────┘ │  └──────────────────┘        │  └─────────────┘
│        5 minutes           │      5 minutes               │    1 minute
│                            │                              │
└────────────────────────────┴──────────────────────────────┴─────────────
      One time setup               First build              Every rebuild
```

---

## Decision Tree

```
Do you want uiAccess?
    │
    ├─ YES (typical for HintOverlay)
    │   │
    │   ├─ Create certificate
    │   │  └─ .\Create-CodeSigningCert.ps1
    │   │
    │   ├─ Build application
    │   │  └─ Visual Studio: Ctrl+Shift+B
    │   │
    │   ├─ Sign executable
    │   │  └─ .\Sign-HintOverlay.ps1 -BuildConfiguration Release
    │   │
    │   └─ Deploy
    │      └─ Share HintOverlay.exe (signed, with uiAccess)
    │
    └─ NO
        └─ Don't add manifest, don't sign, proceed normally
```

---

## Certificate Management

```
┌─────────────────────────────────────────────────┐
│        Create-CodeSigningCert.ps1               │
│         (Run once, creates files)               │
└────────────┬────────────────────────────────────┘
             │
    ┌────────┴────────┬─────────────┐
    │                 │             │
    ▼                 ▼             ▼
┌──────────┐  ┌──────────────┐  ┌──────────────┐
│ In Store │  │ PFX File     │  │ CER File     │
│ Cert:\   │  │ (signing)    │  │ (trust)      │
│ Current  │  │ Password:    │  │ (optional)   │
│ User\My  │  │ Protected    │  │              │
└────┬─────┘  └──────┬───────┘  └──────┬───────┘
     │               │                  │
     │        ┌──────┴────────────┐     │
     │        │                  │     │
     ▼        ▼                  ▼     ▼
   Used by  Used by             Used by
   Windows  Sign-Hint           Trust (optional)
   Cert Store Overlay.ps1
```

---

## Code Signing Flow

```
Certificate (PFX)
    │ (with password)
    │
    ▼
┌─────────────────────┐
│ Extract Private Key │
└──────────┬──────────┘
           │
    ┌──────┴──────────────────────┐
    │                             │
    ▼                             ▼
HintOverlay.exe          SHA256 Hash
Calculate Hash                │
    │                         │
    │                         ▼
    │                 ┌──────────────────┐
    │                 │ Sign Hash with   │
    │                 │ Private Key      │
    │                 │ (unforgeable)    │
    │                 └──────────┬───────┘
    │                            │
    └────────────┬───────────────┘
                 │
                 ▼
          ┌──────────────────┐
          │ Embed Signature  │
          │ in PE Header     │
          │ with Certificate │
          │ Chain            │
          └──────────┬───────┘
                     │
                     ▼
             ┌───────────────────┐
             │ SIGNED Executable │
             │ Ready for uiAccess│
             └───────────────────┘
```

---

## Signature Verification (Windows)

```
User launches HintOverlay.exe
    │
    ▼
Windows Loader Checks:
    │
    ├─ 1. Extract signature from PE
    │   └─ ✓ Found
    │
    ├─ 2. Verify signature
    │   └─ ✓ Valid (not tampered)
    │
    ├─ 3. Check certificate trust
    │   ├─ Self-signed? (yes)
    │   └─ In trusted store? (optional)
    │
    ├─ 4. Read manifest
    │   ├─ Found: ✓
    │   └─ uiAccess="true"? ✓
    │
    └─ 5. Grant uiAccess
       └─ Application loaded with uiAccess privileges ✅
```

---

## Security Layers

```
┌────────────────────────────────────────┐
│      Your Application Code             │
│      (what you write)                  │
└──────────────┬───────────────────────┘
               │
        ┌──────▼──────┐
        │  app.manifest
        │  uiAccess
        │  declaration
        └──────┬──────┘
               │
        ┌──────▼────────────┐
        │  Built Executable │
        │  With Manifest    │
        └──────┬────────────┘
               │
        ┌──────▼──────────────┐
        │  Digital Signature  │
        │  (unforgeable)      │
        └──────┬──────────────┘
               │
        ┌──────▼────────────────────────┐
        │  Certificate Chain             │
        │  (proves you signed it)        │
        └──────┬───────────────────────┘
               │
        ┌──────▼────────────────────────┐
        │  Windows Verification         │
        │  • Signature valid?           │
        │  • Certificate trusted?       │
        │  • Manifest declares intent?  │
        └──────┬───────────────────────┘
               │
        ┌──────▼────────────────────────┐
        │  ✅ uiAccess Privileges       │
        │  Application can:             │
        │  • Access elevated UI         │
        │  • Send global input          │
        │  • Bypass UIPI                │
        └───────────────────────────────┘
```

---

## Command Reference

```
╔════════════════════════════════════════════════════════════╗
║ ONE-TIME SETUP                                             ║
╠════════════════════════════════════════════════════════════╣
║ .\Create-CodeSigningCert.ps1                               ║
║   → Creates: HintOverlay_CodeSign.pfx                      ║
║   → Saves password (you'll need it later)                  ║
╚════════════════════════════════════════════════════════════╝

╔════════════════════════════════════════════════════════════╗
║ AFTER EACH BUILD                                           ║
╠════════════════════════════════════════════════════════════╣
║ .\Sign-HintOverlay.ps1 -BuildConfiguration Release        ║
║   → Signs: bin\Release\net8.0-windows\HintOverlay.exe      ║
║   → Verifies: Signature automatically checked             ║
╚════════════════════════════════════════════════════════════╝

╔════════════════════════════════════════════════════════════╗
║ VERIFY SIGNATURE                                           ║
╠════════════════════════════════════════════════════════════╣
║ Get-AuthenticodeSignature bin\Release\net8.0-windows\    ║
║   HintOverlay.exe                                          ║
║   → Shows: Status, Certificate, Thumbprint, etc.         ║
╚════════════════════════════════════════════════════════════╝
```

---

## Checklist

```
Setup Phase
  ☐ Run .\Create-CodeSigningCert.ps1
  ☐ Save the password somewhere
  ☐ Verify PFX file exists: C:\Users\knausj\HintOverlay_CodeSign.pfx

Build & Sign Phase (after first build, then repeat after each build)
  ☐ Build in Visual Studio (Ctrl+Shift+B) [Release mode]
  ☐ Run .\Sign-HintOverlay.ps1 -BuildConfiguration Release
  ☐ Enter password from setup phase
  ☐ Verify ✓ Signature successful message
  ☐ Verify ✓ Signed executable location

Testing Phase
  ☐ Run Get-AuthenticodeSignature on signed .exe
  ☐ Check Status = "Valid"
  ☐ Test executable works
  ☐ Test uiAccess features work (if applicable)

Deployment Phase
  ☐ Sign executable [done above]
  ☐ Share bin\Release\net8.0-windows\HintOverlay.exe
  ☐ Application can now use uiAccess
```

---

## Time Investment

```
Activity                        First Time    Each Rebuild
─────────────────────────────   ──────────    ────────────
Create certificate              5 minutes     (one-time)
Build application               5 minutes     5 minutes
Sign executable                 1 minute      1 minute
Verify signature                1 minute      (as needed)
─────────────────────────────   ──────────    ────────────
TOTAL                           12 minutes    6 minutes
```

---

## Your Next 3 Steps

```
NOW (5 min)
┌──────────────────────────────────────┐
│ .\Create-CodeSigningCert.ps1          │
│ → Creates your certificate           │
│ → Saves password (keep it safe!)     │
└──────────────────────────────────────┘
          ↓
AFTER FIRST BUILD (1 min)
┌──────────────────────────────────────┐
│ .\Sign-HintOverlay.ps1                │
│ -BuildConfiguration Release           │
│ → Signs your executable              │
│ → Ready to use                        │
└──────────────────────────────────────┘
          ↓
EVERY FUTURE BUILD (1 min)
┌──────────────────────────────────────┐
│ Build → Sign → Deploy                 │
│ (repeat this cycle)                   │
└──────────────────────────────────────┘
```

---

## Success Indicators

```
✅ Certificate Created
   • File exists: C:\Users\knausj\HintOverlay_CodeSign.pfx
   • Can be imported in Windows

✅ Executable Built
   • File exists: bin\Release\net8.0-windows\HintOverlay.exe
   • Contains embedded manifest

✅ Executable Signed
   • Get-AuthenticodeSignature shows Status = "Valid"
   • Shows your certificate details

✅ Ready to Deploy
   • Can share signed .exe with others
   • Executable has uiAccess enabled
   • Windows trusts the signature
```

---

That's it! You now have:
- ✅ Manifest with uiAccess
- ✅ Scripts to create certificates
- ✅ Scripts to sign executables
- ✅ Complete documentation

**Next step**: Run `.\Create-CodeSigningCert.ps1` 🚀

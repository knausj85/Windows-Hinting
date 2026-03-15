# .NET 8 Upgrade Plan

**Generated**: 2024
**Target Framework**: .NET 8.0
**Projects**: 2
**Strategy**: All-At-Once

---

## Overview

This plan upgrades the HintOverlay solution to .NET 8.0 using an **All-At-Once** strategy. Both the main WinForms application and the WiX installer project will be updated simultaneously.

### Assessment Summary
- **HintOverlay.csproj**: Already on net8.0-windows ✅
- **HintOverlay.Installer.wixproj**: WiX installer (requires modernization)
- **Total NuGet Packages**: 2 (all compatible)
- **Code Files**: 38
- **Complexity**: Low
- **Estimated Effort**: Minimal

---

## Selected Strategy

### All-At-Once
**Rationale**: 2 projects, both modern format, clear dependency structure, straightforward upgrade with no breaking API changes detected.

**Key execution principle**: Both projects updated simultaneously in a single atomic operation.

---

## Projects to Upgrade

### Tier: All Projects (Atomic)

| Project | Type | Current TFM | Target TFM | Status |
|---------|------|-------------|-----------|--------|
| HintOverlay.csproj | WinForms App | net8.0-windows | net8.0-windows | ✅ Already current |
| HintOverlay.Installer.wixproj | WiX Installer | (not .NET) | net8.0 compatible | ⏳ To upgrade |

---

## Task Breakdown

### Task 1: Update HintOverlay.Installer.wixproj

**Objective**: Modernize the WiX installer project to target .NET 8 and ensure it properly references the upgraded main application.

**Scope**:
- Verify WiX project target framework compatibility
- Update WiX project references/imports if needed
- Ensure installer references the correct .NET 8 output from HintOverlay.csproj
- Validate WiX project builds successfully
- Confirm installer creation completes without errors

**Validation**:
- WiX project loads without errors in Visual Studio
- Solution builds cleanly
- Installer (MSI/Bundle) builds successfully

### Task 2: Verify Solution Integration

**Objective**: Ensure both projects build and package correctly as a unified solution.

**Scope**:
- Build full solution (both projects together)
- Run any existing unit tests
- Verify installer includes correct application binaries
- Test installer functionality (basic smoke test)

**Validation**:
- Solution builds with 0 errors
- All tests pass
- Installer package is created successfully

---

## Execution Constraints

Based on All-At-Once strategy:

1. **Single atomic operation** — Both projects updated together; full solution validated after changes
2. **No tier ordering** — All projects treated as one group
3. **Build-and-fix pass** — Single bounded compilation pass; fix all errors at once, not iteratively
4. **Validation** — Full solution must build with 0 errors before moving to testing

---

## NuGet Packages

All existing packages are compatible with .NET 8.0:

| Package | Current | Target | Projects |
|---------|---------|--------|----------|
| Microsoft.Extensions.Hosting | 10.0.3 | 10.0.3 | HintOverlay.csproj |
| Microsoft.Extensions.Hosting.Abstractions | 10.0.3 | 10.0.3 | HintOverlay.csproj |

No package version updates required.

---

## Success Criteria

- [ ] HintOverlay.csproj confirms .NET 8.0-windows targeting
- [ ] HintOverlay.Installer.wixproj loads without errors
- [ ] Solution builds successfully with 0 errors
- [ ] Installer package is created and validated
- [ ] All tests pass
- [ ] Working branch contains all upgrade changes

---

## Next Steps

1. **Review this plan** — confirm the approach and tasks
2. **Execute tasks** — follow the task-execution workflow
3. **Validate** — build and test each change
4. **Complete** — merge working branch back to main development branch

---

## Appendix: Project Dependency Graph

```
┌─────────────────────────────────────┐
│   HintOverlay.csproj (net8.0)       │
│   WinForms Application              │
│   - Microsoft.Extensions.Hosting    │
│   - Abstractions (10.0.3)           │
└──────────────┬──────────────────────┘
               │
               ▼
┌──────────────────────────────────────┐
│  HintOverlay.Installer.wixproj      │
│  WiX Setup Package                   │
│  (References HintOverlay.csproj)    │
└──────────────────────────────────────┘
```


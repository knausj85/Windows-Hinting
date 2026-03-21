# Scenario Instructions

## Scenario Context
- **Scenario ID**: dotnet-version-upgrade
- **Description**: Upgrade Windows-Hinting and WiX installer project to .NET 8
- **Target Framework**: .NET 8.0 (net8.0 / net8.0-windows)
- **Date Initialized**: 2024

---

## Preferences

### Flow Mode
**Mode**: Automatic

The agent will run end-to-end, only pausing when:
- Blocked (missing information, conflicting decisions)
- Requiring explicit user input (important choices)
- Critical validation fails

### Technical Preferences
- **Target Framework**: .NET 8.0 (LTS)
- **Package Update Strategy**: Compatible versions only — no breaking changes
- **WiX Toolset**: Installed (v4.0+)

### Execution Style
- **Commit Strategy**: After Each Task
- **Validation**: Build after each task completion
- **Testing**: Run full solution validation after all changes

---

## Strategy

**Selected**: All-At-Once

**Rationale**: 
- 2 projects (well under 30 project threshold)
- Main application already on .NET 8.0
- Clear, straightforward upgrade with no breaking API changes
- All dependencies compatible with target framework
- Modern SDK-style project format

### Execution Constraints

1. **Single atomic upgrade** — Both Windows-Hinting.csproj and Windows-Hinting.Installer.wixproj updated together
2. **No tier ordering** — All projects treated as one group; no phased rollout needed
3. **Build-and-fix pass** — Single comprehensive compilation pass; fix all errors at once
4. **Full solution validation** — Entire solution must build with 0 errors before testing
5. **Installer verification** — Confirm WiX project builds MSI/Bundle successfully after upgrade

---

## Source Control

- **Repository**: C:\Users\knausj\git\Windows-Hinting (Git)
- **Source Branch**: expiremental-refactor
- **Working Branch**: upgrade-to-NET8
- **Pending Changes**: Committed before workflow start

---

## Key Decisions Log

| Date | Decision | Context |
|------|----------|---------|
| 2024 | Install WiX Toolset 4.0 | Required for WiX project analysis and modernization |
| 2024 | Select All-At-Once strategy | Straightforward upgrade, small project count, clear dependencies |
| 2024 | Include WiX installer in upgrade | User preference to modernize both projects together |

---

## Task Execution Notes

- **Total Tasks**: 2
- **Task 1**: Update Windows-Hinting.Installer.wixproj for .NET 8 compatibility
- **Task 2**: Verify solution integration and build validation

---

## Known Issues & Notes

- WiX Toolset must remain installed throughout upgrade
- WiX project references Windows-Hinting.csproj; ensure updatedpaths/references after any file reorganization
- Installer package creation depends on successful Windows-Hinting.csproj build
- No security vulnerabilities detected in dependencies

---

## Related Artifacts

- `assessment.md` — Full project analysis and compatibility assessment
- `plan.md` — Detailed upgrade plan with task breakdown
- `tasks.md` — Real-time task status and execution tracker
- `execution-log.md` — Chronological log of all completed work


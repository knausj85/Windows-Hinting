# Research: Distribution and update tooling landscape (2026)

Resolves [#30](https://github.com/knausj85/Windows-Hinting/issues/30).
Researched 2026-08-06 against primary sources (project docs/repos, Microsoft Learn); URLs inline.

## 1. Current state: what Windows-Hinting ships today

Sources: `Windows-Hinting.Installer/` (WiX), `Windows-Hinting/Services/UpdateService.cs`,
`Windows-Hinting/Services/PortableUpdateInstaller.cs`, `Windows-Hinting/Services/DeploymentMode.cs`,
`.github/workflows/build.yml`, `Windows-Hinting/app.manifest`.

### Packaging
- **WiX v6.0.2** (SDK-style `WixToolset.Sdk/6.0.2` wixproj), per-machine x64 MSI (`Scope="perMachine"` in
  `Package.wxs`) installing to Program Files, with `MajorUpgrade` (`AllowSameVersionUpgrades`,
  `Schedule="afterInstallInitialize"`), HKLM registry keys, Start-menu/desktop shortcuts, and a WixUI license dialog.
- **Portable builds**: self-contained single-file `dotnet publish` for win-x64 and win-x86
  (`PublishSingleFile`, `PublishReadyToRun`, compression), plus an unsigned Debug zip.
- **Load-bearing constraint — UIAccess**: the installed build compiles with `app.manifest`
  `uiAccess="true"` so hints can render above the Start menu etc. Windows only honors `uiAccess="true"` for
  binaries that are (a) Authenticode-signed and (b) installed under a secure path (Program Files / System32)
  ([Microsoft Learn: UIAccess](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/user-account-control/how-it-works)).
  This is why the MSI is per-machine and why `SignOutput=true` exists in the wixproj. Portable builds
  deliberately use `app.debug.manifest` (`uiAccess="false"`). **Any replacement stack that installs per-user to
  `%LocalAppData%` loses UIAccess for the installed mode.**

### Updates
- **NetSparkle** (`NetSparkleUpdater`) with `Ed25519Checker(SecurityMode.Strict)`; the public key is an
  embedded resource. Appcasts are GitHub Release assets: stable via `releases/latest/download/appcast-*.xml`,
  beta via a rolling `beta` tag; three appcasts (installer, portable win-x64, portable win-x86) selected by
  `DeploymentMode` (`Installer` / `Portable` / `Unknown`, with a compile-time `PORTABLE` define plus a
  Program Files runtime heuristic).
- **Not silent**: `UserInteractionMode.NotSilent` — the user sees the Sparkle prompt; MSI path launches
  `msiexec /qb /norestart` which fires UAC (per-machine); portable path uses the custom
  `PortableUpdateInstaller` (writability preflight, staged exe, generated `update.cmd` that waits on the PID,
  `move /Y`, relaunches, with ~20 retries for AV file locks).
- No delta updates; every update downloads the full MSI or full ~single-file exe.

### Signing and CI
- **SSL.com eSigner** cloud signing (`sslcom/esigner-codesign` action) signs `Windows-Hinting.exe`,
  `Windows-Hinting.dll`, the MSI, and both portable exes on main pushes / opt-in dispatch. A paid signing
  credential already exists and works headlessly in CI.
- Single `build.yml`: lint → Debug build → Release exe → sign → MSI (with
  `/p:BuildProjectReferences=false` to avoid clobbering signed binaries) → sign MSI → portable x64/x86
  publishes → sign → release job generates Ed25519-signed appcasts
  (`NetSparkleUpdater.Tools.AppCastGenerator` 2.9.*) and publishes via `ncipollo/release-action`.
- Not published to winget today.

### Pain points in the current stack
1. Three hand-rolled update paths (MSI, portable x64, portable x86) with three appcasts and a bespoke
   cmd-script self-replacer to maintain.
2. Updates are interactive and, for the MSI, UAC-prompting — no silent background update.
3. ~845-line workflow with fragile ordering invariants (sign-before-MSI, `BuildProjectReferences=false`).
4. No winget presence.

## 2. Options

### Option A — Velopack

- **Health**: active, MIT, ~2.3k stars, Rust core with first-class C#/.NET SDK; stable 1.x line (1.2.0 current
  on [NuGet](https://www.nuget.org/packages/velopack)); direct successor of Squirrel.Windows via
  Clowd.Squirrel (archived 2024-07-05, [releases](https://github.com/clowd/Clowd.Squirrel/releases)).
  Repo: <https://github.com/velopack/velopack>. Supports all .NET >= 5, so .NET 10 is fine.
- **Updates**: delta updates built in (`vpk pack` emits full + delta; fallback after >10 deltas —
  [docs](https://docs.velopack.io/packaging/deltas)); silent background updates are a headline feature —
  check/download/apply are separate API calls, apply-on-restart, no UAC for per-user installs
  ([integrating overview](https://docs.velopack.io/integrating/overview)).
- **Portable mode**: every `vpk pack` also emits `{PackId}-Portable.zip` that is **self-updating via the same
  UpdateManager code path** ([packaging overview](https://docs.velopack.io/packaging/overview)) — would
  delete `PortableUpdateInstaller` entirely.
- **Install model**: default Setup.exe is per-user into `%LocalAppData%` (no elevation —
  [installer docs](https://docs.velopack.io/packaging/installer)). **This breaks UIAccess.** Velopack can also
  emit a real MSI via WiX 5 (`--msi`, `--instLocation PerMachine`, Program Files, ARP) — same docs — but
  machine-wide as the primary flow has long-open issues
  ([#32](https://github.com/velopack/velopack/issues/32), [#30](https://github.com/velopack/velopack/issues/30)),
  and a per-machine install reintroduces UAC on update. UIAccess vs. silent-per-user-update is a genuine
  either/or that Velopack does not dissolve.
- **Signing**: integrated into `vpk pack` (its Update.exe/Setup.exe stubs must be signed mid-pack):
  `--signParams` (signtool), native Azure Trusted/Artifact Signing (`--azureTrustedSignFile`), or arbitrary
  `--signTemplate` ([signing docs](https://docs.velopack.io/packaging/signing)). The existing SSL.com eSigner
  flow would have to move inside the pack step via `--signTemplate`.
- **CI**: documented GitHub Actions flow with built-in GitHub Releases upload/download providers
  ([github-actions docs](https://docs.velopack.io/distributing/github-actions)).
- **winget**: no official Velopack↔winget guidance found (unverified/roll-your-own; its exe or `--msi`
  output is mechanically listable).
- **Migration from existing MSI installs**: documented auto-migrations cover Squirrel/Clowd.Squirrel/
  ClickOnce only ([README](https://github.com/velopack/velopack)); nothing found for detecting/removing a
  WiX MSI install. Plan a one-time step (e.g. first Velopack release runs `msiexec /x {UpgradeCode-product}`
  in an install hook, or a final MSI release that chains the Velopack setup).

### Option B — MSIX + App Installer (+ winget)

- **Auto-update**: `.appinstaller` gives genuine hands-off background updates (`OnLaunch`,
  `HoursBetweenUpdateChecks`, background update, `ShowPrompt`) —
  [App Installer file overview](https://learn.microsoft.com/en-us/windows/msix/app-installer/app-installer-file-overview),
  [update settings](https://learn.microsoft.com/en-us/windows/msix/app-installer/update-settings).
  But the `ms-appinstaller:` web protocol has been disabled by default since Dec 2023 (same doc), so no
  one-click web install.
- **Signing is mandatory, no exceptions**: "Windows requires MSIX packages to be signed with a valid code
  signing certificate" chaining to a trusted root
  ([Sign an MSIX package](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview)).
- **Tray-app fit is poor**: run-at-startup must use the manifest `StartupTask` (user-controllable, sometimes
  disabled until first launch — [StartupTask](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.startuptask?view=winrt-26100));
  file/registry virtualization applies; and an MSIX-packaged app is not a Program Files install, so the
  UIAccess story is at best unclear. No portable mode by definition.
- Tooling itself is current (docs updated Apr 2026; new WinApp CLI `winapp sign`; single-project MSIX via
  `dotnet publish` — [single-project MSIX](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix)).

### Option C — Stay on NetSparkle + WiX (status quo, incrementally improved)

- **NetSparkle is healthy**: `NetSparkleUpdater.SparkleUpdater` 3.1.0 (2026-05-05) adds .NET 10
  compatibility; steady release cadence through 2025
  ([NuGet](https://www.nuget.org/packages/NetSparkleUpdater.SparkleUpdater),
  [releases](https://github.com/NetSparkleUpdater/NetSparkle/releases)). Supports fully silent modes via
  `UserInteractionMode`, so today's `NotSilent` is a choice, not a ceiling. No delta updates; no installer of
  its own — the MSI + `PortableUpdateInstaller` plumbing stays ours.
- **WiX is healthy but fee-encumbered for revenue orgs**: v6.0.0 (2025-04-07) → v6.0.2 (current here) →
  **v7.0.0 (2026-04-06, Heat removed)** ([FireGiant release notes](https://docs.firegiant.com/wix/whatsnew/releasenotes/)).
  The Open Source Maintenance Fee (from v6) applies only to orgs with > $10k/yr revenue
  ([OSMF docs](https://docs.firegiant.com/wix/osmf/), [issue #8974](https://github.com/wixtoolset/issues/issues/8974));
  a $0-revenue OSS project is exempt, but v7 adds EULA-acceptance enforcement
  ([issue #9196](https://github.com/wixtoolset/issues/issues/9196)) — worth re-reading before upgrading past v6.
- This is the only option with **zero migration burden** and the only one that keeps UIAccess exactly as-is.

### Option D — Other contenders (brief)

- **Inno Setup**: alive (6.7.2 May 2026; new 7.0.x line July 2026 — [jrsoftware.org](https://jrsoftware.org/));
  free for OSS (commercial-use license asked from 6.5.0). No update framework — it would replace WiX, not
  NetSparkle. Lateral move; only compelling if leaving WiX over OSMF.
- **Squirrel.Windows**: unmaintained, do not adopt ([repo](https://github.com/Squirrel/Squirrel.Windows),
  acknowledged in [electron/electron#17722](https://github.com/electron/electron/issues/17722)).
  **Clowd.Squirrel**: archived 2024-07-05, superseded by Velopack.
- **`dotnet tool`**: wrong model for a consumer tray app (requires SDK, no shortcuts/startup).

### winget publishing (orthogonal to all options)

- winget accepts `msi, wix, exe, inno, nullsoft, burn, zip, portable, msix, ...`
  ([installer schema 1.12](https://github.com/microsoft/winget-pkgs/blob/master/doc/manifest/schema/1.12.0/installer.md))
  — today's MSI and even the portable exe are listable now; MSIX is not required.
- Submission: YAML manifest PR to [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs);
  automated validation requires **silent** install/uninstall
  ([Submit your manifest](https://learn.microsoft.com/en-us/windows/package-manager/package/repository)) —
  the MSI qualifies (`/qn`).
- CI automation: `wingetcreate update -u <url> -v <ver> --submit --token <PAT>` in a release-triggered job
  ([microsoft/winget-create](https://github.com/microsoft/winget-create)); community
  [WinGet Releaser action](https://github.com/marketplace/actions/winget-releaser) also exists.
- **winget does NOT auto-update apps** — users must run `winget upgrade`
  ([upgrade docs](https://learn.microsoft.com/en-us/windows/package-manager/winget/upgrade)). Microsoft's
  Windows Update orchestration platform (announced May 2025, would let Win32 apps opt in to
  Windows-Update-driven updates —
  [Windows IT Pro blog](https://techcommunity.microsoft.com/blog/windows-itpro-blog/introducing-a-unified-future-for-app-updates-on-windows/4416354))
  was still private preview as of the latest verifiable reporting (Insider builds, Nov 2025); no GA date found.
  In-app updating remains necessary regardless.

## 3. Code signing for indie OSS in 2026

The project already pays for SSL.com eSigner, so this section is about alternatives/cost reduction:

- **Azure Trusted Signing → renamed "Azure Artifact Signing", GA ~2026-01-12**
  ([announcement](https://techcommunity.microsoft.com/blog/microsoft-security-blog/simplifying-code-signing-for-windows-apps-artifact-signing-ga/4482789),
  [product page](https://azure.microsoft.com/en-us/products/artifact-signing)). Timeline: individual signups
  opened Nov 2024, were **paused April 2025** (restricted to 3-year-history US/CA orgs —
  [update post](https://techcommunity.microsoft.com/blog/microsoft-security-blog/trusted-signing-public-preview-update/4399713)),
  and at GA the **individual-developer flow is documented again for US/Canada residents** (Verified ID +
  AU10TIX government-ID validation — [quickstart](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart));
  orgs in US/CA/EU/UK/AU/NZ/JP/KR/SG/CH/NO/IL with 3+ years verifiable history. Requires a paid Azure
  subscription ([FAQ](https://learn.microsoft.com/en-us/azure/artifact-signing/faq)).
  **Pricing: Basic $9.99/mo** (5,000 sigs/mo) ([pricing](https://azure.microsoft.com/en-us/pricing/details/artifact-signing/)).
  Certs are short-lived (issued daily, valid ~3 days per
  [Learn](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview)); SmartScreen
  reputation is identity-based and persists across cert rotations, though new apps still accrue reputation
  over weeks ([SmartScreen doc](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)).
  GitHub Actions: [Azure/artifact-signing-action](https://github.com/Azure/artifact-signing-action)
  (formerly trusted-signing-action); Velopack supports it natively.
- **SignPath Foundation**: free OSS signing, but with conditions — OSI license, prior releases, published
  signing policy, signing runs through their SaaS, and the cert subject is "SignPath Foundation" rather than
  the project ([terms](https://signpath.org/terms.html), [OSS page](https://signpath.io/solutions/open-source-community)).
- **Certum Open Source**: ~$50–130/yr via resellers, SimplySign cloud avoids a USB token
  ([Certum store](https://certum.store/open-source-code-signing-on-simplysign.html)).
- **Traditional OV/EV**: $300–500/yr (Microsoft's own estimate in the
  [MSIX signing doc](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview)) and,
  since 2023-06-01, CA/B Forum rules force keys onto hardware/HSM — awkward in CI without a cloud service.
- **Unsigned**: SmartScreen interstitials per release file until per-hash reputation accrues; MSIX
  impossible; and UIAccess would stop working. Not an option for this app.

## 4. Comparison table

| Criterion | NetSparkle + WiX (today) | Velopack | MSIX + App Installer | Inno Setup (+ NetSparkle) |
|---|---|---|---|---|
| Silent background updates | Possible (NetSparkle silent modes) but MSI path still UAC-prompts per-machine | Yes, core feature; no UAC (per-user) | Yes (best-in-class via .appinstaller) | Same as today (NetSparkle drives it) |
| Delta updates | No | Yes | Effectively (streaming block map) | No |
| Portable mode | Yes — custom `PortableUpdateInstaller` (ours to maintain) | Yes — auto-generated self-updating Portable.zip | No | No (installer only) |
| UIAccess (Program Files + signed) | Yes — designed for it | Only via `--msi --instLocation PerMachine` side-path; default per-user install breaks it | Unclear/at risk (WindowsApps, container) | Yes (per-machine install) |
| Code signing required | For UIAccess + SmartScreen (already have SSL.com) | Same; integrated in `vpk pack` (Artifact Signing native) | **Mandatory** — unsigned won't install | Same as today |
| winget listing | Yes (MSI listable today) | Yes mechanically; no official guidance | Yes (native) | Yes (`inno` type) |
| CI (GitHub Actions) | Works today; ~845-line bespoke workflow | Documented flow; would shrink workflow substantially | `dotnet publish` + signing; appinstaller hosting needed | Simple; community actions |
| Migration burden for existing installs | None | Must uninstall old MSI ourselves (no WiX-MSI auto-migration found); portable users re-download once | Full repackage; MSI uninstall; startup/registry semantics change | Installer swap; MSI uninstall handling |
| Maintenance health (2026) | NetSparkle 3.1.0 May 2026 (.NET 10); WiX v6/v7 active (OSMF caveat, exempt at $0 revenue) | Active, stable 1.x, MIT | Microsoft-maintained, docs current | Active (7.0.x July 2026) |
| Update paths we maintain | 3 (MSI, portable x64, portable x86) + cmd self-replacer | 1 (Velopack channels per RID) | 1 (but no portable) | 3 (unchanged) |

## 5. Facts for the decision

1. **The UIAccess requirement is the fulcrum.** `uiAccess="true"` needs a signed exe in Program Files.
   Velopack's flagship experience (silent, UAC-free, delta updates) is per-user in `%LocalAppData%` — adopting
   it as-is means the installed mode loses hints-above-Start-menu, unless we use its WiX-5 `--msi`
   per-machine side-path, which reintroduces UAC-on-update and is the least-paved part of Velopack.
2. **Velopack would delete the most code**: `PortableUpdateInstaller`, three appcasts, the appcast-generator
   step, and most of the signing/ordering choreography in `build.yml` collapse into `vpk pack` + upload. Its
   portable zip is self-updating with the same code path as installed mode.
3. **No option gives silent background updates for a per-machine Program Files install without UAC** —
   that's a Windows Installer/ACL reality, not a tooling gap. Getting there requires either a privileged
   update service (out of scope for an indie tray app) or accepting per-user install (and losing UIAccess),
   or MSIX (poor tray-app fit).
4. **Staying put is defensible**: NetSparkle (3.1.0, .NET 10) and WiX (v6/v7) are both actively maintained in
   2026; the OSMF does not apply at $0 revenue. Cheap incremental wins without migration: enable NetSparkle's
   silent download-and-prompt-on-quit mode, and publish the existing MSI to winget via `wingetcreate` in the
   release job.
5. **Signing costs can drop** from SSL.com eSigner to Azure Artifact Signing Basic ($9.99/mo, GA Jan 2026,
   individual US/Canada onboarding documented again) or to $0 via SignPath Foundation (with their policy
   strings attached). Both work in GitHub Actions; Velopack integrates Artifact Signing natively.
6. **winget is discoverability, not updating** (no auto-update; Windows Update orchestration for Win32 still
   private preview per Nov 2025 reporting). It can be added to the current stack today with one CI step.
7. **Migration burden ranking** (lowest first): stay (none) < Velopack (one-time MSI uninstall step we write
   ourselves + portable re-download) < Inno swap (installer rework, nothing gained on updates) < MSIX (full
   repackage, startup/UIAccess/portable regressions).

### Unverified items (flagged during research)

- Velopack: exact 1.x release dates (2025-vs-2026 retrieval ambiguity); official winget guidance (none
  found); WiX-MSI install detection/takeover (none found).
- Artifact Signing cert validity: Learn says ~3 days; GA press said 24h.
- Windows Update orchestration platform status beyond Nov 2025 Insider-build reporting.

# Research: Review of `build/build-ci-tooling` (PR #116)

Reviews [PR #116](https://github.com/knausj85/Windows-Hinting/pull/116) "Build, CI, installer and pre-commit
hardening" at tip `41bc383` (25 commits on top of `origin/main` `7b14849`).
Researched 2026-10-16 against primary sources (GitHub Docs, Microsoft Learn, WiX/FireGiant docs, action
repositories and their source). Line references are to files on `build/build-ci-tooling`.

Also checked: the repo's live GitHub settings (environments, branch protection, secrets) through `gh api`, the
CI runs on the PR, a local build, and a local `pre-commit run --all-files`.

## Summary verdict

**Merge it after two fixes, which are mostly settings.** The PR is a clear improvement. Every action is pinned
to a SHA that matches its tag. Signing secrets moved from repo level into GitHub Environments. Jobs that hold
secrets no longer build code. The MSI harvests its files instead of listing them by hand. The pipeline is
easier to follow.

Two problems need fixing before relying on it:

1. **The `signing-branch` and `release-branch` environments can't be used.** They only allow protected
   branches to deploy, and `main` is the only protected branch. So any signed or beta run started by hand on
   another branch fails.
2. **A release can publish unsigned binaries,** including on the stable channel from `main`. The "fails on an
   unsigned MSI" check only runs when signing is turned on.

The rest are hardening and maintenance items. Some parts are more machinery than a one-maintainer repo needs
(the run gate, four environments), but they work and are well commented. The sibling branch
`build/process-hardening` (`7a9a242`, closed PR #115) is **superseded**. Its build and CI commits are in
this branch in a newer form. Its code commits (DPI, warnings, Start-with-Windows) moved to the stacked
PR #117 (`fix/dpi-startup-warnings`). It can be deleted.

## Prioritized recommendations

### Must-fix

**M1. The `*-branch` environments reject every branch they exist for.**
`build.yml:315`, `:466` and `:510` send non-`main` runs to `signing-branch` and `release-branch`. Live
settings (`gh api repos/.../environments`) show both use the deployment-branch policy *Protected branches
only*. The only protected branch is `main` (`gh api .../branches?protected=true`).

GitHub defines that policy as "Only branches with branch protection rules enabled can deploy to the
environment" ([Deployments and environments](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments)).
A `workflow_dispatch` on a feature branch therefore can't reach these environments. `sign_binaries` defaults
to `true` (`build.yml:10-13`), so a default manual run on a branch fails `sign-binaries`, and `build-msi`
(`:392`) and `release` are then skipped.

`.github/copilot-instructions.md:36-40` documents the intended behavior: other branches deploy with required
approval. **Fix (settings only):** change both environments' deployment branches to *No restriction*, or to a
custom pattern `*`. Keep the required reviewer, since that is the real gate: "a job cannot access environment
secrets until one of the required reviewers approves it" (same page).

**M2. Nothing stops an unsigned build from being released.**
`metadata` sets `sign` to true only for pushes to `main` or for manual runs with `sign_binaries=true`
(`build.yml:155`). The `release` job's `if:` (`build.yml:521-524`) doesn't check it. So a manual run with
`publish_release=true` and `sign_binaries=false` publishes unsigned assets.

On `main` that run uses the stable `v<ver>` tag, and with `prerelease=false` it is marked Latest
(`:715-716`). The release notes still say "signed" (`:724-727`). The Ed25519-signed appcast then serves the
release to every installed client.

An unsigned exe also loses UIAccess, which is why the MSI is per-machine and signed
([Microsoft Learn: How UAC works](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/user-account-control/how-it-works);
see also `docs/research/distribution.md` on `research/distribution`). **Fix:** add
`needs.metadata.outputs.sign == 'true'` to the `release` condition, at least for the stable channel. Or add a
`Get-AuthenticodeSignature` check in `release` before publishing. That job runs on Ubuntu, so the check would
need a Windows step or a separate job.

### Should

**S1. The release job holds the update-signing key while it installs a floating NuGet tool.**
`build.yml:579-580` runs `dotnet tool install --global NetSparkleUpdater.Tools.AppCastGenerator --version
2.9.*`. Later steps in the same job get the NetSparkle Ed25519 private key (`:584-585`, `:619-620`), and
`:630-638` writes it to disk.

This contradicts the PR's own rule ("never reference these secrets from a job that runs a build",
`copilot-instructions.md:36-40`, "the release job builds nothing"). That key is the one secret that can push
code to every installed client.

**Fix:** pin an exact version (2.9.0 is the latest stable on [nuget.org's flat container](https://api.nuget.org/v3-flatcontainer/netsparkleupdater.tools.appcastgenerator/index.json))
in a `dotnet-tools.json` manifest. Consider generating the appcast in a keyless job and signing it in a small
job that has the key. This is pre-existing (`origin/main` had the same line), but the PR's stated invariant
should cover it.

**S2. Pin `sslcom/esigner-codesign` to its release commit, and note what pinning doesn't cover.**
`build.yml:339` and `:477` pin `b7f8ff36…` "develop as of 2026-10-16". The GitHub compare API shows that
commit's tree is **identical** to `v1.3.2` (`cf5f6c1d38ad10f47e3ed9aca873f429b1a8d85b`), so pin the tag
commit with a `# v1.3.2` comment. Dependabot can then track releases.

GitHub recommends full-SHA pins ([Secure use reference](https://docs.github.com/en/actions/reference/security/secure-use)).
That page also notes Dependabot "will not create alerts for actions pinned to SHA values".

Pinning the action does not pin what it downloads. At runtime it fetches the CodeSignTool zip by release tag
with `tc.downloadTool(link)` and no hash check, and it installs a Corretto JDK from a "latest" index
([`src/setup-codesigner/codesigner.ts`](https://github.com/SSLcom/esigner-codesign/blob/cf5f6c1d38ad10f47e3ed9aca873f429b1a8d85b/src/setup-codesigner/codesigner.ts),
[`src/constants.ts`](https://github.com/SSLcom/esigner-codesign/blob/cf5f6c1d38ad10f47e3ed9aca873f429b1a8d85b/src/constants.ts)).
The action honors a preset `CODESIGNTOOL_PATH`, so a hash-checked pre-download is possible if this residual
risk matters. The action's `action.yml` declares `using: node20`.

**S3. Update the actions that still run on Node 20.**
The last PR run warns "Node.js 20 is deprecated … forced to run on Node.js 24" for `actions/cache@v4`
(`build.yml:189`, `:239`, `:408`) and `actions/setup-python@v5` (`:111`). GitHub removed Node 20 from runners
on 2026-09-23 ([GitHub changelog](https://github.blog/changelog/2025-09-19-deprecation-of-node-20-on-github-actions-runners/)).

Current majors (from `gh api …/releases/latest`): checkout v7.0.1, setup-python v7.0.0, setup-dotnet v6.0.0
(its README says it "upgraded action from node20 to node24"; [setup-dotnet](https://github.com/actions/setup-dotnet)),
cache v6.1.0.

`pre-commit/action` (`:126`) also caches with `actions/cache@v4` internally. Its README says it is "in
maintenance-only mode" ([pre-commit/action](https://github.com/pre-commit/action)). Replacing it with
`pip install pre-commit==<ver>` plus `pre-commit run --all-files --show-diff-on-failure` and your own cache
step removes one dependency.

**S4. Have Dependabot cover the app's packages and add a cooldown.**
`.github/dependabot.yml:5-20` covers only `github-actions` and `pre-commit`. Add `nuget` for
Microsoft.Extensions.Hosting, CsWin32, NetSparkle and WixToolset.\*
([supported ecosystems](https://docs.github.com/en/code-security/dependabot/ecosystems-supported-by-dependabot/supported-ecosystems-and-repositories)).
Optionally add `dotnet-sdk` for `global.json`
([GitHub changelog](https://github.blog/changelog/2024-11-19-dependabot-can-now-perform-version-updates-for-the-net-sdk/)).

Add `cooldown: default-days: N`, which is supported for github-actions, nuget and pre-commit
([Dependabot options reference](https://docs.github.com/en/code-security/reference/supply-chain-security/dependabot-options-reference)).
It keeps brand-new releases out of the signing pipeline for a few days.

**S5. NuGet restore is not locked, and the cache key and contents don't match.**
- There is no `packages.lock.json`. Microsoft recommends committing the lock file for apps and restoring with
  `--locked-mode` in CI
  ([NuGet: Locking dependencies](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies)).
  The portable publishes restore RID-specific runtime packs, so list
  `<RuntimeIdentifiers>win-x64;win-x86</RuntimeIdentifiers>` so they are in the locked graph
  ([MSBuild props: RuntimeIdentifiers](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props)).
- Three jobs save the cache under the **same** key (`build.yml:192`, `:242`, `:411`) with different package
  sets: Debug, Release plus RID runtime packs, and WiX. Cache entries are immutable ("You cannot change the
  contents of an existing cache";
  [Dependency caching](https://docs.github.com/en/actions/reference/workflows-and-actions/dependency-caching)),
  so whichever job saves first wins. In the last Release Build log the cache hits, but both RID restores still
  take about 7.5 s ("Restored … (in 7.43 sec)") against about 0.5 s for the others. Key the cache on the lock
  files plus the job name, or use setup-dotnet's `cache: true`, which requires lock files
  ([setup-dotnet README](https://github.com/actions/setup-dotnet)).
- Trade-off: Microsoft's note says lock files and a floating SDK can drift. "set `rollForward` to `disable` so
  the SDK version and dependency graph stay in lockstep"
  ([global.json](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json)). Today `global.json:3-5`
  (`10.0.100` + `latestFeature`) means CI installed **10.0.402**, as the setup-dotnet log shows. setup-dotnet
  "installs the appropriate SDK version" for `latest*` roll-forward (README), so the CI SDK changes with each
  monthly release. Either is reasonable. Pick one on purpose and let Dependabot `dotnet-sdk` bump it.

**S6. Pin PSScriptAnalyzer in CI.**
`build.yml:122` runs `Install-Module -Name PSScriptAnalyzer -Force -Scope CurrentUser` with no version (local
is 1.25.0). A new rule release can turn CI red with no repo change. Add `-RequiredVersion`. Also note the
`-Fix` used by `build/Invoke-PSScriptAnalyzer.ps1:30`: "It tries to preserve the file encoding but there are
still some cases where the encoding can change"
([Invoke-ScriptAnalyzer](https://learn.microsoft.com/en-us/powershell/module/psscriptanalyzer/invoke-scriptanalyzer)).
The `fix-byte-order-marker` hook would catch a BOM on the next run.

**S7. Require the checks on `main`.**
Branch protection on `main` has `required_status_checks.contexts: []` (from `gh api`), so CI only advises.
Require `pre-commit` and either the builds or an aggregate "all green" job. Be careful with names: "A job that
is skipped will report its status as 'Success'"
([Status checks](https://docs.github.com/en/pull-requests/collaborating-with-pull-requests/collaborating-on-repositories-with-code-quality-features/about-status-checks)).
The gate makes every push-run job skip under the same names as the PR-run jobs (see `gh pr checks 116`).

### Nice-to-have

- **Smaller `app-release` artifact (97 MB, while the MSI is 1 MB).** `build.yml:270` and `:289` upload all of
  `bin\Release\net10.0-windows\`, including the RID-specific `win-x64\` and `win-x86\` output that the
  portable publish leaves there. `Components.wxs:31-32` already excludes those folders from the MSI. Excluding
  them from the upload (or giving the portable builds their own `OutputPath`) also cuts what is sent to the
  signing job.
- **Artifact attestations.** Add `actions/attest` to the release job (`id-token: write`,
  `attestations: write`), so users can check provenance with `gh attestation verify`
  ([Use artifact attestations](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations)).
  This is cheap, and it complements the Authenticode signature.
- **`persist-credentials: false`** on the checkouts (`build.yml:108`, `:181`, `:231`, `:400`, `:528`). No step
  needs git auth (lite pushes through its app), and the default is `true`
  ([actions/checkout](https://github.com/actions/checkout)).
- **Don't let a push cancel a manual release.** `cancel-in-progress` (`build.yml:36`) is true for every non-main
  ref, so a push to a branch during a manual beta release can cancel it between "Delete existing beta release
  and tag" (`:695`) and "Create GitHub release" (`:708`), which leaves no beta release. `cancel-in-progress`
  accepts an expression
  ([Control workflow concurrency](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency)),
  so add `&& github.event_name != 'workflow_dispatch'`.
- **x64 apphost.** `Directory.Build.targets:25-27` sets `AppHostRuntimeIdentifier`, which isn't on the
  [MSBuild props page](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props). It works: a
  local Release build on this ARM64 box gave an exe with PE machine `0x8664` (x64). The SDK's documented input
  is `PlatformTarget`, which picks `DefaultAppHostRuntimeIdentifier` ("…and '$(PlatformTarget)' == 'x64'">win-x64",
  [RuntimeIdentifierInference.targets](https://github.com/dotnet/sdk/blob/main/src/Tasks/Microsoft.NET.Build.Tasks/targets/Microsoft.NET.RuntimeIdentifierInference.targets)).
  Separately, the condition is Release-only. A local `dotnet build Windows-Hinting.slnx` (Debug) on ARM64
  built an **ARM64** exe (`0xAA64`) into an x64 Debug MSI. That's harmless for CI, but surprising locally.
- **ARM64 target.** .NET 10 supports Arm64 on Windows 11
  ([Install .NET on Windows](https://learn.microsoft.com/en-us/dotnet/core/install/windows)), and the x64 MSI
  runs under emulation (the DotNetCompatibilityCheck targets the x64 runtime, `Package.wxs:23-28`). A
  `Portable-win-arm64.pubxml` costs about one file. `windows-11-arm` runners exist for public repos
  ([GitHub-hosted runners](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)) if you
  want a smoke test. A native ARM64 MSI is a bigger decision (see Open questions).
- **WIX8601 warnings locally.** `Components.wxs:31-32` warn "Missing directory for harvesting files" when no
  portable build has run, which is every local MSI build. This is cosmetic.
- **Satellite assemblies.** The MSI ships 12 `NetSparkleUpdater.UI.WinForms.resources.dll` files (checked in
  the built MSI's File table). `SatelliteResourceLanguages` can trim them if only English is supported
  ([MSBuild props](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props)).
- **Over-engineering check.** The gate job (`build.yml:49-85`) polls PR mergeability for up to 60 s on each
  push. It works around a documented rule: "Workflows will not run on `pull_request` activity if the pull
  request has a merge conflict"
  ([Events that trigger workflows](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows)).
  It's correct and commented, but it's the most fragile part of the workflow. The simpler, conventional shape
  is `push: branches: [main]` + `pull_request` + `workflow_dispatch`, which builds branches without a PR only
  on demand.
- **No test job.** `copilot-instructions.md:64` says verification is manual. A test job has nothing to run
  until a test project exists. A cheap smoke check would be `msiexec /a` (administrative extract) of the MSI on
  the runner, asserting the exe and `runtimeconfig.json` are present.

## What's already good

- **Every SHA pin matches its tag.** Checked with `gh api repos/<action>/commits/<tag>`: checkout v6,
  setup-python v5, setup-dotnet v5, cache v4, upload-artifact v7, download-artifact v8, pre-commit/action
  v3.0.1, lite-action v1.2.0, release-action v1.21.0 and esigner develop. This matches the
  [Secure use reference](https://docs.github.com/en/actions/reference/security/secure-use).
- **Least privilege.** `contents: read` at workflow level (`build.yml:26-27`), with per-job exceptions
  (`pull-requests: read` for the gate, `contents: write` only for release). This follows the
  [Secure use reference](https://docs.github.com/en/actions/reference/security/secure-use) ("set the default
  permission for the `GITHUB_TOKEN` to read access only").
- **Secrets are only in environments.** `gh secret list` at repo level is empty. ES\_\* are in
  `signing`/`signing-branch`, and NetSparkle keys are in `release`/`release-branch`. `signing` and `release`
  allow only `main`. `release` also requires a reviewer.
- **Uses `pull_request`, never `pull_request_target`.** Fork PRs get no secrets and a read-only token
  ([Events that trigger workflows](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows)).
  Signing only runs on `main` pushes or manual runs (`build.yml:155`).
- **Signing jobs don't check out code.** They verify with `Get-AuthenticodeSignature` before and after packaging
  (`:350-363`, `:421-441`, `:488-492`). `-InstallerOnly` builds with `BuildProjectReferences=false`
  (`build-complete.ps1:191-193`), so the signed binaries aren't rebuilt.
- **Correct status functions.** `!cancelled()` and `!failure()` are used as documented: "`failure()` returns
  `true` if any ancestor job fails" ([Expressions](https://docs.github.com/en/actions/reference/workflows-and-actions/expressions)).
  Every job has `timeout-minutes`.
- **ContinuousIntegrationBuild.** `Directory.Build.props:14-17` uses exactly the `GITHUB_ACTIONS` pattern from
  [MSBuild props](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props). Source Link
  ships in the SDK since .NET 8 ([What's new in .NET 8 SDK](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8/sdk)).
  `IncludeSourceRevisionInInformationalVersion=false` (`csproj:28`) handles the side effect that page
  describes.
- **`global.json` blocks prerelease SDKs.** `allowPrerelease: false` keeps the `11.0.100-rc.2` SDK on this box
  from being picked. The default is `true` outside Visual Studio
  ([global.json](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json)).
- **Installer:**
  - **Harvesting is correct.** The MSI built locally has 46 files: the exe, the dll, deps/runtimeconfig, NuGet
    dependencies and satellites. It has no PDBs and no RID folders. The `Files` element is the WiX v5+
    harvesting mechanism ([Files element](https://docs.firegiant.com/wix/schema/wxs/files/)).
  - **The runtime check runs in time.** `DotNetCompatibilityCheck` (`Package.wxs:23-31`) sets the property to
    0 on success ([schema](https://docs.firegiant.com/wix/schema/netfx/dotnetcompatibilitycheck/)). Its custom
    action is scheduled `Before="LaunchConditions"` in both sequences
    ([NetFxExtension_Platform.wxi @ v6.0.2](https://github.com/wixtoolset/wix/blob/v6.0.2/src/ext/NetFx/wixlib/NetFxExtension_Platform.wxi)),
    so the `Launch` condition sees it.
  - **Upgrades roll back cleanly.** `Schedule="afterInstallInitialize"` rolls back the removal if the upgrade
    fails ([MajorUpgrade](https://docs.firegiant.com/wix/schema/wxs/majorupgrade/)).
  - **The ICE61 suppression is justified.** ICE61 flags exactly the same-version removal that
    `AllowSameVersionUpgrades` asks for ([ICE61](https://learn.microsoft.com/en-us/windows/win32/msi/ice61)).
    MajorUpgrade docs warn this also allows same-3-field downgrades. That's acceptable here because the
    version is `0.1.<run_number>`.
- **The netsparkle key is protected.** `.gitattributes:13` marks `netsparkle_pub.txt` binary, and every
  whitespace/EOL hook excludes it.
- **CodeQL default setup already runs** (Analyze actions + csharp checks on the PR). It doesn't need adding.

## Test results (local, ARM64, SDK 10.0.402)

- `pwsh build/build-complete.ps1` (Release exe + MSI): **succeeded**, exit 0. 14 warnings: CsWin32
  PInvoke004, CS8602 ×3, WFO0003 and NETSDK1187, which PR #117 addresses. 2 WIX8601 warnings. The Release
  exe's PE machine is `0x8664` (x64 apphost, as intended).
- `dotnet build Windows-Hinting.slnx` (Debug): **succeeded**, exit 0. It produced the app and
  `Windows-Hinting.Installer\bin\x64\Debug\en-US\Windows-Hinting.msi`. The Debug exe is ARM64 (see
  Nice-to-have).
- `pre-commit run --all-files`: **all 13 hooks passed**, and no files changed.
- CI on the PR (run 38970047420): gate, pre-commit, metadata, Debug, Release and MSI all succeeded. Signing and
  release were skipped as expected. CodeQL passed.

## Open questions

1. Should `signing-branch` and `release-branch` really work from any branch with approval, as
   `copilot-instructions.md` says? The live settings only allow protected branches (M1).
2. `release` (the environment for `main`) requires a reviewer, so every push to `main` waits for approval
   before publishing, and `cancel-in-progress` is off for `main`. Is that queueing intended?
   `copilot-instructions.md` doesn't mention it.
3. The PR body says eSigner auth has been failing since 2026-10-15. The environment secrets were updated on
   2026-10-16 around 21:55Z, after the last CI run. Has a signed `main` run passed since then?
4. The pre-commit.ci lite app isn't installed yet (PR body), so the autofix path hasn't run. Lite requires the
   step's job to be "either by itself in the workflow or … the last job to finish"
   ([pre-commit.ci lite](https://pre-commit.ci/lite.html)). That holds today only because `metadata` (about
   3 s) finishes before `pre-commit` (about 1 min) and the builds are skipped. Confirm it on the first real
   fix.
5. ARM64: is a native ARM64 build (portable, or a separate MSI) wanted, or is x64-under-emulation enough?
6. Do you want a fixed SDK version (`rollForward: disable` + lock files), or the latest 10.0.x on each run
   (currently 10.0.402)?

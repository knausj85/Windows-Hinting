# NetSparkle: automated release notes + implementation review

_Researched 2026-10-16 against `main` @ `7b14849` and upstream NetSparkle @ [`b3df04ab`](https://github.com/NetSparkleUpdater/NetSparkle/commit/b3df04ab2d06230a28e12cfd7c613d25e27d2672) ("Finalize NetSparkle 3.1.0"). That one commit carries the tags `3.1.0`, `3.1.0-UI-WinForms` and `2.9.0-app-cast-generator`, which are the exact library, UI and generator versions this repo uses. All upstream links below are pinned to that commit. `NS` is short for `https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672`._

## TL;DR

- **Today the update dialog's release-notes pane is empty.** The published appcast has no `<description>` and no `<sparkle:releaseNotesLink>` (see the [v0.1.242 appcast](https://github.com/knausj85/Windows-Hinting/releases/download/v0.1.242/appcast-installer.xml)). `ReleaseNotesGrabber.GetReleaseNotes` therefore returns `null` and the template renders only the title and date ([NS/src/NetSparkle/ReleaseNotesGrabber.cs#L178-L210](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/src/NetSparkle/ReleaseNotesGrabber.cs)). The GitHub Release page does have auto-generated notes (`generateReleaseNotes: true`, `.github/workflows/build.yml:755`), but they never reach the appcast.
- **Recommended:** generate the notes once with the GitHub "generate release notes" REST endpoint. Write them to `changelog/<version>.md`. Pass that folder to `netsparkle-generate-appcast -p changelog` **without `-l`**, so the Markdown is embedded in `<description>` and covered by the appcast's Ed25519 signature. Add `--reparse-existing`, seeded with the previous appcast, so users who skipped versions see every intervening version's notes. Use the same file as the GitHub Release body. The app needs no code changes.
- **Top review findings:** (1) the portable update path likely launches the new exe twice; (2) the generator's key environment variables are misnamed, which is why the workflow has to write the private key to disk; (3) the generator is installed with a floating `2.9.*` version while it holds the signing key; (4) the appcast holds a single item, so users who skipped versions only see the latest notes; (5) PR-title HTML is rendered in the legacy IE `WebBrowser` control. The full ranked list is in the Review section.

---

## 1. Where the NetSparkle implementation lives

All of it is on `main` (and is identical on `modernize`; `git diff origin/main origin/modernize` touches only MSBuild invocation lines in `build.yml`, not the NetSparkle parts).

| Piece | Location |
|---|---|
| Packages | `Windows-Hinting/Windows-Hinting.csproj:79-80`: `NetSparkleUpdater.SparkleUpdater` **3.1.0**, `NetSparkleUpdater.UI.WinForms.NetCore` **3.1.0** |
| Embedded Ed25519 public key | `Windows-Hinting/Resources/netsparkle_pub.txt`, `Windows-Hinting.csproj:84` |
| Updater wrapper | `Windows-Hinting/Services/UpdateService.cs` |
| Deployment detection (MSI / portable) | `Windows-Hinting/Services/DeploymentMode.cs` |
| Portable self-replace | `Windows-Hinting/Services/PortableUpdateInstaller.cs` |
| Log adapter | `Windows-Hinting/Logging/NetSparkleLoggerAdapter.cs` |
| Preferences | `Windows-Hinting/Models/HintOverlayOptions.cs:45` (`AutoCheckForUpdates`), `:77` (`NetSparkleEnabled`) |
| Installer payload | `Windows-Hinting.Installer/ExampleComponents.wxs:113-155` (NetSparkle DLLs and satellite resources) |
| CI / appcast | `.github/workflows/build.yml`, `release` job, lines 541-780; generator `NetSparkleUpdater.Tools.AppCastGenerator --version 2.9.*` (`:621`) |
| Related decision | issue #35: "stay on NetSparkle + WiX … enable NetSparkle background download" |

## 2. How NetSparkle 3.1.0 handles release notes (verified in source)

**Two appcast channels for notes** (README, [NS/README.md#L316-L324](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/README.md)):

- `<description>`: Markdown or HTML embedded in the item. It takes precedence over the link.
- `<sparkle:releaseNotesLink sparkle:signature="…">`: a URL to an HTML or Markdown file.

**Rendering** ([NS/src/NetSparkle/ReleaseNotesGrabber.cs](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/src/NetSparkle/ReleaseNotesGrabber.cs)):

- An embedded `Description` is used first (L182). A regex looks for any paired HTML tag (`<x>…</x>`). If one is found, the whole description is treated as HTML. Otherwise it is converted with `MarkdownSharp` (L185-L203).
- A linked file is downloaded with a plain `HttpClient.GetStringAsync` (L266-L281). It is converted from Markdown **only if the URL's extension is `.md/.mkdn/.mkd/.markdown`** (L34, L237-L253); otherwise it is used as raw HTML.
- **The signature on a linked file is checked only if `ChecksReleaseNotesSignature == true`, which defaults to false** (L40, L122, L224-L234). The `SecurityMode` docs say the same ([NS/src/NetSparkle/Enums/SecurityMode.cs#L9-L11](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/src/NetSparkle/Enums/SecurityMode.cs)), and so does the README (L324). Even with the check on, a link that has *no* signature attribute is accepted (the check sits inside `if (!IsNullOrWhiteSpace(item.ReleaseNotesSignature))`, L224).
- An embedded `<description>` is protected by the **appcast** signature. In `Strict` mode the whole appcast is rejected unless its `.signature` verifies ([NS/src/NetSparkle/AppCastHandlers/AppCastHelper.cs#L160-L215](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/src/NetSparkle/AppCastHandlers/AppCastHelper.cs), [NS/src/NetSparkle/Utilities.cs#L168-L185](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/src/NetSparkle/Utilities.cs)).
- `DownloadAllReleaseNotes` renders **one block per update item newer than the installed version**, with the latest highlighted (L143-L168). Items come from `AppCastHelper.FilterUpdates`, which drops items whose version is ≤ the installed one ([AppCastHelper.cs#L230-L276](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/src/NetSparkle/AppCastHandlers/AppCastHelper.cs)). So **a multi-item appcast gives cumulative notes for free**.

**WinForms UI** ([NS/src/NetSparkle.UI.WinForms.NetCore/UpdateAvailableWindow.cs](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/src/NetSparkle.UI.WinForms.NetCore/UpdateAvailableWindow.cs)):

- Notes are shown in a `System.Windows.Forms.WebBrowser`, i.e. the legacy MSHTML/IE engine (`UpdateAvailableWindow.Designer.cs#L41`), with `AllowNavigation = false` (L76-L77). The HTML is written straight into the document (L151-L158). `AllowNavigation = false` means links in the notes most likely do not open when clicked ([WebBrowser.AllowNavigation docs](https://learn.microsoft.com/dotnet/api/system.windows.forms.webbrowser.allownavigation)).
- `UIFactory` can change how notes look and load: `HideReleaseNotes`, `ReleaseNotesHTMLTemplate`, `AdditionalReleaseNotesHeaderHTML`, `ReleaseNotesDateTimeFormat`, and `ReleaseNotesGrabberOverride`, which lets you subclass the grabber or turn on `ChecksReleaseNotesSignature` ([NS/src/NetSparkle.UI.WinForms.NetCore/UIFactory.cs#L44-L115](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/src/NetSparkle.UI.WinForms.NetCore/UIFactory.cs)).
- With `UserInteractionMode.DownloadNoInstall` (the background download chosen in #35), the same `UpdateAvailableWindow`, notes included, is shown after the download ([NS/src/NetSparkle/SparkleUpdater.cs#L1968-L1985](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/src/NetSparkle/SparkleUpdater.cs)). The changelog design therefore holds after that change.

**Generator (`netsparkle-generate-appcast` 2.9.0)** ([NS/src/NetSparkle.Tools.AppCastGenerator/Options.cs](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/src/NetSparkle.Tools.AppCastGenerator/Options.cs), [AppCastMaker.cs#L337-L433](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/src/NetSparkle.Tools.AppCastGenerator/AppCastMaker.cs)):

- `-p/--change-log-path <dir>` (Options L52-L54): looks for `<version>.md`, then `<prefix> <version>.md` / `<prefix><version>.md` (`--change-log-name-prefix`), then `<product-name> <version>.md` / `<product-name><version>.md` (AppCastMaker L362-L390). The version string is the one passed in, here `--file-version 0.1.N`, so the file is `changelog/0.1.N.md`.
- If `-l/--change-log-url` is **not** set, the file's text is embedded as `Description` (L427-L430). If it **is** set, the item gets a `ReleaseNotesLink = <url>/<file>` plus an Ed25519 `ReleaseNotesSignature` of the file (L420-L426). The URL is joined with `Path.Combine`, which is harmless on the Linux runner the workflow uses.
- A missing changelog is a warning, not an error (L399-L402).
- `--reparse-existing` reads the existing appcast **at the output path**, keeps its items (and their descriptions), and adds the new binary's item. It sorts items newest first and re-signs the whole file (L509-L575, Options L94-L98). The README notes that combining `--file-version` with `--reparse-existing` is the supported way to build a multi-item appcast ([NS/README.md#L379](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/README.md)). It also says a latest-only appcast is fine *if* you don't need history (L561-L566).
- Upstream doc bug: the built-in `--show-examples` text uses `-p` twice instead of `-p … -l …` ([Program.cs#L191](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/src/NetSparkle.Tools.AppCastGenerator/Program.cs)). It was fixed after 3.1.0 in `5135bd43`, and the README shows the correct form at L123/L480.

**GitHub side (first-party):**

- `POST /repos/{owner}/{repo}/releases/generate-notes` takes `tag_name`, `target_commitish`, `previous_tag_name` and `configuration_file_path`. It returns `{name, body}` (Markdown) and **does not create a release** ([REST docs](https://docs.github.com/en/rest/releases/releases#generate-release-notes-content-for-a-release)).
- `.github/release.yml` supports `changelog.exclude.labels`, `changelog.exclude.authors`, and `changelog.categories[].{title,labels,exclude}` ([docs](https://docs.github.com/en/repositories/releasing-projects-on-github/automatically-generated-release-notes)). The repo has no `.github/release.yml` today.
- `gh release create` supports `--generate-notes`, `--notes-start-tag`, `--notes-file`, and `--notes` (prepended to generated notes) (`gh release create --help`, gh CLI).
- The current release step uses `ncipollo/release-action` with `generateReleaseNotes: true` plus a static `body` (`build.yml:749-779`). That action also accepts `bodyFile` and `generateReleaseNotesPreviousTag` ([action.yml](https://github.com/ncipollo/release-action/blob/main/action.yml); third-party, not GitHub).

## 3. Recommended design: one generated notes file, embedded in a cumulative signed appcast

### Why this shape

- **Embed, don't link.** An embedded `<description>` is covered by the appcast signature `Strict` mode already enforces, so there is nothing more to sign or verify and no extra HTTP fetch. A linked file is *not* verified unless the app opts in through `ReleaseNotesGrabberOverride` with `ChecksReleaseNotesSignature = true`, and even then a link with no signature passes (§2).
- **Cumulative appcast.** The UI already renders one block per newer item (§2). Keeping the last N items with `--reparse-existing` lets a user on 0.1.230 see notes for every release up to 0.1.243, not just the latest one.
- **One source of truth.** The same Markdown feeds the GitHub Release body and the in-app dialog, so the two never drift.
- **No app code changes are required.** `HideReleaseNotes = false` is already set (`UpdateService.cs:178`).

### Workflow sketch (`release` job, after "Compute release metadata", before "Generate signed appcasts")

```yaml
      - name: Generate release notes (GitHub API)
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        shell: bash
        run: |
          set -euo pipefail
          # Previous *stable* release = current "latest" (the new one isn't created yet).
          prev=$(gh release view --repo "$GITHUB_REPOSITORY" --json tagName -q .tagName 2>/dev/null || true)
          args=(-f tag_name="$RELEASE_TAG" -f target_commitish="$GITHUB_SHA")
          [ -n "$prev" ] && args+=(-f previous_tag_name="$prev")
          gh api -X POST "repos/$GITHUB_REPOSITORY/releases/generate-notes" "${args[@]}" -q .body > notes-github.md

          mkdir -p changelog
          # In-app copy: neutralise raw HTML so MarkdownSharp/MSHTML never renders
          # PR-title markup, and so the "contains HTML" regex never flips the whole
          # description into HTML mode (ReleaseNotesGrabber.cs L185-L193).
          sed -e 's/&/\&amp;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g' notes-github.md > "changelog/${BUILD_VERSION}.md"

          # GitHub release body = static Downloads table + the same notes.
          cat .github/release-body-header.md notes-github.md > release-body.md

      - name: Seed previous appcasts for --reparse-existing
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        shell: bash
        run: |
          set -euo pipefail
          mkdir -p appcast-out
          src_tag=$([ "$IS_BETA" = "true" ] && echo beta || echo "${prev:-}")   # export prev via $GITHUB_ENV in the step above
          [ -z "$src_tag" ] && exit 0
          for n in "$APPCAST_INSTALLER_NAME" "$APPCAST_PORTABLE_X64_NAME" "$APPCAST_PORTABLE_X86_NAME"; do
            gh release download "$src_tag" --repo "$GITHUB_REPOSITORY" -p "$n.xml" -D appcast-out --clobber || true
          done
          # Optional: prune each appcast-out/*.xml to the newest ~15 <item>s (small python/xmlstarlet step).
```

Then, in `gen()` (`build.yml:681-698`), add:

```bash
              --change-log-path changelog \
              --reparse-existing \
              --human-readable \
```

Finally, in "Create GitHub release", replace `body:` + `generateReleaseNotes: true` with `bodyFile: release-body.md` and `generateReleaseNotes: false`. The static Downloads table moves into a checked-in `.github/release-body-header.md`.

Add a **`.github/release.yml`** so the generated notes stay user-facing:

```yaml
changelog:
  exclude:
    labels: [skip-changelog, internal]
    authors: [dependabot, github-actions]
  categories:
    - title: New features
      labels: [enhancement, feature]
    - title: Fixes
      labels: [bug]
    - title: Other changes
      labels: ["*"]
```

### Notes and caveats

- **Order matters for beta.** The rolling `beta` release is deleted before it is recreated (`build.yml:736-746`). Download the previous beta appcast *before* that step, which the sketch does because it runs earlier. For beta, `previous_tag_name = latest stable`, so beta notes cover everything since the last stable release.
- **Scope of the generated notes.** The endpoint lists **merged PRs** between the two tags. Direct pushes to `main` without a PR do not appear. For this repo that means mostly the PR-merge flow. If direct pushes matter, see alternative B below.
- **Explicit `previous_tag_name`.** Passing it removes GitHub's own guess. The v0.1.242 release compared `v0.1.158...v0.1.242` even though `v0.1.159` exists (`gh release view v0.1.242`).
- **Item date.** The date shown in the dialog is the binary's file `CreationTime` on the runner (AppCastMaker L411), which is effectively the build date.
- **Reparsed items keep their old content.** `--reparse-existing` keeps old items verbatim, including their old download URLs under `/releases/download/v<old>/`, which stay valid. It skips a version already present unless `--overwrite-old-items` is passed (Options L94-L107), so re-running a release job for the same version is a no-op for that item.
- **Styling.** Optional, app-side only: `UIFactory.AdditionalReleaseNotesHeaderHTML` can restyle the notes, for example font and link colour (UIFactory.cs L56).

### Alternatives

| Option | How | Pros | Cons |
|---|---|---|---|
| **A. Recommended**: API notes, embedded, reparse | above | Signed by the existing appcast signature; cumulative; zero app code; matches the GitHub Release | Quality depends on PR titles and labels; appcast grows (prune) |
| **A′. Latest-only embed** | same, minus `--reparse-existing` | Simplest change: three lines | A user several versions behind sees only the newest notes (§2). Can be mitigated by generating notes from an older `previous_tag_name`, at the cost of duplicated content |
| **B. Curated changelog files** | Commit `changelog/<version>.md` (or a `CHANGELOG.md` section extracted per version) in PRs; same `-p` flag | Human-quality, user-facing wording; covers non-PR commits | Manual discipline; versions are `0.1.<run_number>`, so a file name cannot be known before CI runs (needs an "Unreleased.md" → renamed in CI scheme) |
| **C. Linked `.md` release asset** | `-p changelog -l https://github.com/<repo>/releases/download/<tag>` and upload `changelog/<ver>.md` as an asset | Small appcast; notes editable after the fact (with re-signing) | **Not verified by default.** Needs `ReleaseNotesGrabberOverride` with `ChecksReleaseNotesSignature = true`, and unsigned links still pass (§2). Extra fetch through the GitHub redirect; the link extension must be `.md` or it is treated as HTML |
| **D. Git-log-derived notes** | `git log --pretty='* %s' prev..HEAD` in CI | No dependence on PRs or labels | Noisy (merge/fixup commits); no categorisation without a convention such as Conventional Commits |
| **E. Custom `ReleaseNotesGrabber`** | Subclass and fetch the GitHub Release body via the API at runtime | No CI change | Unsigned content from a mutable source, API rate limits, code to maintain. Not recommended |

## 4. Review of the existing implementation (ranked by severity)

### High

1. **The portable update probably launches twice, once from `%TEMP%`.** In portable mode the app hooks `CloseApplicationAsync` and `DownloadFinished` (`UpdateService.cs:200-204`) and runs its own `update.cmd` move-and-relaunch (`PortableUpdateInstaller.cs:91-111`). But NetSparkle's `RunDownloadedInstaller` has already written and started *its own* batch file **before** calling `QuitApplication` → `CloseApplicationAsync` ([SparkleUpdater.cs#L1500-L1680](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/src/NetSparkle/SparkleUpdater.cs)). For an `.exe` download, that batch's installer command is the downloaded exe itself (`GetWindowsInstallerCommand`, L1402-L1418). It waits for the host PID to exit, runs the new exe from the temp download path, then relaunches the old path because `RelaunchAfterUpdate = true` (L1539-L1584). The repo does not handle `InstallerProcessAboutToStart` (no reference outside NetSparkle), so both scripts run. Expected symptom: two instances, or one running from `%TEMP%`. **This conclusion comes from reading the source; confirm it with one portable update run with NetSparkle logging on.** Fix: handle `InstallerProcessAboutToStart`, do the portable swap there, return `false`, and exit the app yourself. Returning `false` skips `QuitApplication` (L1652-L1679), so the exit must be explicit. Alternatively, run the portable path entirely through `DownloadNoInstall` and your own install trigger.

### Medium

2. **The generator's key environment variables are misnamed, so the private key gets written to disk.** The workflow sets `SparklePrivateKey` / `SparklePublicKey` and says they are "documented" but not honoured (`build.yml:649-661`). The generator actually reads **`SPARKLE_PRIVATE_KEY` / `SPARKLE_PUBLIC_KEY`** ([SignatureManager.cs#L18-L19, L176-L210](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/src/NetSparkle.Tools.AppCastGenerator/SignatureManager.cs); [README L390, L454-L455](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/README.md)). That mismatch is the cause of the "File does not exist … NetSparkle_Ed25519.pub" failure the comment describes, and of the workaround that writes the private key to `~/.local/share/netsparkle/` (`build.yml:671-678`). Fix: export the correct names and delete the disk write. Environment variables take priority over disk ([SignatureManager.cs#L195-L210](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/src/NetSparkle.Tools.AppCastGenerator/SignatureManager.cs)). Prefer them over `--private-key-override`, which puts the key on the command line.
3. **The signing tool is installed from a floating version while it has the private key.** `dotnet tool install … AppCastGenerator --version 2.9.*` (`build.yml:621`) means any future 2.9.x release runs with the signing key in its environment. Pin the exact version (`2.9.0`). Also move `NETSPARKLE_ED25519_PRIVATE_KEY` into a GitHub **Environment** (for example `release`, deployable only from `main`) rather than leaving it a repo-wide secret. The workflow also grants `contents: write` at the top level to every job, including PR builds (`build.yml:23-24`); scope it per job.
4. **The release-notes pane is empty** (TL;DR; §3 fixes it).
5. **Every appcast holds a single item** (`gen()` scans a folder holding one binary, `build.yml:635-641`, `687-697`). The README says this is acceptable when you don't need history ([L561-L566](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/README.md)), but it means notes cannot accumulate (§3), and the dialog's "N versions behind" display never triggers.

### Low

6. **The legacy `WebBrowser` (MSHTML) renders unescaped HTML from PR titles.** MarkdownSharp follows Markdown.pl and passes inline HTML through ([Markdown syntax: inline HTML](https://daringfireball.net/projects/markdown/syntax#html)), and the window writes the result straight into an MSHTML document (UpdateAvailableWindow.cs L151-L158). Merged PR titles are low-risk input, but a stray `<kbd>…</kbd>` also flips the whole description into "HTML mode" (ReleaseNotesGrabber L185-L193). Escape `<`, `>` and `&` in CI (§3 sketch).
7. **`TmpDownloadFileNameWithExtension` is computed once, before any update is detected.** `$"Windows-Hinting-{_detectedUpdateVerison}.msi"` (`UpdateService.cs:210`) is evaluated in `CreateSparkle`, while `_detectedUpdateVerison` is still `null`, so the file is always `Windows-Hinting-.msi`. It still works, because a reused file is re-verified against the item's Ed25519 signature ([SparkleUpdater.cs#L1323-L1340](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/src/NetSparkle/SparkleUpdater.cs)), but the intent is lost. Set it in the `UpdateDetected` handler, or drop it. The field name is also misspelled (`Verison`, `UpdateService.cs:46`).
8. **`StartLoop` is not awaited.** `_sparkle.StartLoop(true, false)` returns a `Task` ([SparkleUpdater.cs#L613-L616](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/src/NetSparkle/SparkleUpdater.cs)) and is discarded at `UpdateService.cs:76`, so its exceptions bypass the surrounding `try/catch`. Observe it with a `ContinueWith` that logs faults, or `await` it from an async init.
9. **The rolling beta tag is deleted and recreated** (`build.yml:736-746`). Between deletion and the new upload, `releases/download/beta/appcast-*.xml` returns 404, and a manual check then shows "Could not check for updates" (`UpdateService.cs:126-133`). This is a short window, so the impact is small. Uploading assets before re-pointing the tag would close it, at the cost of more workflow complexity.
10. **Version source.** `BUILD_VERSION = 0.1.${{ github.run_number }}` (`build.yml:27`) is monotonic per workflow, which is fine. The `IncludeSourceRevisionInInformationalVersion=false` workaround (`Windows-Hinting.csproj:27-31`) is correct for 3.1.0. Upstream fixed the underlying bug (build metadata treated as pre-release) on `develop` in [`02260f84`](https://github.com/NetSparkleUpdater/NetSparkle/commit/02260f848139fdbef4a1f786fd0519af4a7f7788), but no NuGet release ships it yet (`develop` is at `3.1.1-preview…`), so keep the workaround.
11. **No key-rotation path.** `Ed25519Checker` gets one embedded key (`UpdateService.cs:149-170`). If the private key leaks or is lost, existing installs can only be moved over by a release signed with the old key that ships the new public key. Write that procedure down, and back the key up offline.

### Confirmed good practice

- `SecurityMode.Strict` with Ed25519 (`UpdateService.cs:170`) is what upstream recommends ([SecurityMode.cs#L7](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/src/NetSparkle/Enums/SecurityMode.cs); [README L112](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/README.md)). Strict rejects an unsigned or invalid appcast (AppCastHelper L160-L215) and verifies the download before `DownloadFinished` fires ([SparkleUpdater.cs#L1245-L1298](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/src/NetSparkle/SparkleUpdater.cs)). The `PortableUpdateInstaller` comment relying on that is correct.
- The appcast is generated **after** Authenticode signing of the MSI and portable exes, so the Ed25519 signature covers the final bytes (job order in `build.yml:191-312` → `release` job).
- CI fails hard if any `.xml.signature` is missing (`build.yml:713-719`), and both `.xml` and `.signature` are uploaded side by side, as `AppCastHelper` expects (`SignatureFileExtension` default `signature`).
- Stable appcasts are hosted at `/releases/latest/download/…` (`UpdateService.cs:34-35`), so prereleases are never offered to stable users; beta is compiled in as a separate channel (`UPDATE_CHANNEL_BETA`, `:26-32`).
- The check cadence is the upstream default: a 24 h loop with `forceInitialCheck: false` ([SparkleUpdater.cs#L613-L616](https://github.com/NetSparkleUpdater/NetSparkle/blob/b3df04ab2d06230a28e12cfd7c613d25e27d2672/src/NetSparkle/SparkleUpdater.cs)). The user opt-out and the manual check (`CheckForUpdatesAtUserRequest`) are both wired up.
- The WinForms .NET Core UI factory matches the app's WinForms stack. The tool version 2.9.0 (generator) next to 3.1.0 (library) is **not** a mismatch: both come from the same upstream commit `b3df04ab`.
- `Product` and `AssemblyTitle` are set so the dialog shows "Windows-Hinting" rather than "Application" (`Windows-Hinting.csproj:8-12`).

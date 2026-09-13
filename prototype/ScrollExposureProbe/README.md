# ScrollExposureProbe — THROWAWAY

Scroll-exposure spike for wayfinder ticket
[#59](https://github.com/knausj85/Windows-Hinting/issues/59) (parent map
[#54](https://github.com/knausj85/Windows-Hinting/issues/54), *keyboard-selectable
scroll regions via UIA — viability*). **Not** part of `Windows-Hinting.sln`, not
referenced by the app, never merges. Lives on branch `prototype/scroll-exposure-probe`.

## The question

How reliably do real apps expose scrollability to UI Automation? Working
hypothesis (map #54): **ScrollPattern exposure is inconsistent and often
false-negative** — content that visibly scrolls reports non-scrollable, or exposes
no `ScrollPattern` at all. This probe censuses, per focused window, **every** UIA
scroll signal on every candidate element, then **objectively tests** whether a
UIA-driven scroll actually moves the content — independent of what the flags claim.

## Build & run

The probe references the native UIA COM TLB (`<COMReference>`), which requires
**full-framework MSBuild** — `dotnet build`/`dotnet run` fail with MSB4803. Build
with Visual Studio's MSBuild, then run the exe:

```powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
  -latest -prerelease -find 'MSBuild\**\Bin\MSBuild.exe'
& $msbuild -restore prototype\ScrollExposureProbe\ScrollExposureProbe.csproj
& .\prototype\ScrollExposureProbe\bin\Debug\net10.0-windows\ScrollExposureProbe.exe
```

## What it does

On the **global hotkey `Ctrl+Alt+S`** (which does not steal focus, so
`GetForegroundWindow()` still points at the app you're looking at), the probe:

1. `FindAll(Subtree)` over the focused window, server-side-filtered to elements that
   bear **any** scroll signal — `IsScrollPatternAvailable`, `IsScrollItemPatternAvailable`,
   `IsRangeValuePatternAvailable`, or `ControlType == ScrollBar`.
2. For each candidate dumps: `ControlType`, `Name`, `AutomationId`, `FrameworkId`,
   `ProviderDescription`, `BoundingRectangle`, and every scroll signal —
   `ScrollPattern.{Vertically/HorizontallyScrollable, V/H ViewSize, V/H ScrollPercent}`,
   `ScrollItemPattern` presence, `RangeValuePattern.{Value, Min, Max}`.
3. **Ground truth:** for every live `ScrollPattern` with a non-sentinel percent, it
   attempts a small UIA scroll (`Scroll(SmallIncrement)`, then `SetScrollPercent`
   fallback), records whether `VerticalScrollPercent` actually changed, and **restores**
   the original position. This is the column that does *not* trust the `*Scrollable` flags.
4. Prints a **signal summary** (counts by signal type + how many actually moved) and a
   **REALITY** line for you to annotate.

Weak **`ScrollItem`-only** nodes (XAML/Blink put `ScrollItemPattern` on nearly every
element) are collapsed to a compact list so a Chromium page's hundreds of them don't
bury the real containers.

### Triggers

- **`Ctrl+Alt+S`** — the workhorse. Doesn't steal focus.
- **`cap` + Enter** — fallback if the hotkey won't register: captures the foreground
  window 3 s later (gives you time to click back to the app).
- **`auto` + Enter** — toggles capture on every foreground change (the ticket's
  "focused-window change" trigger); off by default.
- Type **any other text + Enter** to drop a `MARK` divider before a capture.

**Scoped-out triggers:** the ticket also listed menu-open and title-change
auto-capture. Deliberately not implemented — neither bears on *scroll-region*
exposure, and for a targeted census the operator picks regions by hand. Noted here as
a conscious refinement, per the ticket's "refine in the prototype session."

Output goes to the console **and** `scroll-exposure-probe.log` in the working directory.

## The census (the manual part)

For **each** target app:

1. Focus a region you **know** scrolls (click into it, or scroll it once by hand).
2. Type a MARK label + Enter (e.g. `gmail message list`) to tag the log.
3. Press `Ctrl+Alt+S`. Read the candidates + the `GROUND-TRUTH` line for each.
4. Fill the `REALITY` line: which region really scrolls, and what you scrolled.

**Matrix (7 targets):** GitHub Desktop (Electron) · Slack native (Electron) · Slack
PWA (Edge) · VSCode (Electron editor surface) · Gmail (Edge tab) · File Explorer
(native Win32) · Windows Terminal.

Read each candidate as: *signals present → what UIA claims → did a UIA-scroll actually
move it → does that match reality?* A **0-candidate window on visibly-scrollable
content** (or a candidate with `vScrollable=False` / `vPct=-1` that you *can* scroll)
is the headline **false-negative** case the map is built to characterize.

## What to capture back on #59

Per target/region: which signals present, which accurate, which false-negative, and
whether the UIA-scroll attempt actually worked. Paste the relevant `MARK`-delimited log
sections as evidence. This completed census is the primary input to the go/no-go
([#60](https://github.com/knausj85/Windows-Hinting/issues/60)).

`Ctrl+C` to quit.

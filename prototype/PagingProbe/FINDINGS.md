# PagingProbe findings: paging a picked, background control without the cursor

**Question.** Which mechanisms page, jump and line-step a *picked* scrollable control while its
window is **not** the foreground window, without moving the mouse cursor and without changing
focus? This prototype measures the untested suggestions in
[`docs/research/uia-paging-without-scrollpattern.md`](../../docs/research/uia-paging-without-scrollpattern.md)
(and its predecessor `targeted-paging-and-jump.md`). It feeds #70 (page/jump) and #69 (VS editor
gap).

**Status:** throwaway prototype, measured 2026-10-15. Raw per-run matrices are in
[`results/`](results/) (`<target>.md` + `.csv`, 822 rows in total).

## Environment

- **This was a VM:** Parallels ARM Virtual Machine (ARM64), Windows 11 Pro 10.0.26200, one
  high-DPI monitor (about 3755x2000 physical pixels). The probe is per-monitor-v2 DPI aware.
- Every mechanism tested here is a message or a UIA call. None of them uses the cursor, so the VM
  should not change the results. **The cursor never moved in any of the 822 measurements.**
- Apps tested: Notepad (Win11), File Explorer, Windows Terminal, Settings (Installed apps page),
  Visual Studio 18 Insiders (editor and Solution Explorer), Beyond Compare 5, VS Code, Edge. I
  also wrote a fixture (`prototype/PagingFixture`) that shows WinForms and WPF windows, each with
  500-item ListBox, TreeView, ListView or DataGrid, a multiline TextBox and a RichTextBox. The WPF
  window also has a ListBox nested in an outer ScrollViewer.

## Method

`PagingProbe --target <process|title>` finds candidate elements: ScrollPattern on the element, an
ancestor or a descendant; TextPattern; Grid; a vertical ScrollBar child (RangeValue); an HWND with
a live `SB_VERT`; and a `ScrollBar`-class child window. It then puts its own small "decoy" window
in front, so the target is **visible but not foreground**. Then, for each candidate, mechanism and
command (LineDown, PageDown, PageUp, ToTop, ToBottom), it:

1. Moves the control to the right starting position with a trusted driver: top for most commands,
   about 3 pages down for PageUp, and bottom for ToTop.
2. Snapshots every available oracle. In priority order: `GetScrollInfo` nPos/nPage, the native top
   index (LB/LV), UIA vertical scroll percent and view size, the TextPattern first visible line,
   the index of the first visible ScrollItem item, the ScrollBar RangeValue, and a `PrintWindow`
   pixel hash of the viewport.
3. Actuates, then waits until the oracles are stable (up to 1.5 s).
4. Converts the displacement into **pages** (1.00 = exactly one viewport). It flags side effects:
   **C** = cursor moved, **F** = foreground window changed, **K** = keyboard focus changed
   (thread focus HWND, UIA focused element, or the element's HasKeyboardFocus), **S** = selection
   or caret changed.

Mechanism ids:

| Id | Mechanism |
|---|---|
| `SP` | UIA ScrollPattern (`Scroll`, `SetScrollPercent`) on the element or an ancestor |
| `WMV` | `SendMessage(WM_VSCROLL, SB_*)` to the element's HWND |
| `WMVCTL` | `WM_VSCROLL` to the **parent** of a vertical `ScrollBar`-class child, with `lParam` = that scrollbar |
| `EMS` | `EM_SCROLL` |
| `TXT` | TextPattern `ScrollIntoView` of the line just outside the view. Uses `alignToTop=FALSE` for PageUp and End. |
| `TXTT` | Same as `TXT`, but everything is top-aligned. PageUp targets line *first − (visible − 1)*. |
| `GRIDGET` / `GRIDSI` | `GridPattern.GetItem(row, 0)` on its own, or followed by `ScrollItem` |
| `SI` | `ScrollItem.ScrollIntoView` on the item one viewport past the edge |
| `NAT` | `TVM_SELECTITEM(TVGN_FIRSTVISIBLE)`, `LB_SETTOPINDEX`, `LVM_ENSUREVISIBLE` |
| `RV` | RangeValue `SetValue` on the vertical ScrollBar element |
| `WHEEL` / `WHEELS` | `PostMessage` / `SendMessage(WM_MOUSEWHEEL, −120)`. Sent to the HWND, with `lParam` = the screen centre of the target. |
| `WHEELX` | Control for `WHEEL`: the same post, but `lParam` points at (5,5), off the target |

Focus-then-key (rank 6 in the research doc) was not tested: it changes focus by definition.

## Headline results

1. **UIA ScrollPattern is cursor-free but NOT focus-free.** On every framework (WinForms, WPF,
   Win32 RichEdit, DirectUI/Explorer, WinUI/Settings, Chromium/Edge, VS Code, VS), `Scroll` and
   `SetScrollPercent` on a **background** window **activated the window** (F) and **moved keyboard
   focus** into it (K). RangeValue `SetValue` did the same everywhere it worked, and so did VCL
   scrollbars in Beyond Compare.
   - When the target was already foreground and focus was parked on another control
     (`--target-fg --focus-name`), ScrollPattern **moved in-app focus to the scrolled control**.
     WPF moved it from TextBox to ListBox. WinForms moved it from TextBox to `ListBox item 000`.
   - Activation can **undo the scroll**. When the window activates, the app restores focus, and
     the restore scrolled the view back: RichEdit scrolls the caret back into view, and Settings
     focuses its Search box and jumps to the top. The two WinForms RichTextBox runs disagree
     (PageDown and ToBottom were exact in one run and did not move in the other). Background
     Settings gave PageUp −2.99 pages and ToBottom "no move".
   - This happens across all providers, so it is most likely in UIAutomationCore's handling of
     these two patterns, not in any one provider. **The cause was not identified.**
   - Earlier ScrollSelectProbe evidence (#61) ran with the target in the foreground, so it could
     not see this.
2. **`ScrollItem`, `TextRange.ScrollIntoView`, `Grid.GetItem`, `WM_VSCROLL`, `EM_SCROLL`, native
   item messages and posted `WM_MOUSEWHEEL` had no side effects** (no C, F, K or S), with one
   exception: the Win32 TreeView proxy's ScrollItem (see claim 8).
3. **`WM_VSCROLL` is exact page and jump wherever there is a Win32 scrollbar.** Sent straight to
   the HWND, it works when the HWND owns its `SB_VERT`. When the scrollbar is a separate
   `ScrollBar`-class child window, send it to that scrollbar's parent with `lParam` = the
   scrollbar HWND. That variant was exact on the WinForms DataGridView, the **Explorer items view
   (DirectUI)** and **Beyond Compare**.
4. **The VS editor pages cursor-free through TextPattern.** It is exact for line, page, top and
   bottom, with no side effects. This closes the #69 gap on this machine.
5. **Posted `WM_MOUSEWHEEL` is cursor-free on WPF, Chromium (Edge), Windows Terminal and Explorer
   when `lParam` holds the target's screen point.** The `WHEELX` control (`lParam` off the target)
   did not scroll them, so these apps route the wheel by `lParam`, not by where the cursor is.
   Win32 and WinForms controls and Beyond Compare route by HWND and ignore `lParam`. `SendMessage`
   behaved the same as `PostMessage`.
   - Neither worked on Settings (UWP CoreWindow), the VS editor or Solution Explorer.
   - This refutes the earlier note that "SendMessage(WM_MOUSEWHEEL) fails on WPF/Chromium": the
     message has to go to the right HWND (the WPF `HwndSource` or `Chrome_RenderWidgetHostHWND`)
     with screen coordinates.
   - It is a line step (3 lines per notch), not an exact page.

## Result matrix (background target)

Legend:

- ✅ = exact (page within 0.9–1.1 of a viewport; a line step moves 1–3 lines; jumps land
  exactly) with **no** side effects.
- ⚠️ = moves, but with side effects. **FK** = activates the window and steals focus.
- ≈ = moves, but not by an exact page.
- ✗ = no movement.
- — = not applicable.

"Line / Page / Jump" summarises LineDown / PageDown+PageUp / ToTop+ToBottom.

| Surface | SP | WM_VSCROLL (HWND) | WM_VSCROLL to sb parent | EM_SCROLL | TextPattern | Grid.GetItem | ScrollItem | Native item msg | RangeValue | Posted wheel |
|---|---|---|---|---|---|---|---|---|---|---|
| WinForms ListBox | ⚠️ FK exact | ✅ | — | — | — | — | ✅ minimal (PageDown +1.06) | ✅ LB_SETTOPINDEX | ⚠️ FK, wrong units (1 = 1%) | ✅ (HWND-routed) |
| WinForms TreeView | ⚠️ FK exact | ✅ | — | — | — | — | ≈ top-aligned: PageUp exact, PageDown +1.97 with the minimal recipe, LineDown no-op | ✅ TVM_SELECTITEM | ✗ + FK | ✅ |
| WinForms ListView (details) | ⚠️ FK exact | ✅ | — | — | — | ✗ (GetItem returns null) | ✅ minimal | ✅ LVM_ENSUREVISIBLE | ✗ + FK | ✅ |
| WinForms DataGridView | — (none) | ✗ | ✅ | — | — | ✗ (no scroll; cells lack ScrollItem) | — | — | — | ✅ |
| WinForms TextBox (Edit) | ⚠️ FK exact | ✅ | — | ✅ line/page | ✅ | — | — | — | ⚠️ FK, wrong units | ✅ |
| WinForms RichTextBox | ⚠️ FK, **racy** (undone by caret restore) | ✅ | — | ✅ | ✅ | — | — | — | ⚠️ FK | ✅ |
| WPF ListBox (virtualized) | ⚠️ FK exact | ✗ | — | — | — | — | Line ✅; page/jump ✗ by index (realized ≠ data index) | — | — | ✅ (`lParam`-routed) |
| WPF TreeView | ⚠️ FK exact | ✗ | — | — | — | — | ✅ minimal (0.92–0.96 pages) | — | — | ✅ |
| WPF DataGrid | ⚠️ FK exact | ✗ | — | — | — | ✅ **GetItem alone scrolls, exactly** | ✗ (virtualization) | — | — | ✅ |
| WPF TextBox | ⚠️ FK exact | ✗ | — | — | Line ✅; page/jump ✗ | — | — | — | ⚠️ FK, pixel units | ✅ |
| WPF RichTextBox | ⚠️ FK exact | ✗ | — | — | ✅ with `TXTT`; `alignToTop=FALSE` is ignored (minimal) | — | — | — | — | ✅ |
| WPF ListBox inside outer ScrollViewer | ⚠️ FK exact (pattern found on the **ancestor**) | ✗ | — | — | — | — | ✅ ≈0.95 pages | — | ⚠️ FK | ✗ (inner list swallows it) |
| Notepad (RichEditD2DPT) | ⚠️ FK exact | ✅ | — | ✅ | ✅ (`TXTT` End exact; `TXT` End 14 px short) | — | — | — | ⚠️ FK, wrong | ✅ (HWND-routed) |
| File Explorer items view (DirectUI) | ⚠️ FK exact | ✗ | ✅ (parent `CtrlNotifySink`) | — | — | ✗ | ≈ (25 realized items; Top only) | — | — | ✅ (`lParam`-routed) |
| File Explorer nav pane (SysTreeView32) | — | (not scrollable at test time) | — | — | — | — | ⚠️ **FK**: the proxy focuses the tree *and activates the window* | (no move to measure) | — | — |
| Windows Terminal | — (none) | ✗ | — | — | ✅ exact, everything | — | — | — | ⚠️ FK, but exact (LargeChange = page) | ✅ (`lParam`-routed) |
| Settings (WinUI, Installed apps) | ⚠️ FK, **racy** (activation refocuses Search and jumps to top). Exact when Settings is already foreground. | ✗ | — | — | — | — | ≈ leading-aligned (LineDown moves ≈1 page; End reaches 92.8%) with no side effects | — | ✗ + FK | ✗ (Post and Send) |
| Edge (web document) | ⚠️ FK exact | ✗ | — | — | ✗ (GetVisibleRanges reports 1 line; no move) | — | — | — | — | ✅ (`lParam`-routed, Post and Send) |
| VS Code (Monaco) | ⚠️ FK, oracle unusable (pattern on the hidden textarea) | ✗ | — | — | ✗ | — | — | — | — | Probably ✅ (pixels change; `WHEELX` does not) |
| **VS editor (WpfTextView)** | — (none) | ✗ | — | — | **✅ exact everything**: Line +1; PageDown +35 lines; PageUp −34 (alignBottom); Top; End (alignBottom) | — | — | — | — | ✗ (Post and Send) |
| VS Solution Explorer (1452 items) | ⚠️ FK exact | ✗ | — | — | — | — | **✅ exact** (minimal; line, ±1.00 page, jumps) | — | — | ✗ |
| **Beyond Compare 5 text pane (VCL TTextEditor)** | — (no UIA patterns) | **✗** | **✅ exact** (parent = TTextEditor, `lParam` = TUiScrollBar) | — | — | — | — | — | ⚠️ FK (focus goes to the scrollbar) | ✅ (HWND-routed) |

Not shown: there was **no S (selection or caret) change anywhere**. The WinForms ListBox
ScrollItem `LB_SETCARETINDEX` risk did not show, because an item was selected.

## Research-doc claims: confirmed, refuted, untestable

| # | Claim | Verdict |
|---|---|---|
| 1 | WinForms TreeView/ListView have no ScrollPattern | **Refuted at the client.** UIA exposes ScrollPattern on both (the HWND proxy is merged in). DataGridView really has none (confirmed). |
| 2 | `WM_VSCROLL` to an HWND with a live scrollbar is exact page/jump, needs no focus or cursor, and is useless on WPF/WinUI/Chromium | **Confirmed** (WinForms LB/TV/LV/Edit/RichEdit, Notepad; ✗ on WPF, Settings, Edge, VS). |
| 3 | Scrollbar-control variant (send to the parent with `lParam` = scrollbar); ❓ Beyond Compare | **Confirmed**, including **Beyond Compare** and the Explorer items view (new). Plain `WM_VSCROLL` to the BC pane does nothing. |
| 4 | `EM_SCROLL` works like `WM_VSCROLL` on Edit/RichEdit | **Confirmed.** |
| 5 | TextPattern `ScrollIntoView` gives an exact page; ❓ VS editor | **Confirmed** on WinForms Edit/RichEdit, Notepad, Terminal and **the VS editor**. **Refuted** on WPF TextBox (works only for already-rendered lines), Edge (GetVisibleRanges is bogus) and Monaco. |
| 6 | WPF skips `ScrollIntoView` when the range is fully visible | **Not observed.** WPF TextBox and RichTextBox scrolled a visible 2nd line to the top with `alignToTop=TRUE`. WPF did **ignore `alignToTop=FALSE`** (it scrolled minimally). Use the all-top-aligned recipe (`TXTT`). |
| 7 | Windows Terminal honours alignment exactly | **Confirmed.** |
| 8 | Win32 TreeView proxy `ScrollItem` calls `SetFocus` | **Confirmed, and worse:** it also activates the background window (Explorer nav pane, VS Server Explorer). |
| 9 | WinForms TreeNode ScrollItem is top-aligned | **Confirmed.** It also does nothing for a partially visible node, so a LineDown target of `lastFull+1` is a no-op. |
| 10 | ListView/WPF ScrollItem is minimal | **Confirmed** (WinForms ListView, WPF TreeView, VS Solution Explorer). |
| 11 | WinUI ScrollItem uses default (minimal) alignment | **Refuted** for Settings. Scrolling down put the target item at the top: LineDown moved about one page. |
| 12 | WPF DataGrid `GridPattern.GetItem` scrolls as a side effect | **Confirmed**: exact page and jump, no side effects. WinForms DataGridView, WinForms ListView and Explorer: `GetItem` does not scroll (or returns null). |
| 13 | `TVM_SELECTITEM(TVGN_FIRSTVISIBLE)`, `LB_SETTOPINDEX` and `LVM_ENSUREVISIBLE` page exactly with no side effects | **Confirmed.** LB caveat: take the page size from `SCROLLINFO.nPage`. `GetClientRect`/`LB_GETITEMHEIGHT` disagree under DPI virtualization (the first run overshot 2x). |
| 14 | "Ranks 0, 1 and 2 are cursor-free and focus-free" | **Refuted for rank 0 (ScrollPattern)**: cursor-free, but it activates and focuses (see headline 1). Ranks 1 and 2 are confirmed cursor- and focus-free. |
| 15 | Virtualized lists need ItemContainer to reach far items | **Consistent.** Index-based ScrollItem paging broke on the WPF ListBox and DataGrid and on Explorer. ItemContainer itself was **not tested**. |
| 16 | (earlier evidence) RangeValue `SetValue` is unreliable | **Confirmed.** Wrong units on Win32 proxies (1 unit = 1%), a no-op on TreeView/ListView/Settings, and FK side effects everywhere. Exact only on Terminal and Beyond Compare. |
| 17 | (earlier evidence) `SendMessage(WM_MOUSEWHEEL)` fails on WPF/UWP/Chromium | **Refuted for WPF and Chromium** when aimed at the right HWND with a screen-coordinate `lParam`. Confirmed for UWP Settings. Also fails on VS. |

## Recommended per-surface ladder

Always try the side-effect-free lanes first. Keep ScrollPattern for when nothing else works:
prefer it only when the target is already foreground, and accept that it moves in-app focus to
the picked control.

### (a) Line steps

1. **HWND scrollbar:** `WM_VSCROLL SB_LINEDOWN/UP`. Send it to the HWND when that window owns its
   `SB_VERT`; otherwise send it to the parent of the vertical `ScrollBar` child with
   `lParam` = the scrollbar. Exact for Win32, WinForms, Notepad, DataGridView, Explorer items view
   and Beyond Compare.
2. **TextPattern:** scroll the 2nd visible line into view with `alignToTop=TRUE`. Exact for the
   VS editor, Terminal, Notepad, RichEdit and WPF RichTextBox. On a WPF TextBox it works for line
   steps only.
3. **Posted `WM_MOUSEWHEEL`** to the content HWND, with `lParam` = the target's screen point.
   Gives 3-line notches for WPF, Edge/Chromium (probably VS Code), Terminal and Explorer.
4. **ScrollItem** on the next item past the edge, for minimal-aligned providers (WPF trees,
   Solution Explorer, WinForms ListView).
5. **ScrollPattern SmallIncrement:** the last resort, and the only line lane found for Settings.
   It activates the window and moves focus.

### (b) Page and jump

1. **`WM_VSCROLL` `SB_PAGE*` / `SB_TOP` / `SB_BOTTOM`**, using the same two variants as (a)1.
   Exact, no side effects.
2. **TextPattern:**
   - PageDown: line after the last visible line, `alignToTop=TRUE`.
   - PageUp: line before the first visible line with `alignToTop=FALSE`. For providers that
     ignore FALSE (WPF), use line *first − (visible−1)* with `alignToTop=TRUE` instead.
   - Top: document start, `alignToTop=TRUE`.
   - End: the last line with `alignToTop=FALSE`. Use FALSE because VS overscrolls with TRUE.
   - This is **the lane for the VS editor**. It is also exact for Terminal and Notepad.
3. **`GridPattern.GetItem(row, 0)`** for WPF DataGrid (row = last visible + visible count; 0;
   RowCount − 1).
4. **Native item messages** (TreeView, ListBox, ListView). Equal to lane 1; useful only when
   `WM_VSCROLL` is filtered.
5. **ScrollItem one viewport past the edge**, with the alignment observed once per region:
   minimal = `last + visible`, top-aligned = `last + 1`, leading (WinUI) = `last + 1`. Exact on
   Solution Explorer, WinForms ListView and ListBox. Approximate on Settings. Not usable by index
   on virtualized WPF lists or Explorer; ItemContainer is untested.
6. **ScrollPattern** `LargeIncrement` / `SetScrollPercent`. Exact everywhere, and the only exact
   lane for **Settings, Edge and VS Code**. It activates and focuses, and on a background window
   the app's focus restore can undo it (Settings, RichEdit).
7. **Wheel × N notches** as an approximate page for Chromium and WPF when ScrollPattern's focus
   side effect is unacceptable.

## The three specific questions

- **Does anything page the VS editor cursor-free?** **Yes: TextPattern `ScrollIntoView`.** Line
  +1, PageDown +35 lines (line after the last visible one, top-aligned), PageUp −34, Top, and End
  (alignBottom) were all exact, with no focus, foreground, selection or caret change.
  `WM_VSCROLL`, posted or sent wheel, and ScrollPattern (absent) do not work. Use `alignToTop=FALSE`
  for End: TRUE scrolls past the end of the document.
- **Does `WM_VSCROLL` work on Beyond Compare?** **Not to the pane HWND** (`TTextEditor`: no
  movement). **Yes, exactly, to the pane with `lParam` = its `TUiScrollBar` child**: line, page,
  top and bottom were exact, with no side effects. Posted wheel also works there (HWND-routed).
- **Does anything cursor-free scroll Settings beyond ScrollPattern?** **Only ScrollItem**, and only
  approximately. Items are leading-aligned, so `lastVisible + 1` ≈ one page, and End reaches about
  93% because of virtualization and the footer. It has no side effects. `WM_VSCROLL`, posted or
  sent wheel, RangeValue (no-op, plus FK) and TextPattern (n/a) do nothing. ScrollPattern is exact
  but activates Settings. When Settings is in the background, that activation refocuses the
  Search box and jumps back to the top.

## Not tested, and why

- **regedit:** it needs elevation, which means a UAC prompt I cannot answer, and UIPI would block
  a non-elevated sender anyway. The native SysTreeView32/SysListView32 lanes were covered by the
  WinForms fixture, which uses the real common controls.
- **Explorer nav pane paging:** the pane did not overflow at test time (22 items). Only the
  ScrollItem side effect was measured.
- **The Edge inner `overflow:auto` div** was not discovered as a candidate (only the root
  document). VS Code/Monaco has only a pixel oracle, so its wheel result is "probably".
- **Edge `WM_VSCROLL` PageUp −0.88 once:** most likely Chromium smooth-scroll from the
  prepare step still settling. Every other `WM_VSCROLL` on Edge was a no-op.
- **ItemContainer / VirtualizedItem** (for virtualized jumps) and **focus-then-key** (rank 6)
  were not implemented.
- **Office** apps and WinUI 3 `ItemsRepeater` outside Settings were not covered.
- **Bare metal:** everything ran in a Parallels ARM64 VM. None of the lanes uses the cursor, so
  that should not matter, but it is not verified.
- **Cause of the ScrollPattern/RangeValue focus steal:** not identified. It is consistent across
  frameworks; I did not trace it into UIAutomationCore.

## Files

- `Program.cs`, `PagingProbe.csproj`: the probe. Build it with VS MSBuild (COM reference; see the
  csproj header).
  - Useful flags: `--list`, `--name`, `--cand`, `--mech`, `--target-fg`, `--focus-name <UIA name>`
    and `--out`.
- `../PagingFixture/`: the WinForms and WPF fixture (`PagingFixture.exe [--wpf]`), built with
  `dotnet build`.
- `results/*.md|csv`: one matrix per run. The `_wheel` files are the `WHEEL`/`WHEELX` control
  runs. `_targetFg*` files are runs with the target in the foreground. `Settings_targetFg_accidental`
  is a run where Settings stayed in the foreground; it shows that ScrollPattern is exact and clean
  there.

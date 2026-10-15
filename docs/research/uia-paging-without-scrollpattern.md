# Page and jump for scrollable controls that don't expose ScrollPattern

**Question.** For the standard UIA controls that are often scrollable (List, Tree, DataGrid/Table,
Document/Edit, Pane/Group, Tab, ComboBox drop-downs), how can we do **Page Up/Down** and **jump to
start/end** on a *picked* control (not focused, not under the mouse) when the control does **not**
expose `ScrollPattern`? This study covers native Win32 common controls, WinForms, WPF, UWP/WinUI
XAML, text controls and Windows Terminal. Chromium/Electron (Slack, VS Code) was covered in
[`targeted-paging-and-jump.md`](targeted-paging-and-jump.md); this note only refers back to it.
Feeds [#70](https://github.com/knausj85/Windows-Hinting/issues/70) and the `IScrollActuator` design
(`docs/scroll-regions/design.md` on `modernize`).

**Method.** This is read-only research from primary sources: the Microsoft Learn UIA and
common-control references; the `dotnet/wpf` source (its WPF peers and the managed Win32 client-side
proxies); the `dotnet/winforms` source; the `microsoft/microsoft-ui-xaml` source (WinUI 3); and
the `microsoft/terminal` source. All snapshots are `main` as of 2026-10-15, and line numbers will
drift. **Nothing was run.** Claims proven by source or docs are cited; inferences are marked
🟡; anything untested is ❓.

---

## TL;DR

1. **"No ScrollPattern" mostly means two things.** Either the framework puts the pattern on a
   *different* element (WPF/WinUI delegate it from the list to an inner or outer `ScrollViewer`;
   Chromium puts it on a role-less ancestor), or the provider never implemented it. The second
   group includes **WinForms `TreeView` and `ListView`**: their UIA providers implement
   `ScrollItem` on items but have **no `ScrollPattern`** (only `ListBox` and `PrintPreviewControl`
   have one in `dotnet/winforms`). **First move:** look for the pattern on the
   ancestor/descendant chain and in the raw view before falling back.
2. **For any control with a real Win32 scrollbar, `SendMessage(hwnd, WM_VSCROLL, SB_PAGEDOWN | SB_PAGEUP | SB_TOP | SB_BOTTOM)`
   is a precise page and jump command.** It needs no focus and no cursor, is aimed by HWND, and
   carries no pointers, so it is safe across processes. It is exactly what Microsoft's own Win32
   UIA proxies do to implement `ScrollPattern.Scroll` (`WindowsScroll.cs`). It covers WinForms
   TreeView/ListView/DataGridView, native SysTreeView32/SysListView32/ListBox, multiline Edit and
   RichEdit, and any custom Win32 or VCL window that handles its own scrollbar. You can detect it
   with `GetScrollBarInfo`/`GetScrollInfo` plus `WS_VSCROLL`. **It is the strongest lane this
   research found.** It is useless on WPF, WinUI and Chromium, which draw their own scrollbars.
3. **Text controls: `TextPattern` gives an exact page.** Take the line just below the last visible
   range and call `ScrollIntoView(alignToTop: TRUE)`; that is Page Down. Use the line just above
   the first visible range with `alignToTop: FALSE` for Page Up. Jump to start or end with the
   document range collapsed to its start or end. It doesn't move focus or the caret. Windows
   Terminal and WPF `TextBox`/`RichTextBox` implement alignment properly. **Use the line just
   outside the view, not the last visible line:** WPF (and Chromium) skip `ScrollIntoView` when
   the range is already fully visible. This is the most promising lead for the **VS editor gap**
   (WPF, scrollbar only), ❓ to be confirmed on that editor.
4. **Item controls: `ScrollItem.ScrollIntoView` on a boundary item works, but alignment varies by
   framework.** WinForms TreeView sets `TopNode` (top-aligned, an exact page). WinForms ListBox
   and the native ListBox proxy use `LB_SETTOPINDEX`/minimal scrolling. ListView uses
   `LVM_ENSUREVISIBLE` (minimal). WPF `ListBox.ScrollIntoView` scrolls minimally. Chromium
   centres. To page with a minimal-scroll provider, target the item **one viewport past** the
   last visible one. **Caveat:** the native and managed **TreeView proxy calls `SetFocus` on the
   tree** before `TVM_ENSUREVISIBLE` (`WindowsTreeView.cs` L1215–1229), a side effect.
5. **Jumping in virtualized lists:**
   - **WPF** `FindItemByProperty(null, 0, …)` returns the first item even when virtualized, and
     `VirtualizedItem.Realize` brings it into view.
   - **WinUI** `Realize` is a **no-op** (`ItemAutomationPeer::RealizeImpl` returns `S_OK`), but
     the data-item peer's `ScrollIntoView` realizes and scrolls.
   - **WPF DataGrid** `GridPattern.GetItem(row, col)` **scrolls the row into view as a side
     effect** (`DataGridAutomationPeer.cs` L143–151), so `GetItem(RowCount-1, 0)` is End.
   - Finding the *last* item through `ItemContainer` means walking every item, O(n) cross-process
     calls. Prefer the delegated `ScrollPattern`, `WM_VSCROLL SB_BOTTOM`, or `Grid.GetItem`.
6. **Native message primitives** for exact item-aligned paging: `TVM_SELECTITEM(TVGN_FIRSTVISIBLE)`
   for TreeView, `LB_SETTOPINDEX` for ListBox, and `LVM_GETTOPINDEX`/`LVM_GETCOUNTPERPAGE` +
   `LVM_ENSUREVISIBLE` for ListView. Avoid `TVM_SELECTITEM(TVGN_CARET)`, which changes the
   selection and expands. Avoid `LVM_SCROLL` in report view (the proxy notes it doesn't work
   there).

### Recommended ladder (Page and ToStart/ToEnd on a picked region)

| Rank | Lane | Applies to | Page | Start / End | Side effects |
|---|---|---|---|---|---|
| 0 | **Find the real `ScrollPattern`**: on the element, its `ScrollViewer` descendant/ancestor, or in the raw view | WPF, WinUI, Chromium | `Scroll(LargeIncrement)` | `SetScrollPercent(cur, 0/100)` | none |
| 1 | **`WM_VSCROLL`/`WM_HSCROLL` to the HWND** when it has a live standard scrollbar | Win32, WinForms, VCL, multiline Edit/RichEdit | `SB_PAGEDOWN/UP` | `SB_TOP/BOTTOM` | none (UIPI: same or lower integrity only) |
| 2 | **`TextPattern` range `ScrollIntoView`** with the line just outside the view | Document/Edit with TextPattern (Terminal, WPF text, ❓ VS editor, Word) | exact | exact | none |
| 3 | **Grid item access** `GridPattern.GetItem(r, 0)` (+ `ScrollItem`) | DataGrid/Table | target row = last visible + rows per page | row 0 / `RowCount-1` | WPF: scrolls on `GetItem` |
| 4 | **`ScrollItem.ScrollIntoView` on an item one viewport beyond the edge** | List/Tree items, WinForms, WPF, WinUI, Chromium | approximate to exact, by framework | first item; last item when cheap | TreeView proxy focuses the tree |
| 5 | Native item messages (`TVM_SELECTITEM FIRSTVISIBLE`, `LB_SETTOPINDEX`, `LVM_ENSUREVISIBLE`) | native/WinForms Tree/List | exact (Tree, ListBox) | exact | none |
| 6 | Focus the control, then replay the real key | anything | app-native | app-native | moves focus, selection or caret; must restore |

Ranks 0, 1 and 2 are cursor-free and focus-free, so they work in a VM.

---

## 1. Which controls are supposed to scroll

From [Control Types and Their Supported Control Patterns](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-controlpatternmapping):

| Control type | Scroll | Item side |
|---|---|---|
| List | conditional | ListItem: ScrollItem conditional |
| Tree | conditional | TreeItem: ScrollItem conditional |
| DataGrid | conditional (+ Grid **required**) | DataItem: ScrollItem, GridItem conditional |
| Table | not listed (Grid/Table required) | — |
| Document | conditional (+ Text **required**) | — |
| Edit | not listed (Text conditional) | — |
| Pane | conditional | — |
| Tab | conditional | TabItem: none listed |
| Calendar | conditional | — |
| ComboBox | **does not support** Scroll | (the drop-down List child does) |
| Group, Custom | not listed | — |
| ScrollBar | **does not support** Scroll (RangeValue conditional) | — |

So "scrollable but no ScrollPattern" is **in-spec** for Edit, Group, Custom and Table, and a
provider choice for List, Tree and DataGrid. `ScrollItem` *"does not imply that its container or
any ancestor must implement the Scroll control pattern"*
([ScrollItem guidelines](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementingscrollitem)),
and `ItemContainer` is required for *"any control that can contain virtualized items"*, while
*"the Scroll control pattern should be unaffected by virtualization"*
([ItemContainer guidelines](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementingitemcontainer)).

## 2. Where the pattern actually lives, per framework

- **WPF.** `ItemsControlAutomationPeer.GetPattern(Scroll)` returns the **`ScrollViewer`'s peer**
  (`owner.ScrollHost`) as the provider (`ItemsControlAutomationPeer.cs` ~L24–42). A WPF
  ListBox/TreeView/DataGrid therefore *answers* ScrollPattern when its template has a
  `ScrollViewer`; when it doesn't (template without one, or an outer `ScrollViewer` doing the
  scrolling), the pattern is on that outer `ScrollViewer`'s peer. 🟡 Inferred from the
  delegation code. **Look upward.**
- **WinUI 3 / UWP XAML.** `ItemsControlAutomationPeer::GetPatternCore(Scroll)` walks from the
  ItemsHost **up toward the owner** looking for a `ScrollViewer`, and returns its peer only if one
  is found *inside* the ItemsControl (`ItemsControlAutomationPeer_Partial.cpp` L134–174). When
  the list sits inside an outer scroller (the "nested scroll viewer" case noted at
  `ListViewItemDataAutomationPeer_Partial.cpp` L77–80), the list has **no** ScrollPattern and the
  outer `ScrollViewer` has it. **Look upward.**
- **WinForms.** In `dotnet/winforms`, only `ListBox.AccessibleObject` and
  `PrintPreviewControlAccessibleObject` report `UIA_ScrollPatternId`. TreeView, ListView and
  DataGridView do not (code search for `UIA_ScrollPatternId` across the repo). Their **items**
  implement `ScrollItem` (`TreeNode.TreeNodeAccessibleObject.cs`,
  `ListViewItem.ListViewItemBaseAccessibleObject.cs`, `ListBox.ItemAccessibleObject.cs`). The
  underlying HWNDs are native common controls with standard scrollbars, so **rank 1 applies**.
- **Native Win32 common controls** (through UIA's built-in proxies). The managed proxies in
  `dotnet/wpf` (`UIAutomationClientSideProviders/.../AutomationProxies`) implement
  `ScrollPattern` for windows with `WS_VSCROLL`/`WS_HSCROLL` via `WindowsScroll.cs`, and the
  native UIAutomationCore proxies are presumably equivalent (🟡: closed source). Where a proxy
  isn't registered for a custom window class, the window shows up as a pattern-less `Pane`, yet
  still answers `WM_VSCROLL`. **Rank 1 applies.**
- **Chromium / Electron.** See [`targeted-paging-and-jump.md`](targeted-paging-and-jump.md) §1:
  any overflowing scroll container gets ScrollPattern whatever its role, often on an ancestor the
  control-view walk skips. VS Code lists defeat native scrolling by design.

## 3. Lane details

### 3.1 `WM_VSCROLL` / `WM_HSCROLL` to the control's HWND (rank 1)

- The message carries `SB_LINEUP/DOWN`, `SB_PAGEUP/DOWN`, `SB_TOP`, `SB_BOTTOM` and
  `SB_THUMBPOSITION`. `lParam` is `NULL` for a standard scrollbar and the scrollbar control's
  handle when it comes from a scrollbar control
  ([WM_VSCROLL](https://learn.microsoft.com/en-us/windows/win32/controls/wm-vscroll)).
- **Microsoft's proxies do exactly this.** `WindowsScroll.ScrollCursor` maps
  `ScrollAmount.LargeIncrement` → `SB_PAGEDOWN` (and `SB_PAGERIGHT` horizontally) and sends
  `WM_VSCROLL`/`WM_HSCROLL` with `SendMessage` (`WindowsScroll.cs` ~L355–440). `SetScrollPercent`
  sends `SB_THUMBPOSITION`; for the **WinForms TreeView**, where `SB_THUMBPOSITION` doesn't move,
  it falls back to repeated `SB_LINEUP`/`SB_LINEDOWN` (comment at ~L323). **For jumps, prefer
  `SB_TOP`/`SB_BOTTOM` over `SB_THUMBPOSITION`.**
- **Detection** (proxy's `Scrollable`, `WindowsScroll.cs` ~L109–150): the window has the
  `WS_VSCROLL`/`WS_HSCROLL` style, it is enabled, `GetScrollBarInfo(OBJID_VSCROLL)` doesn't
  report `STATE_SYSTEM_UNAVAILABLE`, and `GetScrollInfo` shows `nMax != nMin && nPage != range`.
  These calls read user32-held state and work across processes. For Edit and RichEdit,
  `ES_MULTILINE` is also required (`HasScrollableStyle`).
- **Verify:** read `GetScrollInfo.nPos` before and after (no UIA needed).
- **Scrollbar *controls*** (a separate `ScrollBar`-class child window, as in some VCL and Delphi
  apps): send `WM_VSCROLL` to the scrollbar's **parent** with `lParam` = the scrollbar HWND,
  mirroring what the control itself sends. 🟡 Inferred from the WM_VSCROLL contract; ❓ Beyond
  Compare.
- **Text controls** also accept
  [`EM_SCROLL`](https://learn.microsoft.com/en-us/windows/win32/controls/em-scroll)
  (`SB_PAGEDOWN/UP`, "equivalent to sending a WM_VSCROLL").
- **Limits:**
  - Useless for WPF, WinUI and Chromium, which have no HWND scrollbar on the content.
  - Blocked by UIPI towards higher-integrity windows.
  - Use `SendMessageTimeout` to survive a hung target.
  - A window may handle `WM_VSCROLL` only for its own standard bar, so check scrollability first.
- **Map to the scroll target:** a UIA element's `NativeWindowHandle` (or the nearest ancestor
  with one) gives the HWND. The scroll-region detector's child-HWND fragment descent already
  finds these windows.

### 3.2 `TextPattern` ranges (rank 2)

- `IUIAutomationTextRange::ScrollIntoView(alignToTop)`: *"TRUE if the text control should be
  scrolled so that the text range is flush with the top of the viewport; FALSE … flush with the
  bottom"*
  ([doc](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextrange-scrollintoview)).
- `GetVisibleRanges` returns the visible spans
  ([doc](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextpattern-getvisibleranges)).
  `TextUnit_Page` exists, but providers that don't support it substitute the next larger unit
  (`Document`)
  ([text units](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-uiautomationtextunits)),
  so **don't use `Move(Page)`**. Build the page from the visible range.
- **Recipes:**
  - **Page Down:** `r = last visible range`; `r.Move(TextUnit_Line, 1)` on a degenerate end
    point → the first line *below* the viewport; `ExpandToEnclosingUnit(Line)`;
    `ScrollIntoView(TRUE)`.
  - **Page Up:** the first line *above* the first visible range, `ScrollIntoView(FALSE)`.
  - **Start / End:** `DocumentRange`, collapse to start → `ScrollIntoView(TRUE)`; collapse to
    end → `ScrollIntoView(FALSE)`.
- **Why the "outside" line:** WPF's `TextAdaptor.ScrollIntoView` returns early when the range is
  already fully visible ("It's probably not aligned as requested, but who cares since it's
  entirely visible anyway", `TextAdaptor.cs` L224–229). Targeting a line already visible would
  do nothing.
- **Windows Terminal** honors alignment exactly: it moves the viewport so the range's start row
  is the top row, or clamps at the buffer's end (`UiaTextRangeBase::ScrollIntoView`, L1200–1240).
- **Side effects:** `ScrollIntoView` moves the view, not the selection or caret (no `Select` call
  in either implementation read). 🟡 Not checked for every provider.
- ❓ **VS editor:** the Visual Studio text editor (WPF, no ScrollPattern) is believed to expose a
  UIA TextPattern. If its `ScrollIntoView` honors alignment, rank 2 closes the VS-editor gap
  recorded on G [#69](https://github.com/knausj85/Windows-Hinting/issues/69). **Prototype this
  first.**
- ❓ Monaco (VS Code editor) renders only visible lines; see the earlier doc.

### 3.3 Grid access (rank 3)

- **WPF DataGrid:** `IGridProvider.GetItem(row, column)` calls
  `OwningDataGrid.ScrollIntoView(item, column)` and `UpdateLayout()` *before* returning the cell
  (`DataGridAutomationPeer.cs` L143–165). Fetching a cell **is** a scroll: `GetItem(RowCount-1, 0)`
  is End, `GetItem(0, 0)` is Start. For Page Down, read the last visible row (bounds of realized
  rows) and fetch row `last + visibleRows`. The scroll is minimal, so that row lands at the
  bottom and the view moves by one page. 🟡 Inferred.
- Other providers may not scroll on `GetItem`; follow with `ScrollItem.ScrollIntoView` on the
  returned cell or row. ❓ WinForms DataGridView's provider was not read.

### 3.4 `ScrollItem.ScrollIntoView` on boundary items (rank 4)

Alignment differs by framework:

| Provider | Implementation | Alignment |
|---|---|---|
| WinForms TreeNode | `_owningTreeView.TopNode = node` (returns early if already visible) | **top**: exact page |
| WinForms ListViewItem | `_owningItem.EnsureVisible()` → `LVM_ENSUREVISIBLE` | minimal |
| WinForms ListBox item | `LB_SETTOPINDEX` above; `LB_SETTOPINDEX(index − visible + 1)` below; **`LB_SETCARETINDEX` when nothing is selected** (moves the focus caret) | minimal-ish, bottom-aligned below |
| Win32 ListBox proxy | `LB_SETTOPINDEX(item)` | top |
| Win32 ListView proxy | `LVM_ENSUREVISIBLE(item, FALSE)` | minimal |
| Win32 TreeView proxy | **`SetFocus(hwnd)`**, then `TVM_ENSUREVISIBLE` | minimal; **steals focus** |
| WPF ListBoxItem | `ListBox.ScrollIntoView(Item)` | minimal |
| WinUI ListView data item | `Selector.ScrollIntoViewInternal(..., Default)`; nested-scroller case uses `BringIntoView` | default (minimal) |
| Chromium | always centres (earlier doc) | centre |

Sources: `dotnet/winforms` `TreeNode.TreeNodeAccessibleObject.cs` ~L231–244,
`ListViewItem.ListViewItemBaseAccessibleObject.cs` L264, `ListBox.ItemAccessibleObject.cs`
L196–240; `dotnet/wpf` proxies `WindowsListBox.cs` L919–933, `WindowsListViewItem.cs` L654–669,
`WindowsTreeView.cs` L1215–1229, peer `ListBoxItemAutomationPeer.cs` L51–59; WinUI
`ListViewItemDataAutomationPeer_Partial.cpp` L68–101, `SelectorItemAutomationPeer_Partial.cpp`
L304–320.

**Generic page recipe for minimal or top-aligned providers:** find the visible items (realized
children whose bounds intersect the viewport); `v` = how many fully fit. Page Down: target the
item at `lastVisible + v` (minimal providers put it at the bottom → net one page) or at
`lastVisible + 1` (top-aligned providers put it at the top → net one page). Page Up mirrors this.
Identify the alignment by observing once (`MovedSince` on bounds) and remember it per region. A
centre-aligning provider needs `lastVisible + v/2`. 🟡 Inferred recipe; ❓ prototype.

### 3.5 Virtualized lists: ItemContainer and VirtualizedItem

- **Contract:** `FindItemByProperty(startAfter = NULL, propertyId = 0)` returns the first item;
  repeated calls walk in sequence; a virtualized match may be a **placeholder** that only
  supports `VirtualizedItem`; `Realize()` makes it real. Placeholders may be invalidated by the
  next call or any viewport change, so realize at once
  ([ItemContainer guidelines](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementingitemcontainer)).
- **WPF:** `FindItemByProperty` with `propertyId == 0` returns the next item via
  `FindOrCreateItemAutomationPeer`, so a peer exists for virtualized data
  (`ItemsControlAutomationPeer.cs` ~L226–275). `Realize` → `RealizeCore` →
  `parent.OnBringItemIntoView(Item)`, deferred through the dispatcher when generation isn't ready
  (`ItemAutomationPeer.cs` ~L565–603). **So `Realize` on the first item is Start.** Reaching the
  last item needs `count` calls, which is fine for small lists and too slow for large ones.
- **WinUI:** `FindItemByProperty` iterates `Items` ("doesn't support Data virtualization",
  `ItemsControlAutomationPeer_Partial.cpp` ~L680). **`ItemAutomationPeer::RealizeImpl` is a
  no-op** (L1048–1051). Use the item peer's `ScrollIntoView` instead, which realizes it.
- **WinForms / Win32:** no ItemContainer. Use native messages (§3.6).

### 3.6 Native item messages (rank 5)

All take integers or `HTREEITEM` handles only, so they are safe across processes:

- **TreeView:**
  - `TVM_GETNEXTITEM(TVGN_FIRSTVISIBLE)` → top row; step with `TVGN_NEXTVISIBLE` (the item must
    be visible); `TVM_GETVISIBLECOUNT` → rows per page.
  - Page Down = `TVM_SELECTITEM(TVGN_FIRSTVISIBLE, item at top + count)`, which *"ensures that
    the specified item is visible, and, if possible, displays it at the top"*
    ([TVM_SELECTITEM](https://learn.microsoft.com/en-us/windows/win32/controls/tvm-selectitem)).
  - Start = `TVGN_ROOT` + `TVGN_FIRSTVISIBLE`. `TVGN_LASTVISIBLE` *"retrieves the last expanded
    item in the tree"* (not the last on screen), which suits End
    ([TVM_GETNEXTITEM](https://learn.microsoft.com/en-us/windows/win32/controls/tvm-getnextitem)).
  - **Never `TVGN_CARET`:** it changes the selection and can expand.
  - Caveat: `TVGN_FIRSTVISIBLE` *"if the item is the child of a collapsed parent … the parent's
    list … is expanded"*. Visible items have no collapsed parents, so stepping by
    `TVGN_NEXTVISIBLE` avoids this. 🟡
- **ListBox:** `LB_GETTOPINDEX` / `LB_SETTOPINDEX` (+ `LB_GETITEMHEIGHT` per page), as the
  WinForms provider does.
- **ListView:** `LVM_GETTOPINDEX` + `LVM_GETCOUNTPERPAGE`; `LVM_ENSUREVISIBLE(index, FALSE)`
  scrolls minimally and fails under `LVS_NOSCROLL`
  ([LVM_ENSUREVISIBLE](https://learn.microsoft.com/en-us/windows/win32/controls/lvm-ensurevisible)).
  Simpler: rank 1 `SB_PAGEDOWN`. **`LVM_SCROLL` doesn't work in report mode** (proxy comment,
  `WindowsListViewScroll.cs` L76).

### 3.7 Focus, then key (rank 6)

Covered in the earlier doc and #70: it works everywhere the app handles the key, but it moves
focus (and often the selection or caret). Keep it as a last resort with save and restore.

## 4. Capability matrix

Legend: ✅ source or docs prove it · 🟡 inferred · ❓ prototype needed · — not applicable.

| Control (framework) | Delegated ScrollPattern | WM_VSCROLL | TextPattern | Grid.GetItem | ScrollItem | Native item msgs |
|---|---|---|---|---|---|---|
| List/Tree (WPF) | ✅ usually (ScrollViewer peer) | — | — | — | ✅ minimal | — |
| DataGrid (WPF) | ✅ usually | — | — | ✅ scrolls on GetItem | ✅ | — |
| List/Tree (WinUI/UWP) | ✅ unless nested scroller (then on outer) | — | — | — | ✅ default (data peer realizes) | — |
| TreeView (WinForms) | ❌ none | ✅ | — | — | ✅ **top-aligned** | ✅ |
| ListView (WinForms) | ❌ none | ✅ | — | ❓ | ✅ minimal | ✅ |
| ListBox (WinForms) | ✅ | ✅ | — | — | ✅ (caret side effect) | ✅ |
| DataGridView (WinForms) | ❓ not found | ✅ 🟡 | — | ❓ | ❓ | — |
| SysTreeView32 / SysListView32 / ListBox (native) | ✅ proxy | ✅ | — | — | ✅ (Tree: **focus**) | ✅ |
| Custom Win32/VCL window with standard bars | ❌ often bare Pane | ✅ | — | — | — | — |
| Multiline Edit / RichEdit | ✅ proxy 🟡 | ✅ + `EM_SCROLL` | ✅ | — | — | — |
| WPF TextBox/RichTextBox | ✅ (ScrollViewer) 🟡 | — | ✅ (skips if visible) | — | — | — |
| Windows Terminal | ❓ | — | ✅ exact | — | — | — |
| VS editor (WPF) | ❌ (census) | — | ❓ **key lead** | — | — | — |
| Chromium / Electron | see earlier doc | ❌ zero-delta | 🟡 | ❌ | ✅ centre | — |

## 5. Implications for `IScrollActuator`

1. Before the wheel fallback, **resolve a better handle**: delegated `ScrollPattern` (ancestor or
   descendant `ScrollViewer`, or the raw view), then the HWND's standard scrollbar, then
   TextPattern. Record which lane won on the region (the scan could stamp it alongside
   `ScrollCaps`).
2. Rank 1 (`WM_VSCROLL`) and rank 2 (`TextPattern`) are also exact **line** steps (`SB_LINEDOWN`;
   the next line) and are cursor-free in a VM. They may beat the parked wheel for taps on those
   surfaces.
3. Avoid the side effects listed in §3.4 (TreeView proxy `SetFocus`, WinForms ListBox
   `LB_SETCARETINDEX`) by preferring the native message over `ScrollItem` on those controls.

## 6. Uncertain / to prototype

- ❓ VS editor TextPattern `ScrollIntoView` alignment (closes the VS-editor gap if it works).
- ❓ Beyond Compare and other VCL apps: whether `WM_VSCROLL` to the content HWND, or to the parent
  of a scrollbar control, pages them.
- ❓ WinForms DataGridView UIA patterns (its provider was not read).
- 🟡 The native UIAutomationCore Win32 proxies (closed source) are assumed to behave like the
  managed proxies read here.
- ❓ Which alignment each provider actually produces (observe once per region; the table in §3.4
  comes from code, not runs).
- ❓ Office (Word/Excel/Outlook) and WinUI `ItemsRepeater` (Settings) were not covered.

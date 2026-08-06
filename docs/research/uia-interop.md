# Research: UIA interop paths beyond the COM tlbimp reference

- **Ticket:** [#27](https://github.com/knausj85/Windows-Hinting/issues/27) (wayfinder:research, parent map #26)
- **Date:** 2026-08-06
- **Question:** Can Windows-Hinting escape the `UIAutomationClient` COM tlbimp `<COMReference>` — which forces full-framework MSBuild and blocks `dotnet build` / `dotnet test` — without losing UIA capability or scan latency?

## Why the build is blocked today

`Windows-Hinting/Windows-Hinting.csproj` references the UIA client typelib via
`<COMReference Include="UIAutomationClient" WrapperTool="tlbimp" Guid="944de083-8fb8-45cf-bcb7-c477acb2f897" EmbedInteropTypes="true">`.
The .NET SDK's MSBuild cannot run the `ResolveComReference` task, so `dotnet build` fails with
**MSB4803** ("ResolveComReference is not supported on the .NET Core version of MSBuild").
This is a build-*time* limitation only — the built-in COM interop *runtime* (RCWs) is fully supported on
modern .NET on Windows. Microsoft's only documented workaround for MSB4803 is "use MSBuild.exe";
`COMReference` support in SDK MSBuild has been an open request since 2018 and remains open in 2026.

- MSB4803 / ResolveComReference: <https://learn.microsoft.com/en-us/visualstudio/msbuild/errors/msb4803>, <https://learn.microsoft.com/en-us/visualstudio/msbuild/resolvecomreference-task#msb4803-error>
- Open SDK-MSBuild request: <https://github.com/dotnet/msbuild/issues/3986> (see also <https://github.com/dotnet/runtime/issues/97125>)
- COM interop supported on modern .NET: <https://learn.microsoft.com/en-us/dotnet/standard/native-interop/cominterop>

Consequence: any option that replaces the `<COMReference>` with an ordinary assembly/package reference
unblocks `dotnet build`, `dotnet run`, `dotnet publish`, and `dotnet test` (the csproj already carries
`xunit.assert` / `xunit.v3.extensibility.core` references waiting on exactly this).

## What the code actually uses (inventory)

From `Windows-Hinting/Services/UIAutomationService.cs`, `Windows-Hinting/NativeInterop/UIAutomationWrapper.cs`,
`Windows-Hinting/Services/ElementActivators/*.cs`, and `Windows-Hinting/UIAutomationConstants.cs`:

| Area | Members used |
|---|---|
| Activation | `new CUIAutomation()` (coclass activation; csproj comment also references `CUIAutomation8`'s `SafeArrayToRectNativeArray` warning MSB3305) |
| Conditions | `CreatePropertyCondition`, `CreateAndCondition`, `CreateOrConditionFromArray`, `CreateTrueCondition` |
| Caching (hot path) | `CreateCacheRequest`, `TreeScope`, `AddProperty` ×14 (BoundingRectangle, ClickablePoint, ControlType, Name, ClassName, ProcessId, NativeWindowHandle, IsKeyboardFocusable, 4× `IsXxxPatternAvailable`, LegacyIAccessibleState, IsLegacyIAccessiblePatternAvailable), `AddPattern` ×6 (Invoke, ExpandCollapse, Selection, SelectionItem, Toggle, LegacyIAccessible) |
| Bulk scan (latency-critical) | `FindAllBuildCache(TreeScope_Descendants, …)`, `FindFirstBuildCache(TreeScope_Children, …)`, `IUIAutomationElementArray.Length/GetElement` |
| Tree walking | `ControlViewWalker`, `CreateTreeWalker`, `GetParentElement`, `GetFirstChildElement`, `GetNextSiblingElement` |
| Element access | `ElementFromHandle`, `GetRootElement`, `GetFocusedElement`, `GetCachedPropertyValue`, `CachedName`, `CurrentName`, `CurrentClassName`, `CurrentControlType`, `CurrentIsEnabled`, `CurrentBoundingRectangle`, `SetFocus` |
| Patterns (activation) | `GetCachedPattern(...) as IUIAutomationInvokePattern / TogglePattern / ExpandCollapsePattern / SelectionItemPattern` → `Invoke()` / `Toggle()` / `Expand()` / `Select()`; `IUIAutomationLegacyIAccessiblePattern` declared |
| Lifetime | Pervasive `Marshal.IsComObject` / `Marshal.ReleaseComObject` — this only works with **built-in COM interop RCWs**, not `ComWrappers`-based interop |
| Constants | `UIAutomationConstants.cs` defines all control-type/property/pattern IDs as plain `const int` — interop-neutral, works unchanged with every option |

Scan latency is dominated by the single cross-process `FindAllBuildCache` round trip plus cached
property reads; any replacement must preserve exactly that call shape.

## Comparison table

| | 1. Interop.UIAutomationClient (NuGet) | 2. CsWin32-generated UIA | 3. WinRT `Windows.UI.UIAutomation` | 4. FlaUI (FlaUI.Core + FlaUI.UIA3) |
|---|---|---|---|---|
| API coverage vs inventory above | **100 %** — verified in the shipped DLL: `CUIAutomation`/`CUIAutomation8`, `IUIAutomation`…`IUIAutomation6`, `IUIAutomationElement`…`Element9`, CacheRequest, TreeWalker, `FindAll/FindFirstBuildCache`, all 5 patterns | Interfaces generate, but current stable release mass-breaks UIA in the new COM-source-generator mode (1163 compile errors, fixed post-release); classic mode has ergonomics regressions (raw VARIANT/SAFEARRAY, `new`-shadowed base methods) | **~0 %** — 4 metadata-only classes; no ElementFromHandle/FindAll/CacheRequest/TreeWalker/patterns; itself depends on COM-obtained elements | **100 %** wrapped (CacheRequest→`FindAllBuildCache`, ControlViewWalker, all 5 patterns incl. LegacyIAccessible.State) + raw-COM escape hatch (`NativeAutomation`, `NativeElement`) |
| `dotnet build` / `dotnet test` | Yes — plain `PackageReference`, no ResolveComReference | Yes | Yes (but useless for the scenario) | Yes — FlaUI.UIA3 itself depends on package from option 1 |
| Scan latency | Identical — same tlbimp-style RCW calls | Comparable when it compiles; more manual marshaling on the managed side in classic/struct modes | n/a | Same UIA round trips (passes through to `FindAllBuildCache`); adds one managed wrapper allocation per result element |
| Code churn | **Minimal** — swap csproj reference + change `using UIAutomationClient;` → `using Interop.UIAutomationClient;` (~17 files); `Marshal.ReleaseComObject` keeps working | High — rewrite call sites for different type shapes; new source-gen mode uses `ComWrappers`, so `Marshal.ReleaseComObject`/`IsComObject` code must be removed/rewritten | Total rewrite impossible (no client API) | Medium-high if adopting FlaUI idioms; low-medium if used mainly as interop carrier via escape hatch |
| Maintenance | Single maintainer (Roemer); package frozen at 10.19041.0 (2020) but wraps an equally frozen typelib; ~3.8 M downloads; MIT | Microsoft, very active (0.3.298, 2026-06-17); UIA fix merged 2026-07-28 but **unreleased** | Microsoft, but Remote Operations helper repo dormant since 2022 | Active-slow: 5.0.0 (2025-02-25), single maintainer, net10 TFM on master but unreleased; net8 assembly runs on net10 |
| Verdict shape | Drop-in unblocker | Not yet (needs a post-July-2026 CsWin32 release; then attractive for AOT) | Dead end for client scanning | Works, but adds a layer the app doesn't need; its interop dependency alone equals option 1 |

## Option 1 — Pre-generated interop assembly: `Interop.UIAutomationClient` (NuGet)

**What it is.** A genuine tlbimp-generated interop assembly for the same UIAutomationClient typelib
(GUID `944de083-8fb8-45cf-bcb7-c477acb2f897`), published by Roemer (also the FlaUI author).
Latest **10.19041.0** (Windows 10 2004 SDK typelib), published 2020-07-17, ~3.8 M downloads, MIT,
source at <https://github.com/Roemer/UIAutomation-Interop>. Assemblies for
`net35/net40/net45/netcoreapp3.0/netstandard2.0`; a `net10.0-windows` app resolves the
`netcoreapp3.0` asset. A `.Signed` strong-named variant exists
(<https://www.nuget.org/packages/Interop.UIAutomationClient.Signed>).

**Verified API surface** (extracted from the shipped DLL): `IUIAutomation`–`IUIAutomation6`,
`CUIAutomation` and `CUIAutomation8` coclasses, `IUIAutomationCacheRequest`, `IUIAutomationTreeWalker`,
`IUIAutomationElement`–`IUIAutomationElement9`, `FindAllBuildCache`/`FindFirstBuildCache`, and the
Invoke/Toggle/ExpandCollapse/SelectionItem/LegacyIAccessible pattern interfaces — every member in the
inventory above. The typelib tops out at `IUIAutomation6`; UIA3 additions after SDK 19041 are minimal
and nothing in this codebase uses them.

**Build/runtime.** Ordinary `PackageReference` → no `ResolveComReference` → `dotnet build`/`dotnet test`
work. Runtime is the same built-in COM interop (RCWs) the tlbimp reference produces today, so
`Marshal.ReleaseComObject`/`IsComObject` and the `rectObj is double[]` VARIANT-to-array marshaling
behavior are unchanged, and scan latency is identical. This is the exact configuration FlaUI.UIA3 5.0.0
ships on for net6/net8 — multi-million-download field evidence on modern .NET.

**Migration mechanics / gotchas.**
- Namespace differs: `using UIAutomationClient;` → `using Interop.UIAutomationClient;` (mechanical, ~17 files).
- The package ships a `build/*.targets` that **forces `EmbedInteropTypes=false`**; the current csproj sets
  `EmbedInteropTypes=true` on the COMReference. Practical effect: the interop DLL is deployed alongside the
  app instead of types being embedded — behaviorally equivalent for this code; don't fight the targets file.
- Do not confuse with the `Interop.UIAutomationClient.dll` inside the Windows SDK's UIAVerify folder, which
  has internal types; Microsoft Q&A explicitly points people to the NuGet package instead
  (<https://learn.microsoft.com/en-gb/answers/questions/1184125/interop-uiautomationclient-private-internal-starti>).
- Fallback if the package ever became unacceptable: run `tlbimp.exe` once and commit/pack the DLL — the same
  thing the package did (<https://learn.microsoft.com/en-us/dotnet/framework/tools/tlbimp-exe-type-library-importer>).
  Note `dscom` (<https://github.com/dspace-group/dscom>) is tlb**exp**-direction only and is *not* a tlbimp replacement.

**Risk profile.** Package frozen since 2020 with a single maintainer — but it wraps a typelib that is
itself effectively frozen, and there is no marshaling logic to rot; the risk is availability, mitigable
by vendoring the DLL or regenerating with tlbimp.

Sources: <https://www.nuget.org/packages/Interop.UIAutomationClient>, <https://github.com/Roemer/UIAutomation-Interop>, <https://github.com/FlaUI/FlaUI/blob/master/src/FlaUI.UIA3/FlaUI.UIA3.csproj>

## Option 2 — CsWin32-generated UIA (`Windows.Win32.UI.Accessibility`)

The repo convention (`.github/copilot-instructions.md`: "Do not try to regenerate UIA via CsWin32")
states the rule without the reason. The issue-tracker history reconstructs it:

- **2022: generation failed outright.** `IUIAutomation` hit an internal generator error
  ([#736](https://github.com/microsoft/CsWin32/issues/736)), fixed within a day
  ([PR #737](https://github.com/microsoft/CsWin32/pull/737)). The "CsWin32 can't do UIA at all" era was brief.
- **Classic `[ComImport]` mode (works today, ergonomics regress).** Derived interfaces
  (`IUIAutomationElement2..9`) re-declare base methods with `new` shadowing
  ([#1391](https://github.com/microsoft/CsWin32/issues/1391), only the doc-comment aspect was fixed);
  VARIANT/SAFEARRAY/BSTR arrive as raw structs/pointers without tlbimp's automatic `object`/array
  marshaling. Nothing is "broken", but every property read like the current
  `GetCachedPropertyValue(...) is double[]` pattern would need manual variant handling.
- **`allowMarshaling: false` struct mode.** Interfaces become structs, inheritance is lost, QI is manual,
  and UIA event-handler CCWs must be hand-built ([#831](https://github.com/microsoft/CsWin32/issues/831),
  [#708](https://github.com/microsoft/CsWin32/issues/708), settings schema:
  <https://github.com/microsoft/CsWin32/blob/main/src/Microsoft.Windows.CsWin32/settings.schema.json>).
  A standing reason to avoid for UIA client work.
- **New `GeneratedComInterface` source-generator mode (the on-paper best path).** Added Oct 2025
  ([PR #1474](https://github.com/microsoft/CsWin32/pull/1474)): true interface inheritance, `ComVariant`
  ([#1552](https://github.com/microsoft/CsWin32/issues/1552)), `CreateInstance<T>()` coclass factories
  ([#1500](https://github.com/microsoft/CsWin32/issues/1500)), NativeAOT support. **But UIA specifically
  was mass-broken in the current stable release**: [#1745](https://github.com/microsoft/CsWin32/issues/1745)
  (2026-07-21) reports 1163 compile errors across ~57 `IUIAutomation*` interfaces against 0.3.298 — nearly
  every UIA method names its out-param `retVal`, colliding with the .NET COM source generator's internal
  `__retVal_native` ([dotnet/runtime#115608](https://github.com/dotnet/runtime/issues/115608), still open).
  Fixed by [PR #1746](https://github.com/microsoft/CsWin32/pull/1746) (merged 2026-07-28), which is
  **not in any stable release as of 2026-08-06** (latest: 0.3.298, 2026-06-17,
  <https://www.nuget.org/packages/Microsoft.Windows.CsWin32>).
- **Migration cost beyond generation:** the new mode is `ComWrappers`-based —
  `Marshal.ReleaseComObject`/`IsComObject` (used ~30 times across the services) do not apply and that
  lifetime code would need removal/rewrite.

**Does the ban still hold?** As a hard rule its original justification is stale; as of today it still
holds *practically* for the modern mode (fix merged but unreleased) and *ergonomically* for the classic
mode. Once a post-July-2026 CsWin32 release ships PR #1746, the new mode becomes a plausible path that
also unblocks `dotnet build` — at the cost of the largest rewrite of the four options. Microsoft's own
engineers filing/fixing these UIA issues (jevansaks, #1552/#1746) suggests a first-party consumer is
actively exercising CsWin32-UIA.

## Option 3 — C#/WinRT `Windows.UI.UIAutomation`

Not viable as a client replacement. The namespace contains exactly four classes
(`AutomationConnection`, `AutomationConnectionBoundObject`, `AutomationElement`, `AutomationTextRange`);
`AutomationElement` exposes only `AppUserModelId`, `ExecutableFileName`, `IsRemoteSystem` — no factory,
no `ElementFromHandle`, no FindAll/CacheRequest/TreeWalker, no patterns, no events
(<https://learn.microsoft.com/en-us/uwp/api/windows.ui.uiautomation>,
<https://learn.microsoft.com/en-us/uwp/api/windows.ui.uiautomation.automationelement>).
Microsoft's own xlang tracker confirms there is no WinRT-only way to obtain an element — "You must always
start with an IUIAutomationElement which you got from … IUIAutomation (CUIAutomation coclass)"
(<https://github.com/microsoft/xlang/issues/728>). `Windows.UI.UIAutomation.Core` is the Remote
Operations / custom-pattern provider surface, and the companion helper repo
<https://github.com/microsoft/Microsoft-UI-UIAutomation> (batching UIA calls into one cross-process round
trip) is C++-oriented, has no NuGet package, and has been dormant since June 2022. It sits *on top of*
the COM client API, never instead of it. Additionally, the repo's bare `net10.0-windows` TFM projects
Windows APIs at 10.0.19041 — below the 20348 contract where these types even appear.

*(Aside: Remote Operations is the only thing in this corner relevant to scan latency, and it is not
practically consumable from C# today.)*

## Option 4 — FlaUI (FlaUI.Core + FlaUI.UIA3)

**Coverage.** Everything in the inventory is wrapped: `CacheRequest` with `Add(PropertyId)`/`Add(PatternId)`,
`TreeScope`, `AutomationElementMode`; when a cache request is active, `FindAll/FindFirst` route to native
`FindAllBuildCache`/`FindFirstBuildCache` (<https://github.com/FlaUI/FlaUI/blob/master/src/FlaUI.UIA3/UIA3FrameworkAutomationElement.cs>),
so the bulk-scan round-trip count is identical to the current code. `ITreeWalkerFactory.GetControlViewWalker()`,
`FocusedElement()`, `FromHandle()`, and all five needed patterns exist, including
`LegacyIAccessiblePattern.State` (<https://github.com/FlaUI/FlaUI/blob/master/src/FlaUI.Core/Patterns/LegacyIAccessiblePattern.cs>).
Activation tries `CUIAutomation8` first, falling back to `CUIAutomation`.

**Build.** No `<COMReference>` anywhere — FlaUI.UIA3 takes a plain `PackageReference` on
`Interop.UIAutomationClient` 10.19041.0 (option 1's package), so `dotnet build` works. Released 5.0.0
targets `net48/net6.0-windows/net8.0-windows` (net8 assembly runs fine on net10); master already targets
`net10.0-windows` but that is unreleased.

**Escape hatch.** `UIA3Automation.NativeAutomation` exposes the raw `IUIAutomation`, and
`UIA3FrameworkAutomationElement.NativeElement` the raw `IUIAutomationElement`, using the *same public
interop types* as option 1 — incremental porting is possible and existing raw-COM code can coexist.

**Costs/risks.**
- The ambient, thread-static cache model (`using (cacheRequest.Activate())`) throws when reading a
  property not in the cache — a different failure mode than the explicit `Cached*`/`Current*` split the
  code uses now (<https://github.com/FlaUI/FlaUI/wiki/Caching>).
- Wrapper allocation per result element (managed-side only; UIA round trips dominate at hint-overlay
  scale — no issue reports the wrapper itself as a bottleneck vs raw COM).
- Maintenance: single maintainer, 251 open issues, ~16 months between releases; alive (pushes through
  2026-06) but slow (<https://github.com/FlaUI/FlaUI/releases>).
- Net effect for *this* app: FlaUI's value-add (Application/Window/retry/test tooling) is aimed at UI
  testing; Windows-Hinting only needs the interop layer, which FlaUI itself outsources to option 1.

## Facts for the decision

1. **The blocker is build-time only.** MSB4803 comes from `ResolveComReference`; the COM *runtime* path
   is fully supported on .NET 10. Every option that removes the `<COMReference>` restores
   `dotnet build`/`dotnet test` and lets CI drop `setup-msbuild`.
2. **A verified drop-in exists.** `Interop.UIAutomationClient` 10.19041.0 contains every interface,
   coclass, and method this codebase uses (checked against the shipped DLL). Migration is a csproj swap
   plus a namespace `using` change; RCW semantics, `Marshal.ReleaseComObject`, VARIANT-to-`double[]`
   marshaling, and `FindAllBuildCache` latency are unchanged. It is the same dependency FlaUI has shipped
   on for years. Risk: frozen single-maintainer package (mitigable by vendoring or one-time tlbimp).
3. **The CsWin32 ban's original reason is obsolete, but a new one exists.** Generation failures from 2022
   are long fixed; however the modern `GeneratedComInterface` mode produced 1163 compile errors for UIA in
   the current stable CsWin32 (0.3.298), and the fix (PR #1746, 2026-07-28) is unreleased as of this
   writing. Classic mode compiles but regresses ergonomics (raw VARIANT/SAFEARRAY, shadowed methods).
   CsWin32-UIA also forces rewriting all `Marshal.ReleaseComObject` lifetime code (`ComWrappers`).
   The copilot-instructions rule is worth updating to state the *current* reason rather than a blanket ban.
4. **WinRT is a dead end** for client-side scanning — no element acquisition, no search, no patterns.
5. **FlaUI works but is a superset**: it proves the option-1 package in production and adds a wrapper the
   app doesn't strictly need; its cache model differs semantically and its release cadence is slow.
6. **Scan latency is preserved by options 1 and 4** (identical `FindAllBuildCache` round trips); CsWin32
   is comparable once compilable; WinRT n/a.
7. **Leaning** (not a decision): option 1 is the only path that changes nothing at runtime while removing
   the full-framework MSBuild requirement; CsWin32's new mode is the one to re-evaluate after the next
   CsWin32 stable release ships PR #1746.

## Sources

- MSB4803 / ResolveComReference: <https://learn.microsoft.com/en-us/visualstudio/msbuild/errors/msb4803>, <https://learn.microsoft.com/en-us/visualstudio/msbuild/resolvecomreference-task>, <https://github.com/dotnet/msbuild/issues/3986>, <https://github.com/dotnet/runtime/issues/97125>
- COM interop on modern .NET: <https://learn.microsoft.com/en-us/dotnet/standard/native-interop/cominterop>
- Interop.UIAutomationClient: <https://www.nuget.org/packages/Interop.UIAutomationClient>, <https://www.nuget.org/packages/Interop.UIAutomationClient.Signed>, <https://github.com/Roemer/UIAutomation-Interop>, <https://learn.microsoft.com/en-gb/answers/questions/1184125/interop-uiautomationclient-private-internal-starti>
- tlbimp / dscom: <https://learn.microsoft.com/en-us/dotnet/framework/tools/tlbimp-exe-type-library-importer>, <https://learn.microsoft.com/en-us/dotnet/framework/interop/importing-a-type-library-as-an-assembly>, <https://github.com/dspace-group/dscom>
- CsWin32: <https://www.nuget.org/packages/Microsoft.Windows.CsWin32>, <https://github.com/microsoft/CsWin32/releases>, issues/PRs [#736](https://github.com/microsoft/CsWin32/issues/736)/[#737](https://github.com/microsoft/CsWin32/pull/737), [#1745](https://github.com/microsoft/CsWin32/issues/1745)/[#1746](https://github.com/microsoft/CsWin32/pull/1746), [#1552](https://github.com/microsoft/CsWin32/issues/1552), [#1391](https://github.com/microsoft/CsWin32/issues/1391)/[#1715](https://github.com/microsoft/CsWin32/pull/1715), [#1716](https://github.com/microsoft/CsWin32/issues/1716)/[#1717](https://github.com/microsoft/CsWin32/pull/1717), [#167](https://github.com/microsoft/CsWin32/issues/167)/[#1536](https://github.com/microsoft/CsWin32/pull/1536), [#1500](https://github.com/microsoft/CsWin32/issues/1500)/[#1502](https://github.com/microsoft/CsWin32/pull/1502), [#1168](https://github.com/microsoft/CsWin32/issues/1168), [#1273](https://github.com/microsoft/CsWin32/issues/1273)/[#1474](https://github.com/microsoft/CsWin32/pull/1474), [#831](https://github.com/microsoft/CsWin32/issues/831), [#708](https://github.com/microsoft/CsWin32/issues/708), [dotnet/runtime#115608](https://github.com/dotnet/runtime/issues/115608), [getting-started.md](https://github.com/microsoft/CsWin32/blob/main/docfx/docs/getting-started.md), [settings.schema.json](https://github.com/microsoft/CsWin32/blob/main/src/Microsoft.Windows.CsWin32/settings.schema.json)
- WinRT: <https://learn.microsoft.com/en-us/uwp/api/windows.ui.uiautomation>, <https://learn.microsoft.com/en-us/uwp/api/windows.ui.uiautomation.automationelement>, <https://learn.microsoft.com/en-us/uwp/api/windows.ui.uiautomation.core>, <https://learn.microsoft.com/en-us/uwp/api/windows.ui.uiautomation.core.coreautomationremoteoperation>, <https://github.com/microsoft/Microsoft-UI-UIAutomation>, <https://github.com/microsoft/xlang/issues/728>, <https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-enhance>, <https://www.nuget.org/packages/Microsoft.Windows.SDK.NET.Ref>
- FlaUI: <https://github.com/FlaUI/FlaUI>, <https://www.nuget.org/packages/FlaUI.UIA3>, [FlaUI.UIA3.csproj](https://github.com/FlaUI/FlaUI/blob/master/src/FlaUI.UIA3/FlaUI.UIA3.csproj), [CacheRequest.cs](https://github.com/FlaUI/FlaUI/blob/master/src/FlaUI.Core/CacheRequest.cs), [CacheRequestExtensions.cs](https://github.com/FlaUI/FlaUI/blob/master/src/FlaUI.UIA3/Extensions/CacheRequestExtensions.cs), [UIA3FrameworkAutomationElement.cs](https://github.com/FlaUI/FlaUI/blob/master/src/FlaUI.UIA3/UIA3FrameworkAutomationElement.cs), [UIA3Automation.cs](https://github.com/FlaUI/FlaUI/blob/master/src/FlaUI.UIA3/UIA3Automation.cs), [LegacyIAccessiblePattern.cs](https://github.com/FlaUI/FlaUI/blob/master/src/FlaUI.Core/Patterns/LegacyIAccessiblePattern.cs), [Caching wiki](https://github.com/FlaUI/FlaUI/wiki/Caching), [AutomationElementConverter.cs](https://github.com/FlaUI/FlaUI/blob/master/src/FlaUI.UIA3/Converters/AutomationElementConverter.cs), [CHANGELOG](https://github.com/FlaUI/FlaUI/blob/master/CHANGELOG.md), [releases](https://github.com/FlaUI/FlaUI/releases), [#668](https://github.com/FlaUI/FlaUI/issues/668), [#368](https://github.com/FlaUI/FlaUI/issues/368), [#616](https://github.com/FlaUI/FlaUI/issues/616)

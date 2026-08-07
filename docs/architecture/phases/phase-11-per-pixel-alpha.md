# Phase 11 — Per-pixel alpha presenter (optional)

**Depends on:** 06
**Optional:** the spec does not gate on this phase; if it stalls or is never picked up, the app
ships looking exactly like today.
**Decisions:** [#34](https://github.com/knausj85/Windows-Hinting/issues/34)

## Goal

Swap the blit presenter for an `UpdateLayeredWindow` presenter so hints render with real per-pixel
alpha (soft edges, antialiased text over any background) instead of `TransparencyKey` color-keying.

## Work

1. **ULW presenter** implementing the phase-06 presenter seam: 32-bit ARGB premultiplied bitmap
   from the composer → `UpdateLayeredWindow` (via CsWin32 if phase 10 has landed, else existing
   interop patterns). After one ULW call the DWM composes the window with no further paint
   messages — remove `OnPaint` blitting for this presenter.
2. **Composer emits real alpha:** antialiased text and rounded/soft hint backgrounds with actual
   transparency; drop the `TransparencyKey` color hack in ULW mode.
3. **Window styles:** keep `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE` and topmost
   behavior; verify click-through and UIA invisibility are unchanged.
4. **Escalation path on record:** if ULW text rendering quality disappoints (ClearType over
   per-pixel alpha is grayscale-only), DirectComposition is the documented escalation — see the
   overlay research (#29). Do not build it speculatively.

## Definition of done

- Smoke test sections **1, 2, 3** pass on mixed-DPI multi-monitor; hints show smooth antialiased
  edges with no color-key fringing.
- Click-through verified (clicks land on the app under the overlay); overlay still invisible to
  UIA (inspect with Accessibility Insights: zero overlay elements).
- Performance sanity: hint show/hide latency indistinguishable from the blit presenter.
- The blit presenter remains in-tree and selectable (one-line swap) as the fallback.

## Out of scope

- DirectComposition; animations; any new hint visuals beyond what alpha enables.

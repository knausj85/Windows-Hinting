# Phase 06 — Overlay composer/presenter split

**Depends on:** 03 (consumes the display model from `DesiredConditions`); parallel-safe with 04–05
**Decisions:** [#34](https://github.com/knausj85/Windows-Hinting/issues/34)

## Goal

Restructure the overlay into the composer/presenter shape inside a bounded `Overlay`
namespace/folder in the host — no new assembly, no visual change. This seam is what makes per-pixel
alpha (phase 11) a detachable swap instead of a rewrite.

## Work

1. **`Overlay` namespace.** Move `Forms/OverlayForm.cs` and `Forms/OverlayManager.cs` under
   `Overlay/`; the manager keeps one form per screen with PerMonitorV2 handling.
2. **Composer:** display model in → `Bitmap` out, pure GDI+ drawing (labels, prefix highlighting,
   opacity). It owns the offscreen bitmap that previously existed implicitly via
   `OptimizedDoubleBuffer` — dispose it when the overlay hides. The composer takes only the
   display-model slice of `DesiredConditions` plus screen geometry; it holds no reference back into
   the engine or funnel.
3. **Blit presenter:** `OnPaint` blits the composer's bitmap under today's window styles
   (`TransparencyKey`, `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_LAYERED` etc. unchanged).
   Presenter interface is the seam phase 11 swaps.
4. **Constraints enforced in code shape:** redraw only when the display model changes (no timers);
   the overlay handles no input (no key/mouse handlers on the forms); hints are pixels, never child
   controls.
5. **Tests:** composer unit tests in the host test tier are **not** possible (host isn't a test
   target) — instead, if the composer's drawing core is pure enough to live in Core (display model
   → drawing command list), put that part in Core with tests, and keep only the GDI+ rasterization
   in the host. Do this only if it falls out naturally; don't force it.

## Definition of done

- CI green.
- Smoke test sections **1, 2, 3** pass; visual output indistinguishable from before (same fonts,
  colors, positions, opacity behavior — compare screenshots on both monitors).
- `OverlayForm` contains no drawing logic beyond presenting the composer's bitmap; no input
  handlers on overlay forms.

## Out of scope

- `UpdateLayeredWindow`, per-pixel alpha, any visual change (phase 11).
- New display-model features (badges, action picker — future work rides the display model).

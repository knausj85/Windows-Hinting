using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using HintOverlay.Logging;
using HintOverlay.Models;
using UIAutomationClient;

namespace HintOverlay.Services
{
    /// <summary>
    /// Resolves overlapping hint labels by computing position-aware label bounds
    /// and nudging colliding labels to alternative positions before dropping them.
    /// </summary>
    internal static class ElementDeduplicator
    {
        /// <summary>
        /// Priority by control type — lower value = higher priority (more likely click target).
        /// </summary>
        private static readonly Dictionary<int, int> ControlTypePriority = new()
        {
            // Tier 1: Direct action elements
            [UIA_ControlTypeIds.UIA_ButtonControlTypeId] = 1,
            [UIA_ControlTypeIds.UIA_HyperlinkControlTypeId] = 1,
            [UIA_ControlTypeIds.UIA_MenuItemControlTypeId] = 1,
            [UIA_ControlTypeIds.UIA_SplitButtonControlTypeId] = 1,

            // Tier 2: State-change controls
            [UIA_ControlTypeIds.UIA_CheckBoxControlTypeId] = 2,
            [UIA_ControlTypeIds.UIA_RadioButtonControlTypeId] = 2,
            [UIA_ControlTypeIds.UIA_ComboBoxControlTypeId] = 2,
            [UIA_ControlTypeIds.UIA_TabItemControlTypeId] = 2,

            // Tier 3: Content interaction
            [UIA_ControlTypeIds.UIA_EditControlTypeId] = 3,
            [UIA_ControlTypeIds.UIA_ListItemControlTypeId] = 3,
            [UIA_ControlTypeIds.UIA_DataItemControlTypeId] = 3,
            [UIA_ControlTypeIds.UIA_TreeItemControlTypeId] = 3,

            // Tier 4: Containers (least likely direct target)
            [UIA_ControlTypeIds.UIA_ListControlTypeId] = 4,
            [UIA_ControlTypeIds.UIA_DataGridControlTypeId] = 4,
            [UIA_ControlTypeIds.UIA_TreeControlTypeId] = 4,
            [UIA_ControlTypeIds.UIA_GroupControlTypeId] = 4,
            [UIA_ControlTypeIds.UIA_MenuControlTypeId] = 4,
        };

        /// <summary>
        /// Positions to try when nudging a label away from a collision.
        /// The preferred position is tried first, then alternatives in a
        /// sensible order (corners → edges → center).
        /// </summary>
        private static readonly HintPosition[] NudgeOrder =
        {
            HintPosition.UpperLeft,
            HintPosition.UpperRight,
            HintPosition.LowerLeft,
            HintPosition.LowerRight,
            HintPosition.UpperCenter,
            HintPosition.LowerCenter,
            HintPosition.Left,
            HintPosition.Right,
            HintPosition.Center,
        };

        // Approximate label size used for overlap checks.
        // Matches the typical rendered size of a 1-2 character label in Segoe UI 9pt Bold.
        private const float LabelWidth = 30f;
        private const float LabelHeight = 18f;

        // Minimum element dimensions (pixels) — elements smaller than this
        // are untargetable or invisible (separators, collapsed controls, etc.)
        private const int MinElementWidth = 4;
        private const int MinElementHeight = 4;

        /// <summary>
        /// Assigns each element a non-overlapping label position, nudging
        /// collisions to alternative positions before dropping elements that
        /// cannot be placed without overlap.
        /// </summary>
        /// <param name="elements">Raw clickable elements from UI Automation.</param>
        /// <param name="preferredPosition">The user's preferred hint label position.</param>
        /// <param name="logger">Logger for diagnostics.</param>
        /// <returns>
        /// A list of <see cref="PlacedElement"/> entries, each carrying the
        /// original element and the resolved <see cref="HintPosition"/> for its label.
        /// </returns>
        public static List<PlacedElement> Deduplicate(
            IReadOnlyList<ClickableElement> elements,
            HintPosition preferredPosition,
            ILogger logger)
        {
            if (elements.Count == 0)
                return new List<PlacedElement>();

            // Filter out elements that are too small to be meaningful targets
            var viable = FilterByMinSize(elements, logger);

            if (viable.Count == 0)
                return new List<PlacedElement>();

            if (viable.Count == 1)
                return new List<PlacedElement> { new(viable[0], preferredPosition) };

            int viableCount = viable.Count;

            // Sort by priority (best first), then by area (smallest first as tiebreaker).
            // This ensures high-priority elements get their preferred position first.
            var sorted = viable
                .OrderBy(e => GetPriority(e))
                .ThenBy(e => (long)e.Bounds.Width * e.Bounds.Height)
                .ToList();

            var placed = new List<PlacedElement>(sorted.Count);
            var occupiedLabels = new List<RectangleF>(sorted.Count);

            // Build the nudge sequence: preferred position first, then the rest
            var nudgeSequence = BuildNudgeSequence(preferredPosition);

            foreach (var elem in sorted)
            {
                bool wasPlaced = false;

                foreach (var candidatePos in nudgeSequence)
                {
                    var labelBounds = ComputeLabelBounds(elem.Bounds, candidatePos);

                    if (!OverlapsAny(labelBounds, occupiedLabels))
                    {
                        placed.Add(new PlacedElement(elem, candidatePos));
                        occupiedLabels.Add(labelBounds);
                        wasPlaced = true;
                        break;
                    }
                }

                if (!wasPlaced)
                {
                    // All 9 positions overlap — drop this element
                    logger.Debug($"Dropped element at ({elem.Bounds.X},{elem.Bounds.Y} {elem.Bounds.Width}x{elem.Bounds.Height}) — all label positions overlap");
                }
            }

            logger.Debug($"Dedup: {elements.Count} raw → {viableCount} viable → {placed.Count} placed ({elements.Count - placed.Count} removed, preferred={preferredPosition})");
            return placed;
        }

        /// <summary>
        /// Removes elements whose bounds are too small to be meaningful
        /// interaction targets (e.g. separators, collapsed controls, zero-area items).
        /// </summary>
        private static List<ClickableElement> FilterByMinSize(IReadOnlyList<ClickableElement> elements, ILogger logger)
        {
            var result = new List<ClickableElement>(elements.Count);
            int filtered = 0;

            foreach (var e in elements)
            {
                if (e.Bounds.Width >= MinElementWidth && e.Bounds.Height >= MinElementHeight)
                {
                    result.Add(e);
                }
                else
                {
                    filtered++;
                }
            }

            if (filtered > 0)
                logger.Debug($"Size filter: removed {filtered} elements smaller than {MinElementWidth}x{MinElementHeight}px");

            return result;
        }

        /// <summary>
        /// Builds the nudge sequence with the preferred position first,
        /// followed by the remaining positions in the default nudge order.
        /// </summary>
        private static HintPosition[] BuildNudgeSequence(HintPosition preferred)
        {
            var sequence = new HintPosition[NudgeOrder.Length];
            sequence[0] = preferred;
            int idx = 1;
            foreach (var pos in NudgeOrder)
            {
                if (pos != preferred)
                    sequence[idx++] = pos;
            }
            return sequence;
        }

        /// <summary>
        /// Computes the label rectangle for a given element bounds and position,
        /// using the same positioning logic as <c>OverlayForm.OnPaint</c>.
        /// </summary>
        internal static RectangleF ComputeLabelBounds(Rectangle elementBounds, HintPosition position)
        {
            float bgWidth = LabelWidth;
            float bgHeight = LabelHeight;
            float bgX, bgY;

            switch (position)
            {
                case HintPosition.UpperLeft:
                    bgX = elementBounds.Left;
                    bgY = elementBounds.Top;
                    break;
                case HintPosition.UpperCenter:
                    bgX = elementBounds.Left + (elementBounds.Width - bgWidth) / 2;
                    bgY = elementBounds.Top;
                    break;
                case HintPosition.UpperRight:
                    bgX = elementBounds.Right - bgWidth;
                    bgY = elementBounds.Top;
                    break;
                case HintPosition.Left:
                    bgX = elementBounds.Left;
                    bgY = elementBounds.Top + (elementBounds.Height - bgHeight) / 2;
                    break;
                case HintPosition.Center:
                    bgX = elementBounds.Left + (elementBounds.Width - bgWidth) / 2;
                    bgY = elementBounds.Top + (elementBounds.Height - bgHeight) / 2;
                    break;
                case HintPosition.Right:
                    bgX = elementBounds.Right - bgWidth;
                    bgY = elementBounds.Top + (elementBounds.Height - bgHeight) / 2;
                    break;
                case HintPosition.LowerLeft:
                    bgX = elementBounds.Left;
                    bgY = elementBounds.Bottom - bgHeight;
                    break;
                case HintPosition.LowerCenter:
                    bgX = elementBounds.Left + (elementBounds.Width - bgWidth) / 2;
                    bgY = elementBounds.Bottom - bgHeight;
                    break;
                case HintPosition.LowerRight:
                    bgX = elementBounds.Right - bgWidth;
                    bgY = elementBounds.Bottom - bgHeight;
                    break;
                default:
                    bgX = elementBounds.Left;
                    bgY = elementBounds.Top;
                    break;
            }

            return new RectangleF(bgX, bgY, bgWidth, bgHeight);
        }

        private static bool OverlapsAny(RectangleF candidate, List<RectangleF> existing)
        {
            foreach (var r in existing)
            {
                if (candidate.IntersectsWith(r))
                    return true;
            }
            return false;
        }

        private static int GetPriority(ClickableElement element)
        {
            try
            {
                var controlType = element.Element.GetCachedPropertyValue(
                    UIA_PropertyIds.UIA_ControlTypePropertyId);
                if (controlType is int typeId && ControlTypePriority.TryGetValue(typeId, out int priority))
                    return priority;
            }
            catch
            {
                // COM call may fail — treat as lowest priority
            }

            return 5; // Unknown control type
        }
    }

    /// <summary>
    /// An element paired with its resolved label position after deduplication.
    /// </summary>
    internal sealed record PlacedElement(ClickableElement Element, HintPosition LabelPosition);
}

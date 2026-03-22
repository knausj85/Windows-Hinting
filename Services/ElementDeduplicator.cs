using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using HintOverlay.Logging;
using UIAutomationClient;

namespace HintOverlay.Services
{
    /// <summary>
    /// Removes overlapping and redundant clickable elements so that only
    /// the most likely interaction targets receive hint labels.
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
        /// Filters a list of clickable elements, removing duplicates caused by
        /// parent-child containment and spatial label overlap.
        /// </summary>
        /// <param name="overlapThresholdPercent">
        /// Area ratio threshold (0–100). When a smaller element's area covers at
        /// least this percentage of a larger containing element, the larger one is
        /// removed. Lower values are more aggressive.
        /// </param>
        public static List<ClickableElement> Deduplicate(
            IReadOnlyList<ClickableElement> elements,
            ILogger logger,
            int overlapThresholdPercent = 25)
        {
            if (elements.Count <= 1)
                return elements.ToList();

            int originalCount = elements.Count;
            double threshold = Math.Clamp(overlapThresholdPercent, 0, 100) / 100.0;

            // Phase 1: Remove elements whose bounds are nearly identical to
            // (or fully contain) another element — keep the smallest (most specific).
            var afterContainment = RemoveContainedDuplicates(elements, threshold);
            logger.Debug($"Dedup phase 1 (containment, threshold={overlapThresholdPercent}%): {originalCount} → {afterContainment.Count}");

            // Phase 2: Remove lower-priority elements whose bounds significantly
            // overlap a higher-priority element.
            var afterOverlap = RemoveOverlapping(afterContainment);
            logger.Debug($"Dedup phase 2 (overlap): {afterContainment.Count} → {afterOverlap.Count}");

            return afterOverlap;
        }

        /// <summary>
        /// When element A fully contains element B (or their bounds are nearly
        /// identical), discard the larger container element.
        /// </summary>
        private static List<ClickableElement> RemoveContainedDuplicates(IReadOnlyList<ClickableElement> elements, double areaRatioThreshold)
        {
            // Sort by area ascending so smaller (more specific) elements come first
            var sorted = elements.OrderBy(e => (long)e.Bounds.Width * e.Bounds.Height).ToList();
            var removed = new HashSet<int>();

            for (int i = 0; i < sorted.Count; i++)
            {
                if (removed.Contains(i))
                    continue;

                for (int j = i + 1; j < sorted.Count; j++)
                {
                    if (removed.Contains(j))
                        continue;

                    var smaller = sorted[i].Bounds;
                    var larger = sorted[j].Bounds;

                    // If the larger element fully contains the smaller one
                    // and they share a similar origin (within a small margin),
                    // the larger one is likely a container — remove it.
                    if (larger.Contains(smaller) && BoundsAreSimilar(smaller, larger, areaRatioThreshold))
                    {
                        removed.Add(j);
                    }
                }
            }

            return sorted.Where((_, idx) => !removed.Contains(idx)).ToList();
        }

        /// <summary>
        /// Two bounds are "similar" when they share a close origin and
        /// the smaller one covers a significant portion of the larger one.
        /// This catches parent-child pairs like a Button inside a ListItem.
        /// </summary>
        private static bool BoundsAreSimilar(Rectangle smaller, Rectangle larger, double areaRatioThreshold)
        {
            const int margin = 8; // pixels

            // Check if origins are close
            bool originsClose = Math.Abs(smaller.Left - larger.Left) <= margin
                             && Math.Abs(smaller.Top - larger.Top) <= margin;

            // Check if the smaller covers a significant portion of the larger
            long smallerArea = (long)smaller.Width * smaller.Height;
            long largerArea = (long)larger.Width * larger.Height;
            bool significantOverlap = largerArea > 0 && (double)smallerArea / largerArea > areaRatioThreshold;

            return originsClose || significantOverlap;
        }

        /// <summary>
        /// For elements that significantly overlap spatially, keep only the
        /// highest-priority element (by control type).
        /// </summary>
        private static List<ClickableElement> RemoveOverlapping(List<ClickableElement> elements)
        {
            // Sort by priority (best first), then by area (smallest first as tiebreaker)
            var sorted = elements
                .OrderBy(e => GetPriority(e))
                .ThenBy(e => (long)e.Bounds.Width * e.Bounds.Height)
                .ToList();

            var kept = new List<ClickableElement>();
            var keptLabelBounds = new List<RectangleF>();

            foreach (var elem in sorted)
            {
                // Approximate the label position (upper-left, matching OnPaint default)
                var labelBounds = new RectangleF(elem.Bounds.Left, elem.Bounds.Top, 30f, 18f);

                bool overlapsExisting = false;
                foreach (var existing in keptLabelBounds)
                {
                    if (labelBounds.IntersectsWith(existing))
                    {
                        overlapsExisting = true;
                        break;
                    }
                }

                if (!overlapsExisting)
                {
                    kept.Add(elem);
                    keptLabelBounds.Add(labelBounds);
                }
            }

            return kept;
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
}

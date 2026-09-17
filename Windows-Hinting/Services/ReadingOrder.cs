using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace WindowsHinting.Services
{
    /// <summary>
    /// Orders elements the way a person reads them: left-to-right, top-to-bottom.
    /// UIA returns elements in accessibility-tree / z-order, and multi-taskbar scans
    /// are appended one window after another — neither matches spatial position, so
    /// labels (A, B, C…) come out scrambled unless the list is sorted first.
    /// </summary>
    internal static class ReadingOrder
    {
        /// <summary>
        /// Sort into reading order using greedy row-banding. A pure "top then left"
        /// sort interleaves columns because items nominally on the same row differ by
        /// a few pixels of top jitter; banding groups items whose vertical centres are
        /// close into a row, sorts each row left-to-right, then stacks rows top-to-bottom.
        /// Degrades cleanly to top-to-bottom for a single-column (vertically-docked) taskbar.
        /// </summary>
        public static List<T> Sort<T>(IReadOnlyList<T> items, Func<T, Rectangle> bounds)
        {
            if (items.Count <= 1)
                return items.ToList();

            var ordered = items.OrderBy(i => bounds(i).Top).ToList();

            // Row tolerance = half the median element height (floored), so items whose
            // vertical centres fall within this of the band's anchor share a row.
            var heights = ordered
                .Select(i => bounds(i).Height)
                .Where(h => h > 0)
                .OrderBy(h => h)
                .ToList();
            int median = heights.Count > 0 ? heights[heights.Count / 2] : 0;
            int tolerance = Math.Max(median / 2, 8);

            var result = new List<T>(ordered.Count);
            int idx = 0;
            while (idx < ordered.Count)
            {
                // Anchor the band on its topmost (first) element and gather everything
                // whose centre is within tolerance of it.
                int anchorCentre = Centre(bounds(ordered[idx]));
                var band = new List<T> { ordered[idx] };
                idx++;
                while (idx < ordered.Count &&
                       Math.Abs(Centre(bounds(ordered[idx])) - anchorCentre) <= tolerance)
                {
                    band.Add(ordered[idx]);
                    idx++;
                }

                band.Sort((a, b) => bounds(a).Left.CompareTo(bounds(b).Left));
                result.AddRange(band);
            }

            return result;
        }

        private static int Centre(Rectangle r) => r.Top + r.Height / 2;
    }
}

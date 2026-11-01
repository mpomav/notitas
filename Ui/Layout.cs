using System;
using System.Collections.Generic;
using System.Linq;

namespace Notitas
{
    /// <summary>Placement rules for notes on screen. All numbers are physical pixels.</summary>
    public static class Layout
    {
        private const string Untagged = "￿";

        /// <summary>Pulls a dragged rectangle onto screen edges and neighbouring notes.</summary>
        public static void Snap(ref RECT rect, IEnumerable<RECT> others, RECT work, int reach, int gap)
        {
            var bestX = int.MaxValue;
            var bestY = int.MaxValue;

            void TryX(int edge, int target) { var d = target - edge; if (Math.Abs(d) <= reach && Math.Abs(d) < Math.Abs(bestX)) bestX = d; }
            void TryY(int edge, int target) { var d = target - edge; if (Math.Abs(d) <= reach && Math.Abs(d) < Math.Abs(bestY)) bestY = d; }

            TryX(rect.Left, work.Left + gap);
            TryX(rect.Right, work.Right - gap);
            TryY(rect.Top, work.Top + gap);
            TryY(rect.Bottom, work.Bottom - gap);

            foreach (var other in others)
            {
                if (rect.Top < other.Bottom + reach && other.Top < rect.Bottom + reach)
                {
                    TryX(rect.Left, other.Right + gap);
                    TryX(rect.Right, other.Left - gap);
                    TryX(rect.Left, other.Left);
                    TryX(rect.Right, other.Right);
                }
                if (rect.Left < other.Right + reach && other.Left < rect.Right + reach)
                {
                    TryY(rect.Top, other.Bottom + gap);
                    TryY(rect.Bottom, other.Top - gap);
                    TryY(rect.Top, other.Top);
                    TryY(rect.Bottom, other.Bottom);
                }
            }

            var dx = bestX == int.MaxValue ? 0 : bestX;
            var dy = bestY == int.MaxValue ? 0 : bestY;
            rect.Left += dx;
            rect.Right += dx;
            rect.Top += dy;
            rect.Bottom += dy;
        }

        /// <summary>Stacks notes in columns from the right edge, clear of the desktop icons.</summary>
        public static void Arrange(IList<NoteWindow> windows, bool byTag)
        {
            if (windows.Count == 0) return;
            var work = Native.WorkAreaAtCursor();
            var gap = windows[0].Px(10);

            var groups = byTag
                ? windows.GroupBy(w => w.Note.Tags.OrderBy(t => t).FirstOrDefault() ?? Untagged)
                         .OrderBy(g => g.Key, StringComparer.Ordinal)
                         .Select(g => g.OrderByDescending(w => w.Note.Updated).ToList())
                : Enumerable.Repeat(windows.OrderByDescending(w => w.Note.Updated).ToList(), 1);

            var right = work.Right - gap;
            var y = work.Top + gap;
            var columnWidth = 0;

            void NextColumn()
            {
                right -= columnWidth + gap;
                columnWidth = 0;
                y = work.Top + gap;
            }

            foreach (var group in groups)
            {
                if (y > work.Top + gap) NextColumn();
                foreach (var window in group)
                {
                    var size = window.Bounds;
                    if (y + size.Height > work.Bottom - gap && y > work.Top + gap) NextColumn();
                    if (right - size.Width < work.Left + gap)
                    {
                        // Out of columns: start over from the right, on top of the first ones.
                        right = work.Right - gap - window.Px(24);
                        columnWidth = 0;
                        y = work.Top + gap + window.Px(24);
                    }
                    window.MoveTo(right - size.Width, y);
                    y += size.Height + gap;
                    columnWidth = Math.Max(columnWidth, size.Width);
                }
            }
        }

        /// <summary>First spot not covered by a note, scanning columns from the top right.</summary>
        public static POINT FreeSpot(RECT size, IEnumerable<RECT> others, int gap)
        {
            var work = Native.WorkAreaAtCursor();
            var taken = others.ToList();
            for (var x = work.Right - gap - size.Width; x >= work.Left + gap; x -= size.Width + gap)
            {
                for (var y = work.Top + gap; y + size.Height <= work.Bottom - gap; y += gap * 3)
                {
                    var candidate = RECT.At(x, y, size.Width, size.Height);
                    if (!taken.Any(candidate.Intersects)) return new POINT { X = x, Y = y };
                }
            }
            return new POINT { X = work.Right - gap - size.Width, Y = work.Top + gap };
        }

        /// <summary>Brings a note back when the monitor it was on is gone.</summary>
        public static POINT KeepOnScreen(RECT rect)
        {
            if (Native.IsOnAnyMonitor(rect)) return new POINT { X = rect.Left, Y = rect.Top };
            var work = Native.WorkAreaOf(rect);
            return new POINT
            {
                X = Math.Max(work.Left, Math.Min(rect.Left, work.Right - rect.Width)),
                Y = Math.Max(work.Top, Math.Min(rect.Top, work.Bottom - rect.Height)),
            };
        }
    }
}

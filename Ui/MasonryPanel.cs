using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Notitas
{
    /// <summary>Keep-style layout: equal-width columns, each card goes under the shortest one.</summary>
    public sealed class MasonryPanel : Panel
    {
        private const double MinColumnWidth = 190;
        private const double Gap = 12;

        protected override Size MeasureOverride(Size available)
        {
            var width = AvailableWidth(available);
            var columnWidth = ColumnWidth(width);
            foreach (UIElement child in InternalChildren)
                child.Measure(new Size(columnWidth, double.PositiveInfinity));
            return new Size(width, Place(width, null));
        }

        protected override Size ArrangeOverride(Size final)
        {
            Place(final.Width, (child, rect) => child.Arrange(rect));
            return final;
        }

        private double Place(double width, Action<UIElement, Rect> arrange)
        {
            var columns = new double[ColumnCount(width)];
            var columnWidth = ColumnWidth(width);
            foreach (UIElement child in InternalChildren)
            {
                var index = Array.IndexOf(columns, columns.Min());
                var x = index * (columnWidth + Gap);
                arrange?.Invoke(child, new Rect(x, columns[index], columnWidth, child.DesiredSize.Height));
                columns[index] += child.DesiredSize.Height + Gap;
            }
            return Math.Max(0, columns.Max() - Gap);
        }

        private static double AvailableWidth(Size available) => double.IsInfinity(available.Width) ? 800 : available.Width;

        private static int ColumnCount(double width) => Math.Max(1, (int)((width + Gap) / (MinColumnWidth + Gap)));

        private static double ColumnWidth(double width)
        {
            var count = ColumnCount(width);
            return (width - Gap * (count - 1)) / count;
        }
    }
}

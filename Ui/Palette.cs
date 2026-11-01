using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media;

namespace Notitas
{
    public sealed class NoteColor
    {
        public NoteColor(string key, string name, string paper, string band)
        {
            Key = key;
            Name = name;
            Paper = Freeze(paper);
            Band = Freeze(band);
        }

        public string Key { get; }
        public string Name { get; }
        public SolidColorBrush Paper { get; }
        public SolidColorBrush Band { get; }

        private static SolidColorBrush Freeze(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }

    public static class Palette
    {
        public const string DefaultKey = "yellow";

        public static IReadOnlyList<NoteColor> All { get; } = new[]
        {
            new NoteColor("yellow", "Amarillo", "#FFF3B0", "#FBE68B"),
            new NoteColor("peach", "Durazno", "#FFE0C2", "#FCCFA3"),
            new NoteColor("pink", "Rosa", "#FFD9DE", "#FBC4CC"),
            new NoteColor("lilac", "Lila", "#E7DFFB", "#D8CDF6"),
            new NoteColor("sky", "Cielo", "#D5E9FA", "#BFDCF5"),
            new NoteColor("mint", "Menta", "#D3F0DC", "#BCE6C9"),
            new NoteColor("paper", "Papel", "#F1EDE4", "#E5DFD2"),
        };

        public static NoteColor Get(string key) => All.FirstOrDefault(c => c.Key == key) ?? All[0];
    }

    /// <summary>Note color key to its paper brush.</summary>
    public sealed class PaperBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            Palette.Get(value as string).Paper;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}

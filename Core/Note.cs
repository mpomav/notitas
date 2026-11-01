using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace Notitas
{
    /// <summary>A note: content lives in its .md file, placement in layout.json.</summary>
    public sealed class Note : INotifyPropertyChanged
    {
        private string _title = "";
        private string _body = "";
        private string _color = Palette.DefaultKey;
        private IReadOnlyList<string> _tags = new string[0];
        private bool _onDesktop;
        private bool _collapsed;
        private bool _topmost;
        private bool _trashed;
        private bool _loading;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Raised on any change that has to reach disk.</summary>
        public event Action<Note> Touched;

        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime Created { get; set; } = DateTime.Now;
        public DateTime Updated { get; set; } = DateTime.Now;
        public string FilePath { get; set; }
        public DateTime LastWriteUtc { get; set; }
        public bool Dirty { get; set; }

        /// <summary>Title as of the last read or write; the file is only renamed when this differs.</summary>
        public string SavedTitle { get; set; }

        /// <summary>Frontmatter lines the app doesn't own, kept verbatim on save.</summary>
        public List<string> ExtraFrontmatter { get; set; } = new List<string>();

        public double Width { get; set; } = 270;
        public double Height { get; set; } = 250;
        public int? X { get; set; }
        public int? Y { get; set; }

        public string Title
        {
            get => _title;
            set { if (SetContent(ref _title, value ?? "")) Notify(nameof(DisplayTitle)); }
        }

        public string Body
        {
            get => _body;
            set
            {
                if (!SetContent(ref _body, value ?? "")) return;
                Notify(nameof(DisplayTitle));
                Notify(nameof(Preview));
            }
        }

        public string Color
        {
            get => _color;
            set => SetContent(ref _color, Palette.Get(value).Key);
        }

        public IReadOnlyList<string> Tags
        {
            get => _tags;
            private set { _tags = value; Notify(); }
        }

        public bool OnDesktop { get => _onDesktop; set => SetLayout(ref _onDesktop, value); }
        public bool Collapsed { get => _collapsed; set => SetLayout(ref _collapsed, value); }
        public bool Topmost { get => _topmost; set => SetLayout(ref _topmost, value); }
        public bool Trashed { get => _trashed; set => SetLayout(ref _trashed, value); }

        public bool IsBlank => _title.Trim().Length == 0 && _body.Trim().Length == 0 && _tags.Count == 0;

        public string DisplayTitle
        {
            get
            {
                if (_title.Trim().Length > 0) return _title.Trim();
                var first = _body.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
                return first ?? "Nota vacía";
            }
        }

        public string Preview => _body.Length > 500 ? _body.Substring(0, 500).TrimEnd() + "…" : _body.TrimEnd();

        public bool AddTag(string raw)
        {
            var tag = NormalizeTag(raw);
            if (tag.Length == 0 || _tags.Contains(tag)) return false;
            Tags = _tags.Concat(new[] { tag }).ToArray();
            MarkContentChanged();
            return true;
        }

        public void RemoveTag(string tag)
        {
            if (!_tags.Contains(tag)) return;
            Tags = _tags.Where(t => t != tag).ToArray();
            MarkContentChanged();
        }

        /// <summary>Obsidian tags take no spaces and ignore case, so neither do ours.</summary>
        public static string NormalizeTag(string raw)
        {
            var text = (raw ?? "").Trim().TrimStart('#').Trim().ToLowerInvariant().Replace(' ', '-');
            var clean = new StringBuilder();
            foreach (var c in text)
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '/') clean.Append(c);
            return clean.ToString().Trim('/');
        }

        /// <summary>Applies values read from disk without marking the note dirty.</summary>
        public void ApplyLoaded(string title, string body, string color, IEnumerable<string> tags,
                                DateTime created, DateTime updated, List<string> extra)
        {
            _loading = true;
            Title = title;
            SavedTitle = title;
            Body = body;
            Color = color;
            Tags = tags.Select(NormalizeTag).Where(t => t.Length > 0).Distinct().ToArray();
            Created = created;
            Updated = updated;
            ExtraFrontmatter = extra;
            _loading = false;
        }

        public void TouchLayout() => Touched?.Invoke(this);

        private bool SetContent<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            Notify(name);
            MarkContentChanged();
            return true;
        }

        private void SetLayout<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            Notify(name);
            if (!_loading) Touched?.Invoke(this);
        }

        private void MarkContentChanged()
        {
            if (_loading) return;
            Dirty = true;
            Updated = DateTime.Now;
            Touched?.Invoke(this);
        }

        private void Notify([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

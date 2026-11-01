using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Threading;

namespace Notitas
{
    /// <summary>Owns the notes folder. All members run on the UI thread.</summary>
    public sealed class NoteStore
    {
        private const string TrashFolderName = ".papelera";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        private readonly DispatcherTimer _saveTimer;
        private readonly DispatcherTimer _scanTimer;
        private readonly Dictionary<string, NoteLayout> _layout = new Dictionary<string, NoteLayout>();
        private FileSystemWatcher _watcher;
        private bool _layoutDirty;
        private bool _changed;
        private bool _reportedFailure;

        public string Folder { get; private set; }
        public List<Note> Notes { get; } = new List<Note>();
        public List<Note> Trash { get; } = new List<Note>();

        public event Action Changed;
        public event Action<Note> NoteRemoved;
        public event Action<string> SaveFailed;

        private string TrashFolder => Path.Combine(Folder, TrashFolderName);

        public NoteStore()
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _saveTimer.Tick += (s, e) => Flush();
            _scanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _scanTimer.Tick += (s, e) => { _scanTimer.Stop(); Rescan(); };
            foreach (var entry in Json.ReadLayout()) _layout[entry.Id] = entry;
        }

        public void Open(string folder)
        {
            StopWatching();
            Notes.Clear();
            Trash.Clear();
            Folder = folder;
            Directory.CreateDirectory(Folder);
            Directory.CreateDirectory(TrashFolder);
            File.SetAttributes(TrashFolder, File.GetAttributes(TrashFolder) | FileAttributes.Hidden);

            var seen = new HashSet<string>();
            foreach (var path in Directory.GetFiles(Folder, "*.md"))
            {
                var note = TryRead(path);
                if (note == null) continue;
                if (!seen.Add(note.Id))
                {
                    // A file duplicated by hand carries its original's id.
                    note.Id = Guid.NewGuid().ToString("N");
                    note.Dirty = true;
                }
                Attach(note, Notes);
            }
            foreach (var path in Directory.GetFiles(TrashFolder, "*.md"))
            {
                var note = TryRead(path);
                if (note == null) continue;
                Attach(note, Trash);
                note.Trashed = true;
                note.OnDesktop = false;
            }
            StartWatching();
        }

        public Note Create(string color)
        {
            var note = new Note { OnDesktop = true };
            note.ApplyLoaded("", "", color, new string[0], DateTime.Now, DateTime.Now, new List<string>());
            Attach(note, Notes);
            return note;
        }

        public void MoveToTrash(Note note)
        {
            Notes.Remove(note);
            note.OnDesktop = false;
            if (note.FilePath == null) { Forget(note); return; }

            Save(note);
            var target = UniquePath(TrashFolder, Path.GetFileNameWithoutExtension(note.FilePath), null);
            File.Move(note.FilePath, target);
            note.FilePath = target;
            note.LastWriteUtc = File.GetLastWriteTimeUtc(target);
            note.Trashed = true;
            Trash.Add(note);
            MarkChanged();
        }

        public void Restore(Note note)
        {
            Trash.Remove(note);
            var target = UniquePath(Folder, FileBaseName(note), null);
            File.Move(note.FilePath, target);
            note.FilePath = target;
            note.LastWriteUtc = File.GetLastWriteTimeUtc(target);
            note.Trashed = false;
            Notes.Add(note);
            MarkChanged();
        }

        public void DeleteForever(Note note)
        {
            Trash.Remove(note);
            if (note.FilePath != null && File.Exists(note.FilePath)) File.Delete(note.FilePath);
            Forget(note);
        }

        /// <summary>Drops a note that never reached disk.</summary>
        public void Forget(Note note)
        {
            Notes.Remove(note);
            note.Touched -= OnTouched;
            _layout.Remove(note.Id);
            MarkChanged();
        }

        public IEnumerable<KeyValuePair<string, int>> TagCounts() =>
            Notes.SelectMany(n => n.Tags)
                 .GroupBy(t => t)
                 .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                 .Select(g => new KeyValuePair<string, int>(g.Key, g.Count()));

        /// <summary>Copies every note first so a failure part-way leaves the old folder intact.</summary>
        public void MoveAllTo(string folder)
        {
            Flush();
            Directory.CreateDirectory(folder);
            var newTrash = Path.Combine(folder, TrashFolderName);
            Directory.CreateDirectory(newTrash);

            var copied = new List<string>();
            try
            {
                foreach (var note in Notes.Where(n => n.FilePath != null).Concat(Trash))
                {
                    var targetFolder = note.Trashed ? newTrash : folder;
                    var target = UniquePath(targetFolder, Path.GetFileNameWithoutExtension(note.FilePath), null);
                    File.Copy(note.FilePath, target);
                    copied.Add(target);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                foreach (var path in copied) File.Delete(path);
                throw;
            }
            foreach (var note in Notes.Where(n => n.FilePath != null).Concat(Trash).ToList())
                File.Delete(note.FilePath);
        }

        public void Flush()
        {
            _saveTimer.Stop();
            foreach (var note in Notes.Where(n => n.Dirty && !(n.FilePath == null && n.IsBlank)).ToList())
            {
                try
                {
                    Save(note);
                    _reportedFailure = false;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    // Stays dirty so the next tick retries; the text is safe in memory.
                    _saveTimer.Start();
                    if (!_reportedFailure) SaveFailed?.Invoke(e.Message);
                    _reportedFailure = true;
                }
            }

            if (_layoutDirty)
            {
                try
                {
                    Json.Write(Paths.Layout, Notes.Concat(Trash).Select(ToLayout).ToList());
                    _layoutDirty = false;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    _saveTimer.Start();
                }
            }

            if (_changed)
            {
                _changed = false;
                Changed?.Invoke();
            }
        }

        private void Save(Note note)
        {
            var hadFile = note.FilePath != null && File.Exists(note.FilePath);
            if (hadFile && File.GetLastWriteTimeUtc(note.FilePath) != note.LastWriteUtc && note.LastWriteUtc != default(DateTime))
            {
                KeepConflictCopy(note);
            }

            // A file renamed in Obsidian keeps that name until the title changes here.
            var target = hadFile && note.Title == note.SavedTitle
                ? note.FilePath
                : UniquePath(note.Trashed ? TrashFolder : Folder, FileBaseName(note), note.FilePath);
            var temp = target + ".tmp";
            File.WriteAllText(temp, NoteFile.Serialize(note), Utf8);
            if (hadFile)
            {
                File.Replace(temp, note.FilePath, null);
                if (!string.Equals(note.FilePath, target, StringComparison.OrdinalIgnoreCase)) File.Move(note.FilePath, target);
            }
            else
            {
                File.Move(temp, target);
            }
            note.FilePath = target;
            note.LastWriteUtc = File.GetLastWriteTimeUtc(target);
            note.SavedTitle = note.Title;
            note.Dirty = false;
        }

        /// <summary>Edited both here and outside: the outside text survives as a note of its own.</summary>
        private void KeepConflictCopy(Note note)
        {
            var theirs = NoteFile.Parse(File.ReadAllText(note.FilePath, Encoding.UTF8), note.FilePath);
            theirs.Id = Guid.NewGuid().ToString("N");
            theirs.Title += " (conflicto)";
            File.WriteAllText(UniquePath(Folder, FileBaseName(theirs), null), NoteFile.Serialize(theirs), Utf8);
        }

        /// <summary>Picks up edits made in Obsidian or the file explorer.</summary>
        private void Rescan()
        {
            if (!Directory.Exists(Folder)) return;
            string[] files;
            try { files = Directory.GetFiles(Folder, "*.md"); }
            catch (IOException) { return; }

            var known = Notes.Where(n => n.FilePath != null)
                             .GroupBy(n => n.FilePath, StringComparer.OrdinalIgnoreCase)
                             .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var path in files)
            {
                if (known.TryGetValue(path, out var note))
                {
                    if (!note.Dirty && File.GetLastWriteTimeUtc(path) != note.LastWriteUtc) Reload(note, path);
                    continue;
                }

                var fresh = TryRead(path);
                if (fresh == null) continue;
                var sameId = Notes.FirstOrDefault(n => n.Id == fresh.Id);
                if (sameId != null && !File.Exists(sameId.FilePath))
                {
                    Reload(sameId, path);
                    continue;
                }
                if (sameId != null)
                {
                    // A copy of an existing note, not a rename.
                    fresh.Id = Guid.NewGuid().ToString("N");
                    fresh.Dirty = true;
                }
                Attach(fresh, Notes);
                MarkChanged();
            }

            var present = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            foreach (var note in Notes.Where(n => n.FilePath != null && !n.Dirty && !present.Contains(n.FilePath)).ToList())
            {
                Forget(note);
                NoteRemoved?.Invoke(note);
            }
            Flush();
        }

        private void Reload(Note note, string path)
        {
            var fresh = TryRead(path);
            if (fresh == null) return;
            note.ApplyLoaded(fresh.Title, fresh.Body, fresh.Color, fresh.Tags, fresh.Created, fresh.Updated, fresh.ExtraFrontmatter);
            note.FilePath = path;
            note.LastWriteUtc = fresh.LastWriteUtc;
            MarkChanged();
        }

        private static Note TryRead(string path)
        {
            try
            {
                var stamp = File.GetLastWriteTimeUtc(path);
                var note = NoteFile.Parse(File.ReadAllText(path, Encoding.UTF8), path);
                note.LastWriteUtc = stamp;
                return note;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private void Attach(Note note, List<Note> into)
        {
            if (_layout.TryGetValue(note.Id, out var entry))
            {
                note.OnDesktop = entry.OnDesktop;
                note.Collapsed = entry.Collapsed;
                note.Topmost = entry.Topmost;
                if (entry.Width > 0) note.Width = entry.Width;
                if (entry.Height > 0) note.Height = entry.Height;
                note.X = entry.X;
                note.Y = entry.Y;
            }
            into.Add(note);
            note.Touched += OnTouched;
        }

        private void OnTouched(Note note)
        {
            _layoutDirty = true;
            MarkChanged();
        }

        private void MarkChanged()
        {
            _changed = true;
            _layoutDirty = true;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private static NoteLayout ToLayout(Note note) => new NoteLayout
        {
            Id = note.Id,
            OnDesktop = note.OnDesktop,
            Collapsed = note.Collapsed,
            Topmost = note.Topmost,
            Width = note.Width,
            Height = note.Height,
            X = note.X,
            Y = note.Y,
        };

        private static string FileBaseName(Note note) => NoteFile.BaseName(note);

        private static string UniquePath(string folder, string baseName, string self)
        {
            for (var i = 1; ; i++)
            {
                var name = i == 1 ? baseName : baseName + " " + i;
                var path = Path.Combine(folder, name + ".md");
                if (string.Equals(path, self, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return path;
            }
        }

        private void StartWatching()
        {
            _watcher = new FileSystemWatcher(Folder, "*.md")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            FileSystemEventHandler onChange = (s, e) => QueueRescan();
            _watcher.Created += onChange;
            _watcher.Changed += onChange;
            _watcher.Deleted += onChange;
            _watcher.Renamed += (s, e) => QueueRescan();
        }

        private void QueueRescan() => _scanTimer.Dispatcher.BeginInvoke(new Action(() =>
        {
            _scanTimer.Stop();
            _scanTimer.Start();
        }));

        private void StopWatching()
        {
            _watcher?.Dispose();
            _watcher = null;
            _scanTimer.Stop();
        }
    }
}

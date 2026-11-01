using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Notitas.Tests
{
    /// <summary>
    /// Drives the real app in-process through WPF events, never through global keyboard or
    /// mouse input, so it can't type into whatever else is open on the machine.
    /// Run the exe from an empty folder; its data lives beside it. Stage "first" then "second".
    /// </summary>
    public static class Program
    {
        private static App _app;
        private static int _failed;
        private static string Shots => Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, ref RECT rect);

        [STAThread]
        public static int Main(string[] args)
        {
            _app = new App();
            _app.InitializeComponent();
            _app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
            {
                try
                {
                    await (args[0] == "first" ? First() : Second());
                }
                catch (Exception e)
                {
                    Check("scenario finished", e.ToString(), "no exception");
                }
                var error = Paths.ErrorLog;
                Check("no unhandled errors", File.Exists(error) ? File.ReadAllText(error) : "", "");
                typeof(App).GetMethod("Quit", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_app, null);
            }));
            _app.Run();
            Console.WriteLine(_failed == 0 ? "ALL PASSED" : _failed + " FAILED");
            return _failed;
        }

        private static async Task First()
        {
            // New notes
            var before = _app.Store.Notes.Count;
            Check("welcome note created", before, 1);
            _app.NewNote();
            var note = _app.Store.Notes.Last();
            var window = WindowOf(note);
            await Wait(100);

            // Tags
            var tagBox = Find<TextBox>(window, "TagBox");
            tagBox.Text = "#Casa Nueva";
            Press(tagBox, Key.Enter);
            tagBox.Text = "urgente";
            Press(tagBox, Key.OemComma);
            tagBox.Text = "urgente";
            Press(tagBox, Key.Enter);
            Check("tags added, normalized, no duplicates", string.Join(",", note.Tags), "casa-nueva,urgente");
            Check("chips rendered", Find<WrapPanel>(window, "TagsHost").Children.Count, 3);
            Press(tagBox, Key.Back);
            Check("backspace removes last tag", string.Join(",", note.Tags), "casa-nueva");

            _app.NewNote();
            var other = _app.Store.Notes.Last();
            var otherBox = Find<TextBox>(WindowOf(other), "TagBox");
            otherBox.Text = "cas";
            await Wait(50);
            var suggestions = Find<ListBox>(WindowOf(other), "TagSuggestions");
            Check("suggests existing tag", string.Join(",", suggestions.Items.Cast<string>()), "casa-nueva");
            Press(otherBox, Key.Down);
            Press(otherBox, Key.Enter);
            Check("picks suggestion", string.Join(",", other.Tags), "casa-nueva");

            // Content reaches disk through the debounce, under a name from the title
            Find<TextBox>(window, "TitleBox").Text = "Lista: súper?";
            Find<TextBox>(window, "BodyBox").Text = "Café\r\nLeche";
            await Wait(900);
            var path = Path.Combine(_app.Store.Folder, "Lista súper.md");
            Check("file named after title", File.Exists(path), true);
            Check("body saved", File.ReadAllText(path).EndsWith("---\nCafé\nLeche"), true);
            Check("tags saved", File.ReadAllText(path).Contains("tags:\n  - casa-nueva\n"), true);

            Find<TextBox>(window, "TitleBox").Text = "Compras";
            await Wait(900);
            Check("rename follows title", File.Exists(Path.Combine(_app.Store.Folder, "Compras.md")), true);
            Check("old name gone", File.Exists(path), false);

            // Color
            note.Color = "mint";
            Check("color applied", ((SolidColorBrush)window.Background).Color, Palette.Get("mint").Paper.Color);

            // Collapse
            var expanded = window.ActualHeight;
            note.Collapsed = true;
            await Wait(150);
            Check("collapses to one line", window.ActualHeight, 32d);
            Check("label shows title", Find<TextBlock>(window, "CollapsedLabel").Text, "Compras");
            note.Collapsed = false;
            await Wait(150);
            Check("expands back", window.ActualHeight, expanded);

            // Snap through the real message hook
            WindowOf(_app.Store.Notes.First()).MoveTo(600, 200);
            window.MoveTo(1000, 500);
            var dragged = window.Bounds;
            var near = RECT.At(600 + 270 + 6, 203, dragged.Width, dragged.Height);
            SendMessage(new WindowInteropHelper(window).Handle, Native.WM_MOVING, IntPtr.Zero, ref near);
            Check("WM_MOVING snaps beside a neighbour", near.Left, 600 + 270 + 10);
            Check("WM_MOVING aligns tops", near.Top, 200);

            // Arrange
            _app.Arrange(false);
            await Wait(50);
            var rects = _app.Store.Notes.Where(n => n.OnDesktop).Select(n => WindowOf(n).Bounds).ToList();
            var overlaps = rects.SelectMany((a, i) => rects.Skip(i + 1).Where(a.Intersects)).Count();
            Check("arranged notes don't overlap", overlaps, 0);
            _app.Arrange(true);
            await Wait(50);
            Check("tagged notes share a column", WindowOf(note).Bounds.Left, WindowOf(other).Bounds.Left);

            // Blank notes leave no trace
            _app.NewNote();
            var blank = _app.Store.Notes.Last();
            _app.HideNote(blank);
            await Wait(50);
            Check("blank note discarded", _app.Store.Notes.Contains(blank), false);

            // Clear and restore desktop
            var shown = _app.Store.Notes.Count(n => n.OnDesktop);
            _app.ToggleDesktop();
            await Wait(50);
            Check("desktop cleared", _app.Store.Notes.Count(n => n.OnDesktop), 0);
            Check("notes still exist", _app.Store.Notes.Count, 3);
            _app.ToggleDesktop();
            await Wait(50);
            Check("desktop restored", _app.Store.Notes.Count(n => n.OnDesktop), shown);

            // Trash
            _app.TrashNote(other);
            await Wait(50);
            Check("in trash", _app.Store.Trash.Contains(other), true);
            Check("file moved to trash", other.FilePath.Contains(".papelera") && File.Exists(other.FilePath), true);
            _app.RestoreNote(other);
            Check("restored", _app.Store.Notes.Contains(other) && !other.FilePath.Contains(".papelera"), true);

            // Edits from outside, as Obsidian would make them
            var compras = note.FilePath;
            File.WriteAllText(compras, File.ReadAllText(compras).Replace("Leche", "Leche editada en Obsidian"));
            await Wait(1600);
            Check("outside edit reaches open note", Find<TextBox>(window, "BodyBox").Text.Contains("editada en Obsidian"), true);
            Check("outside edit doesn't mark dirty", note.Dirty, false);

            File.WriteAllText(Path.Combine(_app.Store.Folder, "Desde Obsidian.md"), "---\ntags: [boveda]\n---\nCreada fuera");
            await Wait(1600);
            var foreign = _app.Store.Notes.FirstOrDefault(n => n.Title == "Desde Obsidian");
            Check("outside file appears", foreign != null && foreign.Tags.Contains("boveda"), true);
            Check("outside file not auto-placed on desktop", foreign?.OnDesktop, false);

            File.Delete(foreign.FilePath);
            await Wait(1600);
            Check("outside delete removes note", _app.Store.Notes.Contains(foreign), false);

            // Same note edited on both sides at once
            File.WriteAllText(compras, File.ReadAllText(compras) + "\nfuera");
            note.Body = "dentro";
            await Wait(1800);
            Check("conflict keeps our text", File.ReadAllText(compras).EndsWith("dentro"), true);
            var conflict = Directory.GetFiles(_app.Store.Folder, "*conflicto*.md");
            Check("conflict keeps their text", conflict.Length == 1 && File.ReadAllText(conflict[0]).Contains("fuera"), true);
            await Wait(1600);
            Check("note stays on its own file after conflict", note.FilePath, compras);
            Check("note keeps our text after rescan", note.Body, "dentro");
            Check("conflict is a separate note", _app.Store.Notes.Count(n => n.Title == "Compras (conflicto)"), 1);
            Check("only one Compras", _app.Store.Notes.Count(n => n.Title == "Compras"), 1);

            // Renamed in Obsidian: same note, new path
            var renamedPath = Path.Combine(_app.Store.Folder, "Renombrada.md");
            File.Move(compras, renamedPath);
            await Wait(1600);
            Check("rename outside follows the file", note.FilePath, renamedPath);
            Check("rename outside keeps the note open", _app.Store.Notes.Contains(note), true);
            note.Body = "dentro 2";
            await Wait(900);
            Check("editing here keeps the outside name", File.Exists(renamedPath), true);
            File.Move(renamedPath, compras);
            await Wait(1600);

            // Copied in the explorer: a second note
            var copied = Path.Combine(_app.Store.Folder, "Copia manual.md");
            File.Copy(compras, copied);
            await Wait(1600);
            Check("copy doesn't steal the note", note.FilePath, compras);
            Check("copy is a new note", _app.Store.Notes.Single(n => n.FilePath == copied).Id != note.Id, true);
            _app.Store.DeleteForever(_app.Store.Notes.Single(n => n.FilePath == copied));
            await Wait(1600);

            // Panel
            note.Topmost = true;
            note.Collapsed = true;
            _app.OpenPanel();
            var panel = _app.Windows.OfType<PanelWindow>().Single();
            await Wait(300);
            var cards = Find<ItemsControl>(panel, "Cards");
            var total = _app.Store.Notes.Count;
            Check("panel lists all notes", cards.Items.Count, total);
            Find<TextBox>(panel, "SearchBox").Text = "cafe";
            await Wait(50);
            var withCafe = _app.Store.Notes.Count(n => n.Body.ToLowerInvariant().Contains("café"));
            Check("search ignores accents", cards.Items.Count, withCafe);
            Find<TextBox>(panel, "SearchBox").Text = "";
            _app.OpenPanel("casa-nueva");
            await Wait(50);
            Check("tag filter", cards.Items.Cast<Note>().All(n => n.Tags.Contains("casa-nueva")) && cards.Items.Count > 0, true);
            Find<RadioButton>(panel, "NavAll").IsChecked = true;
            await Wait(200);
            var firstCard = (FrameworkElement)cards.ItemContainerGenerator.ContainerFromIndex(0);
            Check("cards start at the top", firstCard.TransformToAncestor(cards).Transform(new Point(0, 0)).Y, 0d);
            var settings = new SettingsWindow(_app);
            settings.Show();
            await Wait(100);
            Check("settings shows the notes folder", Find<TextBlock>(settings, "FolderLabel").Text, _app.Store.Folder);
            settings.Close();
            Shot(panel, "panel.png");
            Shot(WindowOf(_app.Store.Notes.First(n => n.OnDesktop && !n.Collapsed)), "note.png");
            panel.Hide();
        }

        /// <summary>Second launch: everything from the first run must come back.</summary>
        private static async Task Second()
        {
            await Wait(300);
            var note = _app.Store.Notes.Single(n => n.Title == "Compras");
            Check("reopens desktop notes", note.OnDesktop, true);
            Check("keeps collapsed", note.Collapsed, true);
            Check("keeps topmost", WindowOf(note).Topmost, true);
            Check("keeps color", note.Color, "mint");
            Check("keeps position", WindowOf(note).Bounds.Left, note.X);
            Check("no second welcome", _app.Store.Notes.Count(n => n.Title.StartsWith("Hola")), 1);
            Check("expanded height kept", note.Height > 100, true);

            // Pointing at another folder carries the notes along
            var vault = Path.Combine(Shots, "boveda", "Postits");
            var count = _app.Store.Notes.Count;
            _app.SwitchFolder(vault, true);
            await Wait(200);
            Check("notes moved to new folder", Directory.GetFiles(vault, "*.md").Length, count);
            Check("old folder emptied", Directory.GetFiles(Paths.DefaultNotes, "*.md").Length, 0);
            Check("notes loaded from new folder", _app.Store.Notes.Count, count);
            Check("desktop notes reopened", WindowOf(_app.Store.Notes.Single(n => n.Title == "Compras")).IsVisible, true);
            _app.SwitchFolder(null, true);
            await Wait(200);
            Check("moved back", Directory.GetFiles(Paths.DefaultNotes, "*.md").Length, count);
        }

        private static NoteWindow WindowOf(Note note) =>
            _app.Windows.OfType<NoteWindow>().Single(w => w.Note == note);

        private static T Find<T>(Window window, string name) where T : class => (T)window.FindName(name);

        private static void Press(UIElement target, Key key)
        {
            var source = PresentationSource.FromVisual(target);
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        }

        private static Task Wait(int ms) => Task.Delay(ms);

        private static void Shot(Window window, string name)
        {
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(window.Background, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
                context.DrawRectangle(new VisualBrush(window), null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
            }
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(Shots, name))) encoder.Save(file);
        }

        private static void Check(string name, object actual, object expected)
        {
            if (Equals(actual, expected) || string.Equals(actual?.ToString(), expected?.ToString()))
            {
                Console.WriteLine("ok   " + name);
                return;
            }
            Console.WriteLine("FAIL " + name + " -> got: " + actual + " | want: " + expected);
            _failed++;
        }
    }
}

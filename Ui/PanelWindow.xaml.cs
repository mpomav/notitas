using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Notitas
{
    public partial class PanelWindow : Window
    {
        private const int ConfettiPieces = 70;

        private readonly App _app;
        private readonly KonamiCode _konami = new KonamiCode();
        private readonly Random _random = new Random();
        private string _tag;

        public PanelWindow(App app)
        {
            _app = app;
            InitializeComponent();
        }

        public void Open(string tag)
        {
            if (tag != null) SelectTag(tag);
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Refresh();
            SearchBox.Focus();
        }

        public void Refresh()
        {
            if (!IsVisible) return;
            var store = _app.Store;

            var visible = store.Notes.Where(n => !n.IsBlank).ToList();
            var onDesktop = visible.Count(n => n.OnDesktop);
            AllLabel.Text = "Todas · " + visible.Count;
            DesktopLabel.Text = "En el escritorio · " + onDesktop;
            StoredLabel.Text = "Guardadas · " + (visible.Count - onDesktop);
            TrashLabel.Text = "Papelera · " + store.Trash.Count;
            ClearButton.Content = onDesktop == 0 && _app.CanRestoreDesktop ? "Devolver al escritorio" : "Despejar escritorio";

            RenderTags();

            var query = Fold(SearchBox.Text.Trim());
            var trash = NavTrash.IsChecked == true;
            var source = trash ? store.Trash : visible;
            var shown = source
                .Where(n => _tag == null || n.Tags.Contains(_tag))
                .Where(n => NavDesktop.IsChecked != true || n.OnDesktop)
                .Where(n => NavStored.IsChecked != true || !n.OnDesktop)
                .Where(n => query.Length == 0 || Matches(n, query))
                .OrderByDescending(n => n.Updated)
                .ToList();

            Cards.ItemsSource = shown;
            EmptyLabel.Text = EasterEggs.SearchReply(query) ?? EmptyMessage(trash, query.Length > 0 || _tag != null);
            EmptyLabel.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // The panel lives in the tray: closing just tucks it away.
            if (_app.IsExiting) return;
            e.Cancel = true;
            Hide();
        }

        private static string EmptyMessage(bool trash, bool filtering)
        {
            if (filtering) return "Nada coincide con tu búsqueda";
            return trash ? "La papelera está vacía" : "Aún no hay notas. Crea una con Ctrl+Alt+N";
        }

        private static bool Matches(Note note, string query) =>
            Fold(note.Title).Contains(query)
            || Fold(note.Body).Contains(query)
            || note.Tags.Any(t => Fold(t).Contains(query));

        /// <summary>Lower-cases and drops accents, so "cafe" finds "café".</summary>
        private static string Fold(string text)
        {
            var decomposed = text.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
            return new string(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        }

        private void RenderTags()
        {
            TagsList.Children.Clear();
            foreach (var entry in _app.Store.TagCounts())
            {
                var tag = entry.Key;
                var item = new RadioButton
                {
                    Style = (Style)FindResource("NavItem"),
                    Content = new TextBlock { Text = "#" + tag + " · " + entry.Value, TextTrimming = TextTrimming.CharacterEllipsis },
                    IsChecked = tag == _tag,
                };
                item.Checked += (s, e) => SelectTag(tag);
                TagsList.Children.Add(item);
            }
        }

        private void SelectTag(string tag)
        {
            _tag = tag;
            NavAll.IsChecked = false;
            NavDesktop.IsChecked = false;
            NavStored.IsChecked = false;
            NavTrash.IsChecked = false;
            Refresh();
        }

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            _tag = null;
            if (IsLoaded) Refresh();
        }

        private void Search_Changed(object sender, TextChangedEventArgs e)
        {
            if (IsLoaded) Refresh();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var finished = _konami.Feed(e.Key);
            if (finished) ThrowConfetti();
            if (finished || _konami.HoldingLetters) e.Handled = true;

            if (e.Key != Key.Escape) return;
            if (SearchBox.Text.Length > 0) SearchBox.Clear();
            else Hide();
            e.Handled = true;
        }

        private void ThrowConfetti()
        {
            for (var i = 0; i < ConfettiPieces; i++)
            {
                var spin = new RotateTransform(_random.Next(360));
                var piece = new Rectangle
                {
                    Width = 13,
                    Height = 13,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = Palette.All[_random.Next(Palette.All.Count)].Band,
                    RenderTransform = spin,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                };
                Canvas.SetLeft(piece, _random.NextDouble() * ActualWidth);
                Confetti.Children.Add(piece);

                var seconds = 1.6 + _random.NextDouble() * 1.4;
                var fall = new DoubleAnimation(-20, ActualHeight + 20, TimeSpan.FromSeconds(seconds))
                {
                    BeginTime = TimeSpan.FromSeconds(_random.NextDouble() * 0.8),
                };
                fall.Completed += (s, e) => Confetti.Children.Remove(piece);
                Canvas.SetTop(piece, -20);
                piece.BeginAnimation(Canvas.TopProperty, fall);
                spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(spin.Angle, spin.Angle + 540, TimeSpan.FromSeconds(seconds)));
            }
        }

        private static Note NoteOf(object sender) => (Note)((FrameworkElement)sender).DataContext;

        private void New_Click(object sender, RoutedEventArgs e) => _app.NewNote();
        private void Arrange_Click(object sender, RoutedEventArgs e) => _app.Arrange(false);
        private void ArrangeByTag_Click(object sender, RoutedEventArgs e) => _app.Arrange(true);
        private void Clear_Click(object sender, RoutedEventArgs e) => _app.ToggleDesktop();
        private void Settings_Click(object sender, RoutedEventArgs e) => _app.OpenSettings();

        private void Card_Click(object sender, MouseButtonEventArgs e)
        {
            var note = NoteOf(sender);
            if (!note.Trashed) _app.ShowNote(note, true);
        }

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            var note = NoteOf(sender);
            if (note.OnDesktop) _app.HideNote(note);
            else _app.ShowNote(note, false);
            e.Handled = true;
        }

        private void Trash_Click(object sender, RoutedEventArgs e)
        {
            _app.TrashNote(NoteOf(sender));
            e.Handled = true;
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            _app.RestoreNote(NoteOf(sender));
            e.Handled = true;
        }

        private void DeleteForever_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var note = NoteOf(sender);
            var answer = MessageBox.Show(this, "¿Eliminar «" + note.DisplayTitle + "» para siempre? Esto no se puede deshacer.",
                                         "Notitas", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer == MessageBoxResult.Yes) _app.DeleteForever(note);
        }
    }
}

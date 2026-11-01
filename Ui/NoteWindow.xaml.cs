using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Notitas
{
    public partial class NoteWindow : Window
    {
        private const double HeaderHeight = 32;
        private const double MinExpandedHeight = 130;
        private const string PinOff = "";
        private const string PinOn = "";

        private readonly App _app;
        private IntPtr _hwnd;

        public Note Note { get; }

        public NoteWindow(App app, Note note)
        {
            _app = app;
            Note = note;
            DataContext = note;
            InitializeComponent();

            Swatches.ItemsSource = Palette.All;
            Width = note.Width;
            Topmost = note.Topmost;
            ApplyColor();
            ApplyPin();
            ApplyCollapsed();
            RenderTags();
            note.PropertyChanged += OnNoteChanged;
        }

        public RECT Bounds
        {
            get { Native.GetWindowRect(_hwnd, out var rect); return rect; }
        }

        public int Px(double dips) => (int)Math.Round(dips * VisualTreeHelper.GetDpi(this).DpiScaleX);

        public void MoveTo(int x, int y)
        {
            Native.Move(_hwnd, x, y);
            CaptureGeometry();
        }

        public void RaiseToFront()
        {
            Topmost = true;
            Topmost = Note.Topmost;
        }

        public void FocusForTyping()
        {
            Show();
            Activate();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Note.Collapsed) return;
                BodyBox.Focus();
                BodyBox.CaretIndex = BodyBox.Text.Length;
            }), DispatcherPriority.Input);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(_hwnd).AddHook(WndProc);

            // No maximize box means dragging to the screen top can't blow a note up full screen.
            Native.ChangeStyle(_hwnd, Native.GWL_STYLE, 0, Native.WS_MAXIMIZEBOX | Native.WS_MINIMIZEBOX);
            Native.ChangeStyle(_hwnd, Native.GWL_EXSTYLE, Native.WS_EX_TOOLWINDOW, 0);
            Native.RoundCorners(_hwnd);

            var size = Bounds;
            POINT place;
            if (Note.X.HasValue && Note.Y.HasValue)
                place = Layout.KeepOnScreen(RECT.At(Note.X.Value, Note.Y.Value, size.Width, size.Height));
            else
                place = Layout.FreeSpot(size, _app.OtherNoteBounds(this), Px(10));
            MoveTo(place.X, place.Y);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            CaptureGeometry();
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            Note.PropertyChanged -= OnNoteChanged;
            base.OnClosed(e);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_MOVING)
            {
                // Holding Alt moves the note freely.
                if ((Native.GetAsyncKeyState(Native.VK_MENU) & 0x8000) != 0) return IntPtr.Zero;
                var rect = System.Runtime.InteropServices.Marshal.PtrToStructure<RECT>(lParam);
                Layout.Snap(ref rect, _app.OtherNoteBounds(this), Native.WorkAreaOf(rect), Px(14), Px(10));
                System.Runtime.InteropServices.Marshal.StructureToPtr(rect, lParam, false);
                handled = true;
                return (IntPtr)1;
            }
            if (msg == Native.WM_EXITSIZEMOVE) CaptureGeometry();
            return IntPtr.Zero;
        }

        private void CaptureGeometry()
        {
            if (_hwnd == IntPtr.Zero) return;
            var rect = Bounds;
            Note.X = rect.Left;
            Note.Y = rect.Top;
            Note.Width = ActualWidth > 0 ? ActualWidth : Note.Width;
            if (!Note.Collapsed && ActualHeight > 0) Note.Height = ActualHeight;
            Note.TouchLayout();
        }

        private void OnNoteChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(Note.Color): ApplyColor(); break;
                case nameof(Note.Tags): RenderTags(); break;
                case nameof(Note.Collapsed): ApplyCollapsed(); break;
                case nameof(Note.Topmost): Topmost = Note.Topmost; ApplyPin(); break;
            }
        }

        private void ApplyColor()
        {
            var color = Palette.Get(Note.Color);
            Background = color.Paper;
            Header.Background = color.Band;
        }

        private void ApplyPin()
        {
            PinButton.Content = Note.Topmost ? PinOn : PinOff;
            PinButton.ToolTip = Note.Topmost ? "Fijada encima de todo (clic para soltar)" : "Fijar encima de las demás ventanas";
        }

        private void ApplyCollapsed()
        {
            var collapsed = Note.Collapsed;
            Editor.Visibility = TagsHost.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            CollapsedLabel.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
            if (collapsed)
            {
                MinHeight = MaxHeight = Height = HeaderHeight;
            }
            else
            {
                MaxHeight = double.PositiveInfinity;
                MinHeight = MinExpandedHeight;
                Height = Math.Max(Note.Height, MinExpandedHeight);
            }
        }

        private void RenderTags()
        {
            while (TagsHost.Children.Count > 1) TagsHost.Children.RemoveAt(0);
            foreach (var tag in Note.Tags) TagsHost.Children.Insert(TagsHost.Children.Count - 1, MakeChip(tag));
        }

        private UIElement MakeChip(string tag)
        {
            var label = new TextBlock
            {
                Text = "#" + tag,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = "Ver notas con este tag",
            };
            label.MouseLeftButtonUp += (s, e) => _app.OpenPanel(tag);

            var remove = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Content = "",
                FontSize = 7,
                Width = 15,
                Height = 15,
                Margin = new Thickness(3, 0, -3, 0),
                ToolTip = "Quitar tag",
                Opacity = 0.55,
            };
            remove.Click += (s, e) => Note.RemoveTag(tag);

            return new Border
            {
                Background = (Brush)FindResource("Chip"),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 1, 6, 2),
                Margin = new Thickness(0, 2, 4, 2),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { label, remove } },
            };
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                Note.Collapsed = !Note.Collapsed;
                e.Handled = true;
                return;
            }
            DragMove();
        }

        private void New_Click(object sender, RoutedEventArgs e) => _app.NewNote();
        private void Stash_Click(object sender, RoutedEventArgs e) => Close();
        private void More_Click(object sender, RoutedEventArgs e) => MorePopup.IsOpen = true;
        private void Pin_Click(object sender, RoutedEventArgs e) => Note.Topmost = !Note.Topmost;

        private void Collapse_Click(object sender, RoutedEventArgs e)
        {
            MorePopup.IsOpen = false;
            Note.Collapsed = !Note.Collapsed;
        }

        private void Panel_Click(object sender, RoutedEventArgs e)
        {
            MorePopup.IsOpen = false;
            _app.OpenPanel();
        }

        private void Trash_Click(object sender, RoutedEventArgs e)
        {
            MorePopup.IsOpen = false;
            _app.TrashNote(Note);
        }

        private void Swatch_Click(object sender, RoutedEventArgs e)
        {
            Note.Color = ((NoteColor)((FrameworkElement)sender).DataContext).Key;
            MorePopup.IsOpen = false;
        }

        private void TitleBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            BodyBox.Focus();
            e.Handled = true;
        }

        private void TagBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var open = TagPopup.IsOpen && TagSuggestions.Items.Count > 0;
            switch (e.Key)
            {
                case Key.Enter:
                case Key.Tab when TagBox.Text.Length > 0:
                case Key.OemComma:
                case Key.Space:
                    CommitTag(TagSuggestions.SelectedItem as string ?? TagBox.Text);
                    e.Handled = true;
                    break;
                case Key.Down when open:
                    TagSuggestions.SelectedIndex = Math.Min(TagSuggestions.SelectedIndex + 1, TagSuggestions.Items.Count - 1);
                    e.Handled = true;
                    break;
                case Key.Up when open:
                    TagSuggestions.SelectedIndex = Math.Max(TagSuggestions.SelectedIndex - 1, -1);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    TagPopup.IsOpen = false;
                    e.Handled = true;
                    break;
                case Key.Back when TagBox.Text.Length == 0 && Note.Tags.Count > 0:
                    Note.RemoveTag(Note.Tags.Last());
                    e.Handled = true;
                    break;
            }
        }

        private void TagBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var typed = Note.NormalizeTag(TagBox.Text);
            var matches = typed.Length == 0
                ? new string[0]
                : _app.Store.TagCounts()
                      .Select(t => t.Key)
                      .Where(t => t.Contains(typed) && !Note.Tags.Contains(t))
                      .Take(6)
                      .ToArray();
            TagSuggestions.ItemsSource = matches;
            TagPopup.IsOpen = matches.Length > 0;
        }

        private void TagBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (TagBox.Text.Trim().Length > 0) CommitTag(TagBox.Text);
            TagPopup.IsOpen = false;
        }

        private void TagSuggestions_Click(object sender, MouseButtonEventArgs e)
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext is string tag) CommitTag(tag);
        }

        private void CommitTag(string text)
        {
            Note.AddTag(text);
            TagBox.Clear();
            TagPopup.IsOpen = false;
        }
    }
}

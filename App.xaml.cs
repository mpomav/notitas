using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Notitas
{
    /// <summary>Runs from the tray and coordinates the note windows, the panel and the store.</summary>
    public partial class App : Application
    {
        private const string InstanceName = @"Local\Notitas.Instance";
        private const string WakeName = @"Local\Notitas.Wake";
        private const string Welcome =
            "Arrastra esta franja para moverme.\n" +
            "Doble clic: me pliego a una línea.\n" +
            "X: me guardo en el panel.\n\n" +
            "Ctrl+Alt+N: nueva nota\n" +
            "Ctrl+Alt+K: panel y tags";

        private readonly Dictionary<Note, NoteWindow> _windows = new Dictionary<Note, NoteWindow>();
        private readonly List<Note> _cleared = new List<Note>();
        private readonly Dictionary<int, Action> _hotkeys = new Dictionary<int, Action>();
        private Mutex _instance;
        private EventWaitHandle _wake;
        private Forms.NotifyIcon _tray;
        private HwndSource _hotkeyHost;
        private PanelWindow _panel;

        public Config Config { get; private set; }
        public NoteStore Store { get; private set; }
        public bool IsExiting { get; private set; }
        public bool CanRestoreDesktop => _cleared.Count > 0;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _instance = new Mutex(true, InstanceName, out var first);
            _wake = new EventWaitHandle(false, EventResetMode.AutoReset, WakeName);
            if (!first)
            {
                // Opening the exe again just brings up the running copy.
                _wake.Set();
                Shutdown();
                return;
            }

            DispatcherUnhandledException += OnUnhandled;
            new Thread(WaitForWake) { IsBackground = true }.Start();

            Config = Config.Load();
            Store = new NoteStore();
            Store.Changed += () => _panel?.Refresh();
            Store.NoteRemoved += OnNoteRemoved;
            Store.SaveFailed += message => _tray?.ShowBalloonTip(5000, "Notitas no pudo guardar",
                "Se reintentará solo. " + message, Forms.ToolTipIcon.Warning);
            Store.Open(Config.EffectiveNotesFolder);

            _panel = new PanelWindow(this);
            CreateTray();
            RegisterHotkeys();

            foreach (var note in Store.Notes.Where(n => n.OnDesktop).ToList()) ShowNote(note, false);
            ShowWelcomeOnce();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            _hotkeyHost?.Dispose();
            base.OnExit(e);
        }

        public void NewNote()
        {
            var note = Store.Create(Config.DefaultColor);
            ShowNote(note, true);
        }

        public void ShowNote(Note note, bool focus)
        {
            note.OnDesktop = true;
            if (!_windows.TryGetValue(note, out var window))
            {
                window = new NoteWindow(this, note);
                window.Closed += (s, e) => OnWindowClosed(note);
                _windows[note] = window;
                window.Show();
            }
            if (focus)
            {
                window.RaiseToFront();
                window.FocusForTyping();
            }
            _cleared.Clear();
        }

        public void HideNote(Note note)
        {
            if (_windows.TryGetValue(note, out var window)) window.Close();
        }

        public void TrashNote(Note note)
        {
            HideNote(note);
            if (Store.Notes.Contains(note)) Store.MoveToTrash(note);
            _cleared.Remove(note);
        }

        public void RestoreNote(Note note) => Store.Restore(note);

        public void DeleteForever(Note note) => Store.DeleteForever(note);

        public void OpenPanel(string tag = null) => _panel.Open(tag);

        public void OpenSettings()
        {
            var dialog = new SettingsWindow(this);
            if (_panel.IsVisible) dialog.Owner = _panel;
            else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.ShowDialog();
        }

        public void Arrange(bool byTag) => Layout.Arrange(_windows.Values.ToList(), byTag);

        public void ToggleDesktop()
        {
            if (_windows.Count > 0)
            {
                var notes = _windows.Keys.ToList();
                foreach (var window in _windows.Values.ToList()) window.Close();
                _cleared.Clear();
                _cleared.AddRange(notes.Where(Store.Notes.Contains));
            }
            else
            {
                foreach (var note in _cleared.ToList()) ShowNote(note, false);
            }
            _panel.Refresh();
        }

        public void ToggleCollapseAll()
        {
            var collapse = _windows.Keys.Any(n => !n.Collapsed);
            foreach (var note in _windows.Keys) note.Collapsed = collapse;
        }

        public void BringAllToFront()
        {
            foreach (var window in _windows.Values) window.RaiseToFront();
        }

        public void SwitchFolder(string folder, bool move)
        {
            var target = folder ?? Paths.DefaultNotes;
            Store.Flush();
            if (move) Store.MoveAllTo(target);

            IsExiting = true;   // Closing windows here must not mark notes as stashed.
            foreach (var window in _windows.Values.ToList()) window.Close();
            IsExiting = false;

            Config.NotesFolder = folder;
            Config.Save();
            Store.Open(target);
            foreach (var note in Store.Notes.Where(n => n.OnDesktop).ToList()) ShowNote(note, false);
            _panel.Refresh();
        }

        public IEnumerable<RECT> OtherNoteBounds(NoteWindow self) =>
            _windows.Values.Where(w => w != self && w.IsLoaded).Select(w => w.Bounds);

        private void OnWindowClosed(Note note)
        {
            _windows.Remove(note);
            if (IsExiting || !Store.Notes.Contains(note)) return;

            note.OnDesktop = false;
            if (note.IsBlank)
            {
                if (note.FilePath == null) Store.Forget(note);
                else Store.MoveToTrash(note);
            }
            _panel.Refresh();
        }

        private void OnNoteRemoved(Note note)
        {
            _cleared.Remove(note);
            HideNote(note);
        }

        private void ShowWelcomeOnce()
        {
            if (Config.WelcomeShown) return;
            Config.WelcomeShown = true;
            Config.Save();
            if (Store.Notes.Count > 0) return;

            var note = Store.Create(Config.DefaultColor);
            note.Title = "Hola, soy una notita";
            note.Body = Welcome;
            note.AddTag("notitas");
            ShowNote(note, false);
        }

        private void CreateTray()
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Nueva nota\tCtrl+Alt+N", null, (s, e) => NewNote());
            menu.Items.Add("Abrir panel\tCtrl+Alt+K", null, (s, e) => OpenPanel());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Traer notas al frente\tCtrl+Alt+F", null, (s, e) => BringAllToFront());
            menu.Items.Add("Ordenar en columnas", null, (s, e) => Arrange(false));
            menu.Items.Add("Ordenar por tag", null, (s, e) => Arrange(true));
            menu.Items.Add("Plegar o desplegar todas", null, (s, e) => ToggleCollapseAll());
            menu.Items.Add("Despejar o devolver notas\tCtrl+Alt+D", null, (s, e) => ToggleDesktop());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Configuración", null, (s, e) => OpenSettings());
            menu.Items.Add("Salir", null, (s, e) => Quit());

            var stream = GetResourceStream(new Uri("pack://application:,,,/Notitas;component/Assets/notitas.ico")).Stream;
            _tray = new Forms.NotifyIcon
            {
                Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize),
                Text = "Notitas",
                ContextMenuStrip = menu,
                Visible = true,
            };
            _tray.MouseClick += (s, e) => { if (e.Button == Forms.MouseButtons.Left) OpenPanel(); };
        }

        private void RegisterHotkeys()
        {
            var parameters = new HwndSourceParameters("NotitasHotkeys") { ParentWindow = new IntPtr(-3) };
            _hotkeyHost = new HwndSource(parameters);
            _hotkeyHost.AddHook(HotkeyProc);

            var failed = new List<string>();
            void Bind(int id, Key key, string label, Action action)
            {
                _hotkeys[id] = action;
                var modifiers = Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT;
                if (!Native.RegisterHotKey(_hotkeyHost.Handle, id, modifiers, KeyInterop.VirtualKeyFromKey(key))) failed.Add(label);
            }

            Bind(1, Key.N, "Ctrl+Alt+N", NewNote);
            Bind(2, Key.K, "Ctrl+Alt+K", () => OpenPanel());
            Bind(3, Key.F, "Ctrl+Alt+F", BringAllToFront);
            Bind(4, Key.D, "Ctrl+Alt+D", ToggleDesktop);

            if (failed.Count > 0)
                _tray.ShowBalloonTip(5000, "Atajos ocupados",
                    string.Join(", ", failed) + " ya los usa otra aplicación.", Forms.ToolTipIcon.Info);
        }

        private IntPtr HotkeyProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY && _hotkeys.TryGetValue(wParam.ToInt32(), out var action))
            {
                action();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void WaitForWake()
        {
            while (_wake.WaitOne()) Dispatcher.BeginInvoke(new Action(() => OpenPanel()));
        }

        private void Quit()
        {
            IsExiting = true;
            Store.Flush();
            foreach (var window in _windows.Values.ToList()) window.Close();
            Store.Flush();
            _panel.Close();
            Shutdown();
        }

        private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                File.AppendAllText(Paths.ErrorLog, DateTime.Now.ToString("s") + " " + e.Exception + Environment.NewLine);
                Store?.Flush();
            }
            catch (IOException) { }
            e.Handled = true;

            // Without the panel there's no way to use the app, so don't linger invisible in the tray.
            if (_panel != null) return;
            MessageBox.Show("Notitas no pudo iniciar. El detalle está en " + Paths.ErrorLog, "Notitas",
                            MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}

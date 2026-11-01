using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Notitas
{
    public partial class SettingsWindow : Window
    {
        private readonly App _app;

        public SettingsWindow(App app)
        {
            _app = app;
            InitializeComponent();
            AutostartBox.IsChecked = Autostart.Enabled;
            Render();
        }

        private void Render()
        {
            FolderLabel.Text = _app.Store.Folder;
            FolderLabel.ToolTip = _app.Store.Folder;
            ResetButton.Visibility = _app.Config.NotesFolder == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Change_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new Forms.FolderBrowserDialog
            {
                Description = "Elige la carpeta para tus notas (por ejemplo, una subcarpeta de tu bóveda de Obsidian)",
                SelectedPath = _app.Store.Folder,
            })
            {
                if (dialog.ShowDialog() == Forms.DialogResult.OK) Switch(dialog.SelectedPath);
            }
        }

        private void Reset_Click(object sender, RoutedEventArgs e) => Switch(null);

        private void Switch(string folder)
        {
            var target = folder ?? Paths.DefaultNotes;
            if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(_app.Store.Folder), StringComparison.OrdinalIgnoreCase)) return;

            var count = _app.Store.Notes.Count(n => n.FilePath != null) + _app.Store.Trash.Count;
            var move = false;
            if (count > 0)
            {
                var answer = MessageBox.Show(this,
                    "¿Mover tus " + count + " notas a la nueva carpeta?\n\nSí: se mueven contigo.\nNo: se quedan donde están y Notitas mostrará lo que haya en la nueva carpeta.",
                    "Notitas", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Cancel) return;
                move = answer == MessageBoxResult.Yes;
            }

            try
            {
                _app.SwitchFolder(folder, move);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                MessageBox.Show(this, "No se pudo cambiar de carpeta, tus notas siguen donde estaban.\n\n" + ex.Message,
                                "Notitas", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Render();
        }

        private void Open_Click(object sender, RoutedEventArgs e) =>
            Process.Start("explorer.exe", "\"" + _app.Store.Folder + "\"");

        private void Autostart_Click(object sender, RoutedEventArgs e) =>
            Autostart.Enabled = AutostartBox.IsChecked == true;

        private void Done_Click(object sender, RoutedEventArgs e) => Close();
    }
}

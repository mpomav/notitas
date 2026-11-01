using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Microsoft.Win32;

namespace Notitas
{
    /// <summary>Everything lives beside the exe so the app stays portable.</summary>
    public static class Paths
    {
        public static string Exe { get; } = Assembly.GetEntryAssembly().Location;
        public static string Data { get; } = ResolveDataFolder();
        public static string DefaultNotes => Path.Combine(Data, "notas");
        public static string Config => Path.Combine(Data, "config.json");
        public static string Layout => Path.Combine(Data, "layout.json");
        public static string ErrorLog => Path.Combine(Data, "error.log");

        private static string ResolveDataFolder()
        {
            var beside = Path.Combine(Path.GetDirectoryName(Exe), "datos");
            try
            {
                Directory.CreateDirectory(beside);
                var probe = Path.Combine(beside, ".probe");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return beside;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // Read-only location (a USB lock, Program Files): fall back to the user profile.
                var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notitas");
                Directory.CreateDirectory(fallback);
                return fallback;
            }
        }
    }

    [DataContract]
    public sealed class Config
    {
        /// <summary>Null means the default folder beside the exe, which moves with it.</summary>
        [DataMember] public string NotesFolder { get; set; }
        [DataMember] public string DefaultColor { get; set; } = Palette.DefaultKey;
        [DataMember] public bool WelcomeShown { get; set; }

        public string EffectiveNotesFolder => string.IsNullOrEmpty(NotesFolder) ? Paths.DefaultNotes : NotesFolder;

        public static Config Load() => Json.Read<Config>(Paths.Config) ?? new Config();
        public void Save() => Json.Write(Paths.Config, this);
    }

    [DataContract]
    public sealed class NoteLayout
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public bool OnDesktop { get; set; }
        [DataMember] public bool Collapsed { get; set; }
        [DataMember] public bool Topmost { get; set; }
        [DataMember] public double Width { get; set; }
        [DataMember] public double Height { get; set; }
        [DataMember] public int? X { get; set; }
        [DataMember] public int? Y { get; set; }
    }

    public static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Notitas";

        public static bool Enabled
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return string.Equals(key?.GetValue(ValueName) as string, Quoted, StringComparison.OrdinalIgnoreCase);
            }
            set
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) key.SetValue(ValueName, Quoted);
                    else key.DeleteValue(ValueName, false);
                }
            }
        }

        private static string Quoted => "\"" + Paths.Exe + "\"";
    }

    public static class Json
    {
        private static readonly DataContractJsonSerializerSettings Options =
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true };

        public static T Read<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (var stream = File.OpenRead(path))
                    return (T)new DataContractJsonSerializer(typeof(T), Options).ReadObject(stream);
            }
            catch (Exception e) when (e is IOException || e is SerializationException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }

        public static void Write<T>(string path, T value)
        {
            var temp = path + ".tmp";
            using (var stream = File.Create(temp))
                new DataContractJsonSerializer(typeof(T), Options).WriteObject(stream, value);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        public static List<NoteLayout> ReadLayout() => Read<List<NoteLayout>>(Paths.Layout) ?? new List<NoteLayout>();
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Notitas
{
    /// <summary>Reads and writes notes as Markdown with YAML frontmatter, the shape Obsidian expects.</summary>
    public static class NoteFile
    {
        private const string DateFormat = "yyyy-MM-ddTHH:mm:ss";
        private static readonly Regex KeyLine = new Regex(@"^([A-Za-z_][\w-]*):\s*(.*)$");
        private static readonly Regex ListItem = new Regex(@"^\s*-\s*(.*)$");
        private static readonly HashSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        public static Note Parse(string text, string path)
        {
            var lines = text.TrimStart('﻿').Replace("\r\n", "\n").Split('\n');
            var end = -1;
            if (lines[0].TrimEnd() == "---")
                end = Array.FindIndex(lines, 1, l => l.TrimEnd() == "---");

            var note = new Note { FilePath = path };
            var title = (string)null;
            var color = Palette.DefaultKey;
            var tags = new List<string>();
            var extra = new List<string>();
            var fallbackDate = path != null && File.Exists(path) ? File.GetLastWriteTime(path) : DateTime.Now;
            var created = fallbackDate;
            var updated = fallbackDate;
            var hasId = false;

            for (var i = 1; i < end; i++)
            {
                var match = KeyLine.Match(lines[i]);
                if (!match.Success) { extra.Add(lines[i]); continue; }

                var value = match.Groups[2].Value.Trim();
                switch (match.Groups[1].Value.ToLowerInvariant())
                {
                    case "id": note.Id = Unquote(value); hasId = note.Id.Length > 0; break;
                    case "title": title = Unquote(value); break;
                    case "color": color = Unquote(value); break;
                    case "created": created = ParseDate(value, created); break;
                    case "updated": updated = ParseDate(value, updated); break;
                    case "tags":
                    case "tag":
                        tags.AddRange(value.Split(new[] { ',', ' ', '[', ']' }, StringSplitOptions.RemoveEmptyEntries).Select(Unquote));
                        while (i + 1 < end && ListItem.IsMatch(lines[i + 1]))
                            tags.Add(Unquote(ListItem.Match(lines[++i]).Groups[1].Value.Trim()));
                        break;
                    default:
                        extra.Add(lines[i]);
                        while (i + 1 < end && (lines[i + 1].StartsWith(" ") || lines[i + 1].StartsWith("\t") || lines[i + 1].StartsWith("-")))
                            extra.Add(lines[++i]);
                        break;
                }
            }

            var body = string.Join("\n", lines.Skip(end + 1));
            var stem = path == null ? "" : Path.GetFileNameWithoutExtension(path);
            if (!hasId) note.Id = stem.Length > 0 ? stem : note.Id;
            note.ApplyLoaded(title ?? stem, body, color, tags, created, updated, extra);
            return note;
        }

        public static string Serialize(Note note)
        {
            var text = new StringBuilder();
            text.Append("---\n");
            text.Append("id: ").Append(note.Id).Append('\n');
            text.Append("title: ").Append(Quote(note.Title)).Append('\n');
            if (note.Tags.Count > 0)
            {
                text.Append("tags:\n");
                foreach (var tag in note.Tags) text.Append("  - ").Append(tag).Append('\n');
            }
            text.Append("color: ").Append(note.Color).Append('\n');
            text.Append("created: ").Append(note.Created.ToString(DateFormat, CultureInfo.InvariantCulture)).Append('\n');
            text.Append("updated: ").Append(note.Updated.ToString(DateFormat, CultureInfo.InvariantCulture)).Append('\n');
            foreach (var line in note.ExtraFrontmatter) text.Append(line).Append('\n');
            text.Append("---\n");
            text.Append(note.Body.Replace("\r\n", "\n"));
            return text.ToString();
        }

        /// <summary>File name for a note, without folder or extension.</summary>
        public static string BaseName(Note note)
        {
            var name = new string(note.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? ' ' : c).ToArray());
            name = Regex.Replace(name, @"\s+", " ").Trim().TrimEnd('.', ' ').TrimStart('.', ' ');
            if (name.Length > 80) name = name.Substring(0, 80).TrimEnd('.', ' ');
            if (name.Length == 0) name = "Nota " + note.Created.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture);
            return ReservedNames.Contains(name) ? "Nota " + name : name;
        }

        private static DateTime ParseDate(string value, DateTime fallback) =>
            DateTime.TryParse(Unquote(value), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : fallback;

        private static string Quote(string value) =>
            "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ") + "\"";

        private static string Unquote(string value)
        {
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                return value.Substring(1, value.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\");
            if (value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'')
                return value.Substring(1, value.Length - 2).Replace("''", "'");
            return value;
        }
    }
}

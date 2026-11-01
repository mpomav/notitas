using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace Notitas
{
    /// <summary>Small surprises. None of them touch the notes.</summary>
    public static class EasterEggs
    {
        private static readonly Dictionary<string, string> SearchReplies = new Dictionary<string, string>
        {
            ["xyzzy"] = "Nada ocurre.",
            ["42"] = "Esa es la respuesta. Falta anotar la pregunta.",
            ["sudo"] = "Aquí mandan las notas.",
            ["hola"] = "Hola. Ninguna nota te responde, pero yo sí.",
        };

        public static string SearchReply(string query) =>
            SearchReplies.TryGetValue(query, out var reply) ? reply : null;

        public static string BodyHint(DateTime now) =>
            now.Hour >= 2 && now.Hour < 5 ? "¿Sigues por aquí a esta hora?" : "Escribe algo…";
    }

    /// <summary>Follows the Konami code one key at a time.</summary>
    public sealed class KonamiCode
    {
        private static readonly Key[] Sequence =
        {
            Key.Up, Key.Up, Key.Down, Key.Down, Key.Left, Key.Right, Key.Left, Key.Right, Key.B, Key.A,
        };

        private int _next;

        /// <summary>True right after the B, so the A doesn't land in whatever has focus either.</summary>
        public bool HoldingLetters => _next == Sequence.Length - 1;

        /// <summary>Returns true when the key completes the code.</summary>
        public bool Feed(Key key)
        {
            if (key == Sequence[_next]) _next++;
            else if (key == Key.Up) _next = _next == 2 ? 2 : 1;   // Extra ups before the downs still count.
            else _next = 0;

            if (_next < Sequence.Length) return false;
            _next = 0;
            return true;
        }
    }
}

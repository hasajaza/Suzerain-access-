using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SuzerainAccess.Config;

namespace SuzerainAccess.UI
{
    /// <summary>
    /// Turns TextMeshPro rich text into clean speakable text and formats numbers.
    /// </summary>
    internal static class TextUtil
    {
        // TMP inline sprites: <sprite name="x">, <sprite="asset" name="x">, <sprite=3>
        private static readonly Regex SpriteName = new Regex("<sprite[^>]*?name\\s*=\\s*\"?([^\">]+)\"?[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SpriteAny = new Regex("<sprite[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LineBreakTag = new Regex("<br\\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AnyTag = new Regex("<[^<>]{1,200}>", RegexOptions.Compiled);
        // Febucci Text Animator appearance/behaviour tags such as {fade} ... {/fade}
        private static readonly Regex BraceTag = new Regex("\\{/?[a-z]+(=[^{}\\s]*)?\\}", RegexOptions.Compiled);
        private static readonly Regex Whitespace = new Regex("[\\s\\u00A0\\u200B\\u200C\\u200D\\uFEFF]+", RegexOptions.Compiled);
        private static readonly Regex Fraction = new Regex("^\\s*(\\d+)\\s*/\\s*(\\d+)\\s*$", RegexOptions.Compiled);
        private static readonly Regex CamelSplit = new Regex("(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])|_+", RegexOptions.Compiled);

        /// <summary>Removes rich-text markup and collapses whitespace.</summary>
        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string s = text;
            if (s.IndexOf('<') >= 0)
            {
                s = LineBreakTag.Replace(s, " ");
                s = SpriteName.Replace(s, SpriteToWords);
                s = SpriteAny.Replace(s, " ");
                s = AnyTag.Replace(s, "");
            }
            if (s.IndexOf('{') >= 0) s = BraceTag.Replace(s, "");
            s = Whitespace.Replace(s, " ").Trim();
            return s;
        }

        /// <summary>Replaces an inline sprite tag with its (word-split) sprite name.</summary>
        private static string SpriteToWords(Match m) => " " + CamelToWords(m.Groups[1].Value) + " ";

        /// <summary>Clean, but first split on line breaks and return the first non-empty line.</summary>
        public static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string s = LineBreakTag.Replace(text, "\n");
            foreach (var line in s.Split('\n'))
            {
                string c = Clean(line);
                if (c.Length > 0) return c;
            }
            return string.Empty;
        }

        /// <summary>"ContinueButton" -> "Continue Button", "news_panel" -> "news panel".</summary>
        public static string CamelToWords(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            return Whitespace.Replace(CamelSplit.Replace(name, " "), " ").Trim();
        }

        /// <summary>"2/5" -> "2 of 5". Other strings unchanged.</summary>
        public static string SpeakFraction(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var m = Fraction.Match(text);
            return m.Success ? m.Groups[1].Value + " of " + m.Groups[2].Value : text;
        }

        /// <summary>Signed value, e.g. "plus 2" / "minus 3" / "0" (or "+2" when detailed numbers are off).</summary>
        public static string Signed(int value)
        {
            if (!ModConfig.DetailedNumbers.Value) return value.ToString("+0;-0;0", CultureInfo.InvariantCulture);
            if (value > 0) return "plus " + value.ToString(CultureInfo.InvariantCulture);
            if (value < 0) return "minus " + (-value).ToString(CultureInfo.InvariantCulture);
            return "0";
        }

        public static string Number(float value)
        {
            if (Math.Abs(value - Math.Round(value)) < 0.0001f) return ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>Joins non-empty parts with ", ".</summary>
        public static string Join(params string[] parts) => JoinWith(", ", parts);

        public static string JoinWith(string separator, IEnumerable<string> parts)
        {
            var sb = new StringBuilder();
            foreach (var p in parts)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                if (sb.Length > 0) sb.Append(separator);
                sb.Append(p.Trim());
            }
            return sb.ToString();
        }

        /// <summary>Appends a sentence terminator if missing, so queued parts are read as separate sentences.</summary>
        public static string Sentence(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            text = text.Trim();
            char last = text[text.Length - 1];
            return last == '.' || last == '!' || last == '?' || last == ':' ? text : text + ".";
        }

        /// <summary>True if the text carries no information (empty, or a single non-alphanumeric symbol).</summary>
        public static bool IsDecorative(string cleaned)
        {
            if (string.IsNullOrWhiteSpace(cleaned)) return true;
            if (cleaned.Length == 1 && !char.IsLetterOrDigit(cleaned[0])) return true;
            return false;
        }
    }
}

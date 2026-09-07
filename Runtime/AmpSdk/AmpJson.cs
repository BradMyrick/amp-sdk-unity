// AMP Unity SDK — minimal JSON helpers (AOT/IL2CPP-safe, zero deps).
// Builds flat request bodies and extracts top-level fields; the long tail
// of API responses is surfaced as raw JSON strings.

using System;
using System.Collections.Generic;
using System.Text;

namespace Amp.Sdk
{
    internal static class AmpJson
    {
        /// <summary>Build a flat JSON object from string + integer fields. Null/empty strings are omitted.</summary>
        public static string Build(
            IEnumerable<KeyValuePair<string, string>> strings = null,
            IEnumerable<KeyValuePair<string, long>> numbers = null)
        {
            var sb = new StringBuilder("{");
            var first = true;

            if (strings != null)
            {
                foreach (var kv in strings)
                {
                    if (string.IsNullOrEmpty(kv.Value)) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"').Append(Escape(kv.Key)).Append("\":\"").Append(Escape(kv.Value)).Append('"');
                }
            }
            if (numbers != null)
            {
                foreach (var kv in numbers)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"').Append(Escape(kv.Key)).Append("\":").Append(kv.Value);
                }
            }
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>Extract a top-level string field ("field":"value"). Returns null when absent or null-valued.</summary>
        public static string GetString(string json, string field)
        {
            var pos = json.IndexOf("\"" + field + "\"", StringComparison.Ordinal);
            if (pos < 0) return null;
            var colon = json.IndexOf(':', pos);
            if (colon < 0) return null;
            var p = colon + 1;
            while (p < json.Length && (json[p] == ' ' || json[p] == '\t')) p++;
            if (p >= json.Length || json[p] != '"') return null; // null / non-string
            p++;
            var sb = new StringBuilder();
            while (p < json.Length && json[p] != '"')
            {
                if (json[p] == '\\' && p + 1 < json.Length)
                {
                    p++;
                    switch (json[p])
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(json[p]); break;
                    }
                }
                else sb.Append(json[p]);
                p++;
            }
            return sb.ToString();
        }

        /// <summary>Extract a top-level numeric field.</summary>
        public static long GetInt(string json, string field, long fallback = 0)
        {
            var pos = json.IndexOf("\"" + field + "\"", StringComparison.Ordinal);
            if (pos < 0) return fallback;
            var colon = json.IndexOf(':', pos);
            if (colon < 0) return fallback;
            var p = colon + 1;
            while (p < json.Length && (json[p] == ' ' || json[p] == '\t')) p++;
            var start = p;
            while (p < json.Length && (char.IsDigit(json[p]) || json[p] == '-')) p++;
            if (p == start) return fallback;
            return long.TryParse(json.Substring(start, p - start), out var value) ? value : fallback;
        }

        public static double GetDouble(string json, string field, double fallback = 0)
        {
            var pos = json.IndexOf("\"" + field + "\"", StringComparison.Ordinal);
            if (pos < 0) return fallback;
            var colon = json.IndexOf(':', pos);
            if (colon < 0) return fallback;
            var p = colon + 1;
            while (p < json.Length && (json[p] == ' ' || json[p] == '\t')) p++;
            var start = p;
            while (p < json.Length && (char.IsDigit(json[p]) || json[p] == '-' || json[p] == '.' || json[p] == 'e' || json[p] == 'E' || json[p] == '+' || json[p] == '-')) p++;
            if (p == start) return fallback;
            return double.TryParse(json.Substring(start, p - start), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
        }

        public static bool GetBool(string json, string field, bool fallback = false)
        {
            var pos = json.IndexOf("\"" + field + "\"", StringComparison.Ordinal);
            if (pos < 0) return fallback;
            var colon = json.IndexOf(':', pos);
            if (colon < 0) return fallback;
            var p = json.IndexOfFirstNonSpace(colon + 1);
            if (p < 0) return fallback;
            if (json.StartsWith("true", StringComparison.Ordinal)) return true;
            if (p + 5 <= json.Length && json.Substring(p, 5) == "false") return false;
            if (p + 4 <= json.Length && json.Substring(p, 4) == "true") return true;
            return fallback;
        }

        private static int IndexOfFirstNonSpace(this string s, int from)
        {
            for (var i = from; i < s.Length; i++)
                if (s[i] != ' ' && s[i] != '\t') return i;
            return -1;
        }

        private static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}

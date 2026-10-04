using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BreakersOfE.Services
{
    /// <summary>
    /// A CSV file read the forgiving way, because every app writes CSV a
    /// little differently:
    ///  • comma, semicolon or tab, detected from the header line (or a
    ///    leading "sep=;" line, which Excel and some apps write);
    ///  • quoted fields with "" inside, commas and line breaks inside quotes;
    ///  • a byte-order mark, blank lines, short rows and trailing commas.
    /// Columns are found by NAME, never by position (apps add, drop and
    /// reorder columns between versions), case / space / punctuation blind.
    /// </summary>
    public sealed class CsvText
    {
        public List<string> Headers { get; } = new();
        public List<string[]> Rows { get; } = new();
        public char Delimiter { get; private set; } = ',';
        /// <summary>The line each row started on (1-based, for messages).</summary>
        public List<int> RowLines { get; } = new();
        /// <summary>A quote was opened and never closed (the rest of the file ended up in one field).</summary>
        public int UnclosedQuoteLine { get; private set; }

        public static CsvText Parse(string text)
        {
            var csv = new CsvText();
            text ??= "";
            if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];

            int start = 0, line = 1;
            // "sep=;" first line (Excel convention) names the delimiter.
            int firstEnd = text.IndexOf('\n');
            string first = (firstEnd < 0 ? text : text[..firstEnd]).TrimEnd('\r').Trim();
            if (first.StartsWith("sep=", StringComparison.OrdinalIgnoreCase) && first.Length >= 5)
            {
                csv.Delimiter = first[4];
                start = firstEnd < 0 ? text.Length : firstEnd + 1;
                line = 2;
                first = "";
            }
            else
            {
                csv.Delimiter = GuessDelimiter(first);
            }

            var records = ReadRecords(text, start, line, csv.Delimiter, out int unclosed);
            csv.UnclosedQuoteLine = unclosed;
            bool header = true;
            foreach (var (fields, at) in records)
            {
                if (fields.All(f => f.Trim().Length == 0)) continue;       // blank line
                if (header)
                {
                    csv.Headers.AddRange(fields.Select(f => f.Trim()));
                    header = false;
                    continue;
                }
                csv.Rows.Add(fields);
                csv.RowLines.Add(at);
            }
            return csv;
        }

        /// <summary>The delimiter used most in the header line, outside quotes.</summary>
        private static char GuessDelimiter(string headerLine)
        {
            int comma = 0, semi = 0, tab = 0;
            bool quoted = false;
            foreach (char c in headerLine)
            {
                if (c == '"') quoted = !quoted;
                else if (!quoted)
                {
                    if (c == ',') comma++;
                    else if (c == ';') semi++;
                    else if (c == '\t') tab++;
                }
            }
            if (tab > comma && tab >= semi) return '\t';
            if (semi > comma) return ';';
            return ',';
        }

        private static List<(string[] Fields, int Line)> ReadRecords(string text, int pos, int line, char delim, out int unclosedAt)
        {
            unclosedAt = 0;
            int quoteOpenedOn = 0;
            var result = new List<(string[], int)>();
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false, any = false;
            int recordLine = line;
            for (int i = pos; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                        else quoted = false;
                    }
                    else if (c != '\r')                 // a line break inside quotes stays a plain \n
                    {
                        if (c == '\n') line++;
                        sb.Append(c);
                    }
                    continue;
                }
                if (c == '"' && sb.ToString().Trim().Length == 0) { quoted = true; quoteOpenedOn = line; sb.Clear(); any = true; continue; }
                if (c == delim) { fields.Add(sb.ToString()); sb.Clear(); any = true; continue; }
                if (c == '\r') continue;
                if (c == '\n')
                {
                    fields.Add(sb.ToString());
                    result.Add((fields.ToArray(), recordLine));
                    fields.Clear(); sb.Clear(); any = false;
                    line++;
                    recordLine = line;
                    continue;
                }
                sb.Append(c);
                any = true;
            }
            if (quoted) unclosedAt = quoteOpenedOn;
            if (any || sb.Length > 0 || fields.Count > 0)
            {
                fields.Add(sb.ToString());
                result.Add((fields.ToArray(), recordLine));
            }
            return result;
        }

        /// <summary>"Collector #", "collector_number", "CollectorNumber" → "collectornumber".</summary>
        public static string Key(string header) =>
            new string((header ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        /// <summary>The first column whose name matches any of <paramref name="names"/> (−1 when none).</summary>
        public int Col(params string[] names)
        {
            var keys = Headers.Select(Key).ToList();
            foreach (var n in names)
            {
                int i = keys.IndexOf(Key(n));
                if (i >= 0) return i;
            }
            return -1;
        }

        /// <summary>Has a column of any of these names.</summary>
        public bool Has(params string[] names) => Col(names) >= 0;

        /// <summary>A field of a row (trimmed; "" when the row is short or the column is missing).</summary>
        public static string Get(string[] row, int col) =>
            col >= 0 && col < row.Length ? row[col].Trim() : "";

        // ── Writing ──────────────────────────────────────────────────────
        /// <summary>One CSV line (fields quoted only when needed).</summary>
        public static string Line(IEnumerable<string?> fields) =>
            string.Join(",", fields.Select(Escape));

        public static string Escape(string? s)
        {
            s ??= "";
            return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 || s != s.Trim()
                ? "\"" + s.Replace("\"", "\"\"") + "\""
                : s;
        }

        /// <summary>A number the same way in every country ("4.50", never "4,50").</summary>
        public static string Num(decimal? d) => d.HasValue ? d.Value.ToString("0.##", CultureInfo.InvariantCulture) : "";

        /// <summary>A price or number read the forgiving way: "$4.50", "4,50", "4.50 €".</summary>
        public static decimal? ParseDecimal(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return null;
            var cleaned = new string(s.Where(c => char.IsDigit(c) || c == '.' || c == ',' || c == '-').ToArray());
            if (cleaned.Length == 0) return null;
            // "1,234.50" → "1234.50"; "4,50" (one comma, no dot) → "4.50".
            if (cleaned.Contains(',') && cleaned.Contains('.')) cleaned = cleaned.Replace(",", "");
            else if (cleaned.Count(c => c == ',') == 1) cleaned = cleaned.Replace(',', '.');
            else cleaned = cleaned.Replace(",", "");
            return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
        }

        /// <summary>A quantity ("4", "4x", "x4", " 4 "); null when not a number.</summary>
        public static int? ParseQty(string s)
        {
            string t = (s ?? "").Trim().Trim('x', 'X').Trim();
            if (int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n < 1_000_000) return n;
            // "2.0" (a spreadsheet's way of writing 2)
            if (decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d) &&
                d >= 0 && d < 1_000_000 && d == Math.Floor(d)) return (int)d;
            return null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BreakersOfE.Models;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Reads a card file or pasted text into <see cref="ImportLine"/>s — no
    /// matching yet (that's <see cref="CardMatcher"/>). Every reader is
    /// forgiving: columns by name, any spelling of finish / language /
    /// condition, blank and odd lines reported rather than fatal.
    /// </summary>
    public static class ImportParsers
    {
        /// <summary>Display names for the format list.</summary>
        public static string Display(ImportFormat f) => f switch
        {
            ImportFormat.Auto => "Auto-detect",
            ImportFormat.BoeFull => "BoE full collection file (.csv)",
            ImportFormat.ManaBox => "ManaBox (.csv)",
            ImportFormat.OtherCsv => "Other app's CSV — Moxfield, Archidekt, Deckbox, TCGplayer, Dragon Shield, MTGGoldfish, Deckstats, Delver Lens …",
            ImportFormat.TextList => "Text list — 4 Lightning Bolt (M10) 146 *F*",
            _ => f.ToString(),
        };

        // ══════════════════════════════════════════════════════════════════
        // DETECT
        // ══════════════════════════════════════════════════════════════════
        /// <summary>What the text looks like; <paramref name="refusal"/> when it's something we don't import (here).</summary>
        public static ImportFormat Detect(string text, out string refusal)
        {
            refusal = "";
            string t = (text ?? "").TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            if (t.Length == 0) { refusal = "The file is empty."; return ImportFormat.Auto; }
            if (t.StartsWith("<"))
            {
                refusal = t.Contains("<mtgstudiodeck", StringComparison.OrdinalIgnoreCase)
                    ? "This is an MTG Studio deck file. BoE doesn't read MTG Studio files — export the deck from MTG Studio as a CSV or text list and import that."
                    : "This is an XML file (an MTGO .dek?) — MTGO import comes in a later part.";
                return ImportFormat.Auto;
            }
            if (t.StartsWith("{"))
            {
                refusal = "This is a BoE deck file — open it in Edit → Decks (it's already a deck).";
                return ImportFormat.Auto;
            }

            var csv = CsvText.Parse(FirstLines(t, 3));
            if (LooksLikeCsvHeader(csv))
            {
                if (csv.Has(BoeFormat.TableHeader)) return ImportFormat.BoeFull;
                if (csv.Has("ManaBox ID") || (csv.Has("Set code") && csv.Has("Collector number") && csv.Has("Foil")))
                    return ImportFormat.ManaBox;
                return ImportFormat.OtherCsv;
            }
            return ImportFormat.TextList;
        }

        private static string FirstLines(string t, int n)
        {
            int idx = 0;
            for (int i = 0; i < n && idx >= 0; i++)
            {
                idx = t.IndexOf('\n', idx);
                if (idx >= 0) idx++;
            }
            return idx < 0 ? t : t[..idx];
        }

        /// <summary>A header row: two or more columns, one of them a card name.</summary>
        private static bool LooksLikeCsvHeader(CsvText csv) =>
            csv.Headers.Count >= 2 && (csv.Has(NameCols) || csv.Has(SidCols));

        // ══════════════════════════════════════════════════════════════════
        // PARSE
        // ══════════════════════════════════════════════════════════════════
        public static ImportParseResult Parse(string text, ImportFormat format)
        {
            var result = new ImportParseResult();
            var detected = Detect(text, out var refusal);
            if (format == ImportFormat.Auto)
            {
                if (refusal.Length > 0) { result.Error = refusal; return result; }
                format = detected;
            }
            result.Format = format;
            try
            {
                if (format == ImportFormat.TextList) ParseText(text, result);
                else ParseCsv(text, format, result);
            }
            catch (Exception ex)
            {
                result.Error = $"Could not read the file: {ex.Message}";
            }
            if (result.Error.Length == 0 && result.Lines.Count == 0)
                result.Error = format == ImportFormat.TextList
                    ? "No card lines found. Text lists look like: 4 Lightning Bolt (M10) 146"
                    : "No card rows found under the header line.";
            return result;
        }

        // ── CSV (BoE full, ManaBox, any other app) ──────────────────────
        // TCGplayer's "Simple Name" is the name without "(Borderless)" and the like: preferred when there.
        private static readonly string[] NameCols = { "Simple Name", "Name", "Card Name", "Card", "Product Name", "CardName" };
        private static readonly string[] QtyCols = { "Quantity", "Count", "Qty", "Amount", "Number Owned", "Have" };
        private static readonly string[] SetCodeCols = { "Set code", "Set Code", "Edition Code", "Expansion Code", "Set ID", "SetCode" };
        private static readonly string[] SetNameCols = { "Set name", "Set Name", "Edition Name", "Expansion Name", "Expansion" };
        // "Set" / "Edition" may hold a code (Moxfield) or a name (Deckbox): decided per value.
        private static readonly string[] SetEitherCols = { "Edition", "Set" };
        private static readonly string[] NumberCols = { "Collector number", "Collector Number", "Collector's number", "Card Number", "Number", "Collector #", "CN", "No" };
        private static readonly string[] SidCols = { "Scryfall ID", "Scryfall Id", "ScryfallId", "Scryfall" };
        private static readonly string[] FinishCols = { "Finish", "Foil", "Is Foil", "Printing", "Premium", "Foil/Etched", "Variant" };
        private static readonly string[] LangCols = { "Language", "Lang" };
        private static readonly string[] CondCols = { "Condition", "Cond" };
        private static readonly string[] PriceCols = { "Purchase price", "Purchase Price", "Price Bought", "My Price", "Price", "Cost" };
        private static readonly string[] NotesCols = { "Notes", "Note", "Tags", "Comment", "Comments" };
        private static readonly string[] StorageCols = { "Storage", "Storage Location", "Location", "Binder Name", "Folder Name" };
        private static readonly string[] AddedCols = { "Date Added", "Added", "Date Bought", "Created At" };
        /// <summary>Deck CSVs (BoE's spreadsheet export, Moxfield "Board", Archidekt "Category"): which part of the deck.</summary>
        private static readonly string[] SectionCols = { "Section", "Board", "Deck Section", "Category" };

        /// <summary>A deck part's name → the section ("Commander", "Sideboard", "Tokens", "Maybeboard" …; anything else is the main deck).</summary>
        private static ImportSection SectionOf(string value)
        {
            string v = value.Trim().ToLowerInvariant();
            if (v.Length == 0) return ImportSection.None;
            if (v.Contains("commander")) return ImportSection.Commander;
            if (v.Contains("side") || v == "sb" || v.Contains("companion")) return ImportSection.Sideboard;
            if (v.Contains("token")) return ImportSection.Tokens;
            if (v.Contains("maybe") || v.Contains("consider")) return ImportSection.Maybe;
            return ImportSection.Main;
        }

        private static void ParseCsv(string text, ImportFormat format, ImportParseResult result)
        {
            var csv = CsvText.Parse(text);
            if (csv.UnclosedQuoteLine > 0)
                result.Problems.Add($"Line {csv.UnclosedQuoteLine}: a quote (\") is opened and never closed — the rows after it may be missing.");
            int cName = csv.Col(NameCols), cQty = csv.Col(QtyCols), cSet = csv.Col(SetCodeCols),
                cSetName = csv.Col(SetNameCols), cEither = csv.Col(SetEitherCols), cNum = csv.Col(NumberCols),
                cSid = csv.Col(SidCols), cFinish = csv.Col(FinishCols), cLang = csv.Col(LangCols),
                cCond = csv.Col(CondCols), cPrice = csv.Col(PriceCols), cNotes = csv.Col(NotesCols),
                cStorage = csv.Col(StorageCols), cAdded = csv.Col(AddedCols),
                cTable = csv.Col(BoeFormat.TableHeader), cFav = csv.Col("Favorite"), cSection = csv.Col(SectionCols);
            string finishHeader = cFinish >= 0 ? CsvText.Key(csv.Headers[cFinish]) : "";

            if (cName < 0 && cSid < 0)
            {
                result.Error = "There's no card name (or Scryfall ID) column in this file.";
                return;
            }

            for (int r = 0; r < csv.Rows.Count; r++)
            {
                var row = csv.Rows[r];
                int lineNo = csv.RowLines[r];
                string name = CsvText.Get(row, cName), sid = CsvText.Get(row, cSid);
                if (name.Length == 0 && sid.Length == 0)
                {
                    result.Problems.Add($"Line {lineNo}: no card name — skipped.");
                    continue;
                }
                string qtyText = CsvText.Get(row, cQty);
                int qty = cQty < 0 || qtyText.Length == 0 ? 1 : CsvText.ParseQty(qtyText) ?? -1;
                if (qty < 0)
                {
                    result.Problems.Add($"Line {lineNo}: \"{qtyText}\" isn't a quantity — skipped.");
                    continue;
                }

                string setCode = CsvText.Get(row, cSet), setName = CsvText.Get(row, cSetName);
                string either = CsvText.Get(row, cEither);
                if (either.Length > 0)
                {
                    if (setCode.Length == 0 && LooksLikeSetCode(either)) setCode = either;
                    else if (setName.Length == 0) setName = either;
                }

                string condText = CsvText.Get(row, cCond);
                string? finishFromCondition = null;
                if (condText.EndsWith(" foil", StringComparison.OrdinalIgnoreCase))
                {
                    finishFromCondition = CardFinish.Foil;                  // TCGplayer: "Near Mint Foil"
                    condText = condText[..^5].Trim();
                }
                var line = new ImportLine
                {
                    LineNumber = lineNo,
                    Quantity = qty,
                    Name = name,
                    ScryfallId = sid,
                    SetCode = setCode,
                    SetName = setName,
                    CollectorNumber = CsvText.Get(row, cNum),
                    Finish = cFinish >= 0 ? ParseFinish(CsvText.Get(row, cFinish), finishHeader) : finishFromCondition,
                    Language = cLang >= 0 && CsvText.Get(row, cLang).Length > 0 ? NormalizeLanguage(CsvText.Get(row, cLang)) : null,
                    Condition = condText.Length > 0 ? NormalizeCondition(condText) : null,
                    Price = CsvText.ParseDecimal(CsvText.Get(row, cPrice)),
                    Notes = CsvText.Get(row, cNotes),
                    Storage = CsvText.Get(row, cStorage),
                    DateAdded = ParseDate(CsvText.Get(row, cAdded)),
                    Table = format == ImportFormat.BoeFull && cTable >= 0 ? CsvText.Get(row, cTable) : null,
                    Favorite = cFav >= 0 ? ParseBool(CsvText.Get(row, cFav)) : null,
                    Section = cSection >= 0 ? SectionOf(CsvText.Get(row, cSection)) : ImportSection.None,
                };
                line.Source = $"{qty} {name}" +
                    (setCode.Length > 0 ? $" ({setCode.ToUpperInvariant()})" : setName.Length > 0 ? $" ({setName})" : "") +
                    (line.CollectorNumber.Length > 0 ? $" {line.CollectorNumber}" : "") +
                    (line.Finish is CardFinish.Foil ? " *F*" : line.Finish is CardFinish.Etched ? " *E*" : "");
                result.Lines.Add(line);
            }
        }

        /// <summary>A short code ("M10", "plst", "2X2") rather than a set name ("Magic 2010").</summary>
        private static bool LooksLikeSetCode(string s) =>
            s.Length is >= 2 and <= 6 && s.All(char.IsLetterOrDigit);

        /// <summary>
        /// Any app's finish wording → nonfoil / foil / etched. A "Foil" or
        /// "Premium" yes/no column, or a Finish / Printing column with words
        /// ("Normal", "Foil", "Etched", "Rainbow Foil", "foil_etched" …).
        /// </summary>
        public static string ParseFinish(string value, string headerKey = "")
        {
            string v = (value ?? "").Trim().ToLowerInvariant();
            if (v.Contains("etched")) return CardFinish.Etched;
            if (v is "" or "normal" or "nonfoil" or "non-foil" or "non foil" or "regular" or "no" or "n" or "false" or "0")
                return CardFinish.NonFoil;
            if (v.Contains("foil") || v is "yes" or "y" or "true" or "1" or "premium" or "f")
                return CardFinish.Foil;
            // A "Foil"/"Premium" column with something unexpected: treat as foil only if it's marked at all.
            return headerKey is "foil" or "isfoil" or "premium" ? CardFinish.Foil : CardFinish.NonFoil;
        }

        /// <summary>Every app's condition spelling → ours ("near_mint", "NM", "Good (Lightly Played)", "LightPlayed" …).</summary>
        public static string NormalizeCondition(string value)
        {
            string v = (value ?? "").Trim();
            string squeezed = new string(v.Where(char.IsLetter).ToArray()).ToUpperInvariant();
            return squeezed switch
            {
                "MINT" or "M" or "NM" or "NEARMINT" or "NMM" => CardCondition.NearMint,
                "EXCELLENT" or "EX" or "LP" or "SP" or "LIGHTPLAYED" or "LIGHTLYPLAYED" or "SLIGHTLYPLAYED" or "GOODLIGHTLYPLAYED" => "Lightly Played",
                "GOOD" or "GD" or "MP" or "PL" or "PLAYED" or "MODERATELYPLAYED" => "Moderately Played",
                "HP" or "HEAVILYPLAYED" => "Heavily Played",
                "POOR" or "PR" or "D" or "DMG" or "DAMAGED" => "Damaged",
                _ => CardCondition.Normalize(v),
            };
        }

        /// <summary>Every app's language spelling → ours ("en", "JP", "zh_CN", "Chinese Simplified" …).</summary>
        public static string NormalizeLanguage(string value)
        {
            string v = (value ?? "").Trim();
            string low = v.ToLowerInvariant().Replace('_', '-');
            string known = low switch
            {
                "zh-cn" or "zh-hans" or "chinese" or "zh" or "cn" => "Chinese Simplified",
                "zh-tw" or "zh-hant" or "tw" => "Chinese Traditional",
                _ => CardLanguage.Normalize(v),
            };
            // "en-US", "pt-BR", "de-DE": the language part decides.
            if (!CardLanguage.All.Contains(known) && low.Length > 3 && low[2] == '-')
                known = CardLanguage.Normalize(low[..2]);
            return known;
        }

        private static bool? ParseBool(string s) =>
            s.Trim().ToLowerInvariant() switch
            {
                "true" or "yes" or "1" or "y" or "★" => true,
                "false" or "no" or "0" or "n" or "" => false,
                _ => null,
            };

        private static DateTime? ParseDate(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return null;
            string[] formats = { "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ",
                                 "yyyy-MM-ddTHH:mm:ss.fffZ", "M/d/yyyy", "M/d/yyyy H:mm", "M/d/yyyy h:mm:ss tt", "yyyyMMdd" };
            if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d)) return d;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out d) ? d : null;
        }

        // ── Text lists ───────────────────────────────────────────────────
        private static readonly Regex HeaderRx = new(
            @"^(?<h>commanders?|deck|main|mainboard|main\s*deck|sideboard|side|sb|tokens?|companion|maybeboard|maybe|considering|about)\s*:?\s*(\(\d+\))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex QtyRx = new(@"^(?<q>\d+)\s*[xX]?\s+(?<rest>.+)$", RegexOptions.Compiled);
        private static readonly Regex SetParenRx = new(@"\s\((?<set>[A-Za-z0-9]{2,6})\)(?:\s+(?<cn>[^\s\[\]\*\^]+))?", RegexOptions.Compiled);
        // TCGplayer mass entry "[SLD] 84": a code in brackets counts as a set only with a number after it
        // ("[Ramp]" alone is a tag).
        private static readonly Regex SetBracketRx = new(@"\s\[(?<set>[A-Za-z0-9]{2,6})\]\s+(?<cn>[0-9][^\s\[\]\*\^]*)", RegexOptions.Compiled);
        private static readonly Regex FinishRx = new(@"\*(?<f>[FfEe])\*", RegexOptions.Compiled);
        private static readonly Regex TagsRx = new(@"\[(?<t>[^\]]*)\]", RegexOptions.Compiled);
        private static readonly Regex LabelRx = new(@"\^[^\^]*\^", RegexOptions.Compiled);

        private static void ParseText(string text, ImportParseResult result)
        {
            var section = ImportSection.None;
            bool inAbout = false;
            var lines = (text ?? "").TrimStart('\uFEFF').Replace("\r", "").Split('\n');

            // MTGO .txt (and many plain lists): no section headers; the sideboard
            // is the block after the last blank line.
            int sideboardFrom = int.MaxValue;
            bool anyHeader = lines.Any(l => HeaderRx.IsMatch(l.Trim()) || l.TrimStart().StartsWith("SB:", StringComparison.OrdinalIgnoreCase));
            if (!anyHeader)
            {
                int lastContent = Array.FindLastIndex(lines, l => l.Trim().Length > 0);
                int blank = lastContent < 0 ? -1 : Array.FindLastIndex(lines, lastContent, l => l.Trim().Length == 0);
                bool contentBefore = blank > 0 && lines.Take(blank).Any(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("//") && !l.TrimStart().StartsWith("#"));
                if (contentBefore) sideboardFrom = blank + 1;
            }
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i].Trim();
                int lineNo = i + 1;
                if (raw.Length == 0 || raw.StartsWith("#") || raw.StartsWith("//")) continue;

                var h = HeaderRx.Match(raw);
                if (h.Success)
                {
                    string word = h.Groups["h"].Value.ToLowerInvariant().Replace(" ", "");
                    inAbout = word == "about";
                    section = word switch
                    {
                        "commander" or "commanders" => ImportSection.Commander,
                        "sideboard" or "side" or "sb" or "companion" => ImportSection.Sideboard,
                        "token" or "tokens" => ImportSection.Tokens,
                        "maybeboard" or "maybe" or "considering" => ImportSection.Maybe,
                        "about" => section,
                        _ => ImportSection.Main,
                    };
                    continue;
                }
                if (inAbout) continue;                       // Arena: "Name My Deck" lines

                var lineSection = i >= sideboardFrom ? ImportSection.Sideboard : section;
                string body = raw;
                if (body.StartsWith("SB:", StringComparison.OrdinalIgnoreCase))
                {
                    lineSection = ImportSection.Sideboard;
                    body = body[3..].Trim();
                }

                int qty = 1;
                var q = QtyRx.Match(body);
                if (q.Success)
                {
                    if (!int.TryParse(q.Groups["q"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out qty) || qty > 999_999)
                    {
                        result.Problems.Add($"Line {lineNo}: \"{q.Groups["q"].Value}\" isn't a sensible quantity — skipped.");
                        continue;
                    }
                    body = q.Groups["rest"].Value.Trim();
                }

                var line = new ImportLine { LineNumber = lineNo, Source = raw, Quantity = qty, Section = lineSection };

                // Finish marker (*F* foil, *E* etched).
                var f = FinishRx.Match(body);
                if (f.Success) line.Finish = char.ToUpperInvariant(f.Groups["f"].Value[0]) == 'E' ? CardFinish.Etched : CardFinish.Foil;

                // Set and collector number: "(M10) 146", or TCGplayer's "[M10] 146".
                int cut = body.Length;
                var sp = SetParenRx.Matches(body).LastOrDefault();
                Match? setMatch = sp;
                if (setMatch == null)
                {
                    var sb = SetBracketRx.Match(body);
                    if (sb.Success) setMatch = sb;
                }
                if (setMatch != null)
                {
                    line.SetCode = setMatch.Groups["set"].Value;
                    line.CollectorNumber = setMatch.Groups["cn"].Value;
                    cut = Math.Min(cut, setMatch.Index);
                }
                if (f.Success) cut = Math.Min(cut, f.Index);

                // Tags / categories ("[Removal,Ramp]", Archidekt "[Commander{top}]") and labels.
                foreach (Match t in TagsRx.Matches(body))
                {
                    if (setMatch != null && t.Index > setMatch.Index && t.Index < setMatch.Index + setMatch.Length) continue;
                    string tags = t.Groups["t"].Value;
                    string low = tags.ToLowerInvariant();
                    if (low.Contains("commander")) line.Section = ImportSection.Commander;
                    else if (low.Contains("sideboard")) line.Section = ImportSection.Sideboard;
                    else if (low.Contains("maybe")) line.Section = ImportSection.Maybe;
                    else if (tags.Trim().Length > 0) line.Notes = line.Notes.Length == 0 ? tags.Trim() : $"{line.Notes}, {tags.Trim()}";
                    cut = Math.Min(cut, t.Index);
                }
                var lbl = LabelRx.Match(body);
                if (lbl.Success) cut = Math.Min(cut, lbl.Index);

                line.Name = body[..cut].Trim();
                if (line.Name.Length == 0)
                {
                    result.Problems.Add($"Line {lineNo}: no card name in \"{raw}\" — skipped.");
                    continue;
                }
                result.Lines.Add(line);
            }
        }
    }

    /// <summary>BoE's own full collection file: every field, so a restore brings everything back.</summary>
    public static class BoeFormat
    {
        public const string TableHeader = "BoE Table";

        public static readonly string[] Headers =
        {
            TableHeader, "Scryfall ID", "Name", "Set Code", "Set Name", "Collector Number", "Finish",
            "Language", "Condition", "Quantity", "Price", "Notes", "Storage", "Favorite", "Date Added",
        };
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BreakersOfE.Models;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Writes export rows as:
    ///  • a TEXT LIST — "4 Lightning Bolt (M10) 146" (" *F*" / " *E*" when
    ///    asked), decks under Commander / Deck / Sideboard headers. MTG Deck
    ///    Tools, Moxfield, Archidekt and BoE's own import read it;
    ///  • a MANABOX CSV — ManaBox's columns, the file MTG Deck Tools uploads;
    ///  • a SPREADSHEET CSV — every detail of the list, for Excel.
    /// </summary>
    public static class ExportWriter
    {
        private static readonly string[] SectionOrder = { "Commander", "Deck", "Sideboard", "Tokens" };

        public static ExportResult Write(List<ExportRow> rows, ExportOptions o, string deckName)
        {
            // Same name, then set and number, so lists read like a binder.
            var sorted = rows
                .OrderBy(r => Array.IndexOf(SectionOrder, r.Section.Length == 0 ? "Deck" : r.Section))
                .ThenBy(r => r.IsToken ? 1 : 0)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.SetCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => NumberSort(r.CollectorNumber))
                .ThenBy(r => r.CollectorNumber, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => FinishOrder(r.Finish))
                .ToList();

            string text;
            int lines;
            string note = "";
            switch (o.Format)
            {
                case ExportFormat.ManaBoxCsv:
                    (text, lines) = ManaBox(sorted, o, deckName);
                    if (o.Source == ExportSource.Deck && sorted.Any(r => r.Section != "Deck"))
                        note = "A ManaBox CSV has no Commander / Sideboard parts — every card is listed together. Use the text list to keep them apart.";
                    break;
                case ExportFormat.SpreadsheetCsv:
                    (text, lines) = Spreadsheet(sorted, o);
                    break;
                case ExportFormat.MtgoDek:
                    (text, lines) = MtgoDek(sorted);
                    if (o.Source == ExportSource.Deck && sorted.Any(r => r.Section == "Commander"))
                        note = "MTGO keeps a Commander deck's commander in the sideboard: it's there in this file.";
                    break;
                case ExportFormat.MtgoCsv:
                    (text, lines) = MtgoCsv(sorted);
                    break;
                case ExportFormat.ArenaText:
                    (text, lines) = ArenaText(sorted, o);
                    break;
                default:
                    (text, lines) = TextList(sorted, o);
                    break;
            }
            return new ExportResult
            {
                Text = text,
                Lines = lines,
                Copies = sorted.Sum(r => r.Quantity),
                FileName = FileName(o, deckName),
                Note = note,
            };
        }

        // ── Text list ────────────────────────────────────────────────────
        private static (string, int) TextList(List<ExportRow> rows, ExportOptions o)
        {
            var sb = new StringBuilder();
            int n = 0;
            bool headers = o.Source == ExportSource.Deck;
            // Language and condition aren't part of a text line: the same printing (and finish, when marked) adds up.
            var groups = rows
                .GroupBy(r => (r.Section, Sid: r.ScryfallId.ToLowerInvariant(), r.Name, Set: r.SetCode.ToLowerInvariant(), r.CollectorNumber, Fin: o.MarkFinish ? r.Finish : ""))
                .Select(g => (First: g.First(), Qty: g.Sum(r => r.Quantity)))
                .ToList();
            string? current = null;
            foreach (var (r, qty) in groups)
            {
                if (headers && r.Section != current)
                {
                    if (current != null) sb.AppendLine();
                    sb.AppendLine(r.Section);
                    current = r.Section;
                }
                sb.Append(qty.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(r.Name);
                if (r.SetCode.Length > 0)
                {
                    sb.Append(" (").Append(r.SetCode.ToUpperInvariant()).Append(')');
                    if (r.CollectorNumber.Length > 0) sb.Append(' ').Append(r.CollectorNumber);
                }
                if (o.MarkFinish)
                {
                    if (r.Finish == CardFinish.Foil) sb.Append(" *F*");
                    else if (r.Finish == CardFinish.Etched) sb.Append(" *E*");
                }
                sb.AppendLine();
                n++;
            }
            return (sb.ToString(), n);
        }

        // ── MTGO .dek ────────────────────────────────────────────────────
        /// <summary>
        /// Magic Online's .dek: one &lt;Cards&gt; per MTGO id. A deck's sideboard
        /// (and commander, as MTGO wants it) is Sideboard="true". Rows without
        /// an MTGO id can't be loaded by MTGO and are left out.
        /// </summary>
        private static (string, int) MtgoDek(List<ExportRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<Deck xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">");
            sb.AppendLine("  <NetDeckID>0</NetDeckID>");
            sb.AppendLine("  <PreconstructedDeckID>0</PreconstructedDeckID>");
            int n = 0;
            var groups = rows.Where(r => r.MtgoId is > 0)
                .GroupBy(r => (Id: r.MtgoId!.Value, Side: r.Section is "Sideboard" or "Commander"))
                .Select(g => (g.Key.Id, g.Key.Side, Name: MtgoName(g.First()), Qty: g.Sum(r => r.Quantity)));
            foreach (var (id, side, name, qty) in groups)
            {
                sb.Append("  <Cards CatID=\"").Append(id.ToString(CultureInfo.InvariantCulture))
                  .Append("\" Quantity=\"").Append(qty.ToString(CultureInfo.InvariantCulture))
                  .Append("\" Sideboard=\"").Append(side ? "true" : "false")
                  .Append("\" Name=\"").Append(System.Security.SecurityElement.Escape(name))
                  .AppendLine("\" Annotation=\"0\" />");
                n++;
            }
            sb.AppendLine("</Deck>");
            return (sb.ToString(), n);
        }

        /// <summary>MTGO names a two-part card "Fire/Ice" and a double-faced one by its front.</summary>
        private static string MtgoName(ExportRow r) => FrontOnly(r) ? Front(r.Name) : r.Name.Replace(" // ", "/");

        private static bool FrontOnly(ExportRow r) =>
            r.Layout is "transform" or "modal_dfc" or "adventure" or "flip" or "meld" or "prepare";

        private static string Front(string name)
        {
            int i = name.IndexOf(" // ", StringComparison.Ordinal);
            return i > 0 ? name[..i] : name;
        }

        // ── MTGO CSV ─────────────────────────────────────────────────────
        private static (string, int) MtgoCsv(List<ExportRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine(CsvText.Line(new[] { "Card Name", "Quantity", "ID #", "Rarity", "Set", "Collector #", "Premium" }));
            foreach (var r in rows)
                sb.AppendLine(CsvText.Line(new[]
                {
                    MtgoName(r), r.Quantity.ToString(CultureInfo.InvariantCulture),
                    r.MtgoId?.ToString(CultureInfo.InvariantCulture) ?? "",
                    r.Rarity.Length > 0 ? char.ToUpperInvariant(r.Rarity[0]) + r.Rarity[1..].ToLowerInvariant() : "",
                    GameSetCode(r.SetCode), r.CollectorNumber, r.Finish == CardFinish.NonFoil ? "No" : "Yes",
                }));
            return (sb.ToString(), rows.Count);
        }

        // ── Arena text ───────────────────────────────────────────────────
        /// <summary>
        /// MTG Arena's import text (its Import button reads the clipboard):
        /// "4 Lightning Bolt (STA) 42" under Commander / Deck / Sideboard. No
        /// finish marks (Arena has no foils); double-faced and adventure cards
        /// by their front name, as Arena writes them.
        /// </summary>
        private static (string, int) ArenaText(List<ExportRow> rows, ExportOptions o)
        {
            var sb = new StringBuilder();
            int n = 0;
            bool headers = o.Source == ExportSource.Deck;
            var groups = rows
                .Where(r => !r.IsToken && r.Section != "Tokens")
                .GroupBy(r => (r.Section, Name: ArenaName(r), Set: r.SetCode.ToLowerInvariant(), r.CollectorNumber))
                .Select(g => (g.Key.Section, g.Key.Name, Set: g.First().SetCode, g.Key.CollectorNumber, Qty: g.Sum(r => r.Quantity)));
            string? current = null;
            foreach (var (section, name, set, cn, qty) in groups)
            {
                if (headers && section != current)
                {
                    if (current != null) sb.AppendLine();
                    sb.AppendLine(section.Length == 0 ? "Deck" : section);
                    current = section;
                }
                sb.Append(qty.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(name);
                if (set.Length > 0)
                {
                    sb.Append(" (").Append(GameSetCode(set)).Append(')');
                    if (cn.Length > 0) sb.Append(' ').Append(cn);
                }
                sb.AppendLine();
                n++;
            }
            return (sb.ToString(), n);
        }

        private static string ArenaName(ExportRow r) => FrontOnly(r) ? Front(r.Name) : r.Name;

        /// <summary>The few sets MTGO and Arena call by another code than Scryfall does.</summary>
        private static string GameSetCode(string scryfallCode) => scryfallCode.ToLowerInvariant() switch
        {
            "dom" => "DAR",
            "con" => "CON_",
            _ => scryfallCode.ToUpperInvariant(),
        };

        // ── ManaBox CSV ──────────────────────────────────────────────────
        private static readonly string[] ManaBoxHeaders =
        {
            "Binder Name", "Binder Type", "Name", "Set code", "Set name", "Collector number", "Foil", "Rarity",
            "Quantity", "ManaBox ID", "Scryfall ID", "Purchase price", "Misprint", "Altered", "Condition", "Language",
            "Purchase price currency",
        };

        private static (string, int) ManaBox(List<ExportRow> rows, ExportOptions o, string deckName)
        {
            var sb = new StringBuilder();
            sb.AppendLine(CsvText.Line(ManaBoxHeaders));
            int n = 0;
            foreach (var r in rows)
            {
                // Collection: the binder is where the cards are stored. A deck is its own "binder".
                string binder = o.Source switch
                {
                    ExportSource.Deck => deckName,
                    ExportSource.WantList => "Want List",
                    ExportSource.TradeBinder => "Trade Binder",
                    _ => r.Storage,
                };
                string binderType = o.Source switch
                {
                    ExportSource.Deck => "deck",
                    ExportSource.WantList => "list",
                    _ => binder.Length > 0 ? "binder" : "",
                };
                sb.AppendLine(CsvText.Line(new[]
                {
                    binder, binderType, r.Name, r.SetCode.ToUpperInvariant(), r.SetName, r.CollectorNumber,
                    r.Finish == CardFinish.Foil ? "foil" : r.Finish == CardFinish.Etched ? "etched" : "normal",
                    r.Rarity.ToLowerInvariant(),
                    r.Quantity.ToString(CultureInfo.InvariantCulture), "", r.ScryfallId, "", "false", "false",
                    ManaBoxCondition(r.Condition), LanguageCode(r.Language), "USD",
                }));
                n++;
            }
            return (sb.ToString(), n);
        }

        /// <summary>Our condition → ManaBox's words (blank → near_mint, as ManaBox assumes).</summary>
        private static string ManaBoxCondition(string c) => c switch
        {
            "Lightly Played" => "light_played",
            "Moderately Played" => "played",
            "Heavily Played" or "Damaged" => "poor",
            _ => "near_mint",
        };

        /// <summary>Our language → the short code apps use ("en", "ja", "zh_CN" …).</summary>
        public static string LanguageCode(string language) => language switch
        {
            "" or CardLanguage.English => "en",
            "Spanish" => "es",
            "French" => "fr",
            "German" => "de",
            "Italian" => "it",
            "Portuguese" => "pt",
            "Japanese" => "ja",
            "Korean" => "ko",
            "Russian" => "ru",
            "Chinese Simplified" => "zh_CN",
            "Chinese Traditional" => "zh_TW",
            "Phyrexian" => "ph",
            "Hebrew" => "he",
            "Latin" => "la",
            "Ancient Greek" => "grc",
            "Arabic" => "ar",
            "Sanskrit" => "sa",
            "Quenya" => "qya",
            _ => "en",
        };

        // ── Spreadsheet CSV ──────────────────────────────────────────────
        private static (string, int) Spreadsheet(List<ExportRow> rows, ExportOptions o)
        {
            var sb = new StringBuilder();
            string Num(int i) => i.ToString(CultureInfo.InvariantCulture);
            string Fin(ExportRow r) => CardFinish.Display(r.Finish);
            string Total(ExportRow r) => r.Price.HasValue ? CsvText.Num(r.Price.Value * r.Quantity) : "";
            switch (o.Source)
            {
                case ExportSource.Deck:
                    sb.AppendLine(CsvText.Line(new[] { "Section", "Name", "Set Code", "Set Name", "Collector Number", "Finish", "Quantity", "Price", "Total", "Scryfall ID" }));
                    foreach (var r in rows)
                        sb.AppendLine(CsvText.Line(new[] { r.Section, r.Name, r.SetCode.ToUpperInvariant(), r.SetName, r.CollectorNumber, Fin(r), Num(r.Quantity), CsvText.Num(r.Price), Total(r), r.ScryfallId }));
                    break;
                case ExportSource.WantList:
                    sb.AppendLine(CsvText.Line(new[] { "Name", "Set Code", "Set Name", "Collector Number", "Finish", "Quantity", "Market Price", "My Offer", "Notes", "Scryfall ID" }));
                    foreach (var r in rows)
                        sb.AppendLine(CsvText.Line(new[] { r.Name, r.SetCode.ToUpperInvariant(), r.SetName, r.CollectorNumber, Fin(r), Num(r.Quantity), CsvText.Num(r.Price), CsvText.Num(r.MyPrice), r.Notes, r.ScryfallId }));
                    break;
                case ExportSource.TradeBinder:
                    int pct = Math.Clamp(AppSettingsService.Current.TradePercent, 1, 100);
                    sb.AppendLine(CsvText.Line(new[] { "Name", "Set Code", "Set Name", "Collector Number", "Finish", "Language", "Condition", "Quantity", "Market Price", $"Trade Value ({pct}%)", "Asking Price", "Notes", "Scryfall ID" }));
                    foreach (var r in rows)
                        sb.AppendLine(CsvText.Line(new[]
                        {
                            r.Name, r.SetCode.ToUpperInvariant(), r.SetName, r.CollectorNumber, Fin(r), r.Language, r.Condition, Num(r.Quantity),
                            CsvText.Num(r.Price), r.Price.HasValue ? CsvText.Num(Math.Round(r.Price.Value * pct / 100m, 2)) : "",
                            CsvText.Num(r.MyPrice), r.Notes, r.ScryfallId,
                        }));
                    break;
                default:
                    sb.AppendLine(CsvText.Line(new[] { "Name", "Set Code", "Set Name", "Collector Number", "Finish", "Language", "Condition", "Quantity",
                                                       o.OnlyFree ? "Also in Decks / Binder" : "In Decks / Binder", "Price", "Total", "Storage", "Notes", "Scryfall ID" }));
                    foreach (var r in rows)
                        sb.AppendLine(CsvText.Line(new[]
                        {
                            r.Name, r.SetCode.ToUpperInvariant(), r.SetName, r.CollectorNumber, Fin(r), r.Language, r.Condition, Num(r.Quantity),
                            Num(r.InUse), CsvText.Num(r.Price), Total(r), r.Storage, r.Notes, r.ScryfallId,
                        }));
                    break;
            }
            return (sb.ToString(), rows.Count);
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private static string FileName(ExportOptions o, string deckName)
        {
            string what = o.Source switch
            {
                ExportSource.Deck => deckName.Length > 0 ? deckName : "Deck",
                ExportSource.WantList => "Want List",
                ExportSource.TradeBinder => "Trade Binder",
                ExportSource.MtgoCollection => "MTGO Collection",
                ExportSource.ArenaCollection => "Arena Collection",
                _ => o.OnlyFree ? "Collection (free copies)" : "Collection",
            };
            string kind = o.Format switch
            {
                ExportFormat.ManaBoxCsv => " - ManaBox.csv",
                ExportFormat.SpreadsheetCsv => ".csv",
                ExportFormat.MtgoDek => ".dek",
                ExportFormat.MtgoCsv => " - MTGO.csv",
                ExportFormat.ArenaText => " - Arena.txt",
                _ => ".txt",
            };
            string name = $"{what} {DateTime.Now:yyyy-MM-dd}{kind}";
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        private static int FinishOrder(string f) => f == CardFinish.NonFoil ? 0 : f == CardFinish.Foil ? 1 : 2;

        private static double NumberSort(string cn)
        {
            int end = 0;
            while (end < cn.Length && char.IsDigit(cn[end])) end++;
            return end > 0 && double.TryParse(cn[..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 1e9;
        }
    }
}

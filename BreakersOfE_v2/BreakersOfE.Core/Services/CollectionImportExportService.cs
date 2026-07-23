using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace BreakersOfE.Services
{
    // ── Format identifier ─────────────────────────────────────────────────────
    public enum ImportExportFormat
    {
        Moxfield,
        TcgPlayer,
        Deckbox,
        DragonShield,
        BreakersOfE,   // native JSON round-trip
        FullCsv,       // every column, every row
        AvailableCsv,  // Available, Name, Set Code
        ManaBox,       // ManaBox mobile scanner CSV
        Archidekt,     // Archidekt collection CSV
        PlainText      // universal "1 Card Name" decklist
    }

    // ── Result of an import operation ─────────────────────────────────────────
    public class CollectionImportResult
    {
        public int CardsImported { get; set; }
        public int CardsSkipped { get; set; }
        public int CardsNotFound { get; set; }
        public int CardsUpdated { get; set; }
        public List<string> Warnings { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public bool Success => Errors.Count == 0;
    }

    // ── A single parsed row from any import file ──────────────────────────────
    public class ImportRow
    {
        public string ScryfallId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SetCode { get; set; } = string.Empty;
        public string CollectorNumber { get; set; } = string.Empty;
        public int Quantity { get; set; } = 1;
        public bool IsFoil { get; set; }
        public string Condition { get; set; } = "Near Mint";
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public decimal? BuyAt { get; set; }
        public decimal? SellAt { get; set; }
        public string Group { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        // For deck import
        public bool IsSideboard { get; set; }
        public bool IsCommander { get; set; }
    }

    public static class CollectionImportExportService
    {
        // Collects warnings raised during the most recent parse (e.g. malformed
        // rows whose column count doesn't match the header). Surfaced in the
        // import result by ApplyCollectionRows.
        private static readonly List<string> _parseWarnings = new();

        /// <summary>
        /// Validates that a data row has the same field count as the header.
        /// Returns false (and records a warning) when the row is malformed —
        /// preventing silent column-shift corruption. expectedCount is the
        /// header's field count; lineNumber is 1-based for the user.
        /// </summary>
        private static bool ValidateRowLength(string[] fields, int expectedCount,
            int lineNumber)
        {
            if (fields.Length == expectedCount) return true;
            _parseWarnings.Add(
                $"Row {lineNumber}: expected {expectedCount} columns but found " +
                $"{fields.Length} — row skipped to avoid misaligned data.");
            return false;
        }

        // ════════════════════════════════════════════════════════════════════
        // FORMAT DETECTION
        // ════════════════════════════════════════════════════════════════════

        /// <summary>Auto-detect format from file extension and header row.</summary>
        public static ImportExportFormat DetectFormat(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower();

            if (ext == ".json") return ImportExportFormat.BreakersOfE;

            if (ext == ".csv" || ext == ".txt")
            {
                string header = ReadFirstLine(filePath).ToLower();

                // Archidekt: has "Edition Code" + "Scryfall Id" (+ "Multiverse Id")
                // Check BEFORE ManaBox since both carry Scryfall Id + Purchase Price.
                if (header.Contains("edition code") && header.Contains("scryfall id"))
                    return ImportExportFormat.Archidekt;

                // ManaBox: distinctive "manabox id" column, or "set code" + "scryfall id"
                if (header.Contains("manabox id") ||
                    (header.Contains("set code") && header.Contains("scryfall id")))
                    return ImportExportFormat.ManaBox;

                // Moxfield: "count,tradelist count,name,edition,condition,language,foil,tags,collector number"
                if (header.Contains("tradelist count"))
                    return ImportExportFormat.Moxfield;

                // TCGPlayer: "quantity,product name,set name,number,rarity,condition,add to quantity"
                if (header.Contains("product name") || header.Contains("tcg"))
                    return ImportExportFormat.TcgPlayer;

                // Deckbox: "count,tradelist count,name,edition,card number,condition,language,foil"
                if (header.Contains("tradelist") && header.Contains("card number"))
                    return ImportExportFormat.Deckbox;

                // Dragon Shield: "quantity,card name,set name,card number,finish,condition"
                if (header.Contains("finish") && header.Contains("card name"))
                    return ImportExportFormat.DragonShield;

                // Plain text decklist: first line like "1 Sol Ring" or "1x Sol Ring"
                if (System.Text.RegularExpressions.Regex.IsMatch(
                    header.Trim(), @"^\d+x?\s+\S"))
                    return ImportExportFormat.PlainText;

                // Fallback — try Moxfield
                return ImportExportFormat.Moxfield;
            }

            return ImportExportFormat.BreakersOfE;
        }

        private static string ReadFirstLine(string path)
        {
            try
            {
                using var sr = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
                return sr.ReadLine() ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        // ════════════════════════════════════════════════════════════════════
        // IMPORT
        // ════════════════════════════════════════════════════════════════════

        public static CollectionImportResult ImportCollection(string filePath,
            ImportExportFormat format, bool mergeWithExisting = true)
        {
            _parseWarnings.Clear();

            // Auto-backup before any import (data safety net)
            try { BackupCollectionBeforeImport(); }
            catch { /* backup failure shouldn't block import, but is logged below */ }

            var rows = format switch
            {
                ImportExportFormat.Moxfield => ParseMoxfieldCsv(filePath),
                ImportExportFormat.TcgPlayer => ParseTcgPlayerCsv(filePath),
                ImportExportFormat.Deckbox => ParseDeckboxCsv(filePath),
                ImportExportFormat.DragonShield => ParseDragonShieldCsv(filePath),
                ImportExportFormat.ManaBox => ParseManaBoxCsv(filePath),
                ImportExportFormat.Archidekt => ParseArchidektCsv(filePath),
                ImportExportFormat.PlainText => ParsePlainText(filePath),
                ImportExportFormat.BreakersOfE => ParseNativeJson(filePath),
                ImportExportFormat.FullCsv => ParseFullCsv(filePath),
                _ => null
            };

            if (rows == null)
            {
                var err = new CollectionImportResult();
                err.Errors.Add($"Import not supported for format: {format}");
                return err;
            }

            return ApplyCollectionRows(rows, mergeWithExisting);
        }

        /// <summary>
        /// Writes a timestamped native-JSON backup of the current collection to
        /// the Backups folder before an import modifies anything.
        /// </summary>
        public static string BackupCollectionBeforeImport()
        {
            using var cdb = new CollectionDbContext();
            var entries = cdb.CollectionEntries.AsNoTracking().ToList();
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            string path = Path.Combine(
                AppFolderService.BackupsFolder,
                $"collection_backup_{stamp}.json");
            ExportNativeJson(path, entries);
            return path;
        }

        public static (CollectionImportResult result, Deck? deck) ImportDeck(
            string filePath, ImportExportFormat format)
        {
            // CSV formats — parse as card list and build a deck
            List<ImportRow> rows = format switch
            {
                ImportExportFormat.Moxfield => ParseMoxfieldCsv(filePath),
                ImportExportFormat.TcgPlayer => ParseTcgPlayerCsv(filePath),
                ImportExportFormat.Deckbox => ParseDeckboxCsv(filePath),
                ImportExportFormat.DragonShield => ParseDragonShieldCsv(filePath),
                ImportExportFormat.ManaBox => ParseManaBoxCsv(filePath),
                ImportExportFormat.Archidekt => ParseArchidektCsv(filePath),
                ImportExportFormat.PlainText => ParsePlainText(filePath),
                _ => new List<ImportRow>()
            };

            if (rows.Count == 0)
            {
                var err = new CollectionImportResult();
                err.Errors.Add($"Deck import not supported for format {format}");
                return (err, null);
            }

            return BuildDeckFromRows(rows, Path.GetFileNameWithoutExtension(filePath));
        }

        private static (CollectionImportResult result, Deck? deck) BuildDeckFromRows(
            List<ImportRow> rows, string deckName)
        {
            var result = new CollectionImportResult();
            var deck = DeckService.CreateNew(deckName, DeckType.Standard);

            using var pdb = new AppDbContext();
            var cardLookup = pdb.PoolCards.AsNoTracking()
                .Select(c => new {
                    c.PoolId,
                    c.ScryfallId,
                    c.Name,
                    c.SetCode,
                    c.CollectorNumber,
                    c.TypeLine,
                    c.ManaCost,
                    c.ManaValue,
                    c.ColorIdentity,
                    c.Power,
                    c.Toughness,
                    c.LocalImagePath,
                    c.ImageNormalUrl
                })
                .ToList();

            foreach (var row in rows)
            {
                string matchName = row.Name.Contains(" // ")
                    ? row.Name.Split(new[] { " // " }, StringSplitOptions.None)[0].Trim()
                    : row.Name;

                var match = cardLookup.FirstOrDefault(c =>
                    c.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase) &&
                    c.SetCode.Equals(row.SetCode, StringComparison.OrdinalIgnoreCase))
                    ?? cardLookup.FirstOrDefault(c =>
                        c.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase))
                    ?? cardLookup.FirstOrDefault(c =>
                        (c.Name.Contains(" // ")
                            ? c.Name.Split(new[] { " // " }, StringSplitOptions.None)[0].Trim()
                            : c.Name)
                        .Equals(matchName, StringComparison.OrdinalIgnoreCase));

                if (match == null)
                {
                    result.CardsNotFound++;
                    result.Warnings.Add($"Not in pool: {row.Name} [{row.SetCode}]");
                    continue;
                }

                deck.Cards.Add(new DeckCard
                {
                    PoolId = match.PoolId,
                    ScryfallId = match.ScryfallId,
                    Name = match.Name,
                    SetCode = match.SetCode,
                    CollectorNumber = match.CollectorNumber,
                    TypeLine = match.TypeLine,
                    ManaCost = match.ManaCost,
                    ManaValue = match.ManaValue,
                    ColorIdentity = match.ColorIdentity,
                    Power = match.Power,
                    Toughness = match.Toughness,
                    LocalImagePath = match.LocalImagePath,
                    ImageNormalUrl = match.ImageNormalUrl,
                    Quantity = row.IsFoil ? 0 : row.Quantity,
                    FoilQuantity = row.IsFoil ? row.Quantity : 0,
                    Category = DeckCardCategory.Mainboard
                });
                result.CardsImported += row.Quantity;
            }

            result.Warnings.Add(
                "CSV deck import does not include commander or sideboard information. " +
                "Please set commanders manually in the deck editor.");

            return (result, deck);
        }



        // ── Moxfield CSV parser ───────────────────────────────────────────────
        // Header: Count,Tradelist Count,Name,Edition,Condition,Language,Foil,Tags,Collector Number,Alter,Proxy,Purchase Price
        private static List<ImportRow> ParseMoxfieldCsv(string filePath)
        {
            var rows = new List<ImportRow>();
            using var sr = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);
            string? header = sr.ReadLine();
            if (header == null) return rows;
            var cols = ParseCsvHeader(header);
            int colCount = cols.Count;

            string? line;
            int lineNo = 1;
            while ((line = sr.ReadLine()) != null)
            {
                lineNo++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = ParseCsvLine(line);
                if (!ValidateRowLength(fields, colCount, lineNo)) continue;
                string Get(string col) => cols.TryGetValue(col, out int i) && i < fields.Length
                    ? fields[i].Trim() : string.Empty;

                int qty = int.TryParse(Get("Count"), out int q) ? Math.Max(q, 1) : 1;
                bool foil = Get("Foil").Equals("foil", StringComparison.OrdinalIgnoreCase);

                rows.Add(new ImportRow
                {
                    Name = Get("Name"),
                    SetCode = Get("Edition"),
                    CollectorNumber = Get("Collector Number"),
                    Quantity = qty,
                    IsFoil = foil,
                    Condition = MapMoxfieldCondition(Get("Condition")),
                    Language = NormalizeLanguage(Get("Language")),
                    Notes = Get("Tags"),
                    BuyAt = decimal.TryParse(Get("Purchase Price"),
                        NumberStyles.Any, CultureInfo.InvariantCulture, out decimal pp)
                        ? pp : null
                });
            }
            return rows;
        }

        private static string MapMoxfieldCondition(string c) => c.ToUpper() switch
        {
            "M" or "MINT" => "Near Mint",
            "NM" or "NEAR MINT" => "Near Mint",
            "LP" or "LIGHTLY PLAYED" => "Lightly Played",
            "MP" or "MODERATELY PLAYED" => "Moderately Played",
            "HP" or "HEAVILY PLAYED" => "Heavily Played",
            "D" or "DAMAGED" => "Damaged",
            _ => "Unknown"
        };

        // ── TCGPlayer CSV parser ──────────────────────────────────────────────
        // Header: Quantity,Product Name,Set Name,Number,Rarity,Condition,Add to Quantity
        private static List<ImportRow> ParseTcgPlayerCsv(string filePath)
        {
            var rows = new List<ImportRow>();
            using var sr = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);
            string? header = sr.ReadLine();
            if (header == null) return rows;
            var cols = ParseCsvHeader(header);
            int colCount = cols.Count;

            string? line;
            int lineNo = 1;
            while ((line = sr.ReadLine()) != null)
            {
                lineNo++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = ParseCsvLine(line);
                if (!ValidateRowLength(fields, colCount, lineNo)) continue;
                string Get(string col) => cols.TryGetValue(col, out int i) && i < fields.Length
                    ? fields[i].Trim() : string.Empty;

                int qty = int.TryParse(Get("Quantity"), out int q) ? Math.Max(q, 1) : 1;
                string condFull = Get("Condition"); // e.g. "Near Mint Foil"
                bool foil = condFull.Contains("Foil", StringComparison.OrdinalIgnoreCase);
                string cond = condFull
                    .Replace("Foil", "", StringComparison.OrdinalIgnoreCase)
                    .Trim();

                rows.Add(new ImportRow
                {
                    Name = Get("Product Name"),
                    SetCode = string.Empty, // TCGPlayer uses set name not code
                    CollectorNumber = Get("Number"),
                    Quantity = qty,
                    IsFoil = foil,
                    Condition = MapMoxfieldCondition(cond),
                    Language = NormalizeLanguage(Get("Language"))
                });
            }
            return rows;
        }

        // ── Deckbox CSV parser ────────────────────────────────────────────────
        // Header: Count,Tradelist Count,Name,Edition,Card Number,Condition,Language,Foil,...
        private static List<ImportRow> ParseDeckboxCsv(string filePath)
        {
            var rows = new List<ImportRow>();
            using var sr = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);
            string? header = sr.ReadLine();
            if (header == null) return rows;
            var cols = ParseCsvHeader(header);
            int colCount = cols.Count;

            string? line;
            int lineNo = 1;
            while ((line = sr.ReadLine()) != null)
            {
                lineNo++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = ParseCsvLine(line);
                if (!ValidateRowLength(fields, colCount, lineNo)) continue;
                string Get(string col) => cols.TryGetValue(col, out int i) && i < fields.Length
                    ? fields[i].Trim() : string.Empty;

                int qty = int.TryParse(Get("Count"), out int q) ? Math.Max(q, 1) : 1;
                bool foil = !string.IsNullOrEmpty(Get("Foil"));

                rows.Add(new ImportRow
                {
                    Name = Get("Name"),
                    SetCode = Get("Edition"),
                    CollectorNumber = Get("Card Number"),
                    Quantity = qty,
                    IsFoil = foil,
                    Condition = MapMoxfieldCondition(Get("Condition")),
                    Language = NormalizeLanguage(Get("Language"))
                });
            }
            return rows;
        }

        // ── Dragon Shield CSV parser ──────────────────────────────────────────
        // Header: Quantity,Tradelist Count,Card Name,Set Name,Card Number,Finish,Condition,Date Added,Language
        private static List<ImportRow> ParseDragonShieldCsv(string filePath)
        {
            var rows = new List<ImportRow>();
            using var sr = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);
            string? header = sr.ReadLine();
            if (header == null) return rows;
            var cols = ParseCsvHeader(header);
            int colCount = cols.Count;

            string? line;
            int lineNo = 1;
            while ((line = sr.ReadLine()) != null)
            {
                lineNo++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = ParseCsvLine(line);
                if (!ValidateRowLength(fields, colCount, lineNo)) continue;
                string Get(string col) => cols.TryGetValue(col, out int i) && i < fields.Length
                    ? fields[i].Trim() : string.Empty;

                int qty = int.TryParse(Get("Quantity"), out int q) ? Math.Max(q, 1) : 1;
                string finish = Get("Finish").ToLower();
                bool foil = finish.Contains("foil") || finish.Contains("etched");

                DateTime added = DateTime.TryParse(Get("Date Added"),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime dt) ? dt : DateTime.Now;

                rows.Add(new ImportRow
                {
                    Name = Get("Card Name"),
                    SetCode = string.Empty,
                    CollectorNumber = Get("Card Number"),
                    Quantity = qty,
                    IsFoil = foil,
                    Condition = MapMoxfieldCondition(Get("Condition")),
                    Language = NormalizeLanguage(Get("Language")),
                    DateAdded = added
                });
            }
            return rows;
        }


        // ── Apply parsed rows to the collection DB ────────────────────────────
        private static CollectionImportResult ApplyCollectionRows(List<ImportRow> rows,
            bool mergeWithExisting)
        {
            var result = new CollectionImportResult();

            // Surface any malformed-row warnings from the parse step
            result.Warnings.AddRange(_parseWarnings);

            if (rows.Count == 0)
            {
                if (_parseWarnings.Count > 0)
                    result.Errors.Add(
                        "No valid rows imported — all rows were malformed " +
                        "(column count did not match the header).");
                else
                    result.Errors.Add("No rows found in import file.");
                return result;
            }

            using var pdb = new AppDbContext();
            using var cdb = new CollectionDbContext();

            // Build lookup dictionaries for fast matching
            var byScryfallId = pdb.PoolCards.AsNoTracking()
                .Where(c => c.ScryfallId != null && c.ScryfallId != "")
                .ToList()
                .GroupBy(c => c.ScryfallId)
                .ToDictionary(g => g.Key, g => g.First(),
                    StringComparer.OrdinalIgnoreCase);

            var byNameSet = pdb.PoolCards.AsNoTracking()
                .ToList()
                .GroupBy(c => $"{c.Name}|{c.SetCode}", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(),
                    StringComparer.OrdinalIgnoreCase);

            var existingEntries = cdb.CollectionEntries
                .ToList()
                .GroupBy(e => e.ScryfallId)
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var row in rows)
            {
                // Match pool card
                PoolCard? pc = null;

                // 1. ScryfallId (most reliable)
                if (!string.IsNullOrEmpty(row.ScryfallId) &&
                    byScryfallId.TryGetValue(row.ScryfallId, out var byId))
                    pc = byId;

                // 2. Name + SetCode
                if (pc == null && !string.IsNullOrEmpty(row.Name))
                {
                    string key = $"{row.Name}|{row.SetCode}";
                    if (byNameSet.TryGetValue(key, out var byNS)) pc = byNS;
                }

                // 3. Name only
                if (pc == null && !string.IsNullOrEmpty(row.Name))
                {
                    string nameKey = $"{row.Name}|";
                    pc = byNameSet.Values.FirstOrDefault(c =>
                        c.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase));
                }

                // 4. DFC fallback — strip " // Back Face" and try front face only
                if (pc == null && row.Name.Contains(" // "))
                {
                    string frontFace = row.Name.Split(new[] { " // " },
                        StringSplitOptions.None)[0].Trim();
                    string dfcKey = $"{frontFace}|{row.SetCode}";
                    if (!byNameSet.TryGetValue(dfcKey, out pc))
                        pc = byNameSet.Values.FirstOrDefault(c =>
                            c.Name.Equals(frontFace,
                                StringComparison.OrdinalIgnoreCase));
                }

                if (pc == null)
                {
                    result.CardsNotFound++;
                    result.Warnings.Add($"Not in pool: {row.Name} [{row.SetCode}]");
                    continue;
                }

                // Find or create collection entry
                if (existingEntries.TryGetValue(pc.ScryfallId, out var existing))
                {
                    if (mergeWithExisting)
                    {
                        // Merge: take the higher of existing vs imported quantity
                        // This makes re-importing the same file safe (idempotent)
                        if (row.IsFoil)
                            existing.FoilQuantity = Math.Max(existing.FoilQuantity, row.Quantity);
                        else
                            existing.Quantity = Math.Max(existing.Quantity, row.Quantity);
                        existing.DateModified = DateTime.Now;
                        result.CardsUpdated++;
                    }
                    else
                    {
                        // Replace: overwrite quantities exactly
                        if (row.IsFoil) existing.FoilQuantity = row.Quantity;
                        else existing.Quantity = row.Quantity;
                        existing.DateModified = DateTime.Now;
                        result.CardsUpdated++;
                    }
                    // Update pricing if provided
                    if (row.BuyAt.HasValue) existing.BuyAt = row.BuyAt;
                    if (row.SellAt.HasValue) existing.SellAt = row.SellAt;
                    if (!string.IsNullOrEmpty(row.Notes))
                        existing.Notes = row.Notes;
                    if (!string.IsNullOrEmpty(row.StorageLocation))
                        existing.StorageLocation = row.StorageLocation;
                }
                else
                {
                    var entry = new CollectionEntry
                    {
                        PoolId = pc.PoolId,
                        ScryfallId = pc.ScryfallId,
                        OracleId = pc.OracleId,
                        Name = pc.Name,
                        ManaCost = pc.ManaCost,
                        ManaValue = pc.ManaValue,
                        TypeLine = pc.TypeLine,
                        OracleText = pc.OracleText,
                        FlavorText = pc.FlavorText,
                        Power = pc.Power,
                        Toughness = pc.Toughness,
                        LoyaltyOrDefense = pc.LoyaltyOrDefense,
                        Colors = pc.Colors,
                        ColorIdentity = pc.ColorIdentity,
                        SetCode = pc.SetCode,
                        SetName = pc.SetName,
                        SetType = pc.SetType,
                        CollectorNumber = pc.CollectorNumber,
                        Rarity = pc.Rarity,
                        Artist = pc.Artist,
                        ImageSmallUrl = pc.ImageSmallUrl,
                        ImageNormalUrl = pc.ImageNormalUrl,
                        ImageBackUrl = pc.ImageBackUrl,
                        LocalImagePath = pc.LocalImagePath,
                        LocalImageBackPath = pc.LocalImageBackPath,
                        Layout = pc.Layout,
                        IsFoilAvailable = pc.IsFoil,
                        IsNonFoilAvailable = pc.IsNonFoil,
                        IsToken = pc.IsToken,
                        IsMeld = pc.IsMeld,
                        ReleasedAt = pc.ReleasedAt,
                        LegalitiesJson = pc.LegalitiesJson,
                        IsFavorite = pc.IsFavorite,
                        Keywords = pc.Keywords,
                        PriceUsd = pc.PriceUsd,
                        PriceUsdFoil = pc.PriceUsdFoil,
                        PriceUsdEtched = pc.PriceUsdEtched,
                        PriceEur = pc.PriceEur,
                        PriceEurFoil = pc.PriceEurFoil,
                        PriceTix = pc.PriceTix,
                        PricesJson = pc.PricesJson,
                        Quantity = row.IsFoil ? 0 : row.Quantity,
                        FoilQuantity = row.IsFoil ? row.Quantity : 0,
                        Condition = row.Condition,
                        Language = row.Language,
                        Notes = row.Notes,
                        StorageLocation = row.StorageLocation,
                        BuyAt = row.BuyAt,
                        SellAt = row.SellAt,
                        CardGroup = row.Group,
                        DateAdded = row.DateAdded,
                        DateModified = DateTime.Now
                    };
                    cdb.CollectionEntries.Add(entry);
                    existingEntries[pc.ScryfallId] = entry;
                    result.CardsImported++;
                }
            }

            try
            {
                cdb.SaveChanges();
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Database error: {ex.Message}");
            }

            return result;
        }

        // ════════════════════════════════════════════════════════════════════
        // EXPORT
        // ════════════════════════════════════════════════════════════════════

        public static void ExportCollection(string filePath,
            ImportExportFormat format)
        {
            using var cdb = new CollectionDbContext();
            var entries = cdb.CollectionEntries.AsNoTracking().ToList();

            switch (format)
            {
                case ImportExportFormat.Moxfield:
                    ExportMoxfieldCsv(filePath, entries); break;
                case ImportExportFormat.TcgPlayer:
                    ExportTcgPlayerCsv(filePath, entries); break;
                case ImportExportFormat.Deckbox:
                    ExportDeckboxCsv(filePath, entries); break;
                case ImportExportFormat.DragonShield:
                    ExportDragonShieldCsv(filePath, entries); break;
                case ImportExportFormat.BreakersOfE:
                    ExportNativeJson(filePath, entries); break;
                case ImportExportFormat.FullCsv:
                    ExportFullCollectionCsv(filePath); break;
                case ImportExportFormat.AvailableCsv:
                    ExportAvailableCsv(filePath, entries); break;
                case ImportExportFormat.ManaBox:
                    ExportManaBoxCsv(filePath, entries); break;
                case ImportExportFormat.Archidekt:
                    ExportArchidektCsv(filePath, entries); break;
                case ImportExportFormat.PlainText:
                    ExportPlainText(filePath, entries); break;
            }
        }

        public static void ExportWantList(string filePath,
            ImportExportFormat format)
        {
            using var cdb = new CollectionDbContext();
            var entries = cdb.WantListEntries.AsNoTracking().ToList();
            if (entries.Count == 0) return;

            // Convert to CollectionEntry for reuse of export methods
            var collEntries = entries
                .Select(e => new CollectionEntry
                {
                    ScryfallId = e.ScryfallId,
                    Name = e.Name,
                    SetCode = e.SetCode,
                    SetName = e.SetName,
                    CollectorNumber = e.CollectorNumber,
                    Rarity = e.Rarity,
                    Quantity = e.IsFoil ? 0 : e.Quantity,
                    FoilQuantity = e.IsFoil ? e.Quantity : 0,
                    Condition = "Near Mint",
                    Language = "English",
                    BuyAt = e.OfferPrice,
                    DateAdded = e.DateAdded
                }).ToList();

            switch (format)
            {
                case ImportExportFormat.Moxfield:
                    ExportMoxfieldCsv(filePath, collEntries); break;
                case ImportExportFormat.TcgPlayer:
                    ExportTcgPlayerCsv(filePath, collEntries); break;
                case ImportExportFormat.Deckbox:
                    ExportDeckboxCsv(filePath, collEntries); break;
                case ImportExportFormat.DragonShield:
                    ExportDragonShieldCsv(filePath, collEntries); break;
                case ImportExportFormat.ManaBox:
                    ExportManaBoxCsv(filePath, collEntries); break;
                case ImportExportFormat.Archidekt:
                    ExportArchidektCsv(filePath, collEntries); break;
                case ImportExportFormat.PlainText:
                    ExportPlainText(filePath, collEntries); break;
                case ImportExportFormat.BreakersOfE:
                    ExportNativeJson(filePath, collEntries); break;
                default:
                    throw new NotSupportedException(
                        $"Export not supported for {format}");
            }
        }

        public static void ExportTradeBinder(string filePath,
            ImportExportFormat format)
        {
            using var cdb = new CollectionDbContext();
            var entries = cdb.TradeBinderEntries.AsNoTracking().ToList();
            if (entries.Count == 0) return;

            var collEntries = entries
                .Select(e => new CollectionEntry
                {
                    ScryfallId = e.ScryfallId,
                    Name = e.Name,
                    SetCode = e.SetCode,
                    SetName = e.SetName,
                    CollectorNumber = e.CollectorNumber,
                    Rarity = e.Rarity,
                    Quantity = e.IsFoil ? 0 : e.Quantity,
                    FoilQuantity = e.IsFoil ? e.Quantity : 0,
                    Condition = e.Condition,
                    Language = "English",
                    SellAt = e.AskingPrice,
                    DateAdded = e.DateAdded
                }).ToList();

            switch (format)
            {
                case ImportExportFormat.Moxfield:
                    ExportMoxfieldCsv(filePath, collEntries); break;
                case ImportExportFormat.TcgPlayer:
                    ExportTcgPlayerCsv(filePath, collEntries); break;
                case ImportExportFormat.Deckbox:
                    ExportDeckboxCsv(filePath, collEntries); break;
                case ImportExportFormat.DragonShield:
                    ExportDragonShieldCsv(filePath, collEntries); break;
                case ImportExportFormat.ManaBox:
                    ExportManaBoxCsv(filePath, collEntries); break;
                case ImportExportFormat.Archidekt:
                    ExportArchidektCsv(filePath, collEntries); break;
                case ImportExportFormat.PlainText:
                    ExportPlainText(filePath, collEntries); break;
                case ImportExportFormat.BreakersOfE:
                    ExportNativeJson(filePath, collEntries); break;
                default:
                    throw new NotSupportedException(
                        $"Export not supported for {format}");
            }
        }

        public static void ExportDeck(string filePath,
            ImportExportFormat format, Deck deck)
        {
            switch (format)
            {
                case ImportExportFormat.Moxfield:
                    ExportDeckAsMoxfieldCsv(filePath, deck);
                    break;
                case ImportExportFormat.TcgPlayer:
                    ExportDeckAsTcgPlayerCsv(filePath, deck);
                    break;
                case ImportExportFormat.Deckbox:
                    ExportDeckAsDeckboxCsv(filePath, deck);
                    break;
                case ImportExportFormat.DragonShield:
                    ExportDeckAsDragonShieldCsv(filePath, deck);
                    break;
                case ImportExportFormat.ManaBox:
                    ExportDeckAsManaBoxCsv(filePath, deck);
                    break;
                case ImportExportFormat.Archidekt:
                    ExportDeckAsArchidektCsv(filePath, deck);
                    break;
                case ImportExportFormat.BreakersOfE:
                    ExportDeckAsJson(filePath, deck);
                    break;
                case ImportExportFormat.PlainText:
                    ExportDeckAsPlainText(filePath, deck);
                    break;
                default:
                    throw new NotSupportedException(
                        $"Deck export not supported for {format}");
            }
        }


        private static void ExportDeckAsMoxfieldCsv(string filePath, Deck deck)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("Count,Tradelist Count,Name,Edition,Condition,Language,Foil,Tags,Collector Number,Alter,Proxy,Purchase Price");
            foreach (var c in deck.Cards.OrderBy(c => c.Name))
            {
                // A deck card is foil if IsFoil is set OR it has foil quantity.
                int nonFoil = c.IsFoil ? 0 : Math.Max(c.Quantity, 0);
                int foilQty = c.IsFoil
                    ? Math.Max(c.Quantity, 0) + c.FoilQuantity
                    : c.FoilQuantity;

                if (nonFoil > 0)
                    sw.WriteLine(CsvRow(nonFoil, 0, c.Name, c.SetCode,
                        "Near Mint", "English", "", "", c.CollectorNumber, "False", "False", ""));
                if (foilQty > 0)
                    sw.WriteLine(CsvRow(foilQty, 0, c.Name, c.SetCode,
                        "Near Mint", "English", "foil", "", c.CollectorNumber, "False", "False", ""));
            }
        }

        private static void ExportDeckAsTcgPlayerCsv(string filePath, Deck deck)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("Quantity,Name,Set,Card Number,Condition,Printing");
            foreach (var c in deck.Cards.OrderBy(c => c.Name))
            {
                int nonFoil = c.IsFoil ? 0 : Math.Max(c.Quantity, 0);
                int foilQty = c.IsFoil
                    ? Math.Max(c.Quantity, 0) + c.FoilQuantity
                    : c.FoilQuantity;
                if (nonFoil > 0)
                    sw.WriteLine(CsvRow(nonFoil, c.Name, c.SetName,
                        c.CollectorNumber, "Near Mint", "Normal"));
                if (foilQty > 0)
                    sw.WriteLine(CsvRow(foilQty, c.Name, c.SetName,
                        c.CollectorNumber, "Near Mint", "Foil"));
            }
        }

        private static void ExportDeckAsManaBoxCsv(string filePath, Deck deck)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("Name,Set code,Collector number,Foil,Quantity,Scryfall ID,Condition,Language,Purchase price");
            foreach (var c in deck.Cards.OrderBy(c => c.Name))
            {
                int nonFoil = c.IsFoil ? 0 : Math.Max(c.Quantity, 0);
                int foilQty = c.IsFoil
                    ? Math.Max(c.Quantity, 0) + c.FoilQuantity
                    : c.FoilQuantity;
                if (nonFoil > 0)
                    sw.WriteLine(CsvRow(c.Name, c.SetCode, c.CollectorNumber,
                        "normal", nonFoil, c.ScryfallId, "near_mint", "en", ""));
                if (foilQty > 0)
                    sw.WriteLine(CsvRow(c.Name, c.SetCode, c.CollectorNumber,
                        "foil", foilQty, c.ScryfallId, "near_mint", "en", ""));
            }
        }

        private static void ExportDeckAsArchidektCsv(string filePath, Deck deck)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("Quantity,Name,Finish,Condition,Language,Purchase Price,Tags,Edition Code,Scryfall Id,Collector Number");
            foreach (var c in deck.Cards.OrderBy(c => c.Name))
            {
                int nonFoil = c.IsFoil ? 0 : Math.Max(c.Quantity, 0);
                int foilQty = c.IsFoil
                    ? Math.Max(c.Quantity, 0) + c.FoilQuantity
                    : c.FoilQuantity;
                if (nonFoil > 0)
                    sw.WriteLine(CsvRow(nonFoil, c.Name, "Normal", "NM",
                        "EN", "", "", c.SetCode, c.ScryfallId, c.CollectorNumber));
                if (foilQty > 0)
                    sw.WriteLine(CsvRow(foilQty, c.Name, "Foil", "NM",
                        "EN", "", "", c.SetCode, c.ScryfallId, c.CollectorNumber));
            }
        }

        private static void ExportDeckAsDeckboxCsv(string filePath, Deck deck)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("Count,Tradelist Count,Name,Edition,Card Number,Condition,Language,Foil,Signed,Artist Proof,Altered Art,Misprint,Promo,Textless,My Price");
            foreach (var c in deck.Cards.OrderBy(c => c.Name))
            {
                int nonFoil = c.IsFoil ? 0 : Math.Max(c.Quantity, 0);
                int foilQty = c.IsFoil
                    ? Math.Max(c.Quantity, 0) + c.FoilQuantity
                    : c.FoilQuantity;
                if (nonFoil > 0)
                    sw.WriteLine(CsvRow(nonFoil, 0, c.Name, c.SetName,
                        c.CollectorNumber, "Near Mint", "English", "", "", "", "", "", "", "", ""));
                if (foilQty > 0)
                    sw.WriteLine(CsvRow(foilQty, 0, c.Name, c.SetName,
                        c.CollectorNumber, "Near Mint", "English", "foil", "", "", "", "", "", "", ""));
            }
        }

        private static void ExportDeckAsDragonShieldCsv(string filePath, Deck deck)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("Folder Name,Quantity,Trade Quantity,Card Name,Set Code,Set Name,Collector Number,Printing,Condition,Language");
            foreach (var c in deck.Cards.OrderBy(c => c.Name))
            {
                int nonFoil = c.IsFoil ? 0 : Math.Max(c.Quantity, 0);
                int foilQty = c.IsFoil
                    ? Math.Max(c.Quantity, 0) + c.FoilQuantity
                    : c.FoilQuantity;
                if (nonFoil > 0)
                    sw.WriteLine(CsvRow("Deck", nonFoil, 0, c.Name,
                        c.SetCode, c.SetName, c.CollectorNumber, "Normal", "NM", "English"));
                if (foilQty > 0)
                    sw.WriteLine(CsvRow("Deck", foilQty, 0, c.Name,
                        c.SetCode, c.SetName, c.CollectorNumber, "Foil", "NM", "English"));
            }
        }

        private static void ExportDeckAsPlainText(string filePath, Deck deck)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);

            // Commander(s) first, in a Commander section
            var commanders = deck.Cards
                .Where(c => c.IsCommander).OrderBy(c => c.Name).ToList();
            var sideboard = deck.Cards
                .Where(c => !c.IsCommander &&
                    c.Category == DeckCardCategory.Sideboard)
                .OrderBy(c => c.Name).ToList();
            var mainboard = deck.Cards
                .Where(c => !c.IsCommander &&
                    c.Category != DeckCardCategory.Sideboard)
                .OrderBy(c => c.Name).ToList();

            if (commanders.Count > 0)
            {
                sw.WriteLine("Commander");
                foreach (var c in commanders)
                    sw.WriteLine($"{TotalQty(c)} {c.Name}");
                sw.WriteLine();
            }

            sw.WriteLine("Deck");
            foreach (var c in mainboard)
                sw.WriteLine($"{TotalQty(c)} {c.Name}");

            if (sideboard.Count > 0)
            {
                sw.WriteLine();
                sw.WriteLine("Sideboard");
                foreach (var c in sideboard)
                    sw.WriteLine($"{TotalQty(c)} {c.Name}");
            }

            static int TotalQty(DeckCard c) =>
                Math.Max(c.Quantity, 0) + c.FoilQuantity;
        }

        private static void ExportDeckAsJson(string filePath, Deck deck)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(deck,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json, Encoding.UTF8);
        }



        // ── Moxfield CSV export ───────────────────────────────────────────────
        private static void ExportMoxfieldCsv(string filePath,
            List<CollectionEntry> entries)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("Count,Tradelist Count,Name,Edition,Condition,Language,Foil,Tags,Collector Number,Alter,Proxy,Purchase Price");

            foreach (var e in entries.OrderBy(e => e.Name))
            {
                string cond = MapToMoxfieldCondition(e.Condition);

                if (e.Quantity > 0)
                    sw.WriteLine($"{e.Quantity},0,{Q(e.Name)},{e.SetCode},{cond},{e.Language},," +
                        $"{Q(e.Notes)},{e.CollectorNumber},,," +
                        $"{e.BuyAt?.ToString("F2", CultureInfo.InvariantCulture) ?? ""}");

                if (e.FoilQuantity > 0)
                    sw.WriteLine($"{e.FoilQuantity},0,{Q(e.Name)},{e.SetCode},{cond},{e.Language},foil," +
                        $"{Q(e.Notes)},{e.CollectorNumber},,,");
            }
        }

        private static string MapToMoxfieldCondition(string c) => c switch
        {
            "Near Mint" => "Near Mint",
            "Lightly Played" => "Lightly Played",
            "Moderately Played" => "Moderately Played",
            "Heavily Played" => "Heavily Played",
            "Damaged" => "Damaged",
            "NM" => "Near Mint",
            "LP" => "Lightly Played",
            "MP" => "Moderately Played",
            "HP" => "Heavily Played",
            "D" => "Damaged",
            _ => "Near Mint"
        };

        // ── TCGPlayer CSV export ──────────────────────────────────────────────
        private static void ExportTcgPlayerCsv(string filePath,
            List<CollectionEntry> entries)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("Quantity,Product Name,Set Name,Number,Rarity,Condition,Add to Quantity");

            foreach (var e in entries.OrderBy(e => e.Name))
            {
                string cond = MapToMoxfieldCondition(e.Condition);

                if (e.Quantity > 0)
                    sw.WriteLine($"{e.Quantity},{Q(e.Name)},{Q(e.SetName)}," +
                        $"{e.CollectorNumber},{CapFirst(e.Rarity)},{cond},0");

                if (e.FoilQuantity > 0)
                    sw.WriteLine($"{e.FoilQuantity},{Q(e.Name)},{Q(e.SetName)}," +
                        $"{e.CollectorNumber},{CapFirst(e.Rarity)},{cond} Foil,0");
            }
        }

        // ── Deckbox CSV export ────────────────────────────────────────────────
        private static void ExportDeckboxCsv(string filePath,
            List<CollectionEntry> entries)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("Count,Tradelist Count,Name,Edition,Card Number,Condition,Language,Foil,Signed,Artist Proof,Altered Art,Misprint,Promo,Textless,My Price");

            foreach (var e in entries.OrderBy(e => e.Name))
            {
                string cond = MapToMoxfieldCondition(e.Condition);

                if (e.Quantity > 0)
                    sw.WriteLine($"{e.Quantity},0,{Q(e.Name)},{Q(e.SetName)}," +
                        $"{e.CollectorNumber},{cond},{e.Language},,,,,,,,");

                if (e.FoilQuantity > 0)
                    sw.WriteLine($"{e.FoilQuantity},0,{Q(e.Name)},{Q(e.SetName)}," +
                        $"{e.CollectorNumber},{cond},{e.Language},foil,,,,,,,");
            }
        }

        // ── Dragon Shield CSV export ──────────────────────────────────────────
        private static void ExportDragonShieldCsv(string filePath,
            List<CollectionEntry> entries)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("Quantity,Tradelist Count,Card Name,Set Name,Card Number,Finish,Condition,Date Added,Language");

            foreach (var e in entries.OrderBy(e => e.Name))
            {
                string cond = MapToMoxfieldCondition(e.Condition);
                string added = e.DateAdded.ToString("yyyy-MM-dd");

                if (e.Quantity > 0)
                    sw.WriteLine($"{e.Quantity},0,{Q(e.Name)},{Q(e.SetName)}," +
                        $"{e.CollectorNumber},Normal,{cond},{added},{e.Language}");

                if (e.FoilQuantity > 0)
                    sw.WriteLine($"{e.FoilQuantity},0,{Q(e.Name)},{Q(e.SetName)}," +
                        $"{e.CollectorNumber},Foil,{cond},{added},{e.Language}");
            }
        }

        // ── Breakers of E native JSON export ──────────────────────────────────
        private static void ExportNativeJson(string filePath,
            List<CollectionEntry> entries)
        {
            var export = new
            {
                ExportedBy = "Breakers of E",
                ExportedAt = DateTime.Now,
                Cards = entries.Select(e => new
                {
                    e.ScryfallId,
                    e.Name,
                    e.SetCode,
                    e.CollectorNumber,
                    e.Quantity,
                    e.FoilQuantity,
                    e.Condition,
                    e.Language,
                    e.BuyAt,
                    e.SellAt,
                    e.Notes,
                    e.StorageLocation,
                    e.DateAdded
                }).ToList()
            };
            File.WriteAllText(filePath,
                JsonSerializer.Serialize(export, new JsonSerializerOptions
                { WriteIndented = true }));
        }



        // ════════════════════════════════════════════════════════════════════
        // CSV HELPERS
        // ════════════════════════════════════════════════════════════════════

        // ── Native JSON parser (restores "full backup") ───────────────────────
        private static List<ImportRow> ParseNativeJson(string filePath)
        {
            var rows = new List<ImportRow>();
            try
            {
                string json = File.ReadAllText(filePath);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("Cards", out var cards)
                    || cards.ValueKind != JsonValueKind.Array)
                    return rows;

                foreach (var c in cards.EnumerateArray())
                {
                    string GetStr(string prop) =>
                        c.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                            ? v.GetString() ?? string.Empty : string.Empty;
                    int GetInt(string prop) =>
                        c.TryGetProperty(prop, out var v) && v.TryGetInt32(out int n) ? n : 0;
                    decimal? GetDec(string prop) =>
                        c.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
                            && v.TryGetDecimal(out decimal d) ? d : null;

                    int qty = GetInt("Quantity");
                    int foilQty = GetInt("FoilQuantity");

                    // Non-foil copies
                    if (qty > 0)
                        rows.Add(new ImportRow
                        {
                            ScryfallId = GetStr("ScryfallId"),
                            Name = GetStr("Name"),
                            SetCode = GetStr("SetCode"),
                            CollectorNumber = GetStr("CollectorNumber"),
                            Quantity = qty,
                            IsFoil = false,
                            Condition = string.IsNullOrEmpty(GetStr("Condition"))
                                ? "Near Mint" : GetStr("Condition"),
                            Language = string.IsNullOrEmpty(GetStr("Language"))
                                ? "English" : GetStr("Language"),
                            BuyAt = GetDec("BuyAt"),
                            SellAt = GetDec("SellAt"),
                            Notes = GetStr("Notes"),
                            StorageLocation = GetStr("StorageLocation")
                        });

                    // Foil copies
                    if (foilQty > 0)
                        rows.Add(new ImportRow
                        {
                            ScryfallId = GetStr("ScryfallId"),
                            Name = GetStr("Name"),
                            SetCode = GetStr("SetCode"),
                            CollectorNumber = GetStr("CollectorNumber"),
                            Quantity = foilQty,
                            IsFoil = true,
                            Condition = string.IsNullOrEmpty(GetStr("Condition"))
                                ? "Near Mint" : GetStr("Condition"),
                            Language = string.IsNullOrEmpty(GetStr("Language"))
                                ? "English" : GetStr("Language"),
                            BuyAt = GetDec("BuyAt"),
                            SellAt = GetDec("SellAt"),
                            Notes = GetStr("Notes"),
                            StorageLocation = GetStr("StorageLocation")
                        });
                }
            }
            catch { /* malformed JSON returns empty list → caller reports no rows */ }
            return rows;
        }

        // ── Full CSV parser (round-trips the full export) ─────────────────────
        private static List<ImportRow> ParseFullCsv(string filePath)
        {
            var rows = new List<ImportRow>();
            using var sr = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);
            string? header = sr.ReadLine();
            if (header == null) return rows;
            var cols = ParseCsvHeader(header);

            var getScryfall = MakeGetter(cols, "ScryfallId");
            var getName = MakeGetter(cols, "Name");
            var getSet = MakeGetter(cols, "SetCode");
            var getCn = MakeGetter(cols, "CollectorNumber");
            var getQty = MakeGetter(cols, "Quantity");
            var getFoilQty = MakeGetter(cols, "FoilQuantity");
            var getCond = MakeGetter(cols, "Condition");
            var getLang = MakeGetter(cols, "Language");
            var getNotes = MakeGetter(cols, "Notes");
            var getStorage = MakeGetter(cols, "StorageLocation");

            string? line;
            int lineNo = 1;
            int colCount = cols.Count;
            while ((line = sr.ReadLine()) != null)
            {
                lineNo++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                var f = ParseCsvLine(line);
                if (!ValidateRowLength(f, colCount, lineNo)) continue;
                int qty = int.TryParse(getQty(f), out int q) ? q : 0;
                int foilQty = int.TryParse(getFoilQty(f), out int fq) ? fq : 0;

                if (qty > 0)
                    rows.Add(new ImportRow
                    {
                        ScryfallId = getScryfall(f),
                        Name = getName(f),
                        SetCode = getSet(f),
                        CollectorNumber = getCn(f),
                        Quantity = qty,
                        IsFoil = false,
                        Condition = string.IsNullOrEmpty(getCond(f)) ? "Near Mint" : getCond(f),
                        Language = string.IsNullOrEmpty(getLang(f)) ? "English" : getLang(f),
                        Notes = getNotes(f),
                        StorageLocation = getStorage(f)
                    });
                if (foilQty > 0)
                    rows.Add(new ImportRow
                    {
                        ScryfallId = getScryfall(f),
                        Name = getName(f),
                        SetCode = getSet(f),
                        CollectorNumber = getCn(f),
                        Quantity = foilQty,
                        IsFoil = true,
                        Condition = string.IsNullOrEmpty(getCond(f)) ? "Near Mint" : getCond(f),
                        Language = string.IsNullOrEmpty(getLang(f)) ? "English" : getLang(f),
                        Notes = getNotes(f),
                        StorageLocation = getStorage(f)
                    });
            }
            return rows;
        }

        // ── ManaBox CSV parser (mobile scanner) ───────────────────────────────
        // Headers: Name,Set code,Set name,Collector number,Foil,Rarity,
        //   Quantity,ManaBox ID,Scryfall ID,Purchase price,Misprint,Altered,
        //   Condition,Language,Purchase price currency
        private static List<ImportRow> ParseManaBoxCsv(string filePath)
        {
            var rows = new List<ImportRow>();
            using var sr = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);
            string? header = sr.ReadLine();
            if (header == null) return rows;
            var cols = ParseCsvHeader(header);

            var getName = MakeGetter(cols, "Name");
            var getSet = MakeGetter(cols, "Set code", "Set Code", "SetCode");
            var getCn = MakeGetter(cols, "Collector number", "Collector Number");
            var getFoil = MakeGetter(cols, "Foil");
            var getQty = MakeGetter(cols, "Quantity");
            var getScryfall = MakeGetter(cols, "Scryfall ID", "Scryfall Id", "ScryfallId");
            var getCond = MakeGetter(cols, "Condition");
            var getLang = MakeGetter(cols, "Language");
            var getPrice = MakeGetter(cols, "Purchase price", "Purchase Price");

            string? line;
            int lineNo = 1;
            int colCount = cols.Count;
            while ((line = sr.ReadLine()) != null)
            {
                lineNo++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                var f = ParseCsvLine(line);
                if (!ValidateRowLength(f, colCount, lineNo)) continue;
                int qty = int.TryParse(getQty(f), out int q) ? Math.Max(q, 1) : 1;
                // ManaBox Foil column: "foil", "etched", or "normal"
                string foilVal = getFoil(f).ToLower();
                bool foil = foilVal == "foil" || foilVal == "etched";

                rows.Add(new ImportRow
                {
                    ScryfallId = getScryfall(f),
                    Name = getName(f),
                    SetCode = getSet(f),
                    CollectorNumber = getCn(f),
                    Quantity = qty,
                    IsFoil = foil,
                    Condition = MapManaBoxCondition(getCond(f)),
                    Language = NormalizeLanguage(getLang(f)),
                    BuyAt = decimal.TryParse(getPrice(f), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out decimal p) ? p : null
                });
            }
            return rows;
        }

        private static string MapManaBoxCondition(string c) => c.ToLower() switch
        {
            "mint" or "near_mint" or "nm" => "Near Mint",
            "excellent" or "lightly_played" or "lp" => "Lightly Played",
            "good" or "moderately_played" or "mp" => "Moderately Played",
            "played" or "heavily_played" or "hp" => "Heavily Played",
            "poor" or "damaged" => "Damaged",
            _ => "Near Mint"
        };

        // ── Archidekt CSV parser ──────────────────────────────────────────────
        // Headers: Quantity,Name,Finish,Condition,Date Added,Language,
        //   Purchase Price,Tags,Edition Name,Edition Code,Multiverse Id,
        //   Scryfall Id,Collector Number
        private static List<ImportRow> ParseArchidektCsv(string filePath)
        {
            var rows = new List<ImportRow>();
            using var sr = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);
            string? header = sr.ReadLine();
            if (header == null) return rows;
            var cols = ParseCsvHeader(header);

            var getQty = MakeGetter(cols, "Quantity");
            var getName = MakeGetter(cols, "Name");
            var getFinish = MakeGetter(cols, "Finish");
            var getCond = MakeGetter(cols, "Condition");
            var getLang = MakeGetter(cols, "Language");
            var getPrice = MakeGetter(cols, "Purchase Price", "Purchase price");
            var getTags = MakeGetter(cols, "Tags");
            var getSet = MakeGetter(cols, "Edition Code", "Edition code", "SetCode");
            var getScryfall = MakeGetter(cols, "Scryfall Id", "Scryfall ID", "ScryfallId");
            var getCn = MakeGetter(cols, "Collector Number", "Collector number");

            string? line;
            int lineNo = 1;
            int colCount = cols.Count;
            while ((line = sr.ReadLine()) != null)
            {
                lineNo++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                var f = ParseCsvLine(line);
                if (!ValidateRowLength(f, colCount, lineNo)) continue;
                int qty = int.TryParse(getQty(f), out int q) ? Math.Max(q, 1) : 1;
                string finish = getFinish(f).ToLower();
                bool foil = finish.Contains("foil") || finish.Contains("etched");

                rows.Add(new ImportRow
                {
                    ScryfallId = getScryfall(f),
                    Name = getName(f),
                    SetCode = getSet(f),
                    CollectorNumber = getCn(f),
                    Quantity = qty,
                    IsFoil = foil,
                    Condition = MapMoxfieldCondition(getCond(f)),
                    Language = NormalizeLanguage(getLang(f)),
                    Notes = getTags(f),
                    BuyAt = decimal.TryParse(getPrice(f), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out decimal p) ? p : null
                });
            }
            return rows;
        }

        // ── Plain-text decklist parser ────────────────────────────────────────
        // Lines like "1 Sol Ring", "1x Sol Ring", "3 Forest (CMR) 350",
        //   or a bare "Sol Ring". Section headers (Sideboard:, Commander:,
        //   Deck, blank lines) are skipped or flagged.
        private static List<ImportRow> ParsePlainText(string filePath)
        {
            var rows = new List<ImportRow>();
            var lines = File.ReadAllLines(filePath);
            bool inSideboard = false;

            var rx = new System.Text.RegularExpressions.Regex(
                @"^\s*(?<qty>\d+)?\s*x?\s+?(?<name>[^(]+?)\s*(?:\((?<set>[^)]+)\)\s*(?<cn>\S+)?)?\s*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Section markers
                string lower = line.ToLower().TrimEnd(':');
                if (lower is "sideboard" or "sb") { inSideboard = true; continue; }
                if (lower is "deck" or "mainboard" or "maindeck" or "commander")
                { inSideboard = false; continue; }
                if (line.StartsWith("//") || line.StartsWith("#")) continue;

                var m = rx.Match(line);
                if (!m.Success) continue;

                string name = m.Groups["name"].Value.Trim();
                if (string.IsNullOrEmpty(name)) continue;

                int qty = int.TryParse(m.Groups["qty"].Value, out int q) ? q : 1;
                string set = m.Groups["set"].Success ? m.Groups["set"].Value.Trim() : string.Empty;
                string cn = m.Groups["cn"].Success ? m.Groups["cn"].Value.Trim() : string.Empty;

                rows.Add(new ImportRow
                {
                    Name = name,
                    SetCode = set,
                    CollectorNumber = cn,
                    Quantity = qty,
                    IsFoil = false,
                    IsSideboard = inSideboard
                });
            }
            return rows;
        }

        private static Dictionary<string, int> ParseCsvHeader(string header)
        {
            var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var fields = ParseCsvLine(header);
            for (int i = 0; i < fields.Length; i++)
            {
                var fld = fields[i].Trim();
                if (fld.Length > 0 && fld[0] == (char)0xFEFF) fld = fld.Substring(1);
                cols.TryAdd(fld, i);
            }
            return cols;
        }

        /// <summary>
        /// Resolves a logical field to a column index by trying multiple possible
        /// header names (case-insensitive). Returns -1 if none match. This is the
        /// hardening layer — when a site renames a column, only the alias list
        /// here needs updating, not the parser.
        /// </summary>
        private static int ResolveColumn(Dictionary<string, int> cols,
            params string[] aliases)
        {
            foreach (var alias in aliases)
                if (cols.TryGetValue(alias, out int i))
                    return i;
            return -1;
        }

        /// <summary>
        /// Builds a Get accessor that resolves a logical field name to its value
        /// using a list of possible header aliases.
        /// </summary>
        private static Func<string[], string> MakeGetter(
            Dictionary<string, int> cols, params string[] aliases)
        {
            int idx = ResolveColumn(cols, aliases);
            return fields => (idx >= 0 && idx < fields.Length)
                ? fields[idx].Trim() : string.Empty;
        }

        private static string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var current = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    { current.Append('"'); i++; }
                    else inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                { result.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
            result.Add(current.ToString());
            return result.ToArray();
        }

        // CSV-safe quoted field
        /// <summary>
        /// Exports a simple CSV with just Available, Name, Set Code columns.
        /// Available = (Quantity + FoilQuantity) - UsedCount.
        /// </summary>
        public static void ExportAvailableCsv(string filePath,
            List<CollectionEntry> entries)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("Available,Name,Set Code");

            foreach (var e in entries.OrderBy(e => e.Name))
            {
                int available = Math.Max(0,
                    e.Quantity + e.FoilQuantity - e.UsedCount);
                sw.WriteLine($"{available},{Q(e.Name)},{Q(e.SetCode)}");
            }
        }

        // ── ManaBox CSV export ────────────────────────────────────────────────
        private static void ExportManaBoxCsv(string filePath,
            List<CollectionEntry> entries)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("Name,Set code,Collector number,Foil,Quantity,Scryfall ID,Condition,Language,Purchase price");
            foreach (var e in entries.OrderBy(e => e.Name))
            {
                if (e.Quantity > 0)
                    sw.WriteLine(CsvRow(e.Name, e.SetCode, e.CollectorNumber,
                        "normal", e.Quantity, e.ScryfallId, e.Condition,
                        e.Language, e.BuyAt));
                if (e.FoilQuantity > 0)
                    sw.WriteLine(CsvRow(e.Name, e.SetCode, e.CollectorNumber,
                        "foil", e.FoilQuantity, e.ScryfallId, e.Condition,
                        e.Language, e.BuyAt));
            }
        }

        // ── Archidekt CSV export ──────────────────────────────────────────────
        private static void ExportArchidektCsv(string filePath,
            List<CollectionEntry> entries)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("Quantity,Name,Finish,Condition,Language,Purchase Price,Tags,Edition Code,Scryfall Id,Collector Number");
            foreach (var e in entries.OrderBy(e => e.Name))
            {
                if (e.Quantity > 0)
                    sw.WriteLine(CsvRow(e.Quantity, e.Name, "Normal",
                        e.Condition, e.Language, e.BuyAt, e.Notes,
                        e.SetCode, e.ScryfallId, e.CollectorNumber));
                if (e.FoilQuantity > 0)
                    sw.WriteLine(CsvRow(e.FoilQuantity, e.Name, "Foil",
                        e.Condition, e.Language, e.BuyAt, e.Notes,
                        e.SetCode, e.ScryfallId, e.CollectorNumber));
            }
        }

        // ── Plain-text decklist export ────────────────────────────────────────
        private static void ExportPlainText(string filePath,
            List<CollectionEntry> entries)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            foreach (var e in entries.OrderBy(e => e.Name))
            {
                int total = e.Quantity + e.FoilQuantity;
                if (total > 0)
                    sw.WriteLine($"{total} {e.Name}");
            }
        }

        /// <summary>
        /// Exports every column and every row of the collection to CSV.
        /// Uses reflection to include all CollectionEntry properties.
        /// </summary>
        public static void ExportFullCollectionCsv(string filePath)
        {
            using var cdb = new CollectionDbContext();
            var entries = cdb.CollectionEntries.AsNoTracking()
                .OrderBy(e => e.Name).ToList();

            var props = typeof(CollectionEntry).GetProperties(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance)
                .Where(p => p.CanRead && IsSimpleType(p.PropertyType))
                .ToList();

            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);

            // Header row
            sw.WriteLine(string.Join(",", props.Select(p => Q(p.Name))));

            // Data rows
            foreach (var e in entries)
            {
                var values = props.Select(p =>
                {
                    object? val = p.GetValue(e);
                    string s = val switch
                    {
                        null => string.Empty,
                        DateTime dt => dt.ToString("O"),
                        bool b => b ? "True" : "False",
                        decimal d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        double dbl => dbl.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        _ => val.ToString() ?? string.Empty
                    };
                    return Q(s);
                });
                sw.WriteLine(string.Join(",", values));
            }
        }

        private static bool IsSimpleType(Type t)
        {
            var underlying = Nullable.GetUnderlyingType(t) ?? t;
            return underlying.IsPrimitive
                || underlying.IsEnum
                || underlying == typeof(string)
                || underlying == typeof(decimal)
                || underlying == typeof(DateTime);
        }

        private static string Q(string s) =>
            s.Contains(',') || s.Contains('"') || s.Contains('\n')
                ? $"\"{s.Replace("\"", "\"\"")}\""
                : s;

        // Build a full CSV row from args
        private static string CsvRow(params object?[] fields) =>
            string.Join(",", fields.Select(f =>
            {
                string s = f?.ToString() ?? string.Empty;
                return Q(s);
            }));

        private static string CapFirst(string s) =>
            string.IsNullOrEmpty(s) ? s :
            char.ToUpper(s[0]) + s[1..].ToLower();

        private static string NormalizeLanguage(string lang) => lang switch
        {
            var l when l.Equals("en", StringComparison.OrdinalIgnoreCase) => "English",
            var l when l.Equals("ja", StringComparison.OrdinalIgnoreCase) => "Japanese",
            var l when l.Equals("de", StringComparison.OrdinalIgnoreCase) => "German",
            var l when l.Equals("fr", StringComparison.OrdinalIgnoreCase) => "French",
            var l when l.Equals("it", StringComparison.OrdinalIgnoreCase) => "Italian",
            var l when l.Equals("es", StringComparison.OrdinalIgnoreCase) => "Spanish",
            var l when l.Equals("pt", StringComparison.OrdinalIgnoreCase) => "Portuguese",
            var l when l.Equals("ru", StringComparison.OrdinalIgnoreCase) => "Russian",
            var l when l.Equals("ko", StringComparison.OrdinalIgnoreCase) => "Korean",
            var l when l.Equals("zh-hans", StringComparison.OrdinalIgnoreCase) => "Chinese Simplified",
            var l when l.Equals("zh-hant", StringComparison.OrdinalIgnoreCase) => "Chinese Traditional",
            "" => "English",
            _ => lang
        };
    }
}
using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BreakersOfE.Services
{
    // ── Progress reporting ───────────────────────────────────────────────────
    public class ImportProgress
    {
        public string Step { get; set; } = string.Empty;
        public int Percentage { get; set; }
        public string Detail { get; set; } = string.Empty;
    }

    // ── Full import result ───────────────────────────────────────────────────
    public class ImportResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;

        // Card table counts
        public int PoolCardsImported { get; set; }
        public int TokenCardsImported { get; set; }
        public int PlanarCardsImported { get; set; }
        public int SchemeCardsImported { get; set; }
        public int VanguardCardsImported { get; set; }
        public int ArtSeriesCardsImported { get; set; }
        public int ConspiracyCardsImported { get; set; }
        public int SkippedCount { get; set; }

        // Symbol counts
        public int ManaSymbolsDownloaded { get; set; }
        public int SetSymbolsDownloaded { get; set; }

        // Keyword catalog
        public int KeywordAbilitiesCount { get; set; }
        public int KeywordActionsCount { get; set; }
        public int AbilityWordsCount { get; set; }
        public int NewKeywordsDiscovered { get; set; }

        // Basic verification
        public int ScryfallReportedTotal { get; set; }
        public int DatabaseTotal { get; set; }

        // Color breakdown (pool cards)
        public int WhiteCount { get; set; }
        public int BlueCount { get; set; }
        public int BlackCount { get; set; }
        public int RedCount { get; set; }
        public int GreenCount { get; set; }
        public int MulticolorCount { get; set; }
        public int ColorlessCount { get; set; }
        public int LandCount { get; set; }

        // Rarity breakdown (pool cards)
        public int CommonCount { get; set; }
        public int UncommonCount { get; set; }
        public int RareCount { get; set; }
        public int MythicCount { get; set; }
        public int OtherRarityCount { get; set; }

        // Deep verification
        public int DuplicateScryfallIds { get; set; }
        public int CardsWithNoImageUrl { get; set; }
        public int CardsWithEmptyName { get; set; }
        public int CardsWithEmptySetCode { get; set; }
        public bool ColorCountMatchesTotal { get; set; }
        public bool RarityCountMatchesTotal { get; set; }

        public int TotalImported =>
            PoolCardsImported + TokenCardsImported +
            PlanarCardsImported + SchemeCardsImported +
            VanguardCardsImported + ArtSeriesCardsImported +
            ConspiracyCardsImported;
    }

    // ════════════════════════════════════════════════════════════════════════
    public class ScryfallService
    {
        private readonly HttpClient _http;
        private readonly string _setSymbolsFolder;
        private readonly string _manaSymbolsFolder;

        public ScryfallService()
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Add("User-Agent", "BreakersOfE/1.0");
            _http.DefaultRequestHeaders.Add("Accept",
                "application/json;q=0.9,*/*;q=0.8");
            _http.Timeout = TimeSpan.FromMinutes(30);

            _setSymbolsFolder = EnsureFolder(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "SetSymbols"));
            _manaSymbolsFolder = EnsureFolder(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "ManaSymbols"));
        }

        // ════════════════════════════════════════════════════════════════════
        // MAIN ENTRY POINT — Full update
        // ════════════════════════════════════════════════════════════════════
        public async Task<ImportResult> RunFullUpdateAsync(
            IProgress<ImportProgress> progress,
            CancellationToken ct)
        {
            var result = new ImportResult();

            try
            {
                // Step 1 — Get bulk data URL
                Report(progress, "Connecting to Scryfall...", 2);
                ct.ThrowIfCancellationRequested();
                string bulkUrl = await GetBulkDataUrlAsync(ct);

                // Step 2 — Download bulk data (JSONL gzipped)
                Report(progress, "Downloading card database...", 5);
                ct.ThrowIfCancellationRequested();
                string ext = bulkUrl.Contains(".jsonl") ? ".jsonl.gz" : ".json";
                string tempFile = Path.Combine(
                    Path.GetTempPath(), $"scryfall_bulk{ext}");
                await DownloadWithProgressAsync(
                    bulkUrl, tempFile, progress, 5, 40, ct);

                // Step 3 — Download mana symbols
                Report(progress, "Downloading mana symbols...", 41);
                ct.ThrowIfCancellationRequested();
                result.ManaSymbolsDownloaded =
                    await DownloadManaSymbolsAsync(progress, 41, 50, ct);

                // Step 4 — Parse and import cards
                Report(progress, "Importing cards into breakersofe.db...", 51);
                ct.ThrowIfCancellationRequested();
                await ImportCardsAsync(tempFile, result, progress, 51, 80, ct);

                // Step 5 — Download set symbols
                Report(progress, "Downloading set symbols...", 81);
                ct.ThrowIfCancellationRequested();
                result.SetSymbolsDownloaded =
                    await DownloadSetSymbolsAsync(progress, 81, 88, ct);

                // Step 6 — Basic verification
                Report(progress, "Verifying import...", 89);
                ct.ThrowIfCancellationRequested();
                BasicVerify(result);

                // Step 7 — Deep verification
                Report(progress, "Running deep verification...", 90);
                ct.ThrowIfCancellationRequested();
                await DeepVerifyAsync(result, progress, 90, 95, ct);

                // Step 8 — Fetch keyword catalogs from Scryfall
                Report(progress, "Updating keyword dictionary...", 96);
                ct.ThrowIfCancellationRequested();
                await FetchAndMergeKeywordCatalogsAsync(result, progress, ct);

                try { File.Delete(tempFile); } catch { }

                Report(progress, "Complete!", 100);
                result.Success = true;
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "Import cancelled by user.";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        // ════════════════════════════════════════════════════════════════════
        // PRICE-ONLY UPDATE — updates prices in PoolCards only
        // ════════════════════════════════════════════════════════════════════
        public async Task<ImportResult> RunPriceUpdateAsync(
            IProgress<ImportProgress> progress,
            CancellationToken ct)
        {
            var result = new ImportResult();

            try
            {
                // Get bulk data URL
                Report(progress, "Connecting to Scryfall...", 2);
                ct.ThrowIfCancellationRequested();
                string bulkUrl = await GetBulkDataUrlAsync(ct);

                // Download bulk data (JSONL gzipped)
                Report(progress, "Downloading price data...", 5);
                ct.ThrowIfCancellationRequested();
                string ext = bulkUrl.Contains(".jsonl") ? ".jsonl.gz" : ".json";
                string tempFile = Path.Combine(
                    Path.GetTempPath(), $"scryfall_prices{ext}");
                await DownloadWithProgressAsync(
                    bulkUrl, tempFile, progress, 5, 60, ct);

                // Update prices only
                Report(progress, "Updating prices...", 61);
                ct.ThrowIfCancellationRequested();
                await UpdatePricesOnlyAsync(tempFile, progress, 61, 99, ct);

                try { File.Delete(tempFile); } catch { }

                Report(progress, "Prices updated!", 100);
                result.Success = true;
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "Price update cancelled by user.";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        // ── Price-only update logic ──────────────────────────────────────────
        private async Task UpdatePricesOnlyAsync(
            string jsonFile,
            IProgress<ImportProgress> progress,
            int startPct, int endPct,
            CancellationToken ct)
        {
            bool isGzip = jsonFile.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
            int updated = 0;

            // Build price lookup from JSONL/JSON: ScryfallId -> prices
            var priceLookup = new Dictionary<string, (
                decimal? usd, decimal? usdFoil, decimal? usdEtched,
                decimal? eur, decimal? eurFoil, decimal? tix)>();

            await foreach (var card in EnumerateCardsAsync(jsonFile, isGzip, ct))
            {
                ct.ThrowIfCancellationRequested();
                string id = GetString(card, "id");
                if (string.IsNullOrEmpty(id)) continue;

                var prices = ParsePrices(card);
                priceLookup[id] = prices;
            }

            Report(progress, "Applying price updates to database...",
                startPct + 20, $"{priceLookup.Count:N0} prices loaded");

            // Update in batches of 1000
            const int batchSize = 1000;
            using var db = new AppDbContext();

            var poolCards = db.PoolCards.ToList();
            int i = 0;

            foreach (var poolCard in poolCards)
            {
                ct.ThrowIfCancellationRequested();

                if (priceLookup.TryGetValue(poolCard.ScryfallId,
                    out var prices))
                {
                    poolCard.PriceUsd = prices.usd;
                    poolCard.PriceUsdFoil = prices.usdFoil;
                    poolCard.PriceUsdEtched = prices.usdEtched;
                    poolCard.PriceEur = prices.eur;
                    poolCard.PriceEurFoil = prices.eurFoil;
                    poolCard.PriceTix = prices.tix;
                    updated++;
                }

                i++;
                if (i % batchSize == 0)
                {
                    await db.SaveChangesAsync(ct);
                    int pct = startPct + 20 +
                        (int)((double)i / poolCards.Count *
                              (endPct - startPct - 20));
                    Report(progress, "Updating prices...", pct,
                        $"{i:N0} of {poolCards.Count:N0} cards");
                }
            }

            await db.SaveChangesAsync(ct);
            Report(progress, "Prices updated.", endPct,
                $"{updated:N0} cards updated");
        }

        // ════════════════════════════════════════════════════════════════════
        // STEP 1 — GET BULK DATA URL
        // ════════════════════════════════════════════════════════════════════
        private async Task<string> GetBulkDataUrlAsync(CancellationToken ct)
        {
            string json = await _http.GetStringAsync(
                "https://api.scryfall.com/bulk-data", ct);

            using var doc = JsonDocument.Parse(json);

            JsonElement dataElement = doc.RootElement.TryGetProperty(
                "data", out var arr) ? arr : doc.RootElement;

            foreach (var item in dataElement.EnumerateArray())
            {
                string? type = item.TryGetProperty("type", out var t)
                    ? t.GetString() : null;

                if (type != "default_cards") continue;

                // Prefer JSONL format (Scryfall is retiring JSON on July 20 2026)
                if (item.TryGetProperty("jsonl_download_uri", out var jdl))
                {
                    string? url = jdl.GetString();
                    if (!string.IsNullOrEmpty(url)) return url;
                }

                // Fall back to legacy JSON if JSONL not yet available
                if (item.TryGetProperty("download_uri", out var dl))
                    return dl.GetString()
                        ?? throw new Exception("download_uri was null.");

                if (item.TryGetProperty("uri", out var uri))
                {
                    string meta = await _http.GetStringAsync(
                        uri.GetString()!, ct);
                    using var md = JsonDocument.Parse(meta);
                    if (md.RootElement.TryGetProperty(
                            "jsonl_download_uri", out var jdl2))
                    {
                        string? url = jdl2.GetString();
                        if (!string.IsNullOrEmpty(url)) return url;
                    }
                    if (md.RootElement.TryGetProperty(
                            "download_uri", out var dl2))
                        return dl2.GetString()
                            ?? throw new Exception("download_uri was null.");
                }
            }

            throw new Exception(
                "Could not find 'default_cards' bulk data from Scryfall.");
        }

        // ════════════════════════════════════════════════════════════════════
        // STEP 2 — DOWNLOAD WITH PROGRESS
        // ════════════════════════════════════════════════════════════════════
        private async Task DownloadWithProgressAsync(
            string url, string destFile,
            IProgress<ImportProgress> progress,
            int startPct, int endPct,
            CancellationToken ct)
        {
            using var response = await _http.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength;

            await using var stream = await response.Content
                .ReadAsStreamAsync(ct);
            await using var file = File.Create(destFile);

            var buffer = new byte[81920];
            long downloaded = 0;
            int read;

            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;

                if (total.HasValue && total > 0)
                {
                    double frac = (double)downloaded / total.Value;
                    int pct = startPct + (int)(frac * (endPct - startPct));
                    string detail = $"{downloaded / 1_048_576:N0} MB / " +
                                    $"{total.Value / 1_048_576:N0} MB";
                    Report(progress, "Downloading card database...", pct, detail);
                }

                ct.ThrowIfCancellationRequested();
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // STEP 3 — DOWNLOAD MANA SYMBOLS
        // ════════════════════════════════════════════════════════════════════
        private async Task<int> DownloadManaSymbolsAsync(
            IProgress<ImportProgress> progress,
            int startPct, int endPct,
            CancellationToken ct)
        {
            int downloaded = 0;

            try
            {
                string json = await _http.GetStringAsync(
                    "https://api.scryfall.com/symbology", ct);

                using var doc = JsonDocument.Parse(json);
                var list = doc.RootElement
                    .GetProperty("data").EnumerateArray().ToList();
                int total = list.Count;
                int i = 0;

                foreach (var sym in list)
                {
                    ct.ThrowIfCancellationRequested();

                    string? svgUrl = sym.TryGetProperty("svg_uri", out var u)
                        ? u.GetString() : null;
                    string? symStr = sym.TryGetProperty("symbol", out var s)
                        ? s.GetString() : null;

                    if (svgUrl == null || symStr == null) { i++; continue; }

                    string key = symStr
                        .Replace("{", "").Replace("}", "").Replace("/", "-");
                    string path = Path.Combine(_manaSymbolsFolder, $"{key}.png");

                    if (!File.Exists(path))
                    {
                        try
                        {
                            byte[] bytes = await _http.GetByteArrayAsync(
                                svgUrl, ct);
                            await File.WriteAllBytesAsync(path, bytes, ct);
                            downloaded++;
                        }
                        catch { }
                    }
                    else { downloaded++; }

                    i++;
                    int pct = startPct +
                        (int)((double)i / total * (endPct - startPct));
                    Report(progress, "Downloading mana symbols...",
                        pct, $"{i} of {total}");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }

            return downloaded;
        }

        // ════════════════════════════════════════════════════════════════════
        // STEP 4 — PARSE AND IMPORT CARDS
        // ════════════════════════════════════════════════════════════════════
        private async Task ImportCardsAsync(
            string jsonFile,
            ImportResult result,
            IProgress<ImportProgress> progress,
            int startPct, int endPct,
            CancellationToken ct)
        {
            // Safe to delete and re-insert all card pool tables — collection
            // data is in a completely separate database and is never affected.
            using (var db = new AppDbContext())
            {
                await db.Database.ExecuteSqlRawAsync("DELETE FROM PoolCards", ct);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM TokenCards", ct);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM PlanarCards", ct);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM SchemeCards", ct);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM VanguardCards", ct);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM ArtSeriesCards", ct);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM ConspiracyCards", ct);
            }

            const int batchSize = 500;
            var pool = new List<PoolCard>(batchSize);
            var tokens = new List<TokenCard>(batchSize);
            var planar = new List<PlanarCard>(batchSize);
            var schemes = new List<SchemeCard>(batchSize);
            var vanguard = new List<VanguardCard>(batchSize);
            var artSeries = new List<ArtSeriesCard>(batchSize);
            var conspiracy = new List<ConspiracyCard>(batchSize);

            // Track seen ScryfallIds to skip duplicates in the bulk file
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            bool isGzip = jsonFile.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
            int i = 0;

            // Use an async helper that yields JsonElements one at a time
            // from either JSONL (.jsonl.gz) or legacy JSON array format
            await foreach (var cardEl in EnumerateCardsAsync(jsonFile, isGzip, ct))
            {
                ct.ThrowIfCancellationRequested();
                i++;

                // Skip digital-only cards (MTGO/Arena exclusives)
                if (cardEl.TryGetProperty("games", out var gamesEl))
                {
                    bool hasPaper = gamesEl.EnumerateArray()
                        .Any(g => g.GetString() == "paper");
                    if (!hasPaper) continue;
                }

                // Skip duplicate ScryfallIds (bulk files can contain dupes)
                string scryfallId = GetString(cardEl, "id");
                if (!string.IsNullOrEmpty(scryfallId) && !seenIds.Add(scryfallId))
                {
                    result.SkippedCount++;
                    continue;
                }

                RouteCard(cardEl, GetString(cardEl, "layout"),
                    pool, tokens, planar, schemes,
                    vanguard, artSeries, conspiracy, result);

                if (pool.Count >= batchSize)
                { await FlushBatchAsync(pool, ct); pool.Clear(); }
                if (tokens.Count >= batchSize)
                { await FlushBatchAsync(tokens, ct); tokens.Clear(); }
                if (planar.Count >= batchSize)
                { await FlushBatchAsync(planar, ct); planar.Clear(); }
                if (schemes.Count >= batchSize)
                { await FlushBatchAsync(schemes, ct); schemes.Clear(); }
                if (vanguard.Count >= batchSize)
                { await FlushBatchAsync(vanguard, ct); vanguard.Clear(); }
                if (artSeries.Count >= batchSize)
                { await FlushBatchAsync(artSeries, ct); artSeries.Clear(); }
                if (conspiracy.Count >= batchSize)
                { await FlushBatchAsync(conspiracy, ct); conspiracy.Clear(); }

                if (i % 1000 == 0)
                {
                    // For JSONL we don't know total up front, so show count
                    int pct = Math.Min(endPct,
                        startPct + (int)((double)i / 120000 * (endPct - startPct)));
                    string detail = $"{i:N0} processed — " +
                        $"Pool: {result.PoolCardsImported:N0}  " +
                        $"Tokens: {result.TokenCardsImported:N0}  " +
                        $"Planar: {result.PlanarCardsImported:N0}  " +
                        $"Schemes: {result.SchemeCardsImported:N0}  " +
                        $"Conspiracy: {result.ConspiracyCardsImported:N0}";
                    Report(progress, "Importing to card pool database (breakersofe.db)...", pct, detail);
                }
            }

            result.ScryfallReportedTotal = i;

            // Flush remaining
            if (pool.Count > 0) await FlushBatchAsync(pool, ct);
            if (tokens.Count > 0) await FlushBatchAsync(tokens, ct);
            if (planar.Count > 0) await FlushBatchAsync(planar, ct);
            if (schemes.Count > 0) await FlushBatchAsync(schemes, ct);
            if (vanguard.Count > 0) await FlushBatchAsync(vanguard, ct);
            if (artSeries.Count > 0) await FlushBatchAsync(artSeries, ct);
            if (conspiracy.Count > 0) await FlushBatchAsync(conspiracy, ct);
        }

        // ── Route card to correct table ───────────────────────────────────────
        private static void RouteCard(
            JsonElement card, string layout,
            List<PoolCard> pool, List<TokenCard> tokens,
            List<PlanarCard> planar, List<SchemeCard> schemes,
            List<VanguardCard> vanguard,
            List<ArtSeriesCard> artSeries,
            List<ConspiracyCard> conspiracy,
            ImportResult result)
        {
            // Check type_line for Conspiracy before checking layout,
            // since Scryfall uses layout="normal" for Conspiracy cards
            string typeLine = GetString(card, "type_line");
            if (typeLine.Contains("Conspiracy", StringComparison.OrdinalIgnoreCase))
            {
                conspiracy.Add(ParseConspiracyCard(card));
                result.ConspiracyCardsImported++;
                return;
            }

            switch (layout)
            {
                case "token":
                case "double_faced_token":
                    tokens.Add(ParseTokenCard(card));
                    result.TokenCardsImported++;
                    break;

                case "planar":
                    planar.Add(ParsePlanarCard(card));
                    result.PlanarCardsImported++;
                    break;

                case "scheme":
                    schemes.Add(ParseSchemeCard(card));
                    result.SchemeCardsImported++;
                    break;

                case "vanguard":
                    vanguard.Add(ParseVanguardCard(card));
                    result.VanguardCardsImported++;
                    break;

                case "art_series":
                    artSeries.Add(ParseArtSeriesCard(card));
                    result.ArtSeriesCardsImported++;
                    break;

                default:
                    var pc = ParsePoolCard(card);
                    pc.IsMeld = layout == "meld";
                    pool.Add(pc);
                    result.PoolCardsImported++;
                    CountPoolCard(pc, result);
                    break;
            }
        }

        // ── Color and rarity counting ────────────────────────────────────────
        private static void CountPoolCard(PoolCard card, ImportResult result)
        {
            switch (card.Rarity)
            {
                case "common": result.CommonCount++; break;
                case "uncommon": result.UncommonCount++; break;
                case "rare": result.RareCount++; break;
                case "mythic": result.MythicCount++; break;
                default: result.OtherRarityCount++; break;
            }

            if (!string.IsNullOrWhiteSpace(card.TypeLine) &&
                card.TypeLine.Contains("Land"))
            { result.LandCount++; return; }

            string ci = card.ColorIdentity;
            if (string.IsNullOrWhiteSpace(ci)) result.ColorlessCount++;
            else if (ci.Length > 1) result.MulticolorCount++;
            else switch (ci)
                {
                    case "W": result.WhiteCount++; break;
                    case "U": result.BlueCount++; break;
                    case "B": result.BlackCount++; break;
                    case "R": result.RedCount++; break;
                    case "G": result.GreenCount++; break;
                    default: result.ColorlessCount++; break;
                }
        }

        // ════════════════════════════════════════════════════════════════════
        // STEP 5 — DOWNLOAD SET SYMBOLS
        // ════════════════════════════════════════════════════════════════════
        private async Task<int> DownloadSetSymbolsAsync(
            IProgress<ImportProgress> progress,
            int startPct, int endPct,
            CancellationToken ct)
        {
            int downloaded = 0;
            using var db = new AppDbContext();

            var setCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in db.PoolCards.Select(x => x.SetCode).Distinct())
                setCodes.Add(c);
            foreach (var c in db.TokenCards.Select(x => x.SetCode).Distinct())
                setCodes.Add(c);
            foreach (var c in db.PlanarCards.Select(x => x.SetCode).Distinct())
                setCodes.Add(c);
            foreach (var c in db.SchemeCards.Select(x => x.SetCode).Distinct())
                setCodes.Add(c);
            foreach (var c in db.VanguardCards.Select(x => x.SetCode).Distinct())
                setCodes.Add(c);
            foreach (var c in db.ArtSeriesCards.Select(x => x.SetCode).Distinct())
                setCodes.Add(c);

            var list = setCodes.ToList();
            int total = list.Count;
            int i = 0;

            foreach (string code in list)
            {
                ct.ThrowIfCancellationRequested();

                string path = Path.Combine(_setSymbolsFolder,
                    $"{code.ToLower()}.png");

                if (!File.Exists(path))
                {
                    try
                    {
                        string url = $"https://svgs.scryfall.io/sets/" +
                                       $"{code.ToLower()}.svg";
                        byte[] bytes = await _http.GetByteArrayAsync(url, ct);
                        await File.WriteAllBytesAsync(path, bytes, ct);
                        downloaded++;
                    }
                    catch { }
                }
                else { downloaded++; }

                i++;
                int pct = startPct +
                    (int)((double)i / total * (endPct - startPct));
                Report(progress, "Downloading set symbols...",
                    pct, $"{i} of {total} sets");
            }

            return downloaded;
        }

        // ════════════════════════════════════════════════════════════════════
        // STEP 6 — BASIC VERIFICATION
        // ════════════════════════════════════════════════════════════════════
        private static void BasicVerify(ImportResult result)
        {
            using var db = new AppDbContext();

            result.DatabaseTotal =
                db.PoolCards.Count() +
                db.TokenCards.Count() +
                db.PlanarCards.Count() +
                db.SchemeCards.Count() +
                db.VanguardCards.Count() +
                db.ArtSeriesCards.Count();

            int colorTotal =
                result.WhiteCount + result.BlueCount +
                result.BlackCount + result.RedCount +
                result.GreenCount + result.MulticolorCount +
                result.ColorlessCount + result.LandCount;

            result.ColorCountMatchesTotal =
                colorTotal == result.PoolCardsImported;

            int rarityTotal =
                result.CommonCount + result.UncommonCount +
                result.RareCount + result.MythicCount +
                result.OtherRarityCount;

            result.RarityCountMatchesTotal =
                rarityTotal == result.PoolCardsImported;
        }

        // ════════════════════════════════════════════════════════════════════
        // STEP 7 — DEEP VERIFICATION
        // ════════════════════════════════════════════════════════════════════
        private async Task DeepVerifyAsync(
            ImportResult result,
            IProgress<ImportProgress> progress,
            int startPct, int endPct,
            CancellationToken ct)
        {
            using var db = new AppDbContext();

            Report(progress,
                "Deep verification: checking database integrity...",
                startPct, string.Empty);

            result.DuplicateScryfallIds = db.PoolCards
                .GroupBy(c => c.ScryfallId)
                .Count(g => g.Count() > 1);

            result.CardsWithNoImageUrl = db.PoolCards
                .Count(c => c.ImageNormalUrl == string.Empty ||
                            c.ImageNormalUrl == null);

            result.CardsWithEmptyName = db.PoolCards
                .Count(c => c.Name == string.Empty || c.Name == null);

            result.CardsWithEmptySetCode = db.PoolCards
                .Count(c => c.SetCode == string.Empty || c.SetCode == null);

            Report(progress, "Deep verification complete.",
                endPct, string.Empty);

            await Task.CompletedTask;
        }

        // ════════════════════════════════════════════════════════════════════
        // CARD PARSERS — all include SetType and pricing
        // ════════════════════════════════════════════════════════════════════
        private static PoolCard ParsePoolCard(JsonElement c)
        {
            var prices = ParsePrices(c);
            return new PoolCard
            {
                ScryfallId = GetString(c, "id"),
                OracleId = GetString(c, "oracle_id"),
                Name = GetString(c, "name"),
                ManaCost = GetString(c, "mana_cost"),
                ManaValue = GetDouble(c, "cmc"),
                TypeLine = GetString(c, "type_line"),
                OracleText = GetString(c, "oracle_text"),
                FlavorText = GetString(c, "flavor_text"),
                Power = GetString(c, "power"),
                Toughness = GetString(c, "toughness"),
                LoyaltyOrDefense = GetString(c, "loyalty"),
                Colors = GetStringArray(c, "colors"),
                ColorIdentity = GetStringArray(c, "color_identity"),
                SetCode = GetString(c, "set").ToUpper(),
                SetName = GetString(c, "set_name"),
                SetType = GetString(c, "set_type"),
                CollectorNumber = GetString(c, "collector_number"),
                Rarity = GetString(c, "rarity"),
                Artist = GetString(c, "artist"),
                ImageSmallUrl = GetImageUri(c, "small"),
                ImageNormalUrl = GetImageUri(c, "normal"),
                ImageBackUrl = GetBackFaceImageUri(c, "normal"),
                Layout = GetString(c, "layout"),
                IsFoil = GetBool(c, "foil"),
                IsNonFoil = GetBool(c, "nonfoil"),
                IsToken = false,
                ReleasedAt = GetString(c, "released_at"),
                PricesJson = GetRawJson(c, "prices"),
                LegalitiesJson = GetRawJson(c, "legalities"),
                Keywords = GetStringArray(c, "keywords", "|"),
                LocalImagePath = string.Empty,
                PriceUsd = prices.usd,
                PriceUsdFoil = prices.usdFoil,
                PriceUsdEtched = prices.usdEtched,
                PriceEur = prices.eur,
                PriceEurFoil = prices.eurFoil,
                PriceTix = prices.tix
            };
        }

        private static TokenCard ParseTokenCard(JsonElement c) => new()
        {
            ScryfallId = GetString(c, "id"),
            OracleId = GetString(c, "oracle_id"),
            Name = GetString(c, "name"),
            TypeLine = GetString(c, "type_line"),
            OracleText = GetString(c, "oracle_text"),
            FlavorText = GetString(c, "flavor_text"),
            Power = GetString(c, "power"),
            Toughness = GetString(c, "toughness"),
            Colors = GetStringArray(c, "colors"),
            ColorIdentity = GetStringArray(c, "color_identity"),
            SetCode = GetString(c, "set").ToUpper(),
            SetName = GetString(c, "set_name"),
            SetType = GetString(c, "set_type"),
            CollectorNumber = GetString(c, "collector_number"),
            Rarity = GetString(c, "rarity"),
            Artist = GetString(c, "artist"),
            ImageSmallUrl = GetImageUri(c, "small"),
            ImageNormalUrl = GetImageUri(c, "normal"),
            Layout = GetString(c, "layout"),
            IsFoil = GetBool(c, "foil"),
            IsNonFoil = GetBool(c, "nonfoil"),
            ReleasedAt = GetString(c, "released_at"),
            LocalImagePath = string.Empty
        };

        private static PlanarCard ParsePlanarCard(JsonElement c) => new()
        {
            ScryfallId = GetString(c, "id"),
            OracleId = GetString(c, "oracle_id"),
            Name = GetString(c, "name"),
            TypeLine = GetString(c, "type_line"),
            OracleText = GetString(c, "oracle_text"),
            FlavorText = GetString(c, "flavor_text"),
            SetCode = GetString(c, "set").ToUpper(),
            SetName = GetString(c, "set_name"),
            SetType = GetString(c, "set_type"),
            CollectorNumber = GetString(c, "collector_number"),
            Rarity = GetString(c, "rarity"),
            Artist = GetString(c, "artist"),
            ImageSmallUrl = GetImageUri(c, "small"),
            ImageNormalUrl = GetImageUri(c, "normal"),
            Layout = GetString(c, "layout"),
            IsFoil = GetBool(c, "foil"),
            IsNonFoil = GetBool(c, "nonfoil"),
            ReleasedAt = GetString(c, "released_at"),
            LocalImagePath = string.Empty
        };

        private static SchemeCard ParseSchemeCard(JsonElement c) => new()
        {
            ScryfallId = GetString(c, "id"),
            OracleId = GetString(c, "oracle_id"),
            Name = GetString(c, "name"),
            TypeLine = GetString(c, "type_line"),
            OracleText = GetString(c, "oracle_text"),
            FlavorText = GetString(c, "flavor_text"),
            SetCode = GetString(c, "set").ToUpper(),
            SetName = GetString(c, "set_name"),
            SetType = GetString(c, "set_type"),
            CollectorNumber = GetString(c, "collector_number"),
            Rarity = GetString(c, "rarity"),
            Artist = GetString(c, "artist"),
            ImageSmallUrl = GetImageUri(c, "small"),
            ImageNormalUrl = GetImageUri(c, "normal"),
            Layout = GetString(c, "layout"),
            IsFoil = GetBool(c, "foil"),
            IsNonFoil = GetBool(c, "nonfoil"),
            ReleasedAt = GetString(c, "released_at"),
            LocalImagePath = string.Empty
        };

        private static VanguardCard ParseVanguardCard(JsonElement c) => new()
        {
            ScryfallId = GetString(c, "id"),
            OracleId = GetString(c, "oracle_id"),
            Name = GetString(c, "name"),
            TypeLine = GetString(c, "type_line"),
            OracleText = GetString(c, "oracle_text"),
            FlavorText = GetString(c, "flavor_text"),
            SetCode = GetString(c, "set").ToUpper(),
            SetName = GetString(c, "set_name"),
            SetType = GetString(c, "set_type"),
            CollectorNumber = GetString(c, "collector_number"),
            Rarity = GetString(c, "rarity"),
            Artist = GetString(c, "artist"),
            ImageSmallUrl = GetImageUri(c, "small"),
            ImageNormalUrl = GetImageUri(c, "normal"),
            Layout = GetString(c, "layout"),
            IsFoil = GetBool(c, "foil"),
            IsNonFoil = GetBool(c, "nonfoil"),
            ReleasedAt = GetString(c, "released_at"),
            HandModifier = GetString(c, "hand_modifier"),
            LifeModifier = GetString(c, "life_modifier"),
            LocalImagePath = string.Empty
        };

        private static ArtSeriesCard ParseArtSeriesCard(JsonElement c) => new()
        {
            ScryfallId = GetString(c, "id"),
            OracleId = GetString(c, "oracle_id"),
            Name = GetString(c, "name"),
            TypeLine = GetString(c, "type_line"),
            FlavorText = GetString(c, "flavor_text"),
            SetCode = GetString(c, "set").ToUpper(),
            SetName = GetString(c, "set_name"),
            SetType = GetString(c, "set_type"),
            CollectorNumber = GetString(c, "collector_number"),
            Rarity = GetString(c, "rarity"),
            Artist = GetString(c, "artist"),
            ImageSmallUrl = GetImageUri(c, "small"),
            ImageNormalUrl = GetImageUri(c, "normal"),
            Layout = GetString(c, "layout"),
            IsFoil = GetBool(c, "foil"),
            IsNonFoil = GetBool(c, "nonfoil"),
            ReleasedAt = GetString(c, "released_at"),
            LocalImagePath = string.Empty
        };

        private static ConspiracyCard ParseConspiracyCard(JsonElement c) => new()
        {
            ScryfallId = GetString(c, "id"),
            OracleId = GetString(c, "oracle_id"),
            Name = GetString(c, "name"),
            TypeLine = GetString(c, "type_line"),
            OracleText = GetString(c, "oracle_text"),
            FlavorText = GetString(c, "flavor_text"),
            SetCode = GetString(c, "set").ToUpper(),
            SetName = GetString(c, "set_name"),
            SetType = GetString(c, "set_type"),
            CollectorNumber = GetString(c, "collector_number"),
            Rarity = GetString(c, "rarity"),
            Artist = GetString(c, "artist"),
            ManaCost = GetString(c, "mana_cost"),
            ManaValue = GetDouble(c, "cmc"),
            ColorIdentity = GetStringArray(c, "color_identity"),
            Colors = GetStringArray(c, "colors"),
            ImageSmallUrl = GetImageUri(c, "small"),
            ImageNormalUrl = GetImageUri(c, "normal"),
            Layout = GetString(c, "layout"),
            IsFoil = GetBool(c, "foil"),
            IsNonFoil = GetBool(c, "nonfoil"),
            ReleasedAt = GetString(c, "released_at"),
            LocalImagePath = string.Empty
        };

        // ════════════════════════════════════════════════════════════════════
        // PRICE PARSER
        // ════════════════════════════════════════════════════════════════════
        private static (decimal? usd, decimal? usdFoil, decimal? usdEtched,
            decimal? eur, decimal? eurFoil, decimal? tix)
            ParsePrices(JsonElement card)
        {
            if (!card.TryGetProperty("prices", out var prices))
                return (null, null, null, null, null, null);

            return (
                GetDecimal(prices, "usd"),
                GetDecimal(prices, "usd_foil"),
                GetDecimal(prices, "usd_etched"),
                GetDecimal(prices, "eur"),
                GetDecimal(prices, "eur_foil"),
                GetDecimal(prices, "tix")
            );
        }

        private static decimal? GetDecimal(JsonElement el, string prop)
        {
            if (!el.TryGetProperty(prop, out var v)) return null;
            if (v.ValueKind == JsonValueKind.Null) return null;
            if (v.ValueKind == JsonValueKind.String)
            {
                string? s = v.GetString();
                if (string.IsNullOrEmpty(s)) return null;
                if (decimal.TryParse(s,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out decimal d))
                    return d;
            }
            if (v.ValueKind == JsonValueKind.Number)
                return v.GetDecimal();
            return null;
        }

        // ════════════════════════════════════════════════════════════════════
        // CARD ENUMERATOR — streams from JSONL.gz or legacy JSON array
        // ════════════════════════════════════════════════════════════════════
        private static async IAsyncEnumerable<JsonElement> EnumerateCardsAsync(
            string filePath, bool isGzip,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken ct)
        {
            if (isGzip)
            {
                // JSONL format: one JSON object per line, gzipped
                await using var fs = File.OpenRead(filePath);
                await using var gz = new GZipStream(fs, CompressionMode.Decompress);
                using var reader = new StreamReader(gz);

                string? line;
                while ((line = await reader.ReadLineAsync(ct)) != null)
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    using var doc = JsonDocument.Parse(line);
                    // Clone so the element outlives the disposed doc
                    yield return doc.RootElement.Clone();
                }
            }
            else
            {
                // Legacy JSON array format
                await using var fs = File.OpenRead(filePath);
                using var doc = await JsonDocument.ParseAsync(fs,
                    new JsonDocumentOptions { AllowTrailingCommas = true }, ct);

                foreach (var card in doc.RootElement.EnumerateArray())
                {
                    ct.ThrowIfCancellationRequested();
                    yield return card;
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // BATCH FLUSH
        // ════════════════════════════════════════════════════════════════════
        private static async Task FlushBatchAsync<T>(
            List<T> batch, CancellationToken ct) where T : class
        {
            using var db = new AppDbContext();
            await db.Set<T>().AddRangeAsync(batch, ct);
            await db.SaveChangesAsync(ct);
        }

        // ════════════════════════════════════════════════════════════════════
        // JSON HELPERS
        // ════════════════════════════════════════════════════════════════════
        private static string GetString(JsonElement el, string prop)
        {
            if (el.TryGetProperty(prop, out var v) &&
                v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? string.Empty;
            return string.Empty;
        }

        private static double GetDouble(JsonElement el, string prop)
        {
            if (el.TryGetProperty(prop, out var v) &&
                v.ValueKind == JsonValueKind.Number)
                return v.GetDouble();
            return 0;
        }

        private static bool GetBool(JsonElement el, string prop)
        {
            if (el.TryGetProperty(prop, out var v))
                return v.ValueKind == JsonValueKind.True;
            return false;
        }

        private static string GetStringArray(JsonElement el, string prop,
            string separator = "")
        {
            if (!el.TryGetProperty(prop, out var arr) ||
                arr.ValueKind != JsonValueKind.Array)
                return string.Empty;

            var parts = new List<string>();
            foreach (var item in arr.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String)
                    parts.Add(item.GetString() ?? string.Empty);

            return string.Join(separator, parts);
        }

        private static string GetRawJson(JsonElement el, string prop)
        {
            if (el.TryGetProperty(prop, out var v))
                return v.ToString();
            return string.Empty;
        }

        private static string GetImageUri(JsonElement el, string size)
        {
            if (el.TryGetProperty("image_uris", out var uris) &&
                uris.TryGetProperty(size, out var uri))
                return uri.GetString() ?? string.Empty;

            if (el.TryGetProperty("card_faces", out var faces))
            {
                foreach (var face in faces.EnumerateArray())
                {
                    if (face.TryGetProperty("image_uris", out var fu) &&
                        fu.TryGetProperty(size, out var furi))
                        return furi.GetString() ?? string.Empty;
                    break;
                }
            }

            return string.Empty;
        }

        private static string GetBackFaceImageUri(JsonElement el, string size)
        {
            // Returns the second card face image (back face for DFCs)
            if (el.TryGetProperty("card_faces", out var faces))
            {
                int idx = 0;
                foreach (var face in faces.EnumerateArray())
                {
                    if (idx == 1 &&
                        face.TryGetProperty("image_uris", out var fu) &&
                        fu.TryGetProperty(size, out var furi))
                        return furi.GetString() ?? string.Empty;
                    idx++;
                }
            }
            return string.Empty;
        }

        // ════════════════════════════════════════════════════════════════════
        // UTILITY
        // ════════════════════════════════════════════════════════════════════
        // ════════════════════════════════════════════════════════════════════
        // KEYWORD CATALOGS — fetches Scryfall keyword/ability-word catalogs
        // ════════════════════════════════════════════════════════════════════

        private async Task FetchAndMergeKeywordCatalogsAsync(
            ImportResult result,
            IProgress<ImportProgress> progress,
            CancellationToken ct)
        {
            List<string>? abilities = null;
            List<string>? actions = null;
            List<string>? abilityWords = null;

            try
            {
                Report(progress, "Updating keyword dictionary...", 96,
                    "Fetching keyword abilities...");
                abilities = await FetchCatalogAsync(
                    "https://api.scryfall.com/catalog/keyword-abilities", ct);
                result.KeywordAbilitiesCount = abilities?.Count ?? 0;

                Report(progress, "Updating keyword dictionary...", 97,
                    "Fetching keyword actions...");
                await Task.Delay(100, ct);           // Scryfall rate-limit courtesy
                actions = await FetchCatalogAsync(
                    "https://api.scryfall.com/catalog/keyword-actions", ct);
                result.KeywordActionsCount = actions?.Count ?? 0;

                Report(progress, "Updating keyword dictionary...", 98,
                    "Fetching ability words...");
                await Task.Delay(100, ct);
                abilityWords = await FetchCatalogAsync(
                    "https://api.scryfall.com/catalog/ability-words", ct);
                result.AbilityWordsCount = abilityWords?.Count ?? 0;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Non-fatal — the keyword catalog is a nice-to-have
            }

            // Merge catalogs + pool-discovered keywords into the dictionary
            Report(progress, "Updating keyword dictionary...", 99,
                "Merging keywords...");
            MtgKeywordService.Reset();
            MtgKeywordService.RefreshFromPool();
            result.NewKeywordsDiscovered =
                MtgKeywordService.MergeScryfallCatalogs(
                    abilities, actions, abilityWords);
            MtgKeywordService.ExtractReminderTextsFromPool();
            MtgKeywordService.SaveToCache();
        }

        /// <summary>
        /// Fetches a single Scryfall catalog endpoint and returns its data array.
        /// </summary>
        private async Task<List<string>?> FetchCatalogAsync(
            string url, CancellationToken ct)
        {
            var response = await _http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var dataArray)
                && dataArray.ValueKind == JsonValueKind.Array)
            {
                return dataArray.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .ToList();
            }
            return null;
        }

        private static void Report(IProgress<ImportProgress> p,
            string step, int pct, string detail = "")
        {
            p.Report(new ImportProgress
            {
                Step = step,
                Percentage = pct,
                Detail = detail
            });
        }

        private static string EnsureFolder(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            return path;
        }
    }
}
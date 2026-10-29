using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Phone Lists (Settings → Phone Lists): your Trade Binder and Want List
    /// as one file, "BoE Lists.json", in a folder of your cloud drive. The BoE
    /// Lists page on your phone (<see cref="ViewerUrl"/>) opens it. BoE writes
    /// it again whenever your collection changes (see the app's watcher), so
    /// the phone always gets the newest lists. With Dropbox connected
    /// (<see cref="DropboxService"/>) the file goes straight to Dropbox instead,
    /// and the phone page fetches it by itself. Nothing else of your
    /// collection goes in the file.
    /// </summary>
    public static class PhoneListsService
    {
        /// <summary>The phone page: one page for every BoE user, holding nobody's cards.</summary>
        public const string ViewerUrl = "https://georgebrun.github.io/repos/lists/";

        public const string FileName = "BoE Lists.json";

        /// <summary>
        /// Where the file goes to start: "Breakers of E" in your OneDrive when
        /// Windows has one, else "" (pick a folder).
        /// </summary>
        public static string DefaultFolder()
        {
            foreach (var v in new[] { "OneDriveConsumer", "OneDrive", "OneDriveCommercial" })
            {
                string? od = Environment.GetEnvironmentVariable(v);
                if (!string.IsNullOrWhiteSpace(od) && Directory.Exists(od))
                    return Path.Combine(od, "Breakers of E");
            }
            return "";
        }

        /// <summary>The folder in use (Settings, else the default).</summary>
        public static string Folder =>
            AppSettingsService.Current.PhoneListsFolder is { Length: > 0 } f ? f : DefaultFolder();

        public static string FilePath(string folder) => Path.Combine(folder, FileName);

        // ── The file ─────────────────────────────────────────────────────
        private sealed class ListCard
        {
            [JsonPropertyName("name")] public string Name { get; init; } = "";
            [JsonPropertyName("set")] public string Set { get; init; } = "";
            [JsonPropertyName("setName")] public string SetName { get; init; } = "";
            [JsonPropertyName("no")] public string No { get; init; } = "";
            [JsonPropertyName("finish")] public string Finish { get; init; } = "";
            [JsonPropertyName("qty")] public int Qty { get; init; }
            [JsonPropertyName("price")] public decimal? Price { get; init; }
            [JsonPropertyName("ask")] public decimal? Ask { get; init; }
            [JsonPropertyName("color")] public string Color { get; init; } = "C";
            [JsonPropertyName("type")] public string Type { get; init; } = "";
            [JsonPropertyName("lang")] public string? Lang { get; init; }
            [JsonPropertyName("cond")] public string? Cond { get; init; }
            [JsonPropertyName("forDeck")] public string? ForDeck { get; init; }
            [JsonPropertyName("img")] public string Img { get; init; } = "";
            [JsonPropertyName("imgLarge")] public string ImgLarge { get; init; } = "";
        }

        private sealed class ListsFile
        {
            [JsonPropertyName("format")] public string Format { get; init; } = "boe-lists";
            [JsonPropertyName("version")] public int Version { get; init; } = 1;
            [JsonPropertyName("exported")] public string Exported { get; init; } = "";
            [JsonPropertyName("binder")] public List<ListCard> Binder { get; init; } = new();
            [JsonPropertyName("want")] public List<ListCard> Want { get; init; } = new();
        }

        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,      // "Lathril — Blade" stays readable
        };

        /// <summary>W, U, B, R, G for one color; M for two or more; C for none.</summary>
        private static string ColorCode(string colors)
        {
            var c = "WUBRG".Where(x => (colors ?? "").Contains(x)).ToList();
            return c.Count == 0 ? "C" : c.Count > 1 ? "M" : c[0].ToString();
        }

        /// <summary>Scryfall's small picture link from its normal one (same printing).</summary>
        private static string Small(string normal) =>
            normal.Contains("/normal/") ? normal.Replace("/normal/", "/small/") : normal;

        private static decimal? PriceOf(string finish, decimal? rowPrice, decimal? usd, decimal? usdFoil) =>
            rowPrice ?? (CardFinish.Normalize(finish) == CardFinish.NonFoil ? usd : usdFoil ?? usd);

        /// <summary>
        /// Write the file to <paramref name="folder"/> (made if missing). Written
        /// to a temporary file first, then swapped in, so the cloud drive never
        /// syncs half a file. Returns the cards in each list.
        /// </summary>
        public static (int Binder, int Want) Write(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new InvalidOperationException("No folder chosen for the phone lists.");
            Directory.CreateDirectory(folder);
            var (json, binder, want) = Build();
            string path = FilePath(folder);
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, json);
            File.Move(tmp, path, overwrite: true);
            return (binder, want);
        }

        /// <summary>
        /// The file's contents (UTF-8 JSON) and the cards in each list: for a
        /// cloud-drive folder (<see cref="Write"/>) or Dropbox (<see cref="DropboxService"/>).
        /// </summary>
        public static (byte[] Json, int Binder, int Want) Build()
        {
            List<TradeBinderEntry> binder;
            List<WantListEntry> want;
            using (var db = new CollectionDbContext())
            {
                binder = db.TradeBinderEntries.AsNoTracking().Where(e => e.Quantity > 0).ToList();
                want = db.WantListEntries.AsNoTracking().Where(e => e.Quantity > 0).ToList();
            }

            var file = new ListsFile
            {
                Exported = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                Binder = binder
                    .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(e => new ListCard
                    {
                        Name = e.Name, Set = e.SetCode.ToUpperInvariant(), SetName = e.SetName, No = e.CollectorNumber,
                        Finish = CardFinish.Display(CardFinish.Normalize(e.Finish)), Qty = e.Quantity,
                        Price = PriceOf(e.Finish, e.Price, e.PriceUsd, e.PriceUsdFoil), Ask = e.AskingPrice,
                        Color = ColorCode(e.Colors), Type = e.TypeLine,
                        Lang = CardLanguage.Normalize(e.Language), Cond = CardCondition.Normalize(e.Condition),
                        Img = Small(e.ImageNormalUrl), ImgLarge = e.ImageNormalUrl,
                    }).ToList(),
                Want = want
                    .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(e => new ListCard
                    {
                        Name = e.Name, Set = e.SetCode.ToUpperInvariant(), SetName = e.SetName, No = e.CollectorNumber,
                        Finish = CardFinish.Display(CardFinish.Normalize(e.Finish)), Qty = e.Quantity,
                        Price = PriceOf(e.Finish, e.Price, e.PriceUsd, e.PriceUsdFoil),
                        Color = ColorCode(e.Colors), Type = e.TypeLine,
                        ForDeck = e.ForDisplay,
                        Img = Small(e.ImageNormalUrl), ImgLarge = e.ImageNormalUrl,
                    }).ToList(),
            };

            return (JsonSerializer.SerializeToUtf8Bytes(file, Json), file.Binder.Count, file.Want.Count);
        }

        /// <summary>
        /// When the collection last changed (collection.db and its write-ahead
        /// log): the watcher writes the file again when this moves on.
        /// </summary>
        public static DateTime CollectionStamp()
        {
            DateTime Stamp(string p) => File.Exists(p) ? File.GetLastWriteTimeUtc(p) : DateTime.MinValue;
            string db = AppFolderService.CollectionDatabasePath;
            var a = Stamp(db);
            var b = Stamp(db + "-wal");
            return a > b ? a : b;
        }
    }
}

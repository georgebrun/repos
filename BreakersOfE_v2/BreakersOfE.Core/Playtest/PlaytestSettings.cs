using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BreakersOfE.Services;

namespace BreakersOfE.Playtest
{
    /// <summary>What an ability button does.</summary>
    public enum AbilityUse
    {
        /// <summary>Pays its costs and does what the table can read (the rest is logged for you).</summary>
        Do,
        /// <summary>Only logs what it says: you do it all.</summary>
        Remind,
        /// <summary>No ability buttons.</summary>
        Off,
    }

    /// <summary>
    /// The playtest table's own settings (the ⚙ button on the table), kept
    /// apart from BoE's: Tabletop\PlaytestSettings.json in the data folder.
    /// </summary>
    public sealed class PlaytestSettings
    {
        /// <summary>Card back for your library and face-down cards: a picture in Tabletop\Sleeves ("" = the Magic card back).</summary>
        public string Sleeve { get; set; } = "";

        /// <summary>The playmat under the battlefield: a picture in Tabletop\Playmats ("" = none, just the table color).</summary>
        public string Playmat { get; set; } = "";

        /// <summary>How much the playmat is darkened (0 = not at all, 0.8 = nearly black), so busy art doesn't fight the cards.</summary>
        public double PlaymatDim { get; set; } = 0.25;

        /// <summary>The table's color (behind the playmat, the zones and the hand): "#RRGGBB".</summary>
        public string TableColor { get; set; } = DefaultTableColor;

        public const string DefaultTableColor = "#26302B";

        /// <summary>
        /// A deck's own sleeve and playmat, by its DeckId. null = the
        /// defaults above; "" = none (the Magic card back / no playmat).
        /// </summary>
        public Dictionary<string, DeckLook> Decks { get; set; } = new();

        public sealed class DeckLook
        {
            public string? Sleeve { get; set; }
            public string? Playmat { get; set; }
        }

        /// <summary>
        /// Your badge beside your life: a picture of you, the player (whichever deck
        /// you play), from Tabletop\Badges. "" = none (a plain person icon).
        /// </summary>
        public string Badge { get; set; } = "";

        /// <summary>Player 2 (the deck across the table): the name the table and the log use.</summary>
        public string OpponentName { get; set; } = "Opponent";

        /// <summary>Player 2's badge picture, from Tabletop\Badges. "" = none (a plain person icon).</summary>
        public string OpponentBadge { get; set; } = "";

        /// <summary>Player 2's name, never "You" (that's the player at the near side).</summary>
        public string OpponentNameOrDefault() =>
            string.IsNullOrWhiteSpace(OpponentName) || OpponentName.Trim().Equals("You", System.StringComparison.OrdinalIgnoreCase)
                ? "Opponent" : OpponentName.Trim();

        /// <summary>How each picture is fitted (Fit…), by "Sleeves/name" or "Playmats/name". Not there = centered, no zoom.</summary>
        public Dictionary<string, Framing> Framings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public static string SleeveKey(string file) => "Sleeves/" + file;
        public static string PlaymatKey(string file) => "Playmats/" + file;
        public static string BadgeKey(string file) => "Badges/" + file;

        public Framing FramingOf(string key) =>
            Framings.TryGetValue(key, out var f) ? f : new Framing();

        /// <summary>How big cards are on the table (1 = normal).</summary>
        public double CardScale { get; set; } = 1.0;

        /// <summary>Pass Turn draws a card for the next turn.</summary>
        public bool DrawOnNewTurn { get; set; } = true;

        /// <summary>The first mulligan is free (only in Commander-style decks).</summary>
        public bool FreeFirstMulligan { get; set; } = true;

        /// <summary>Spells (anything but a land) go on the stack first (Resolve, or R, finishes them); off: permanents straight onto the battlefield, instants and sorceries to the graveyard.</summary>
        public bool StackFirst { get; set; } = true;

        /// <summary>Tapping a land or a mana stone adds its mana to your pool (a card with a choice asks which).</summary>
        public bool TapForMana { get; set; } = true;

        /// <summary>When unspent mana empties from your pool: at the end of the turn (default), every step (the rules), or never. Omnath and the like keep theirs either way.</summary>
        public ManaEmptying ManaEmpties { get; set; } = ManaEmptying.EndOfTurn;

        /// <summary>"At the beginning of …" reminders as the steps of the turn come.</summary>
        public bool ShowReminders { get; set; } = true;

        /// <summary>A banner when every piece of one of the deck's combos is out.</summary>
        public bool ComboAlerts { get; set; } = true;

        /// <summary>Keep an unfinished game when the table closes, and offer it back next time.</summary>
        public bool KeepGames { get; set; } = true;

        /// <summary>Maximum hand size at the end of your turn (Next Turn asks you to discard down): 0 = no maximum.</summary>
        public int MaxHandSize { get; set; } = 7;

        /// <summary>Starting life: 0 = by the deck's format (40 Commander, 20 others…).</summary>
        public int StartingLife { get; set; }

        /// <summary>Cards' activated abilities on the action bar and menu: done for you (what the table can read), just logged, or off.</summary>
        public AbilityUse UseAbilities { get; set; } = AbilityUse.Do;

        /// <summary>Keyword badges on creatures (flying, trample …) and the summoning-sickness mark.</summary>
        public bool ShowKeywords { get; set; } = true;

        /// <summary>The card under the mouse grows to 200% where it sits (it was the card panel beside the table).</summary>
        public bool ShowCardPanel { get; set; } = true;

        /// <summary>The message bar along the bottom (the game log).</summary>
        public bool ShowLog { get; set; } = true;

        // ── Load / save ──────────────────────────────────────────────────
        private static PlaytestSettings? _current;

        public static string FilePath =>
            Path.Combine(Path.GetDirectoryName(AppFolderService.SleeveImagesFolder)!, "PlaytestSettings.json");

        public static PlaytestSettings Current => _current ??= Load();

        private static PlaytestSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<PlaytestSettings>(File.ReadAllText(FilePath)) ?? new();
            }
            catch
            {
                // Damaged: start from the defaults.
            }
            return new PlaytestSettings();
        }

        public static void Save()
        {
            try
            {
                SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // Not saved this time: the table keeps working.
            }
        }

        /// <summary>This deck's own look, if it has one.</summary>
        public DeckLook? LookOf(string? deckId) =>
            !string.IsNullOrEmpty(deckId) && Decks.TryGetValue(deckId, out var l) ? l : null;

        /// <summary>The sleeve a deck plays in (its own, else the default): a file name, "" = the Magic card back.</summary>
        public string SleeveFor(string? deckId) => LookOf(deckId)?.Sleeve ?? Sleeve;

        /// <summary>The playmat a deck plays on (its own, else the default): a file name, "" = none.</summary>
        public string PlaymatFor(string? deckId) => LookOf(deckId)?.Playmat ?? Playmat;

        /// <summary>Your badge picture's full path, or "" (none, or the file is gone).</summary>
        public string BadgePath() => PicturePath(AppFolderService.BadgeImagesFolder, Badge);

        /// <summary>Player 2's badge picture's full path, or "".</summary>
        public string OpponentBadgePath() => PicturePath(AppFolderService.BadgeImagesFolder, OpponentBadge);

        /// <summary>The sleeve picture's full path, or "" for the Magic card back (also when the file is gone).</summary>
        public string SleevePath(string? deckId = null) => PicturePath(AppFolderService.SleeveImagesFolder, SleeveFor(deckId));

        /// <summary>The playmat picture's full path, or "" for none (also when the file is gone).</summary>
        public string PlaymatPath(string? deckId = null) => PicturePath(AppFolderService.PlaymatImagesFolder, PlaymatFor(deckId));

        private static string PicturePath(string folder, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            string p = Path.Combine(folder, name);
            return File.Exists(p) ? p : "";
        }

        private static readonly string[] PictureTypes = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff" };

        /// <summary>The pictures in a folder (Sleeves or Playmats), by name: v1's are already there.</summary>
        public static List<string> Pictures(string folder)
        {
            try
            {
                return Directory.EnumerateFiles(folder)
                    .Where(f => PictureTypes.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .Select(f => Path.GetFileName(f)!)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }
    }
}

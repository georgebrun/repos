using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BreakersOfE.Models;
using BreakersOfE.Services;

namespace BreakersOfE.Playtest
{
    /// <summary>
    /// A game left on the table is kept when the table closes
    /// (Tabletop\Games\{deck}.json) and offered back the next time that deck
    /// is played. New Game, or a finished save, replaces it.
    /// </summary>
    public static class SavedGames
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

        public static string Folder
        {
            get
            {
                string f = Path.Combine(Path.GetDirectoryName(AppFolderService.SleeveImagesFolder)!, "Games");
                Directory.CreateDirectory(f);
                return f;
            }
        }

        private static string PathFor(Deck deck) =>
            Path.Combine(Folder, AppFolderService.SafeFileName(string.IsNullOrEmpty(deck.DeckId) ? deck.Name : deck.DeckId) + ".json");

        public static void Save(Deck deck, Game game)
        {
            try
            {
                SafeFile.WriteAllText(PathFor(deck), JsonSerializer.Serialize(game.Save(), Json));
            }
            catch
            {
                // Not saved: the next game starts fresh.
            }
        }

        /// <summary>The saved game for this deck, or null.</summary>
        public static Game.State? Load(Deck deck)
        {
            try
            {
                string p = PathFor(deck);
                if (!File.Exists(p)) return null;
                var s = JsonSerializer.Deserialize<Game.State>(File.ReadAllText(p), Json);
                return s != null && s.Seats.Count > 0 ? s : null;
            }
            catch
            {
                return null;                                     // damaged: offer nothing
            }
        }

        /// <summary>Every saved game (⚙ Table → Clear Saved Games): how many were deleted.</summary>
        public static int DeleteAll()
        {
            int n = 0;
            try
            {
                foreach (var f in Directory.EnumerateFiles(Folder, "*.json"))
                {
                    try { File.Delete(f); n++; } catch { /* in use: left */ }
                }
            }
            catch
            {
                // No folder: nothing saved.
            }
            return n;
        }

        /// <summary>Every saved game (⚙ Table → Saved Games), newest first: its file and what's in it (null when the file can't be read).</summary>
        public static System.Collections.Generic.List<(string File, Game.State? State)> All()
        {
            var list = new System.Collections.Generic.List<(string File, Game.State? State)>();
            try
            {
                foreach (var f in Directory.EnumerateFiles(Folder, "*.json"))
                {
                    Game.State? s = null;
                    try { s = JsonSerializer.Deserialize<Game.State>(File.ReadAllText(f), Json); } catch { /* damaged: listed, to delete */ }
                    list.Add((f, s));
                }
            }
            catch
            {
                // No folder: nothing saved.
            }
            return list.OrderByDescending(e => e.State?.Saved ?? File.GetLastWriteTime(e.File)).ToList();
        }

        /// <summary>One saved game, by its file (from <see cref="All"/>).</summary>
        public static void DeleteFile(string file)
        {
            try
            {
                if (Path.GetDirectoryName(Path.GetFullPath(file)) == Path.GetFullPath(Folder)) File.Delete(file);
            }
            catch
            {
                // In use, or gone already.
            }
        }

        public static int Count()
        {
            try { return Directory.EnumerateFiles(Folder, "*.json").Count(); } catch { return 0; }
        }

        public static void Delete(Deck deck)
        {
            try { File.Delete(PathFor(deck)); } catch { /* not there */ }
        }
    }
}

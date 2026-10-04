using System;
using System.IO;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Centralized service for all user-facing file paths (v2).
    ///
    /// All v2 user data lives under My Documents\BoE_V2\ — completely
    /// separate from v1's "Breakers of E" folder, so the two versions
    /// never touch each other's databases, decks, or images.
    /// </summary>
    public static class AppFolderService
    {
        // ── Root folder — v2 uses its own isolated folder ──────────────────────
        // Normally Documents\BoE_V2. Settings → Data folder can move it: the
        // new place is remembered in a small pointer file OUTSIDE the data
        // folder (%LocalAppData%\BreakersOfE_V2\data-folder.txt), read once
        // at startup. A pointer to a folder that isn't there (an unplugged
        // drive) falls back to the default and says so (DataFolderProblem).
        public static string RootFolder => EnsureFolder(_root ??= ResolveRoot());

        private static string? _root;

        /// <summary>Documents\BoE_V2 — where the data lives unless moved.</summary>
        public static string DefaultRootFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BoE_V2");

        private static string PointerPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "BreakersOfE_V2", "data-folder.txt");

        /// <summary>Set when the remembered data folder couldn't be used (shown in Settings).</summary>
        public static string DataFolderProblem { get; private set; } = "";

        private static string ResolveRoot()
        {
            try
            {
                if (File.Exists(PointerPath))
                {
                    string chosen = File.ReadAllText(PointerPath).Trim();
                    if (chosen.Length > 0)
                    {
                        if (Directory.Exists(chosen) && CanWrite(chosen)) return chosen;
                        if (Directory.Exists(chosen))
                        {
                            DataFolderProblem = $"BoE can't save in your data folder \"{chosen}\" (read-only, or no permission), " +
                                                "so it's using the default folder for now.";
                            return DefaultRootFolder;
                        }
                        DataFolderProblem = $"Your data folder \"{chosen}\" wasn't found (a drive not connected?), " +
                                            "so BoE is using the default folder for now.";
                    }
                }
            }
            catch (Exception ex)
            {
                DataFolderProblem = $"Couldn't read where your data folder is ({ex.Message}); using the default.";
            }
            return DefaultRootFolder;
        }

        /// <summary>
        /// First start after installing: no data folder chosen yet and no
        /// default folder (Documents\BoE_V2) either. BoE then asks where to
        /// keep its data before anything is created.
        /// </summary>
        public static bool IsFirstRun
        {
            get
            {
                if (_root != null) return false;
                try
                {
                    return !File.Exists(PointerPath) && !Directory.Exists(DefaultRootFolder);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Where data would go for a folder the user picked: the folder itself
        /// when it's empty, new, or already holds BoE data; otherwise a
        /// "BoE_V2" folder inside it (so BoE's files don't mix with others).
        /// </summary>
        public static string DataFolderFor(string picked)
        {
            string full = Path.GetFullPath(picked);
            if (!Directory.Exists(full)) return full;
            bool hasBoe = File.Exists(Path.Combine(full, "breakersofe.db")) ||
                          File.Exists(Path.Combine(full, "Collection", "collection.db"));
            if (hasBoe || !Directory.EnumerateFileSystemEntries(full).Any()) return full;
            return Path.Combine(full, "BoE_V2");
        }

        /// <summary>
        /// First start: use this data folder from now on (creates it; checks
        /// BoE can save there). Returns "" or why it can't be used.
        /// </summary>
        public static string ChooseFirstFolder(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                if (!CanWrite(folder)) return "BoE can't save in that folder (read-only, or no permission). Pick another.";
                SetDataFolder(folder);
                _root = Path.GetFullPath(folder);
                return "";
            }
            catch (Exception ex)
            {
                return $"That folder can't be used: {ex.Message}";
            }
        }

        private static bool CanWrite(string folder)
        {
            try
            {
                string probe = Path.Combine(folder, ".boe-write-test");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Remember a new data folder (used from the next start). Null or the
        /// default folder: forget the pointer, back to Documents\BoE_V2.
        /// </summary>
        public static void SetDataFolder(string? folder)
        {
            string? dir = Path.GetDirectoryName(PointerPath);
            if (dir != null) Directory.CreateDirectory(dir);
            if (string.IsNullOrWhiteSpace(folder) ||
                string.Equals(Path.GetFullPath(folder).TrimEnd('\\'), Path.GetFullPath(DefaultRootFolder).TrimEnd('\\'),
                              StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(PointerPath)) File.Delete(PointerPath);
                Directory.CreateDirectory(DefaultRootFolder);     // so the next start doesn't ask again (first run)
                return;
            }
            File.WriteAllText(PointerPath, Path.GetFullPath(folder));
        }

        // ── User data folders ─────────────────────────────────────────────────
        public static string DecksFolder =>
            EnsureFolder(Path.Combine(RootFolder, "Decks"));

        public static string FiltersFolder =>
            EnsureFolder(Path.Combine(RootFolder, "Filters"));

        public static string ExportsFolder =>
            EnsureFolder(Path.Combine(RootFolder, "Exports"));

        public static string BackupsFolder =>
            EnsureFolder(Path.Combine(RootFolder, "Backups"));

        public static string ImportsFolder =>
            EnsureFolder(Path.Combine(RootFolder, "Imports"));

        // ── Program folder (next to the executable) — READ-ONLY at run time:
        //    under Program Files a normal user can't write here. Everything the
        //    app downloads goes under Documents\BoE_V2 instead. ──────────────
        public static string ProgramFolder =>
            AppDomain.CurrentDomain.BaseDirectory;

        // ── Downloaded data (written by Update Database) ──────────────────────
        public static string SetSymbolsFolder =>
            EnsureFolder(Path.Combine(RootFolder, "SetSymbols"));

        public static string ManaSymbolsFolder =>
            EnsureFolder(Path.Combine(RootFolder, "ManaSymbols"));

        public static string RulingsDatabasePath =>
            Path.Combine(RootFolder, "rulings.db");

        // ── Set symbol lookup (cached: the grid asks for every row) ───────────
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _setSymbolCache =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Path of a set's symbol image, or "" when it isn't downloaded. One
        /// helper for every card type; File.Exists runs once per set, not per
        /// row per redraw.
        /// </summary>
        public static string SetSymbolPath(string? setCode)
        {
            if (string.IsNullOrWhiteSpace(setCode)) return string.Empty;
            return _setSymbolCache.GetOrAdd(setCode, code =>
            {
                string path = Path.Combine(SetSymbolsFolder, $"{code.ToLowerInvariant()}.png");
                return File.Exists(path) ? path : string.Empty;
            });
        }

        /// <summary>Forget cached symbol lookups (after new symbols are downloaded).</summary>
        public static void ClearSetSymbolCache() => _setSymbolCache.Clear();

        /// <summary>
        /// One-time move: earlier v2 builds saved symbols and rulings next to
        /// the program. Copy them to Documents\BoE_V2 if they aren't there yet
        /// (the originals are left alone). Never throws.
        /// </summary>
        public static void CopyLegacyDownloads()
        {
            try
            {
                CopyFolderIfEmpty(Path.Combine(ProgramFolder, "SetSymbols"), SetSymbolsFolder);
                CopyFolderIfEmpty(Path.Combine(ProgramFolder, "ManaSymbols"), ManaSymbolsFolder);
                string oldRulings = Path.Combine(ProgramFolder, "rulings.db");
                if (File.Exists(oldRulings) && !File.Exists(RulingsDatabasePath))
                    File.Copy(oldRulings, RulingsDatabasePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CopyLegacyDownloads: {ex.Message}");
            }
        }

        private static void CopyFolderIfEmpty(string from, string to)
        {
            if (!Directory.Exists(from) || Directory.EnumerateFileSystemEntries(to).Any()) return;
            foreach (var file in Directory.EnumerateFiles(from))
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: false);
        }

        public static string CardImagesFolder =>
            EnsureFolder(Path.Combine(RootFolder, "CardImages"));

        public static string DatabasePath =>
            Path.Combine(RootFolder, "breakersofe.db");

        public static string KeywordCachePath =>
            Path.Combine(RootFolder, "keywords.json");

        // ── Tabletop image folders ─────────────────────────────────────────────
        public static string PlaymatImagesFolder =>
            EnsureFolder(Path.Combine(RootFolder, "Tabletop", "Playmats"));

        public static string SleeveImagesFolder =>
            EnsureFolder(Path.Combine(RootFolder, "Tabletop", "Sleeves"));

        // ── Collection database (separate from card pool) ──────────────────────
        public static string CollectionFolder =>
            EnsureFolder(Path.Combine(RootFolder, "Collection"));

        public static string CollectionDatabasePath =>
            Path.Combine(CollectionFolder, "collection.db");

        /// <summary>Call once on startup to ensure all folders exist.</summary>
        public static void EnsureAllFolders()
        {
            _ = RootFolder;
            _ = DecksFolder;
            _ = FiltersFolder;
            _ = ExportsFolder;
            _ = ImportsFolder;
            _ = CardImagesFolder;
            _ = PlaymatImagesFolder;
            _ = SleeveImagesFolder;
            _ = CollectionFolder;
            _ = SetSymbolsFolder;
            _ = ManaSymbolsFolder;
        }

        // ── Helper ────────────────────────────────────────────────────────────
        private static string EnsureFolder(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        /// Returns a safe file name from a deck name
        /// (removes invalid path characters)
        /// </summary>
        public static string SafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        /// <summary>
        /// Returns full path for a deck file
        /// </summary>
        public static string DeckFilePath(string deckName) =>
            Path.Combine(DecksFolder,
                $"{SafeFileName(deckName)}.deck");
    }
}
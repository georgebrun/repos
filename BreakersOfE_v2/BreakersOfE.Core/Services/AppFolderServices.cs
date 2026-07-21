using System;
using System.IO;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Centralized service for all user-facing file paths.
    /// All user data lives under My Documents\Breakers of E\
    /// Program data (DB, symbols) stays next to the executable.
    /// </summary>
    public static class AppFolderService
    {
        // ── Root folder ───────────────────────────────────────────────────────
        public static string RootFolder =>
            EnsureFolder(Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.MyDocuments),
                "Breakers of E"));

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

        // ── Program data folders (next to executable) ─────────────────────────
        public static string ProgramFolder =>
            AppDomain.CurrentDomain.BaseDirectory;

        public static string SetSymbolsFolder =>
            EnsureFolder(Path.Combine(ProgramFolder, "SetSymbols"));

        public static string ManaSymbolsFolder =>
            EnsureFolder(Path.Combine(ProgramFolder, "ManaSymbols"));

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
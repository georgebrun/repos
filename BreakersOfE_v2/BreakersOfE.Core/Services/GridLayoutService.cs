using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BreakersOfE.Services
{
    /// <summary>One column's saved layout within one table.</summary>
    public class ColumnLayout
    {
        public string Header { get; set; } = string.Empty;
        public int DisplayIndex { get; set; }
        public bool Visible { get; set; } = true;
        public double Width { get; set; }
    }

    /// <summary>
    /// Saved grid column layouts — order, visibility, and width — kept
    /// SEPARATELY per table (Cards, Tokens, Planes, …, Collection; decks and
    /// binders later). Stored in My Documents\BoE_V2\GridLayouts.json.
    ///
    /// A table with no saved entry uses the grid's built-in defaults.
    /// </summary>
    public static class GridLayoutService
    {
        private static string FilePath =>
            Path.Combine(AppFolderService.RootFolder, "GridLayouts.json");

        private static readonly JsonSerializerOptions _json =
            new() { WriteIndented = true };

        private static Dictionary<string, List<ColumnLayout>>? _cache;

        private static Dictionary<string, List<ColumnLayout>> All =>
            _cache ??= Load();

        private static Dictionary<string, List<ColumnLayout>> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var loaded = JsonSerializer.Deserialize<
                        Dictionary<string, List<ColumnLayout>>>(
                        File.ReadAllText(FilePath), _json);
                    if (loaded != null)
                        return new Dictionary<string, List<ColumnLayout>>(
                            loaded, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                // A damaged file must never stop the app — fall back to defaults.
                System.Diagnostics.Debug.WriteLine($"GridLayouts load failed: {ex.Message}");
            }
            return new Dictionary<string, List<ColumnLayout>>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The saved layout for a table, or null if it uses defaults.</summary>
        public static List<ColumnLayout>? Get(string table) =>
            All.TryGetValue(table, out var layout) ? layout : null;

        /// <summary>Save a table's layout.</summary>
        public static void Set(string table, List<ColumnLayout> layout)
        {
            All[table] = layout;
            Save();
        }

        /// <summary>Forget a table's layout (back to defaults).</summary>
        public static void Remove(string table)
        {
            if (All.Remove(table)) Save();
        }

        private static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(All, _json));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GridLayouts save failed: {ex.Message}");
            }
        }
    }
}
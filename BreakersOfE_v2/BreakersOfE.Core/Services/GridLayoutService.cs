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

        // ── Change notice: View and Edit show the same table, so a layout
        //    changed on one shows on the other. ─────────────────────────
        private static readonly Dictionary<string, int> _versions = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Raised (with the table key) after a table's layout or zoom is saved or reset.</summary>
        public static event Action<string>? Changed;

        /// <summary>Goes up each time a table's layout or zoom changes (a page compares it on return).</summary>
        public static int Version(string table) => _versions.TryGetValue(table, out int v) ? v : 0;

        private static void Bump(string table)
        {
            _versions[table] = Version(table) + 1;
            Changed?.Invoke(table);
        }

        /// <summary>The saved layout for a table, or null if it uses defaults.</summary>
        public static List<ColumnLayout>? Get(string table) =>
            All.TryGetValue(table, out var layout) ? layout : null;

        /// <summary>Save a table's layout.</summary>
        public static void Set(string table, List<ColumnLayout> layout)
        {
            All[table] = layout;
            Save();
            Bump(table);
        }

        /// <summary>Forget a table's layout (back to defaults).</summary>
        public static void Remove(string table)
        {
            if (All.Remove(table)) Save();
            Bump(table);
        }

        private static void Save()
        {
            try
            {
                SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(All, _json));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GridLayouts save failed: {ex.Message}");
            }
        }

        // ── Zoom per table (grid only), in its own file ──────────────────
        // Kept apart from GridLayouts.json so that file's format never changes.
        private static string ZoomPath =>
            Path.Combine(AppFolderService.RootFolder, "GridZoom.json");

        private static Dictionary<string, double>? _zoom;

        private static Dictionary<string, double> Zooms => _zoom ??= LoadZoom();

        private static Dictionary<string, double> LoadZoom()
        {
            try
            {
                if (File.Exists(ZoomPath))
                {
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, double>>(
                        File.ReadAllText(ZoomPath), _json);
                    if (loaded != null)
                        return new Dictionary<string, double>(loaded, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GridZoom load failed: {ex.Message}");
            }
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        // ── Edit pages: each table's own Grid / Gallery choice ───────────
        private static string EditViewsPath =>
            Path.Combine(AppFolderService.RootFolder, "EditViews.json");

        private static Dictionary<string, bool>? _editViews;

        private static Dictionary<string, bool> EditViews => _editViews ??= LoadEditViews();

        private static Dictionary<string, bool> LoadEditViews()
        {
            try
            {
                if (File.Exists(EditViewsPath))
                {
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, bool>>(
                        File.ReadAllText(EditViewsPath), _json);
                    if (loaded != null)
                        return new Dictionary<string, bool>(loaded, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EditViews load failed: {ex.Message}");
            }
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>An Edit page table shown as a gallery (true) or grid (false); null = never chosen.</summary>
        public static bool? GetEditGallery(string table) =>
            EditViews.TryGetValue(table, out var g) ? g : null;

        public static void SetEditGallery(string table, bool gallery)
        {
            EditViews[table] = gallery;
            try { SafeFile.WriteAllText(EditViewsPath, JsonSerializer.Serialize(EditViews, _json)); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EditViews save failed: {ex.Message}");
            }
        }

        /// <summary>A remembered layout number (a splitter position, a panel width), or null.</summary>
        public static double? GetNumber(string key) =>
            Zooms.TryGetValue("n:" + key, out var v) ? v : null;

        /// <summary>Remember a layout number (kept with the zoom values).</summary>
        public static void SetNumber(string key, double value)
        {
            Zooms["n:" + key] = value;
            try { SafeFile.WriteAllText(ZoomPath, JsonSerializer.Serialize(Zooms, _json)); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Layout number save failed: {ex.Message}");
            }
        }

        /// <summary>A table's grid zoom (1.0 = 100%).</summary>
        public static double GetZoom(string table) =>
            Zooms.TryGetValue(table, out var z) && z > 0 ? z : 1.0;

        /// <summary>Save a table's grid zoom (1.0 removes the entry).</summary>
        public static void SetZoom(string table, double zoom)
        {
            if (Math.Abs(zoom - 1.0) < 0.001) Zooms.Remove(table);
            else Zooms[table] = zoom;
            try { SafeFile.WriteAllText(ZoomPath, JsonSerializer.Serialize(Zooms, _json)); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GridZoom save failed: {ex.Message}");
            }
            Bump(table);
        }
    }
}
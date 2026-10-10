using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Finds the printing in the card pool (cards and tokens) that an imported
    /// line means, most certain first:
    ///   1. Scryfall ID;
    ///   2. set code + collector number;
    ///   3. set (code or name) + card name;
    ///   4. card name only.
    /// Several printings fitting (a name without a set, a set with several
    /// arts) is a "Check": one is picked, the others offered to choose from.
    /// Names are compared without case, accents or spacing differences, and
    /// "A/B", "A // B" and a double-faced card's front face all match.
    /// Load once per import (about 100,000 printings, a second or two).
    /// </summary>
    public sealed class CardMatcher
    {
        private readonly Dictionary<string, PoolPrinting> _bySid = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(string Set, string Cn), PoolPrinting> _bySetCn = new();
        private readonly Dictionary<(string Set, string Name), List<PoolPrinting>> _bySetName = new();
        private readonly Dictionary<string, List<PoolPrinting>> _byName = new();
        private readonly Dictionary<string, string> _setCodeByName = new();
        private readonly HashSet<string> _setCodes = new();

        /// <summary>Other apps' set codes that differ from Scryfall's.</summary>
        private static readonly Dictionary<string, string> SetAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["dar"] = "dom",             // Dominaria (Arena)
            ["con_"] = "con",            // Conflux (some exports escape CON, a Windows reserved name)
            ["mps_akh"] = "mp2",         // Amonkhet Invocations
        };

        public int Count => _bySid.Count;

        public static CardMatcher Load()
        {
            var m = new CardMatcher();
            using var db = new AppDbContext();
            foreach (var c in db.PoolCards.AsNoTracking()
                         .Select(c => new { c.ScryfallId, c.Name, c.SetCode, c.SetName, c.CollectorNumber, c.ReleasedAt, c.IsNonFoil, c.IsFoil, c.IsEtched, c.IsDigital })
                         .ToList())
                m.Add(new PoolPrinting
                {
                    Table = CollectionEditService.CardsTable,
                    ScryfallId = c.ScryfallId, Name = c.Name, SetCode = c.SetCode, SetName = c.SetName,
                    CollectorNumber = c.CollectorNumber, ReleasedAt = c.ReleasedAt ?? "",
                    NonFoil = c.IsNonFoil, Foil = c.IsFoil, Etched = c.IsEtched, Digital = c.IsDigital,
                });
            foreach (var t in db.TokenCards.AsNoTracking()
                         .Select(t => new { t.ScryfallId, t.Name, t.SetCode, t.SetName, t.CollectorNumber, t.ReleasedAt, t.IsNonFoil, t.IsFoil, t.IsEtched })
                         .ToList())
                m.Add(new PoolPrinting
                {
                    Table = CollectionEditService.TokensTable,
                    ScryfallId = t.ScryfallId, Name = t.Name, SetCode = t.SetCode, SetName = t.SetName,
                    CollectorNumber = t.CollectorNumber, ReleasedAt = t.ReleasedAt ?? "",
                    NonFoil = t.IsNonFoil, Foil = t.IsFoil, Etched = t.IsEtched,
                });
            // Oversized and front cards: found by Scryfall ID or set + number, and
            // last among a name's printings (a name alone means the regular card).
            foreach (var o in db.OversizedCards.AsNoTracking()
                         .Select(o => new { o.ScryfallId, o.Name, o.SetCode, o.SetName, o.CollectorNumber, o.ReleasedAt, o.IsNonFoil, o.IsFoil, o.IsEtched })
                         .ToList())
                m.Add(new PoolPrinting
                {
                    Table = CollectionEditService.OversizedTable,
                    ScryfallId = o.ScryfallId, Name = o.Name, SetCode = o.SetCode, SetName = o.SetName,
                    CollectorNumber = o.CollectorNumber, ReleasedAt = o.ReleasedAt ?? "",
                    NonFoil = o.IsNonFoil, Foil = o.IsFoil, Etched = o.IsEtched,
                });
            foreach (var f in db.FrontCards.AsNoTracking()
                         .Select(f => new { f.ScryfallId, f.Name, f.SetCode, f.SetName, f.CollectorNumber, f.ReleasedAt, f.IsNonFoil, f.IsFoil, f.IsEtched })
                         .ToList())
                m.Add(new PoolPrinting
                {
                    Table = CollectionEditService.FrontTable,
                    ScryfallId = f.ScryfallId, Name = f.Name, SetCode = f.SetCode, SetName = f.SetName,
                    CollectorNumber = f.CollectorNumber, ReleasedAt = f.ReleasedAt ?? "",
                    NonFoil = f.IsNonFoil, Foil = f.IsFoil, Etched = f.IsEtched,
                });
            return m;
        }

        private void Add(PoolPrinting p)
        {
            if (string.IsNullOrEmpty(p.ScryfallId) || _bySid.ContainsKey(p.ScryfallId)) return;
            _bySid[p.ScryfallId] = p;
            string set = p.SetCode.ToLowerInvariant();
            _setCodes.Add(set);
            if (p.SetName.Length > 0) _setCodeByName.TryAdd(NameKey(p.SetName), set);
            if (p.CollectorNumber.Length > 0) _bySetCn.TryAdd((set, p.CollectorNumber.ToLowerInvariant()), p);
            foreach (var key in NameKeys(p.Name))
            {
                Bucket(_bySetName, (set, key)).Add(p);
                Bucket(_byName, key).Add(p);
            }
        }

        private static List<PoolPrinting> Bucket<TKey>(Dictionary<TKey, List<PoolPrinting>> d, TKey key) where TKey : notnull
        {
            if (!d.TryGetValue(key, out var list)) d[key] = list = new List<PoolPrinting>();
            return list;
        }

        // ── Names ────────────────────────────────────────────────────────
        /// <summary>
        /// "Lim-Dûl's Vault" → "lim-dul's vault"; "Fire/Ice" → "fire // ice";
        /// tags in brackets ("[Reprint]") dropped; Æ → ae.
        /// </summary>
        public static string NameKey(string name)
        {
            string s = (name ?? "").Trim();
            int b = s.IndexOf(" [", StringComparison.Ordinal);
            if (b > 0 && s.EndsWith("]")) s = s[..b];
            s = s.Replace("Æ", "Ae").Replace("æ", "ae");
            var sb = new StringBuilder(s.Length);
            foreach (char c in s.Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            s = sb.ToString().ToLowerInvariant();
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*/{1,2}\s*", " // ");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"[’‘`]", "'");
            return System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
        }

        /// <summary>The full name and, for a two-part card, its front face.</summary>
        private static IEnumerable<string> NameKeys(string name)
        {
            string full = NameKey(name);
            yield return full;
            int i = full.IndexOf(" // ", StringComparison.Ordinal);
            if (i > 0) yield return full[..i];
        }

        private string? SetCodeOf(ImportLine line)
        {
            string code = line.SetCode.Trim().ToLowerInvariant();
            if (code.Length > 0)
            {
                if (SetAliases.TryGetValue(code, out var alias)) code = alias;
                if (_setCodes.Contains(code)) return code;
            }
            string name = line.SetName.Length > 0 ? line.SetName : (code.Length > 0 && !_setCodes.Contains(code) ? line.SetCode : "");
            if (name.Length > 0 && _setCodeByName.TryGetValue(NameKey(name), out var byName)) return byName;
            return code.Length > 0 ? code : null;
        }

        // ── Match ────────────────────────────────────────────────────────
        /// <summary>
        /// The printing(s) a line can mean: [0] is the pick. Empty = not found.
        /// <paramref name="how"/> says what decided it.
        /// </summary>
        public List<PoolPrinting> Match(ImportLine line, out string how)
        {
            how = "";
            // 1. Scryfall ID
            if (line.ScryfallId.Length > 0 && _bySid.TryGetValue(line.ScryfallId.Trim(), out var bySid))
            {
                how = "Scryfall ID";
                return new List<PoolPrinting> { bySid };
            }

            string? set = SetCodeOf(line);
            string key = NameKey(line.Name);

            // The name as written, then without a trailing decoration other apps add
            // ("Lightning Bolt (Borderless)", "Sol Ring - Foil").
            var keys = new List<string>();
            if (key.Length > 0) keys.Add(key);
            string bare = System.Text.RegularExpressions.Regex.Replace(line.Name, @"\s*(\([^()]*\)|-\s*(foil|etched|non-?foil))\s*$", "",
                                                                       System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            string bareKey = NameKey(bare);
            if (bareKey.Length > 0 && bareKey != key) keys.Add(bareKey);

            // 2. Set + collector number (the name, when given, must agree).
            if (set != null && line.CollectorNumber.Length > 0 &&
                _bySetCn.TryGetValue((set, line.CollectorNumber.Trim().ToLowerInvariant()), out var byCn) &&
                (key.Length == 0 || keys.Any(k => NameKeys(byCn.Name).Contains(k))))
            {
                how = "set and collector number";
                return new List<PoolPrinting> { byCn };
            }

            if (key.Length == 0) return new List<PoolPrinting>();

            // 3. Set + name
            foreach (string k in keys)
                if (set != null && _bySetName.TryGetValue((set, k), out var inSet) && inSet.Count > 0)
                {
                    how = inSet.Count == 1 ? "set and name" : $"set and name — {inSet.Count} versions in that set";
                    return Order(inSet, line, byNumber: true);
                }

            // 4. Name only
            foreach (string k in keys)
                if (_byName.TryGetValue(k, out var all) && all.Count > 0)
                {
                    how = all.Count == 1 ? "name" : $"name only — {all.Count} printings";
                    if (set != null) how += $" (set \"{line.SetCode}{line.SetName}\" not found)";
                    return Order(all, line, byNumber: false);
                }
            return new List<PoolPrinting>();
        }

        /// <summary>
        /// The likeliest first: the right kind (token or card), paper before
        /// digital, the finish asked for, then (same set) the lowest collector
        /// number or (any set) the newest printing.
        /// </summary>
        private static List<PoolPrinting> Order(List<PoolPrinting> list, ImportLine line, bool byNumber)
        {
            bool wantToken = line.Section == ImportSection.Tokens || line.Table == CollectionEditService.TokensTable;
            var q = list.Distinct()
                .OrderBy(p => p.IsToken == wantToken ? 0 : 1)
                .ThenBy(p => p.IsSideTable ? 1 : 0)
                .ThenBy(p => p.Digital ? 1 : 0)
                .ThenBy(p => line.Finish == null || p.Has(line.Finish) ? 0 : 1);
            q = byNumber
                ? q.ThenBy(p => NumberSort(p.CollectorNumber)).ThenBy(p => p.CollectorNumber, StringComparer.OrdinalIgnoreCase)
                : q.ThenByDescending(p => p.ReleasedAt, StringComparer.Ordinal).ThenBy(p => NumberSort(p.CollectorNumber));
            return q.ToList();
        }

        private static double NumberSort(string cn)
        {
            int end = 0;
            while (end < cn.Length && char.IsDigit(cn[end])) end++;
            return end > 0 && double.TryParse(cn[..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 1e9;
        }
    }
}

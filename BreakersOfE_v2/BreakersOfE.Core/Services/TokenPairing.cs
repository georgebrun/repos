using System;
using System.Collections.Generic;
using System.Linq;
using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Two-sided tokens (Reality Fracture, Star Trek, Commander decks): two of
    /// Scryfall's tokens printed back to back as one physical card. Scryfall
    /// lists each side as its own token, so BoE makes the card itself: one
    /// "Officer // Treasure" with its own collection row and deck line, its
    /// id the two sides' ids joined with "+" ("{officer}+{treasure}").
    ///
    /// The sides go alphabetically (a card has no front: "Officer // Treasure"
    /// whichever side was picked first). Each side keeps its own pool card, so
    /// the card shows both faces. Real double-faced tokens (one Scryfall card,
    /// layout "double_faced_token") are one card already and never paired.
    /// </summary>
    public static class TokenPairing
    {
        /// <summary>The layout a two-sided card is stored with (its own, not Scryfall's).</summary>
        public const string Layout = "two_sided_token";

        private const char Join = '+';

        /// <summary>Is this id a two-sided card's ("{a}+{b}")?</summary>
        public static bool IsPairSid(string? sid) => !string.IsNullOrEmpty(sid) && sid.IndexOf(Join) > 0;

        /// <summary>A two-sided card's two side ids (in its order), or null for any other id.</summary>
        public static (string A, string B)? Sides(string? sid)
        {
            if (!IsPairSid(sid)) return null;
            int i = sid!.IndexOf(Join);
            return (sid[..i], sid[(i + 1)..]);
        }

        /// <summary>The two-sided card's id for these sides (already in order).</summary>
        public static string SidOf(string a, string b) => a + Join + b;

        /// <summary>A collection row, deck line or card object of a two-sided card.</summary>
        public static bool IsPair(object? row) => row != null && IsPairSid(CollectionEditService.GetString(row, "ScryfallId"));

        /// <summary>Which side goes first: by name, then collector number, then id (so a card always reads the same).</summary>
        public static bool AFirst(string nameA, string numberA, string sidA, string nameB, string numberB, string sidB)
        {
            int c = string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c < 0;
            c = NumberSort(numberA).CompareTo(NumberSort(numberB));
            if (c != 0) return c < 0;
            c = string.Compare(numberA, numberB, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c < 0;
            return string.CompareOrdinal(sidA, sidB) <= 0;
        }

        private static double NumberSort(string n) =>
            double.TryParse(new string((n ?? "").TakeWhile(char.IsDigit).ToArray()), out var d) ? d : 9999;

        /// <summary>The two-sided card made of these two tokens (in order): one card object, as the pool would have it.</summary>
        public static TokenCard Compose(TokenCard x, TokenCard y)
        {
            var (a, b) = AFirst(x.Name, x.CollectorNumber, x.ScryfallId, y.Name, y.CollectorNumber, y.ScryfallId) ? (x, y) : (y, x);
            static string Two(string p, string q) => string.IsNullOrEmpty(p) && string.IsNullOrEmpty(q) ? ""
                : (string.IsNullOrEmpty(p) ? CardFaces.None : p) + CardFaces.Separator + (string.IsNullOrEmpty(q) ? CardFaces.None : q);
            static string Text(string p, string q) => string.IsNullOrEmpty(p) && string.IsNullOrEmpty(q) ? "" : p + CardFaces.TextSeparator + q;
            static string Colors(string p, string q) =>
                string.Concat("WUBRG".Where(c => (p + q).Contains(c)));
            return new TokenCard
            {
                ScryfallId = SidOf(a.ScryfallId, b.ScryfallId),
                OracleId = "",
                Name = a.Name + CardFaces.Separator + b.Name,
                TypeLine = Two(a.TypeLine, b.TypeLine),
                OracleText = Text(a.OracleText, b.OracleText),
                FlavorText = Text(a.FlavorText, b.FlavorText),
                Power = string.IsNullOrEmpty(a.Power) && string.IsNullOrEmpty(b.Power) ? "" : Two(a.Power, b.Power),
                Toughness = string.IsNullOrEmpty(a.Toughness) && string.IsNullOrEmpty(b.Toughness) ? "" : Two(a.Toughness, b.Toughness),
                Colors = Colors(a.Colors, b.Colors),
                ColorIdentity = Colors(a.ColorIdentity, b.ColorIdentity),
                SetCode = a.SetCode,
                SetName = a.SetName,
                SetType = a.SetType,
                CollectorNumber = a.CollectorNumber + CardFaces.Separator + b.CollectorNumber,
                Rarity = a.Rarity,
                Artist = string.Equals(a.Artist, b.Artist, StringComparison.OrdinalIgnoreCase) ? a.Artist : Two(a.Artist, b.Artist),
                ImageSmallUrl = a.ImageSmallUrl,
                ImageNormalUrl = a.ImageNormalUrl,
                LocalImagePath = a.LocalImagePath,
                Layout = Layout,
                ProducedMana = string.Concat((a.ProducedMana + b.ProducedMana).Distinct()),
                // Both sides of one card share its finish: only the finishes both printings have.
                IsFoil = a.IsFoil && b.IsFoil,
                IsNonFoil = a.IsNonFoil && b.IsNonFoil,
                IsEtched = a.IsEtched && b.IsEtched,
                ReleasedAt = string.CompareOrdinal(a.ReleasedAt, b.ReleasedAt) >= 0 ? a.ReleasedAt : b.ReleasedAt,
            };
        }

        /// <summary>A collection token row as a card object (its stored card data), to compose a two-sided card from.</summary>
        public static TokenCard FromRow(TokenCollectionEntry r) => new()
        {
            ScryfallId = r.ScryfallId, OracleId = r.OracleId, Name = r.Name, TypeLine = r.TypeLine, OracleText = r.OracleText,
            FlavorText = r.FlavorText, Power = r.Power, Toughness = r.Toughness, Colors = r.Colors, ColorIdentity = r.ColorIdentity,
            SetCode = r.SetCode, SetName = r.SetName, SetType = r.SetType, CollectorNumber = r.CollectorNumber, Rarity = r.Rarity,
            Artist = r.Artist, ImageSmallUrl = r.ImageSmallUrl, ImageNormalUrl = r.ImageNormalUrl, Layout = r.Layout,
            IsFoil = r.IsFoilAvailable, IsNonFoil = r.IsNonFoilAvailable, ReleasedAt = r.ReleasedAt, LocalImagePath = r.LocalImagePath,
        };

        /// <summary>The token pool's cards for these ids (missing ones left out).</summary>
        public static Dictionary<string, TokenCard> PoolCards(IEnumerable<string> sids)
        {
            var set = sids.Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var map = new Dictionary<string, TokenCard>(StringComparer.OrdinalIgnoreCase);
            if (set.Count == 0) return map;
            try
            {
                using var adb = new AppDbContext();
                for (int i = 0; i < set.Count; i += 400)
                {
                    var chunk = set.Skip(i).Take(400).ToList();
                    foreach (var t in adb.TokenCards.AsNoTracking().Where(t => chunk.Contains(t.ScryfallId)).ToList())
                        map[t.ScryfallId] = t;
                }
            }
            catch { /* no card pool yet */ }
            return map;
        }

        /// <summary>A two-sided card's two sides from the token pool (null for a side the pool hasn't got).</summary>
        public static (TokenCard? A, TokenCard? B) PoolSides(string? pairSid)
        {
            if (Sides(pairSid) is not { } s) return (null, null);
            var map = PoolCards(new[] { s.A, s.B });
            return (map.GetValueOrDefault(s.A), map.GetValueOrDefault(s.B));
        }

        /// <summary>The two-sided card for this id, from the token pool (null when the pool hasn't both sides).</summary>
        public static TokenCard? FromPool(string? pairSid)
        {
            var (a, b) = PoolSides(pairSid);
            return a != null && b != null ? Compose(a, b) : null;
        }

        /// <summary>A two-sided card as a deck's token line: its back face's picture is the second side.</summary>
        public static DeckCard ToDeckCard(TokenCard pair)
        {
            var line = DeckService.FromTokenCard(pair);
            var (_, b) = PoolSides(pair.ScryfallId);
            if (b != null)
            {
                line.ImageBackUrl = b.ImageNormalUrl;
                line.LocalImageBackPath = b.LocalImagePath;
            }
            return line;
        }

        /// <summary>A two-sided collection row as a deck's token line.</summary>
        public static DeckCard ToDeckCard(TokenCollectionEntry row) =>
            ToDeckCard(FromPool(row.ScryfallId) ?? FromRowAsPair(row));

        /// <summary>A two-sided row's own stored data as a card object (when the pool hasn't its sides).</summary>
        private static TokenCard FromRowAsPair(TokenCollectionEntry row)
        {
            var t = FromRow(row);
            t.Layout = Layout;
            return t;
        }

        /// <summary>The two side names of a two-sided card's name ("Officer // Treasure" → Officer, Treasure).</summary>
        public static (string A, string B) SideNames(string name)
        {
            var s = CardFaces.Sides(name);
            return (s.Length > 0 ? s[0] : name, s.Length > 1 ? s[1] : "");
        }
    }
}

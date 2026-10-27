using System;
using System.ComponentModel.DataAnnotations;

namespace BreakersOfE.Models
{
    /// <summary>
    /// Two token rows that are the two sides of one physical card (Reality
    /// Fracture's two-sided tokens: Scryfall lists each side as its own
    /// token). Stored per copy: "Treasure / Angel × 1" and "Treasure / Goblin
    /// × 1" are two pairs. Each side is one exact collection row of
    /// Collection → Tokens (printing, finish, language, condition).
    ///
    /// The pair's copies in effect are never more than either row has
    /// (<see cref="Count"/> is capped when read), so removing copies and
    /// Undo never leave a pair pointing at copies that aren't there.
    /// Real double-faced tokens (one Scryfall card, layout
    /// "double_faced_token") are never paired: they're one card already.
    /// </summary>
    public class TokenPair
    {
        [Key]
        public int TokenPairId { get; set; }

        public string ASid { get; set; } = string.Empty;
        public string AFinish { get; set; } = CardFinish.NonFoil;
        public string ALanguage { get; set; } = string.Empty;
        public string ACondition { get; set; } = string.Empty;
        /// <summary>Name, set and number, for display without a lookup.</summary>
        public string AName { get; set; } = string.Empty;
        public string ASet { get; set; } = string.Empty;
        public string ANumber { get; set; } = string.Empty;

        public string BSid { get; set; } = string.Empty;
        public string BFinish { get; set; } = CardFinish.NonFoil;
        public string BLanguage { get; set; } = string.Empty;
        public string BCondition { get; set; } = string.Empty;
        public string BName { get; set; } = string.Empty;
        public string BSet { get; set; } = string.Empty;
        public string BNumber { get; set; } = string.Empty;

        /// <summary>Physical cards with these two sides.</summary>
        public int Count { get; set; } = 1;

        public DateTime DateAdded { get; set; } = DateTime.Now;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace BreakersOfE.Models
{
    /// <summary>What leads a deck (the command zone), if anything.</summary>
    public enum DeckLeader
    {
        /// <summary>No command zone (Constructed, Limited).</summary>
        None,
        /// <summary>A legendary creature, or two with Partner / Friends forever /
        /// Choose a Background / Doctor's companion (Commander, Duel Commander).</summary>
        Commander,
        /// <summary>A legendary creature or a planeswalker (Brawl, Standard Brawl).</summary>
        Brawl,
        /// <summary>An uncommon creature (Pauper Commander).</summary>
        PauperCommander,
        /// <summary>A planeswalker plus a signature instant or sorcery (Oathbreaker).</summary>
        Oathbreaker,
    }

    /// <summary>
    /// The construction rules of one kind of deck — THE one place they live.
    /// The deck check (deck view and Deck Statistics), the deck browser and,
    /// later, the Help page all read these, so they can't disagree.
    /// Card legality (bans, "is it on Arena", commons-only…) is Scryfall's,
    /// from each card's stored legalities, under <see cref="LegalityKey"/>.
    /// </summary>
    public sealed class DeckFormatRule
    {
        public DeckType Type { get; init; }
        /// <summary>Display name ("Commander", "Constructed", …).</summary>
        public string Name { get; init; } = "";
        /// <summary>Scryfall legality key; null = no legality list (Limited). Constructed: the chosen format's.</summary>
        public string? LegalityKey { get; init; }
        /// <summary>Main deck size (commander / oathbreaker cards included).</summary>
        public int Size { get; init; }
        /// <summary>True: exactly <see cref="Size"/>. False: at least.</summary>
        public bool ExactSize { get; init; }
        /// <summary>Copies of one card name allowed (basic lands and "any number" cards exempt). 0 = no limit.</summary>
        public int CopyLimit { get; init; }
        /// <summary>Largest sideboard; null = the format has no sideboard (sideboard cards aren't part of the deck).</summary>
        public int? SideboardMax { get; init; }
        public DeckLeader Leader { get; init; }
        public int StartingLife { get; init; }
        /// <summary>One line for the Help page and the deck check.</summary>
        public string Summary { get; init; } = "";

        public bool IsSingleton => CopyLimit == 1;
        public bool HasLeader => Leader != DeckLeader.None;
        public string SizeText => ExactSize ? $"exactly {Size}" : $"at least {Size}";
    }

    /// <summary>A Constructed format a deck can be checked against (60-card rules, its own card list).</summary>
    public sealed record ConstructedFormat(string Key, string Name);

    /// <summary>Every deck type BoE knows, with its rules.</summary>
    public static class DeckFormats
    {
        public static readonly IReadOnlyList<DeckFormatRule> All = new List<DeckFormatRule>
        {
            new()
            {
                Type = DeckType.Commander, Name = "Commander", LegalityKey = "commander",
                Size = 100, ExactSize = true, CopyLimit = 1, SideboardMax = null,
                Leader = DeckLeader.Commander, StartingLife = 40,
                Summary = "100 cards exactly, commander included. One copy of each card except basic lands. " +
                          "A legendary creature leads (two with Partner, Friends forever, Choose a Background " +
                          "or Doctor's companion); every card must fit its color identity. 40 life.",
            },
            new()
            {
                Type = DeckType.Standard, Name = "Constructed", LegalityKey = "standard",
                Size = 60, ExactSize = false, CopyLimit = 4, SideboardMax = 15,
                Leader = DeckLeader.None, StartingLife = 20,
                Summary = "At least 60 cards. Up to 4 copies of each card except basic lands (main deck and " +
                          "sideboard together). Sideboard up to 15. The format (Standard, Pioneer, Modern, " +
                          "Legacy, Vintage, Pauper, …) decides which cards are legal; Vintage restricted cards " +
                          "are limited to 1 copy. 20 life.",
            },
            new()
            {
                Type = DeckType.Brawl, Name = "Brawl", LegalityKey = "brawl",
                Size = 100, ExactSize = true, CopyLimit = 1, SideboardMax = null,
                Leader = DeckLeader.Brawl, StartingLife = 25,
                Summary = "MTG Arena. 100 cards exactly, commander included, one copy of each card except basic " +
                          "lands. A legendary creature or planeswalker leads; every card must fit its color " +
                          "identity. Cards must be on Arena. 25 life.",
            },
            new()
            {
                Type = DeckType.StandardBrawl, Name = "Standard Brawl", LegalityKey = "standardbrawl",
                Size = 60, ExactSize = true, CopyLimit = 1, SideboardMax = null,
                Leader = DeckLeader.Brawl, StartingLife = 25,
                Summary = "MTG Arena. 60 cards exactly, commander included, one copy of each card except basic " +
                          "lands, Standard-legal cards only. A legendary creature or planeswalker leads; every " +
                          "card must fit its color identity. 25 life.",
            },
            new()
            {
                Type = DeckType.PauperCommander, Name = "Pauper Commander", LegalityKey = "paupercommander",
                Size = 100, ExactSize = true, CopyLimit = 1, SideboardMax = null,
                Leader = DeckLeader.PauperCommander, StartingLife = 30,
                Summary = "100 cards exactly, commander included, one copy of each card except basic lands. " +
                          "An uncommon creature leads; the other 99 are commons. Every card must fit the " +
                          "commander's color identity. 30 life (16 commander damage).",
            },
            new()
            {
                Type = DeckType.DuelCommander, Name = "Duel Commander", LegalityKey = "duel",
                Size = 100, ExactSize = true, CopyLimit = 1, SideboardMax = null,
                Leader = DeckLeader.Commander, StartingLife = 20,
                Summary = "One-on-one Commander with its own banned list. 100 cards exactly, commander included, " +
                          "one copy of each card except basic lands; every card must fit the commander's color " +
                          "identity. 20 life.",
            },
            new()
            {
                Type = DeckType.Oathbreaker, Name = "Oathbreaker", LegalityKey = "oathbreaker",
                Size = 60, ExactSize = true, CopyLimit = 1, SideboardMax = null,
                Leader = DeckLeader.Oathbreaker, StartingLife = 20,
                Summary = "60 cards exactly, including a planeswalker (the Oathbreaker) and an instant or sorcery " +
                          "(its Signature Spell). One copy of each card except basic lands; every card must fit " +
                          "the Oathbreaker's color identity. 20 life.",
            },
            new()
            {
                Type = DeckType.Limited, Name = "Limited", LegalityKey = null,
                Size = 40, ExactSize = false, CopyLimit = 0, SideboardMax = null,
                Leader = DeckLeader.None, StartingLife = 20,
                Summary = "Draft or Sealed (FNM, or a box of boosters with friends). At least 40 cards from the " +
                          "cards you opened plus basic lands; any number of copies. 20 life.",
            },
        };

        /// <summary>The rules for a deck type (Constructed for anything unknown).</summary>
        public static DeckFormatRule For(DeckType type) =>
            All.FirstOrDefault(f => f.Type == type) ?? All.First(f => f.Type == DeckType.Standard);

        /// <summary>The rules for a deck; Constructed decks use their chosen format's card list.</summary>
        public static DeckFormatRule For(Deck deck, string? constructedKey = null)
        {
            var rule = For(deck.DeckType);
            if (rule.Type != DeckType.Standard) return rule;
            var cf = Constructed(constructedKey ?? deck.ConstructedFormat);
            return new DeckFormatRule
            {
                Type = rule.Type, Name = $"Constructed — {cf.Name}", LegalityKey = cf.Key,
                Size = rule.Size, ExactSize = rule.ExactSize, CopyLimit = rule.CopyLimit,
                SideboardMax = rule.SideboardMax, Leader = rule.Leader, StartingLife = rule.StartingLife,
                Summary = rule.Summary,
            };
        }

        /// <summary>The Constructed formats (60-card rules; each its own card list, from Scryfall).</summary>
        public static readonly IReadOnlyList<ConstructedFormat> ConstructedFormats = new List<ConstructedFormat>
        {
            new("standard", "Standard"),
            new("pioneer", "Pioneer"),
            new("modern", "Modern"),
            new("legacy", "Legacy"),
            new("vintage", "Vintage"),
            new("pauper", "Pauper"),
            new("premodern", "Premodern"),
            new("oldschool", "Old School"),
            new("historic", "Historic (Arena)"),
            new("timeless", "Timeless (Arena)"),
            new("explorer", "Explorer (Arena)"),
            new("alchemy", "Alchemy (Arena)"),
        };

        public static ConstructedFormat Constructed(string? key) =>
            ConstructedFormats.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? ConstructedFormats[0];
    }
}

using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Category of an MTG keyword ability.
    /// </summary>
    public enum KeywordCategory
    {
        Evasion,
        Combat,
        Protection,
        Triggered,
        Activated,
        Static,
        Spell,
        Cost,
        Replacement,
        FormatSpecific,
        Discovered,
        Other
    }

    /// <summary>
    /// A single MTG keyword entry with its category and rules definition.
    /// </summary>
    public class MtgKeyword
    {
        public string Name { get; set; } = string.Empty;
        public KeywordCategory Category { get; set; }
        public string Definition { get; set; } = string.Empty;
        /// <summary>
        /// Display name of the category for grouping in the UI.
        /// </summary>
        public string CategoryName => Category switch
        {
            KeywordCategory.Evasion => "Evasion",
            KeywordCategory.Combat => "Combat",
            KeywordCategory.Protection => "Protection",
            KeywordCategory.Triggered => "Triggered",
            KeywordCategory.Activated => "Activated",
            KeywordCategory.Static => "Static",
            KeywordCategory.Spell => "Spell",
            KeywordCategory.Cost => "Cost / Payment",
            KeywordCategory.Replacement => "Replacement",
            KeywordCategory.FormatSpecific => "Format-Specific",
            KeywordCategory.Discovered => "Discovered",
            KeywordCategory.Other => "Other",
            _ => "Other"
        };
    }

    /// <summary>
    /// Static dictionary of all major MTG keyword abilities.
    /// Used by the Keyword Search window and the Keyword Dictionary window.
    /// </summary>
    public static class MtgKeywordService
    {
        private static readonly List<MtgKeyword> _all = new()
        {
            // ── EVASION ───────────────────────────────────────────────────────
            new() { Name = "Flying",
                Category = KeywordCategory.Evasion,
                Definition = "This creature can only be blocked by creatures with flying or reach." },

            new() { Name = "Menace",
                Category = KeywordCategory.Evasion,
                Definition = "This creature can't be blocked except by two or more creatures." },

            new() { Name = "Intimidate",
                Category = KeywordCategory.Evasion,
                Definition = "This creature can't be blocked except by artifact creatures and/or creatures that share a color with it." },

            new() { Name = "Shadow",
                Category = KeywordCategory.Evasion,
                Definition = "This creature can only be blocked by creatures with shadow, and can only block creatures with shadow." },

            new() { Name = "Fear",
                Category = KeywordCategory.Evasion,
                Definition = "This creature can't be blocked except by artifact creatures and/or black creatures." },

            new() { Name = "Horsemanship",
                Category = KeywordCategory.Evasion,
                Definition = "This creature can't be blocked except by creatures with horsemanship." },

            new() { Name = "Landwalk",
                Category = KeywordCategory.Evasion,
                Definition = "This creature is unblockable as long as the defending player controls a land of the specified type (e.g. Islandwalk, Swampwalk)." },

            new() { Name = "Skulk",
                Category = KeywordCategory.Evasion,
                Definition = "This creature can't be blocked by creatures with greater power." },

            new() { Name = "Unblockable",
                Category = KeywordCategory.Evasion,
                Definition = "This creature can't be blocked." },

            // ── COMBAT ────────────────────────────────────────────────────────
            new() { Name = "First Strike",
                Category = KeywordCategory.Combat,
                Definition = "This creature deals combat damage before creatures without first strike." },

            new() { Name = "Double Strike",
                Category = KeywordCategory.Combat,
                Definition = "This creature deals both first-strike and regular combat damage." },

            new() { Name = "Trample",
                Category = KeywordCategory.Combat,
                Definition = "This creature can deal excess combat damage to the defending player or planeswalker even if it's blocked." },

            new() { Name = "Vigilance",
                Category = KeywordCategory.Combat,
                Definition = "Attacking doesn't cause this creature to tap." },

            new() { Name = "Haste",
                Category = KeywordCategory.Combat,
                Definition = "This creature can attack and tap as soon as it comes under your control." },

            new() { Name = "Deathtouch",
                Category = KeywordCategory.Combat,
                Definition = "Any amount of damage this creature deals is enough to destroy the creature it damages." },

            new() { Name = "Lifelink",
                Category = KeywordCategory.Combat,
                Definition = "Damage dealt by this creature also causes you to gain that much life." },

            new() { Name = "Reach",
                Category = KeywordCategory.Combat,
                Definition = "This creature can block creatures with flying." },

            new() { Name = "Banding",
                Category = KeywordCategory.Combat,
                Definition = "Creatures with banding can attack or block in groups; the controller of the band assigns all combat damage." },

            new() { Name = "Rampage",
                Category = KeywordCategory.Combat,
                Definition = "Whenever this creature becomes blocked, it gets +N/+N until end of turn for each creature blocking it beyond the first." },

            new() { Name = "Flanking",
                Category = KeywordCategory.Combat,
                Definition = "Whenever a creature without flanking blocks this creature, the blocking creature gets -1/-1 until end of turn." },

            new() { Name = "Bushido",
                Category = KeywordCategory.Combat,
                Definition = "Whenever this creature blocks or becomes blocked, it gets +N/+N until end of turn." },

            new() { Name = "Exalted",
                Category = KeywordCategory.Combat,
                Definition = "Whenever a creature you control attacks alone, that creature gets +1/+1 until end of turn." },

            new() { Name = "Infect",
                Category = KeywordCategory.Combat,
                Definition = "This creature deals damage to creatures in the form of -1/-1 counters and to players in the form of poison counters." },

            new() { Name = "Wither",
                Category = KeywordCategory.Combat,
                Definition = "This creature deals damage to creatures in the form of -1/-1 counters." },

            new() { Name = "Poisonous",
                Category = KeywordCategory.Combat,
                Definition = "Whenever this creature deals combat damage to a player, that player gets N poison counters." },

            new() { Name = "Battle Cry",
                Category = KeywordCategory.Combat,
                Definition = "Whenever this creature attacks, each other attacking creature gets +1/+0 until end of turn." },

            new() { Name = "Melee",
                Category = KeywordCategory.Combat,
                Definition = "Whenever this creature attacks, it gets +1/+1 until end of turn for each opponent you attacked this combat." },

            new() { Name = "Provoke",
                Category = KeywordCategory.Combat,
                Definition = "Whenever this creature attacks, you may have target creature defending player controls untap and block this creature if able." },

            new() { Name = "Ninjutsu",
                Category = KeywordCategory.Combat,
                Definition = "Pay cost, return an unblocked attacker you control to hand: put this card onto the battlefield tapped and attacking." },

            new() { Name = "Commander Ninjutsu",
                Category = KeywordCategory.Combat,
                Definition = "Like Ninjutsu, but can also be activated from the command zone." },

            // ── PROTECTION ────────────────────────────────────────────────────
            new() { Name = "Hexproof",
                Category = KeywordCategory.Protection,
                Definition = "This permanent can't be the target of spells or abilities your opponents control." },

            new() { Name = "Shroud",
                Category = KeywordCategory.Protection,
                Definition = "This permanent can't be the target of spells or abilities." },

            new() { Name = "Indestructible",
                Category = KeywordCategory.Protection,
                Definition = "Effects that say 'destroy' don't destroy this permanent. A creature with indestructible can't be destroyed by damage." },

            new() { Name = "Protection",
                Category = KeywordCategory.Protection,
                Definition = "This permanent can't be damaged, enchanted, equipped, blocked, or targeted by anything with the specified quality." },

            new() { Name = "Ward",
                Category = KeywordCategory.Protection,
                Definition = "Whenever this permanent becomes the target of a spell or ability an opponent controls, counter it unless that player pays the ward cost." },

            new() { Name = "Hexproof from",
                Category = KeywordCategory.Protection,
                Definition = "This permanent can't be the target of spells or abilities of the specified quality that opponents control." },

            new() { Name = "Phasing",
                Category = KeywordCategory.Protection,
                Definition = "This permanent phases out at the beginning of your untap step if it's in play, or phases in if it's phased out." },

            // ── TRIGGERED ─────────────────────────────────────────────────────
            new() { Name = "Enters the Battlefield",
                Category = KeywordCategory.Triggered,
                Definition = "A triggered ability that resolves when this permanent enters the battlefield (ETB effect)." },

            new() { Name = "Dies",
                Category = KeywordCategory.Triggered,
                Definition = "A triggered ability that resolves when this creature is put into the graveyard from the battlefield." },

            new() { Name = "Cascade",
                Category = KeywordCategory.Triggered,
                Definition = "When you cast this spell, exile cards from the top of your library until you exile a nonland card with lesser mana value. You may cast it without paying its mana cost." },

            new() { Name = "Landfall",
                Category = KeywordCategory.Triggered,
                Definition = "A triggered ability that resolves whenever a land enters the battlefield under your control." },

            new() { Name = "Raid",
                Category = KeywordCategory.Triggered,
                Definition = "A triggered or replacement ability that occurs if you attacked with a creature this turn." },

            new() { Name = "Revolt",
                Category = KeywordCategory.Triggered,
                Definition = "An ability that triggers or has an additional effect if a permanent you controlled left the battlefield this turn." },

            new() { Name = "Morbid",
                Category = KeywordCategory.Triggered,
                Definition = "An ability that triggers or has an additional effect if a creature died this turn." },

            new() { Name = "Constellation",
                Category = KeywordCategory.Triggered,
                Definition = "A triggered ability that resolves whenever an enchantment enters the battlefield under your control." },

            new() { Name = "Magecraft",
                Category = KeywordCategory.Triggered,
                Definition = "A triggered ability that resolves whenever you cast or copy an instant or sorcery spell." },

            new() { Name = "Prowess",
                Category = KeywordCategory.Triggered,
                Definition = "Whenever you cast a noncreature spell, this creature gets +1/+1 until end of turn." },

            new() { Name = "Ferocious",
                Category = KeywordCategory.Triggered,
                Definition = "A triggered or replacement ability that occurs if you control a creature with power 4 or greater." },

            new() { Name = "Hellbent",
                Category = KeywordCategory.Triggered,
                Definition = "An ability that has an additional effect if you have no cards in hand." },

            new() { Name = "Metalcraft",
                Category = KeywordCategory.Triggered,
                Definition = "An ability with an additional effect if you control three or more artifacts." },

            new() { Name = "Threshold",
                Category = KeywordCategory.Triggered,
                Definition = "An ability with an additional effect if seven or more cards are in your graveyard." },

            new() { Name = "Delirium",
                Category = KeywordCategory.Triggered,
                Definition = "An ability with an additional effect if there are four or more card types among cards in your graveyard." },

            new() { Name = "Spectacle",
                Category = KeywordCategory.Triggered,
                Definition = "You may cast this spell for its spectacle cost if an opponent lost life this turn." },

            new() { Name = "Undergrowth",
                Category = KeywordCategory.Triggered,
                Definition = "An ability that counts the number of creature cards in your graveyard." },

            new() { Name = "Adamant",
                Category = KeywordCategory.Triggered,
                Definition = "An ability with an additional effect if three or more mana of the same color was spent to cast the spell." },

            new() { Name = "Enrage",
                Category = KeywordCategory.Triggered,
                Definition = "A triggered ability that resolves whenever this creature is dealt damage." },

            new() { Name = "Mentor",
                Category = KeywordCategory.Triggered,
                Definition = "Whenever this creature attacks, put a +1/+1 counter on target attacking creature with lesser power." },

            new() { Name = "Afterlife",
                Category = KeywordCategory.Triggered,
                Definition = "When this creature dies, create N 1/1 white and black Spirit creature tokens with flying." },

            new() { Name = "Amass",
                Category = KeywordCategory.Triggered,
                Definition = "Put N +1/+1 counters on an Army you control (create one if you don't control one)." },

            new() { Name = "Fabricate",
                Category = KeywordCategory.Triggered,
                Definition = "When this creature enters the battlefield, put N +1/+1 counters on it or create N 1/1 colorless Servo artifact creature tokens." },

            new() { Name = "Emerge",
                Category = KeywordCategory.Triggered,
                Definition = "You may cast this spell by sacrificing a creature and paying the emerge cost reduced by that creature's mana value." },

            new() { Name = "Annihilator",
                Category = KeywordCategory.Triggered,
                Definition = "Whenever this creature attacks, defending player sacrifices N permanents." },

            new() { Name = "Soulshift",
                Category = KeywordCategory.Triggered,
                Definition = "When this creature dies, you may return target Spirit card with lesser mana value from your graveyard to your hand." },

            new() { Name = "Haunt",
                Category = KeywordCategory.Triggered,
                Definition = "When this card is put into a graveyard from play, exile it haunting target creature." },

            new() { Name = "Absorb",
                Category = KeywordCategory.Triggered,
                Definition = "If this creature would be dealt damage, prevent N of that damage." },

            // ── ACTIVATED ─────────────────────────────────────────────────────
            new() { Name = "Equip",
                Category = KeywordCategory.Activated,
                Definition = "Attach this equipment to target creature you control. Activate only as a sorcery." },

            new() { Name = "Fortify",
                Category = KeywordCategory.Activated,
                Definition = "Attach this fortification to target land you control. Activate only as a sorcery." },

            new() { Name = "Reconfigure",
                Category = KeywordCategory.Activated,
                Definition = "Attach or unattach this Equipment to target creature you control. Activate only as a sorcery." },

            new() { Name = "Cycling",
                Category = KeywordCategory.Activated,
                Definition = "Pay cost, discard this card: draw a card." },

            new() { Name = "Morph",
                Category = KeywordCategory.Activated,
                Definition = "You may cast this card face down as a 2/2 creature for 3 mana. Turn it face up any time by paying its morph cost." },

            new() { Name = "Megamorph",
                Category = KeywordCategory.Activated,
                Definition = "Like Morph, but when turned face up, a +1/+1 counter is placed on the creature." },

            new() { Name = "Manifest",
                Category = KeywordCategory.Activated,
                Definition = "Put a card from your library face down as a 2/2 creature. Turn it face up by paying its mana cost if it's a creature card." },

            new() { Name = "Disguise",
                Category = KeywordCategory.Activated,
                Definition = "You may cast this card face down as a 2/2 creature with ward 2 for 3 mana." },

            new() { Name = "Cloak",
                Category = KeywordCategory.Activated,
                Definition = "Put a card face down as a 2/2 creature with ward 2. Turn it face up by paying its mana cost if it's a creature card." },

            new() { Name = "Unearth",
                Category = KeywordCategory.Activated,
                Definition = "Pay cost: return this card from your graveyard to the battlefield. It gains haste. Exile it at the beginning of the next end step or if it would leave the battlefield." },

            new() { Name = "Forecast",
                Category = KeywordCategory.Activated,
                Definition = "Reveal this card from your hand: [effect]. Activate only during your upkeep and only once each turn." },

            new() { Name = "Crew",
                Category = KeywordCategory.Activated,
                Definition = "Tap any number of creatures you control with total power N or more: this Vehicle becomes an artifact creature until end of turn." },

            new() { Name = "Level Up",
                Category = KeywordCategory.Activated,
                Definition = "Pay cost: put a level counter on this creature. Level Up only as a sorcery." },

            new() { Name = "Outlast",
                Category = KeywordCategory.Activated,
                Definition = "Pay cost, tap: put a +1/+1 counter on this creature. Activate only as a sorcery." },

            new() { Name = "Monstrosity",
                Category = KeywordCategory.Activated,
                Definition = "If this creature isn't monstrous, put N +1/+1 counters on it and it becomes monstrous." },

            new() { Name = "Reinforce",
                Category = KeywordCategory.Activated,
                Definition = "Pay cost, discard this card: put N +1/+1 counters on target creature." },

            new() { Name = "Ninjutsu",
                Category = KeywordCategory.Activated,
                Definition = "Pay cost, return an unblocked attacker you control to hand: put this card onto the battlefield tapped and attacking." },

            new() { Name = "Transfigure",
                Category = KeywordCategory.Activated,
                Definition = "Pay cost, sacrifice this creature: search your library for a creature card with the same mana value, put it onto the battlefield, then shuffle." },

            new() { Name = "Transmute",
                Category = KeywordCategory.Activated,
                Definition = "Pay cost, discard this card: search your library for a card with the same mana value, reveal it, put it into your hand, then shuffle." },

            // ── STATIC ────────────────────────────────────────────────────────
            new() { Name = "Flash",
                Category = KeywordCategory.Static,
                Definition = "You may cast this spell any time you could cast an instant." },

            new() { Name = "Defender",
                Category = KeywordCategory.Static,
                Definition = "This creature can't attack." },

            new() { Name = "Changeling",
                Category = KeywordCategory.Static,
                Definition = "This card is every creature type." },

            new() { Name = "Convoke",
                Category = KeywordCategory.Static,
                Definition = "Your creatures can help cast this spell. Each creature you tap while casting this spell pays for 1 or one mana of that creature's color." },

            new() { Name = "Delve",
                Category = KeywordCategory.Static,
                Definition = "Each card you exile from your graveyard while casting this spell pays for 1." },

            new() { Name = "Affinity",
                Category = KeywordCategory.Static,
                Definition = "This spell costs 1 less for each artifact (or other specified permanent type) you control." },

            new() { Name = "Devoid",
                Category = KeywordCategory.Static,
                Definition = "This card has no color." },

            new() { Name = "Annex",
                Category = KeywordCategory.Static,
                Definition = "You control the enchanted land." },

            new() { Name = "Storm",
                Category = KeywordCategory.Static,
                Definition = "When you cast this spell, copy it for each spell cast before it this turn." },

            new() { Name = "Epic",
                Category = KeywordCategory.Static,
                Definition = "For the rest of the game, you can't cast spells. At the beginning of each of your upkeeps, copy this spell." },

            new() { Name = "Gravestorm",
                Category = KeywordCategory.Static,
                Definition = "When you cast this spell, copy it for each permanent put into a graveyard from the battlefield this turn." },

            new() { Name = "Cascade",
                Category = KeywordCategory.Static,
                Definition = "When you cast this spell, exile cards from the top of your library until you exile a nonland card with lesser mana value. You may cast it without paying its mana cost." },

            new() { Name = "Bestow",
                Category = KeywordCategory.Static,
                Definition = "If you cast this card for its bestow cost, it's an Aura spell with enchant creature. It becomes a creature again if it's not attached to a creature." },

            new() { Name = "Tribute",
                Category = KeywordCategory.Static,
                Definition = "As this creature enters the battlefield, an opponent may put N +1/+1 counters on it. If that opponent doesn't, [effect]." },

            new() { Name = "Overload",
                Category = KeywordCategory.Static,
                Definition = "You may cast this spell for its overload cost. If you do, change its text by replacing all instances of 'target' with 'each'." },

            new() { Name = "Cipher",
                Category = KeywordCategory.Static,
                Definition = "Then you may exile this spell card encoded on a creature you control. Whenever that creature deals combat damage to a player, its controller may cast a copy of this card without paying its mana cost." },

            new() { Name = "Populate",
                Category = KeywordCategory.Static,
                Definition = "Create a token that's a copy of a creature token you control." },

            new() { Name = "Bloodrush",
                Category = KeywordCategory.Static,
                Definition = "Discard this card: target attacking creature gets +N/+N until end of turn." },

            new() { Name = "Battalion",
                Category = KeywordCategory.Static,
                Definition = "Whenever this creature and at least two other creatures attack, [effect]." },

            // ── SPELL ─────────────────────────────────────────────────────────
            new() { Name = "Flashback",
                Category = KeywordCategory.Spell,
                Definition = "You may cast this card from your graveyard for its flashback cost. Then exile it." },

            new() { Name = "Jump-start",
                Category = KeywordCategory.Spell,
                Definition = "You may cast this card from your graveyard for its cost by discarding a card in addition to paying its other costs. Then exile it." },

            new() { Name = "Aftermath",
                Category = KeywordCategory.Spell,
                Definition = "Cast only from your graveyard. Then exile it." },

            new() { Name = "Retrace",
                Category = KeywordCategory.Spell,
                Definition = "You may cast this card from your graveyard by discarding a land card in addition to paying its other costs." },

            new() { Name = "Rebound",
                Category = KeywordCategory.Spell,
                Definition = "If you cast this spell from your hand, exile it as it resolves. At the beginning of your next upkeep, you may cast this card from exile without paying its mana cost." },

            new() { Name = "Buyback",
                Category = KeywordCategory.Spell,
                Definition = "You may pay an additional cost when casting this spell. If you do, put it into your hand instead of your graveyard as it resolves." },

            new() { Name = "Kicker",
                Category = KeywordCategory.Spell,
                Definition = "You may pay an additional kicker cost as you cast this spell. If you do, [additional effect]." },

            new() { Name = "Multikicker",
                Category = KeywordCategory.Spell,
                Definition = "You may pay an additional kicker cost any number of times as you cast this spell." },

            new() { Name = "Entwine",
                Category = KeywordCategory.Spell,
                Definition = "Choose both options instead of one if you pay the entwine cost." },

            new() { Name = "Splice",
                Category = KeywordCategory.Spell,
                Definition = "As you cast an Arcane spell, you may reveal this card and pay its splice cost. If you do, add this card's effects to that spell." },

            new() { Name = "Replicate",
                Category = KeywordCategory.Spell,
                Definition = "When you cast this spell, copy it for each time you paid its replicate cost." },

            new() { Name = "Haunt",
                Category = KeywordCategory.Spell,
                Definition = "When this spell card is put into a graveyard after resolving, exile it haunting target creature." },

            new() { Name = "Conspire",
                Category = KeywordCategory.Spell,
                Definition = "As you cast this spell, you may tap two untapped creatures you control that share a color with it. When you do, copy it and you may choose a new target for the copy." },

            new() { Name = "Fuse",
                Category = KeywordCategory.Spell,
                Definition = "You may cast one or both halves of this card from your hand." },

            new() { Name = "Emerge",
                Category = KeywordCategory.Spell,
                Definition = "You may cast this spell by sacrificing a creature and paying the emerge cost reduced by that creature's mana value." },

            new() { Name = "Escalate",
                Category = KeywordCategory.Spell,
                Definition = "Pay the escalate cost for each mode you choose beyond the first." },

            new() { Name = "Aftermath",
                Category = KeywordCategory.Spell,
                Definition = "Cast the aftermath half only from your graveyard. Then exile it." },

            new() { Name = "Addendum",
                Category = KeywordCategory.Spell,
                Definition = "If you cast this spell during your main phase, [additional effect]." },

            new() { Name = "Jumpstart",
                Category = KeywordCategory.Spell,
                Definition = "You may cast this card from your graveyard by discarding a card in addition to paying its other costs. Then exile it." },

            // ── COST / PAYMENT ────────────────────────────────────────────────
            new() { Name = "Casualty",
                Category = KeywordCategory.Cost,
                Definition = "As an additional cost to cast this spell, sacrifice a creature with power N or greater. When you do, copy this spell." },

            new() { Name = "Evoke",
                Category = KeywordCategory.Cost,
                Definition = "You may cast this spell for its evoke cost. If you do, it's sacrificed when it enters the battlefield." },

            new() { Name = "Suspend",
                Category = KeywordCategory.Cost,
                Definition = "Rather than cast this card, pay its suspend cost and exile it with N time counters. At the beginning of your upkeep, remove a counter. When the last is removed, cast it for free." },

            new() { Name = "Madness",
                Category = KeywordCategory.Cost,
                Definition = "If you discard this card, you may cast it for its madness cost instead of putting it into your graveyard." },

            new() { Name = "Delve",
                Category = KeywordCategory.Cost,
                Definition = "Each card you exile from your graveyard while casting this spell pays for 1." },

            new() { Name = "Escape",
                Category = KeywordCategory.Cost,
                Definition = "You may cast this card from your graveyard for its escape cost by also exiling the specified number of other cards from your graveyard." },

            new() { Name = "Foretell",
                Category = KeywordCategory.Cost,
                Definition = "During your turn, you may pay 2 and exile this card from your hand face down. Cast it on a later turn for its foretell cost." },

            new() { Name = "Blitz",
                Category = KeywordCategory.Cost,
                Definition = "If you cast this spell for its blitz cost, it gains haste and 'When this creature dies, draw a card.' Sacrifice it at the beginning of the next end step." },

            new() { Name = "Dash",
                Category = KeywordCategory.Cost,
                Definition = "You may cast this spell for its dash cost. If you do, it gains haste, and is returned to its owner's hand at the beginning of the next end step." },

            new() { Name = "Surge",
                Category = KeywordCategory.Cost,
                Definition = "You may cast this spell for its surge cost if you or a teammate has cast another spell this turn." },

            new() { Name = "Awaken",
                Category = KeywordCategory.Cost,
                Definition = "If you cast this spell for its awaken cost, put N +1/+1 counters on target land and it becomes a 0/0 Elemental creature in addition to its other types." },

            new() { Name = "Prowl",
                Category = KeywordCategory.Cost,
                Definition = "You may cast this for its prowl cost if a player was dealt combat damage this turn by a source of the appropriate creature type." },

            new() { Name = "Offering",
                Category = KeywordCategory.Cost,
                Definition = "You may cast this card any time you could cast an instant by sacrificing a creature of the appropriate type and paying the difference in mana costs." },

            new() { Name = "Convoke",
                Category = KeywordCategory.Cost,
                Definition = "Your creatures can help cast this spell. Each creature you tap while casting this spell pays for 1 or one mana of that creature's color." },

            new() { Name = "Improvise",
                Category = KeywordCategory.Cost,
                Definition = "Your artifacts can help cast this spell. Each artifact you tap after you're done activating mana abilities pays for 1." },

            new() { Name = "Assist",
                Category = KeywordCategory.Cost,
                Definition = "Another player can pay up to the generic mana of this spell's cost." },

            new() { Name = "Mutate",
                Category = KeywordCategory.Cost,
                Definition = "If you cast this spell for its mutate cost, put it over or under target non-Human creature. They mutate into the creature on top plus all abilities from under it." },

            new() { Name = "Overload",
                Category = KeywordCategory.Cost,
                Definition = "You may cast this spell for its overload cost. If you do, change its text by replacing all instances of 'target' with 'each'." },

            new() { Name = "Disturb",
                Category = KeywordCategory.Cost,
                Definition = "You may cast this card from your graveyard transformed for its disturb cost." },

            new() { Name = "Cleave",
                Category = KeywordCategory.Cost,
                Definition = "You may cast this spell for its cleave cost. If you do, remove the words in brackets from its text." },

            new() { Name = "Prototype",
                Category = KeywordCategory.Cost,
                Definition = "You may cast this spell with a different cost, color, and size. It keeps its abilities." },

            new() { Name = "Craft",
                Category = KeywordCategory.Cost,
                Definition = "Pay the craft cost and exile the specified cards to transform this permanent." },

            // ── REPLACEMENT ───────────────────────────────────────────────────
            new() { Name = "Undying",
                Category = KeywordCategory.Replacement,
                Definition = "When this creature dies, if it had no +1/+1 counters on it, return it to the battlefield under its owner's control with a +1/+1 counter on it." },

            new() { Name = "Persist",
                Category = KeywordCategory.Replacement,
                Definition = "When this creature dies, if it had no -1/-1 counters on it, return it to the battlefield under its owner's control with a -1/-1 counter on it." },

            new() { Name = "Regenerate",
                Category = KeywordCategory.Replacement,
                Definition = "The next time this creature would be destroyed this turn, it isn't. Instead, tap it, remove it from combat, and remove all damage from it." },

            new() { Name = "Totem Armor",
                Category = KeywordCategory.Replacement,
                Definition = "If enchanted creature would be destroyed, instead remove all damage from it and destroy this Aura." },

            new() { Name = "Dredge",
                Category = KeywordCategory.Replacement,
                Definition = "If you would draw a card, instead you may mill N cards and return this card from your graveyard to your hand." },

            new() { Name = "Scavenge",
                Category = KeywordCategory.Replacement,
                Definition = "Exile this card from your graveyard: put a number of +1/+1 counters equal to this card's power on target creature. Activate only as a sorcery." },

            new() { Name = "Embalm",
                Category = KeywordCategory.Replacement,
                Definition = "Pay cost, exile this card from your graveyard: create a token that's a copy of it, except it's a white Zombie. Activate only as a sorcery." },

            new() { Name = "Eternalize",
                Category = KeywordCategory.Replacement,
                Definition = "Pay cost, exile this card from your graveyard: create a 4/4 black Zombie token that's a copy of it. Activate only as a sorcery." },

            new() { Name = "Encore",
                Category = KeywordCategory.Replacement,
                Definition = "Pay cost, exile this card from your graveyard: for each opponent, create a token copy of this creature that attacks that opponent. Sacrifice them at the beginning of the next end step." },

            // ── FORMAT-SPECIFIC ───────────────────────────────────────────────
            new() { Name = "Partner",
                Category = KeywordCategory.FormatSpecific,
                Definition = "You may have two commanders if both have partner. (Commander format)" },

            new() { Name = "Partner with",
                Category = KeywordCategory.FormatSpecific,
                Definition = "When this creature enters the battlefield, target player may put the named partner card from their library into their hand. (Commander format)" },

            new() { Name = "Friends forever",
                Category = KeywordCategory.FormatSpecific,
                Definition = "You may have two commanders if both have Friends forever. (Commander format)" },

            new() { Name = "Background",
                Category = KeywordCategory.FormatSpecific,
                Definition = "You can have a Background as a second commander. (Commander format)" },

            new() { Name = "Doctor's companion",
                Category = KeywordCategory.FormatSpecific,
                Definition = "You can have a legendary creature with Doctor's companion as a second commander. (Commander format)" },

            new() { Name = "Eminence",
                Category = KeywordCategory.FormatSpecific,
                Definition = "This ability is active even if this commander is in the command zone. (Commander format)" },

            new() { Name = "Myriad",
                Category = KeywordCategory.FormatSpecific,
                Definition = "Whenever this creature attacks, for each opponent other than the defending player, create a tapped and attacking token. Exile those tokens at the beginning of the next end step." },

            new() { Name = "Monarch",
                Category = KeywordCategory.FormatSpecific,
                Definition = "The player who is the Monarch draws an extra card at the beginning of their end step. If a creature deals combat damage to the Monarch, that player becomes the Monarch." },

            new() { Name = "The Initiative",
                Category = KeywordCategory.FormatSpecific,
                Definition = "The player who has the Initiative ventures into Undercity at the beginning of their upkeep." },

            new() { Name = "Dungeon",
                Category = KeywordCategory.FormatSpecific,
                Definition = "A card type representing a location. You can venture into the dungeon to advance through its rooms." },

            // ── OTHER ─────────────────────────────────────────────────────────
            new() { Name = "Transform",
                Category = KeywordCategory.Other,
                Definition = "This card has two faces and can flip to its other side based on game conditions." },

            new() { Name = "Meld",
                Category = KeywordCategory.Other,
                Definition = "Two specific cards can be melded into one powerful card under certain conditions." },

            new() { Name = "Saga",
                Category = KeywordCategory.Other,
                Definition = "An enchantment that adds lore counters each upkeep, triggering chapter abilities, and is sacrificed when the final chapter is reached." },

            new() { Name = "Class",
                Category = KeywordCategory.Other,
                Definition = "An enchantment that levels up by paying its level cost. Each level grants additional abilities." },

            new() { Name = "Venturesome",
                Category = KeywordCategory.Other,
                Definition = "You may venture into the dungeon." },

            new() { Name = "Daybound",
                Category = KeywordCategory.Other,
                Definition = "If a player casts no spells during their own turn, it becomes night. This permanent transforms if it becomes night." },

            new() { Name = "Nightbound",
                Category = KeywordCategory.Other,
                Definition = "If a player casts two or more spells during their own turn, it becomes day. This permanent transforms if it becomes day." },

            new() { Name = "Disturb",
                Category = KeywordCategory.Other,
                Definition = "You may cast this card from your graveyard transformed for its disturb cost." },

            new() { Name = "Decayed",
                Category = KeywordCategory.Other,
                Definition = "This creature can't block. When it attacks, sacrifice it at end of combat." },

            new() { Name = "Toxic",
                Category = KeywordCategory.Other,
                Definition = "Whenever this creature deals combat damage to a player, that player gets N poison counters." },

            new() { Name = "The Ring Tempts You",
                Category = KeywordCategory.Other,
                Definition = "Each time you are tempted, the Ring gains its next ability and you choose a creature to be your Ring-bearer." },

            new() { Name = "Saddle",
                Category = KeywordCategory.Other,
                Definition = "Tap any number of creatures you control with total power N or more: this Mount becomes saddled until end of turn." },

            new() { Name = "Gift",
                Category = KeywordCategory.Other,
                Definition = "You may promise a gift to an opponent. If you do, they get a bonus and you get an additional effect." },

            new() { Name = "Offspring",
                Category = KeywordCategory.Other,
                Definition = "You may pay an additional cost when casting this creature. If you do, create a 1/1 token copy of it." },

            new() { Name = "Impending",
                Category = KeywordCategory.Other,
                Definition = "This isn't a creature until its time counters are removed. At the beginning of your upkeep, remove a time counter." },
        };

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>All keywords, deduplicated by name.</summary>
        private static List<MtgKeyword> _merged = new();
        private static bool _initialized = false;

        /// <summary>All keywords — loaded from cache file if available, otherwise hardcoded list only.</summary>
        public static IReadOnlyList<MtgKeyword> All
        {
            get
            {
                if (!_initialized) LoadFromCache();
                return _merged.AsReadOnly();
            }
        }

        /// <summary>
        /// Loads the keyword dictionary from the JSON cache file.
        /// Falls back to the hardcoded list if no cache exists.
        /// This is FAST — no database queries.
        /// </summary>
        private static void LoadFromCache()
        {
            try
            {
                string path = AppFolderService.KeywordCachePath;
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var cached = JsonSerializer.Deserialize<List<MtgKeyword>>(json);
                    if (cached != null && cached.Count > 0)
                    {
                        _merged = cached
                            .OrderBy(k => k.CategoryName)
                            .ThenBy(k => k.Name)
                            .ToList();
                        _initialized = true;
                        return;
                    }
                }
            }
            catch { /* Cache corrupt or missing — fall through */ }

            // No cache — just use hardcoded list (instant)
            _merged = _all.OrderBy(k => k.CategoryName)
                .ThenBy(k => k.Name).ToList();
            _initialized = true;
        }

        /// <summary>
        /// Persists the current keyword dictionary to a JSON file so subsequent
        /// loads are instant.  Call after RefreshFromPool + MergeScryfallCatalogs.
        /// </summary>
        public static void SaveToCache()
        {
            try
            {
                var json = JsonSerializer.Serialize(_merged,
                    new JsonSerializerOptions { WriteIndented = false });
                File.WriteAllText(AppFolderService.KeywordCachePath, json);
            }
            catch { /* Non-fatal */ }
        }

        /// <summary>
        /// Scans the pool database for keywords not in the built-in list and adds
        /// them with a placeholder definition. Safe to call multiple times.
        /// </summary>
        public static void RefreshFromPool()
        {
            var known = new Dictionary<string, MtgKeyword>(
                System.StringComparer.OrdinalIgnoreCase);
            foreach (var kw in _all)
                known[kw.Name] = kw;

            try
            {
                using var db = new Data.AppDbContext();
                var poolKeywords = db.PoolCards.AsNoTracking()
                    .Where(c => c.Keywords != null && c.Keywords != "")
                    .Select(c => c.Keywords)
                    .ToList();

                foreach (var kwList in poolKeywords)
                {
                    foreach (var kw in kwList.Split('|',
                        System.StringSplitOptions.RemoveEmptyEntries |
                        System.StringSplitOptions.TrimEntries))
                    {
                        if (!known.ContainsKey(kw))
                        {
                            known[kw] = new MtgKeyword
                            {
                                Name = kw,
                                Category = KeywordCategory.Discovered,
                                Definition = "No definition available."
                            };
                        }
                    }
                }

                // For discovered keywords with no definition, extract reminder
                // text from oracle text of a card that has the keyword.
                // Only done on EXPLICIT calls (keyword field match, fast).
                // The heavier oracle-text-wide extraction is in
                // ExtractReminderTextsFromPool() and only runs during
                // database update on a background thread.
                var needDefs = known.Values
                    .Where(k => k.Definition == "No definition available.")
                    .Select(k => k.Name).ToList();

                if (needDefs.Count > 0)
                {
                    foreach (var kwName in needDefs)
                    {
                        var card = db.PoolCards.AsNoTracking()
                            .FirstOrDefault(c =>
                                c.Keywords != null && c.Keywords.Contains(kwName) &&
                                c.OracleText != null && c.OracleText.Contains("("));
                        if (card == null) continue;

                        var text = card.OracleText ?? "";
                        string? reminder = ExtractReminder(text, kwName);
                        if (!string.IsNullOrEmpty(reminder))
                            known[kwName].Definition = reminder;
                    }
                }
            }
            catch { /* Pool may not exist yet */ }

            _merged = known.Values
                .OrderBy(k => k.CategoryName)
                .ThenBy(k => k.Name)
                .ToList();
            _initialized = true;
        }

        /// <summary>
        /// Merges keyword names fetched from Scryfall's catalog endpoints into
        /// the dictionary.  Any keyword not already present is added as
        /// <see cref="KeywordCategory.Discovered"/> with a placeholder definition
        /// that will be replaced by reminder-text extraction on the next
        /// <see cref="RefreshFromPool"/> call.  Returns the count of newly added
        /// keywords.
        /// </summary>
        public static int MergeScryfallCatalogs(
            IEnumerable<string>? keywordAbilities,
            IEnumerable<string>? keywordActions,
            IEnumerable<string>? abilityWords)
        {
            // Ensure base data is loaded first
            if (!_initialized) RefreshFromPool();

            var known = new Dictionary<string, MtgKeyword>(
                System.StringComparer.OrdinalIgnoreCase);
            foreach (var kw in _merged)
                known[kw.Name] = kw;

            int added = 0;

            void Merge(IEnumerable<string>? source)
            {
                if (source == null) return;
                foreach (var name in source)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (known.ContainsKey(name)) continue;

                    known[name] = new MtgKeyword
                    {
                        Name = name,
                        Category = KeywordCategory.Discovered,
                        Definition = "No definition available."
                    };
                    added++;
                }
            }

            Merge(keywordAbilities);
            Merge(keywordActions);
            Merge(abilityWords);

            if (added > 0)
            {
                _merged = known.Values
                    .OrderBy(k => k.CategoryName)
                    .ThenBy(k => k.Name)
                    .ToList();
            }

            return added;
        }

        /// <summary>
        /// Forces the keyword dictionary to re-scan the pool on the next access
        /// to <see cref="All"/>.
        /// </summary>
        public static void Reset()
        {
            _initialized = false;
            _merged = new();
        }

        /// <summary>
        /// Heavy oracle-text extraction — searches the entire pool for reminder
        /// text for keywords that still have no definition.  This does many
        /// individual DB queries and should ONLY be called on a background thread
        /// (e.g. during database update via Task.Run).
        /// </summary>
        public static int ExtractReminderTextsFromPool()
        {
            if (!_initialized) RefreshFromPool();

            var known = new Dictionary<string, MtgKeyword>(
                System.StringComparer.OrdinalIgnoreCase);
            foreach (var kw in _merged)
                known[kw.Name] = kw;

            var needDefs = known.Values
                .Where(k => k.Definition == "No definition available.")
                .Select(k => k.Name).ToList();

            if (needDefs.Count == 0) return 0;

            int found = 0;
            try
            {
                using var db = new Data.AppDbContext();
                foreach (var kwName in needDefs)
                {
                    // Broad search: any card mentioning the keyword in oracle text
                    var card = db.PoolCards.AsNoTracking()
                        .FirstOrDefault(c =>
                            c.OracleText != null &&
                            c.OracleText.Contains(kwName) &&
                            c.OracleText.Contains("("));

                    if (card == null) continue;

                    var text = card.OracleText ?? "";
                    string? reminder = ExtractReminder(text, kwName);
                    if (!string.IsNullOrEmpty(reminder))
                    {
                        known[kwName].Definition = reminder;
                        found++;
                    }
                }
            }
            catch { }

            if (found > 0)
            {
                _merged = known.Values
                    .OrderBy(k => k.CategoryName)
                    .ThenBy(k => k.Name)
                    .ToList();
            }
            return found;
        }

        /// <summary>
        /// Extracts reminder text for a keyword from a card's oracle text.
        /// Tries three strategies: same-line, next-line, and any parenthesized
        /// mention containing the keyword.
        /// </summary>
        private static string? ExtractReminder(string oracleText, string kwName)
        {
            // Strategy A: parenthesized text on the same line as the keyword
            foreach (var line in oracleText.Split('\n'))
            {
                if (!line.Contains(kwName, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                int open = line.IndexOf('(');
                int close = line.LastIndexOf(')');
                if (open >= 0 && close > open)
                    return line.Substring(open + 1, close - open - 1).Trim();
            }

            // Strategy B: keyword on one line, reminder on the next
            var lines = oracleText.Split('\n');
            for (int i = 0; i < lines.Length - 1; i++)
            {
                if (!lines[i].Contains(kwName,
                    System.StringComparison.OrdinalIgnoreCase))
                    continue;
                var next = lines[i + 1].Trim();
                if (next.StartsWith('(') && next.EndsWith(')'))
                    return next.Substring(1, next.Length - 2).Trim();
            }

            // Strategy C: any parenthesized text mentioning the keyword
            int search = 0;
            while (search < oracleText.Length)
            {
                int open = oracleText.IndexOf('(', search);
                if (open < 0) break;
                int close = oracleText.IndexOf(')', open);
                if (close < 0) break;
                var inner = oracleText.Substring(open + 1, close - open - 1);
                if (inner.Contains(kwName,
                    System.StringComparison.OrdinalIgnoreCase))
                    return inner.Trim();
                search = close + 1;
            }

            return null;
        }

        public static IReadOnlyList<MtgKeyword> BuiltIn =
            _all.GroupBy(k => k.Name, System.StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(k => k.CategoryName)
                .ThenBy(k => k.Name)
                .ToList()
                .AsReadOnly();

        /// <summary>All keywords grouped by category for UI display.</summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<MtgKeyword>> ByCategory =>
            All.GroupBy(k => k.CategoryName)
               .OrderBy(g => g.Key)
               .ToDictionary(
                   g => g.Key,
                   g => (IReadOnlyList<MtgKeyword>)g.OrderBy(k => k.Name).ToList())
               as IReadOnlyDictionary<string, IReadOnlyList<MtgKeyword>>;

        /// <summary>Look up a keyword definition by name (case-insensitive).</summary>
        public static MtgKeyword? Find(string name) =>
            All.FirstOrDefault(k =>
                k.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Returns all keywords whose names appear in the card's stored Keywords string.
        /// Matches both Scryfall keywords and oracle text mentions.
        /// </summary>
        public static IEnumerable<string> GetCardKeywords(
            string storedKeywords, string oracleText)
        {
            var found = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            // Primary: Scryfall keywords array (pipe-separated)
            if (!string.IsNullOrEmpty(storedKeywords))
                foreach (var kw in storedKeywords.Split('|',
                    System.StringSplitOptions.RemoveEmptyEntries))
                    found.Add(kw.Trim());

            // Fallback: scan oracle text for known keywords
            if (!string.IsNullOrEmpty(oracleText))
                foreach (var kw in All)
                    if (oracleText.Contains(kw.Name,
                        System.StringComparison.OrdinalIgnoreCase))
                        found.Add(kw.Name);

            return found.OrderBy(k => k);
        }

        /// <summary>
        /// Returns all unique keyword names found across the entire card pool,
        /// sorted alphabetically. Used to build the filter checklist.
        /// </summary>
        public static IReadOnlyList<string> GetAllPoolKeywords(
            System.Collections.Generic.IEnumerable<BreakersOfE.Models.PoolCard> pool)
        {
            var found = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var card in pool)
                foreach (var kw in card.KeywordList)
                    if (!string.IsNullOrWhiteSpace(kw))
                        found.Add(kw.Trim());
            return found.OrderBy(k => k).ToList().AsReadOnly();
        }

        /// <summary>
        /// Search the keyword dictionary by partial name or definition text.
        /// </summary>
        public static IEnumerable<MtgKeyword> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return All;
            return All.Where(k =>
                k.Name.Contains(query, System.StringComparison.OrdinalIgnoreCase) ||
                k.Definition.Contains(query, System.StringComparison.OrdinalIgnoreCase));
        }
    }
}
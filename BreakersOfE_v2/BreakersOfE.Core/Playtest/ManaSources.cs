using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BreakersOfE.Playtest
{
    /// <summary>One way to tap a card for mana: "G", "CC", "WB"; what it costs besides tapping; and what else happens.</summary>
    public sealed record ManaOption(string Mana, string Cost = "", bool Sacrifice = false, int Damage = 0)
    {
        /// <summary>"Add {U}{B} · pay {1} · 1 damage · sacrifice".</summary>
        public string Label =>
            "Add " + ManaSources.Symbols(Mana) +
            (Cost.Length > 0 ? " · pay " + Cost : "") +
            (Damage > 0 ? $" · {Damage} damage" : "") +
            (Sacrifice ? " · sacrifice" : "");
    }

    /// <summary>What else comes with tapping a card for mana: Mana Reflection (×2), Mana Flare, Wild Growth, Utopia Sprawl …</summary>
    public sealed class ManaExtra
    {
        /// <summary>Mana Reflection ×2, Nyxbloom Ancient ×3 (multiplied together).</summary>
        public int Multiplier { get; set; } = 1;
        /// <summary>Mana Flare, Heartbeat of Spring, Zendikar Resurgent: another of what a land made.</summary>
        public int Copies { get; set; }
        /// <summary>Wild Growth's {G}, Overgrowth's {G}{G}: as symbols ("GG").</summary>
        public string Fixed { get; set; } = "";
        /// <summary>Utopia Sprawl, Fertile Ground: one more of a color to choose, this many times.</summary>
        public int AnyColor { get; set; }
        public List<string> Because { get; } = new();
    }

    /// <summary>
    /// Mana, read from the cards: what a card can tap for (its rules text,
    /// checked against Scryfall's produced_mana), what changes that (Blood
    /// Moon, Urborg, Leyline of the Guildpact, Chromatic Lantern, Spreading
    /// Seas, Contamination), what adds to it (Mana Reflection, Wild Growth),
    /// and what keeps unspent mana from emptying (Omnath, Upwelling, Kruphix).
    /// Assistance only: paying for spells stays the player's job.
    /// </summary>
    public static class ManaSources
    {
        /// <summary>The kinds of mana in a pool, in the order they're shown.</summary>
        public static readonly string[] Colors = { "W", "U", "B", "R", "G", "C" };

        public static string ColorName(string m) => m switch
        {
            "W" => "white", "U" => "blue", "B" => "black", "R" => "red", "G" => "green", _ => "colorless",
        };

        /// <summary>"{G}{G}{W}" for "GGW".</summary>
        public static string Symbols(string mana) => string.Concat(mana.Select(ch => "{" + ch + "}"));

        // ── Scryfall's produced_mana, by ScryfallId (from the card pool) ──
        private static readonly Dictionary<string, string> _produced = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Remember what printings make ("BCU" for Talisman of Dominance), from the card pool.</summary>
        public static void Register(IEnumerable<KeyValuePair<string, string>> produced)
        {
            foreach (var (id, m) in produced)
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(m)) _produced[id] = m.ToUpperInvariant();
        }

        /// <summary>What Scryfall says the card makes ("" = not known: the card pool hasn't been updated since).</summary>
        public static string ProducedBy(GameCard c) => _produced.GetValueOrDefault(c.ScryfallId ?? "", "");

        private static readonly Regex Sym = new(@"\{([WUBRGC])\}", RegexOptions.Compiled);
        private static readonly Regex CostSym = new(@"\{(?!T\}|Q\})([^}]+)\}", RegexOptions.Compiled);
        private static readonly Regex Pieces = new(@",\s*(?:or\s+)?|\s+or\s+", RegexOptions.Compiled);
        private static readonly string[] Numbers = { "zero", "one", "two", "three", "four", "five", "six", "seven" };

        private static readonly (string Type, string Mana)[] BasicTypes =
            { ("Plains", "W"), ("Island", "U"), ("Swamp", "B"), ("Mountain", "R"), ("Forest", "G"), ("Wastes", "C") };

        private static string Flat(string text) => text.Replace("\r", " ").Replace("\n", " ");

        /// <summary>The cards on the battlefield whose effects reach this seat's cards: its own, and anyone's that don't say "you control".</summary>
        private static IEnumerable<(GameCard Card, bool Mine)> Around(Seat owner, IEnumerable<Seat> seats) =>
            seats.SelectMany(s => s.Battlefield.Where(c => !c.FaceDown && c.MergedInto == 0).Select(c => (c, s == owner)));

        /// <summary>
        /// The ways a card can be tapped for mana now: its own "{T}: Add …"
        /// abilities (a Talisman's "{T}: Add {U} or {B}" with its damage, a
        /// Signet's "{1}, {T}", a Treasure's sacrifice), and for a land what
        /// its land types give, after what's on the battlefield changes it.
        /// Empty: it doesn't make mana (as far as the table can tell).
        /// </summary>
        public static List<ManaOption> Of(GameCard c, Seat owner, IEnumerable<Seat> seats)
        {
            var result = new List<ManaOption>();
            if (c.FaceDown) return result;
            var around = Around(owner, seats).ToList();
            var auras = owner.Battlefield.Where(o => o.AttachedTo == c.Id).ToList();
            var lines = new List<string>(c.ShownText.Replace("\r", "").Split('\n'));
            string? becomes = null;                                    // Blood Moon, Celestial Dawn, Spreading Seas: just this type
            var alsoTypes = new List<string>();                        // Urborg, Yavimaya, Leyline of the Guildpact
            string? produces = null;                                   // Contamination: all of it this color instead

            if (c.IsLandNow)
            {
                // Blood Moon, Celestial Dawn: from cards that aren't lands (a land doing it could itself be changed).
                var blankets = new List<(string Who, string Type, bool Mine)>();
                foreach (var (o, mine) in around.Where(a => !a.Card.IsLandNow))
                    foreach (Match m in Regex.Matches(Flat(o.ShownText), @"\b(Nonbasic lands|Lands you control|Each land|All lands)\s+(?:are|is)\s+(?:an?\s+)?(Plains|Island|Swamp|Mountain|Forest)s?\s*\.", RegexOptions.IgnoreCase))
                        blankets.Add((m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value, mine));
                // Is this land just a basic type now (and without its own abilities)?
                string? BecomesOf(GameCard land, bool mineLand)
                {
                    string? to = null;
                    foreach (var b in blankets)
                    {
                        if (b.Who.StartsWith("lands you control") && !(b.Mine && mineLand)) continue;
                        if (b.Who.StartsWith("nonbasic") && land.IsBasicLand) continue;
                        to = b.Type;
                    }
                    return to;
                }
                becomes = BecomesOf(c, true);
                foreach (var (o, mine) in around)
                {
                    if (o.IsLandNow && BecomesOf(o, mine) != null) continue;      // Urborg under Blood Moon: its ability is gone
                    string t = Flat(o.ShownText);
                    // Another basic type as well.
                    foreach (Match m in Regex.Matches(t, @"\b(Lands you control|Each land|All lands)\s+(?:are|is)\s+(?:an?\s+)?(every basic land type|Plains|Island|Swamp|Mountain|Forest)s?\s+in addition", RegexOptions.IgnoreCase))
                    {
                        if (m.Groups[1].Value.StartsWith("Lands you control", StringComparison.OrdinalIgnoreCase) && !mine) continue;
                        if (m.Groups[2].Value.StartsWith("every", StringComparison.OrdinalIgnoreCase)) alsoTypes.AddRange(BasicTypes.Take(5).Select(b => b.Type));
                        else alsoTypes.Add(m.Groups[2].Value);
                    }
                    // An ability given to lands (Chromatic Lantern).
                    foreach (Match m in Regex.Matches(t, @"\bLands you control have\s+[""“]([^""”]+)[""”]", RegexOptions.IgnoreCase))
                        if (mine) lines.Add(m.Groups[1].Value);
                    // Every land makes one color instead (Contamination, Infernal Darkness).
                    var instead = Regex.Match(t, @"tapped for mana, it produces \{([WUBRGC])\} instead", RegexOptions.IgnoreCase);
                    if (instead.Success) produces = instead.Groups[1].Value;
                }
                foreach (var a in auras)
                {
                    string t = Flat(a.ShownText);
                    var m = Regex.Match(t, @"Enchanted land is (?:an?\s+)?(Plains|Island|Swamp|Mountain|Forest)\s*\.", RegexOptions.IgnoreCase);
                    if (m.Success) becomes = m.Groups[1].Value;
                    foreach (Match g in Regex.Matches(t, @"Enchanted land is (?:an?\s+)?(Plains|Island|Swamp|Mountain|Forest) in addition", RegexOptions.IgnoreCase))
                        alsoTypes.Add(g.Groups[1].Value);
                }
            }
            foreach (var a in auras)                                   // "Enchanted land has "{T}: Add …"" (and creatures, artifacts)
                foreach (Match m in Regex.Matches(Flat(a.ShownText), @"\bEnchanted \w+ has\s+[""“]([^""”]+)[""”]", RegexOptions.IgnoreCase))
                    lines.Add(m.Groups[1].Value);

            if (becomes != null)
            {
                // It's just that basic type now: only that type's mana (plus anything given above).
                var given = lines.Skip(c.ShownText.Replace("\r", "").Split('\n').Length).ToList();
                lines = given;
                Add(result, new ManaOption(BasicTypes.First(b => b.Type.Equals(becomes, StringComparison.OrdinalIgnoreCase)).Mana));
            }

            var identity = CommanderColors(owner);
            foreach (var raw in lines) ReadLine(raw, result, identity);

            // Scryfall's produced_mana says which colors it really makes (when the pool has it).
            string produced = ProducedBy(c);
            if (produced.Length > 0 && becomes == null && auras.Count == 0 && result.Count > 0)
            {
                var kept = result.Where(o => o.Mana.All(ch => produced.Contains(ch))).ToList();
                if (kept.Count > 0) result = kept;
            }
            // Its land types: a basic land, a dual like Tropical Island, and types given by Urborg and the like.
            if (c.IsLandNow)
            {
                string type = becomes ?? c.FaceType(c.BackUp ? 1 : 0);
                foreach (var (t, m) in BasicTypes)
                    if ((becomes == null && Regex.IsMatch(type, $@"\b{t}\b", RegexOptions.IgnoreCase)) || alsoTypes.Contains(t, StringComparer.OrdinalIgnoreCase))
                        Add(result, new ManaOption(m));
            }
            // Nothing read from the text, but Scryfall knows: each color it makes.
            if (result.Count == 0 && produced.Length > 0 && becomes == null)
                foreach (char ch in produced) Add(result, new ManaOption(ch.ToString()));

            // City of Brass: "Whenever City of Brass becomes tapped, it deals 1 damage to you."
            var hurts = Regex.Match(Flat(c.ShownText), @"becomes tapped, it deals (\d+) damage to you", RegexOptions.IgnoreCase);
            if (hurts.Success && becomes == null)
                result = result.Select(o => o with { Damage = o.Damage + int.Parse(hurts.Groups[1].Value) }).ToList();

            if (produces != null)
                result = result.Select(o => o with { Mana = new string(produces[0], o.Mana.Length) }).Distinct().ToList();
            return result;
        }

        private static void Add(List<ManaOption> list, ManaOption o)
        {
            if (o.Mana.Length > 0 && !list.Contains(o)) list.Add(o);
        }

        /// <summary>The colors of the owner's commanders (Command Tower, Arcane Signet): "" when there's no commander.</summary>
        private static string CommanderColors(Seat owner) =>
            string.Concat(Seat.AllZones.SelectMany(z => owner.ZoneList(z)).Where(x => x.IsCommander)
                              .SelectMany(x => x.Card.ColorIdentity.ToUpperInvariant()).Where(ch => "WUBRG".Contains(ch)).Distinct());

        /// <summary>Read one "{T}: Add …" ability into its choices.</summary>
        private static void ReadLine(string raw, List<ManaOption> result, string identity)
        {
            string line = raw.Replace("(", "").Replace(")", "").Trim();
            int colon = line.IndexOf(':');
            if (colon < 0) return;
            string cost = line[..colon];
            if (!cost.Contains("{T}")) return;
            string effect = line[(colon + 1)..];
            int add = effect.IndexOf("Add ", StringComparison.OrdinalIgnoreCase);
            if (add < 0) return;
            string manaCost = string.Concat(CostSym.Matches(cost).Select(m => m.Value));
            bool sacrifice = cost.Contains("Sacrifice", StringComparison.OrdinalIgnoreCase);
            int damage = 0;
            var life = Regex.Match(cost, @"Pay (\d+) life", RegexOptions.IgnoreCase);
            if (life.Success) damage += int.Parse(life.Groups[1].Value);
            var dealt = Regex.Match(effect, @"deals (\d+) damage to you", RegexOptions.IgnoreCase);
            if (dealt.Success) damage += int.Parse(dealt.Groups[1].Value);

            string what = effect[(add + 4)..];
            int stop = what.IndexOf('.');
            if (stop >= 0) what = what[..stop];
            var any = Regex.Match(what, @"^\s*(\w+) mana (?:of any (?:one )?color|in any combination of colors|of any type)", RegexOptions.IgnoreCase);
            if (any.Success)
            {
                int n = Math.Max(1, Array.IndexOf(Numbers, any.Groups[1].Value.ToLowerInvariant()));
                string colors = what.Contains("commander", StringComparison.OrdinalIgnoreCase) && identity.Length > 0 ? identity : "WUBRG";
                foreach (char ch in colors)
                    Add(result, new ManaOption(new string(ch, n), manaCost, sacrifice, damage));
                return;
            }
            foreach (var piece in Pieces.Split(what))
                Add(result, new ManaOption(string.Concat(Sym.Matches(piece).Select(x => x.Groups[1].Value)), manaCost, sacrifice, damage));
        }

        /// <summary>What comes on top of tapping this card for mana, from the battlefield and its Auras.</summary>
        public static ManaExtra ExtraFor(GameCard c, Seat owner, IEnumerable<Seat> seats)
        {
            var x = new ManaExtra();
            foreach (var (o, mine) in Around(owner, seats))
            {
                string t = Flat(o.ShownText);
                bool hit = false;
                if (mine)
                {
                    if (Regex.IsMatch(t, @"tap a permanent for mana, it produces twice as much", RegexOptions.IgnoreCase)) { x.Multiplier *= 2; hit = true; }
                    if (Regex.IsMatch(t, @"tap a permanent for mana, it produces three times as much", RegexOptions.IgnoreCase)) { x.Multiplier *= 3; hit = true; }
                }
                if (c.IsLandNow)
                {
                    var flare = Regex.Match(t, @"Whenever (a player|you) taps? an? (land|nonbasic land|Forest|Plains|Island|Swamp|Mountain) for mana, (?:that player |you )?adds? (?:an additional |one )?(?:additional )?(?:one )?mana of any type that land produced", RegexOptions.IgnoreCase);
                    if (flare.Success && (mine || flare.Groups[1].Value.Equals("a player", StringComparison.OrdinalIgnoreCase)))
                    {
                        string kind = flare.Groups[2].Value;
                        bool fits = kind.Equals("land", StringComparison.OrdinalIgnoreCase) ||
                                    (kind.StartsWith("nonbasic", StringComparison.OrdinalIgnoreCase) ? !c.IsBasicLand : c.FaceType(c.BackUp ? 1 : 0).Contains(kind, StringComparison.OrdinalIgnoreCase));
                        if (fits) { x.Copies++; hit = true; }
                    }
                }
                if (hit && !x.Because.Contains(o.ShownName)) x.Because.Add(o.ShownName);
            }
            foreach (var a in owner.Battlefield.Where(o => o.AttachedTo == c.Id))
            {
                string t = Flat(a.ShownText);
                var m = Regex.Match(t, @"Whenever enchanted \w+ is tapped for mana, its controller adds an additional (.+?)(?:\.|$)", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                string more = m.Groups[1].Value;
                string fixedMana = string.Concat(Sym.Matches(more).Select(g => g.Groups[1].Value));
                if (fixedMana.Length > 0) x.Fixed += fixedMana;
                else if (more.Contains("any type that land produced", StringComparison.OrdinalIgnoreCase)) x.Copies++;
                else if (more.Contains("mana", StringComparison.OrdinalIgnoreCase)) x.AnyColor++;
                x.Because.Add(a.ShownName);
            }
            return x;
        }

        /// <summary>All the mana one card makes with this choice: the choice (×Mana Reflection), copies (Mana Flare), and Wild Growth's extra.</summary>
        public static string Total(ManaOption o, ManaExtra x, string chosen = "") =>
            string.Concat(Enumerable.Repeat(o.Mana, Math.Max(1, x.Multiplier))) +
            string.Concat(Enumerable.Repeat(o.Mana, x.Copies)) + x.Fixed + chosen;

        // ── Keeping unspent mana ─────────────────────────────────────────
        /// <summary>How a seat's pool behaves as steps end, from what's on the battlefield.</summary>
        public sealed class Keeping
        {
            /// <summary>Colors that don't empty (Omnath: "G"; Upwelling: all six).</summary>
            public HashSet<string> Keep { get; } = new();
            /// <summary>Mana that would empty becomes colorless instead (Kruphix, Horizon Stone).</summary>
            public bool ToColorless { get; set; }
            /// <summary>The cards doing it, for the log.</summary>
            public List<string> Because { get; } = new();
        }

        /// <summary>
        /// What keeps <paramref name="owner"/>'s mana: their own permanents
        /// ("you don't lose", "your mana pool") and anyone's that say
        /// "players" or "each player" (Upwelling).
        /// </summary>
        public static Keeping KeepingFor(Seat owner, IEnumerable<Seat> seats)
        {
            var k = new Keeping();
            foreach (var (c, mine) in Around(owner, seats))
            {
                string text = Flat(c.ShownText);
                bool everyone = Regex.IsMatch(text, @"\b(Players|Each player)\b[^.]*\b(lose|empty)", RegexOptions.IgnoreCase);
                if (!mine && !everyone) continue;
                bool hit = false;
                if (Regex.IsMatch(text, @"would lose unspent mana, that mana becomes colorless", RegexOptions.IgnoreCase))
                {
                    k.ToColorless = true;
                    k.Keep.Add("C");
                    hit = true;
                }
                foreach (Match m in Regex.Matches(text,
                             @"(?:(white|blue|black|red|green|colorless)\s+)?mana\s+(?:doesn't|does not|don't|do not)\s+empty|(?:don't|do not|doesn't|does not)\s+lose\s+(?:unspent|this)\s+(?:(white|blue|black|red|green|colorless)\s+)?mana",
                             RegexOptions.IgnoreCase))
                {
                    hit = true;
                    string color = (m.Groups[1].Value + m.Groups[2].Value).ToLowerInvariant();
                    if (color.Length == 0) foreach (var all in Colors) k.Keep.Add(all);
                    else k.Keep.Add(Colors.First(x => ColorName(x) == color));
                }
                if (hit && !k.Because.Contains(c.ShownName)) k.Because.Add(c.ShownName);
            }
            return k;
        }
    }
}

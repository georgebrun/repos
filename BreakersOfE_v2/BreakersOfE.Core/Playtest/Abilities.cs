using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BreakersOfE.Playtest
{
    /// <summary>What an ability's effect does, as far as the table can read it.</summary>
    public enum EffectKind
    {
        Draw, GainLife, LoseLife, OpponentsLoseLife, Counters, Pump, GainKeywords, Mill, Scry, Surveil, LookTop, Search,
        Token, Damage, Tap, Untap, ToHand, Sacrifice, Exile, Destroy, Transform, Proliferate, Shuffle, Discard, Mana, Attach,
        Regenerate, ExileTop, Coin, Die, AtNextEndStep, Energy, DiscardHand, Monstrosity, Adapt, SetCounter,
        /// <summary>"Destroy all creatures", "Exile all artifacts and enchantments", "Return all … to their owners' hands": <see cref="Effect.What"/> is which.</summary>
        DestroyAll, ExileAll, BounceAll,
        /// <summary>"Untap all creatures you control" (<see cref="Effect.What"/>: which).</summary>
        UntapAll,
        /// <summary>"Bolster N": counters on your creature with the least toughness.</summary>
        Bolster,
        /// <summary>"Manifest the top card of your library": face down onto the battlefield.</summary>
        Manifest,
        /// <summary>"Target creature has base power and toughness 1/1 until end of turn".</summary>
        SetBase,
        /// <summary>"Shuffle ~ into its owner's library".</summary>
        ShuffleIn,
        /// <summary>"Exile all graveyards".</summary>
        ExileGraveyards,
        /// <summary>"Put target creature on top of its owner's library".</summary>
        ToLibraryTop,
        /// <summary>"Create a token that's a copy of target creature you control".</summary>
        CopyToken,
        /// <summary>"…then return it to the battlefield (transformed)": the card exiled just before comes back.</summary>
        ReturnToBattlefield,
        /// <summary>"Draw a card at the beginning of the next turn's upkeep": logged when the upkeep comes.</summary>
        AtNextUpkeep,
        /// <summary>A card in a graveyard ("Return target creature card from your graveyard…"): the graveyard opens.</summary>
        FromGraveyard,
        /// <summary>"Return ~ from your graveyard to your hand / the battlefield".</summary>
        ReturnSelf,
        /// <summary>Information, nothing to do ("It's still a land", "You may play that card this turn").</summary>
        Note,
        /// <summary>For the other players (in a game on your own, nothing to do here).</summary>
        Opponents,
        /// <summary>Not read: the table logs what the card says and you finish it.</summary>
        Reminder,
    }

    /// <summary>What an effect is done to.</summary>
    public enum EffectOn
    {
        None,
        /// <summary>The card itself ("~", "this creature").</summary>
        Self,
        /// <summary>A permanent you pick ("target creature").</summary>
        Target,
        /// <summary>A creature you pick, or a player ("any target").</summary>
        AnyTarget,
        /// <summary>Each creature you control.</summary>
        EachYours,
        /// <summary>The creature this is attached to ("enchanted creature", "equipped creature").</summary>
        Enchanted,
        /// <summary>Each creature and each player.</summary>
        Everything,
        /// <summary>Each creature (<see cref="Effect.What"/>: "", "flying" or "-flying"), and "all creatures".</summary>
        EachCreature,
        You,
        Opponents,
    }

    /// <summary>A token an ability makes: "two 1/1 white Soldier creature tokens with lifelink".</summary>
    public sealed record TokenSpec(string Power, string Toughness, string Colors, string Types, bool Tapped, string With)
    {
        public string Describe =>
            (Power.Length > 0 ? $"{Power}/{Toughness} " : "") + (Colors.Length > 0 ? Colors + " " : "") + Types +
            (With.Length > 0 ? " with " + With : "");
    }

    /// <summary>One effect of an ability. <see cref="N"/> = -1: X.</summary>
    public sealed record Effect(EffectKind Kind, EffectOn On = EffectOn.None, int N = 0, string What = "", int P = 0, int T = 0,
                                TokenSpec? Token = null, string Text = "", string Cond = "")
    {
        public int Amount(int x) => N < 0 ? x : N;
    }

    /// <summary>A cost that's a choice of cards: what to sacrifice, tap, exile from the graveyard, discard or return.</summary>
    public enum ChoiceKind { Sacrifice, Tap, ExileFromGraveyard, Discard, ReturnToHand, ExileFromBattlefield, RemoveCounter, PutCounter }

    /// <summary>"Sacrifice another creature": Count 1, Filter "creature", Other. "Discard a card at random": Random.</summary>
    public sealed record CostChoice(ChoiceKind Kind, int Count, string Filter, bool Other = false, bool Random = false, string Text = "", string Counter = "");

    public enum AbilityKind
    {
        /// <summary>"{2}, {T}: …" on a permanent (or in the hand or graveyard).</summary>
        Activated,
        /// <summary>A planeswalker's "+1: …".</summary>
        Loyalty,
        /// <summary>"Equip {2}": attach to a creature you pick.</summary>
        Equip,
        /// <summary>"Cycling {2}" (and landcycling) from the hand.</summary>
        Cycling,
        /// <summary>An instant or sorcery, as it resolves (a modal one: one per mode).</summary>
        Spell,
    }

    /// <summary>
    /// An activated ability read from rules text: what it costs (mana, {T},
    /// sacrificing it, life, loyalty, counters, energy, cards you choose) and
    /// what it does. Every one is a "you may": pressing it pays what the
    /// table can pay and does what it can read; anything else is logged for you.
    /// </summary>
    public sealed class Ability
    {
        public AbilityKind Kind { get; init; }
        /// <summary>Where it's activated from: the battlefield, the hand (cycling, channel), or the graveyard.</summary>
        public Zone From { get; set; } = Zone.Battlefield;
        /// <summary>The ability as printed (the card's name as "~").</summary>
        public string Text { get; init; } = "";
        public string CostText { get; init; } = "";
        public string EffectText { get; init; } = "";
        /// <summary>The ability word or name in front ("Channel", "Exhaust", "Boast"), when there is one.</summary>
        public string Word { get; init; } = "";

        /// <summary>Mana to pay: "{2}{U}" ("" = none).</summary>
        public string ManaCost { get; set; } = "";
        public bool Tap { get; set; }
        public bool Untap { get; set; }
        public bool SacrificeSelf { get; set; }
        public bool DiscardSelf { get; set; }
        public bool ExileSelf { get; set; }
        public bool ReturnSelfToHand { get; set; }
        public int Life { get; set; }
        /// <summary>"Pay half your life, rounded up".</summary>
        public bool HalfLife { get; set; }
        public int Energy { get; set; }
        /// <summary>"Exert ~": it won't untap during your next untap step.</summary>
        public bool Exert { get; set; }
        /// <summary>"Put a -1/-1 counter on ~", "Put a page counter on ~" as a cost.</summary>
        public int PutCount { get; set; }
        public string PutKind { get; set; } = "";
        /// <summary>"Mill a card" / "Exile the top card of your library" as a cost.</summary>
        public int MillCost { get; set; }
        public int ExileTopCost { get; set; }
        /// <summary>"Tap enchanted creature" as a cost.</summary>
        public bool TapEnchanted { get; set; }
        /// <summary>A loyalty ability's change: +1, -3 (X: -1 with <see cref="LoyaltyX"/>).</summary>
        public int Loyalty { get; set; }
        public bool LoyaltyX { get; set; }
        public int RemoveCount { get; set; }
        public string RemoveKind { get; set; } = "";
        /// <summary>Cards you choose to pay with (sacrifice a creature, tap two Elves, exile a card from your graveyard …).</summary>
        public List<CostChoice> Choices { get; } = new();
        /// <summary>Costs the table can't pay for you: yours to do.</summary>
        public List<string> OtherCosts { get; } = new();
        /// <summary>"Activate only as a sorcery", "only once each turn", exhaust.</summary>
        public string Timing { get; set; } = "";

        public List<Effect> Effects { get; } = new();

        /// <summary>A mode of a spell that says "Choose two" (2), "Choose three" (3), "one or more" (99): the modes are picked together. 1 = choose one, or no modes.</summary>
        public int ChooseCount { get; set; } = 1;

        /// <summary>Asks for X (in the cost or the effect).</summary>
        public bool HasX => ManaCost.Contains("{X}") || LoyaltyX || Effects.Any(e => e.N < 0);

        /// <summary>Needs a permanent (or a player) picked.</summary>
        public bool NeedsTarget => Effects.Any(e => e.On is EffectOn.Target or EffectOn.AnyTarget);

        /// <summary>Everything in it is read: the table does it all.</summary>
        public bool Understood => OtherCosts.Count == 0 && Effects.Count > 0 && Effects.All(e => e.Kind != EffectKind.Reminder);

        /// <summary>Some of it is read.</summary>
        public bool PartlyUnderstood => Effects.Any(e => e.Kind != EffectKind.Reminder) || (Effects.Count > 0 && OtherCosts.Count == 0 && Choices.Count > 0);

        /// <summary>For a button: "{T}: Draw a card" (shortened).</summary>
        public string Label
        {
            get
            {
                string s = Kind switch
                {
                    AbilityKind.Equip => "Equip " + CostText,
                    AbilityKind.Cycling => Text,
                    AbilityKind.Spell => EffectText,
                    _ => (Word.Length > 0 ? Word + ": " : "") + CostText + ": " + EffectText,
                };
                s = s.Replace("~", "this");
                return s.Length > 44 ? s[..42].TrimEnd() + "…" : s;
            }
        }
    }

    /// <summary>Reads a card's activated abilities (see <see cref="Ability"/>), and a whole card pool's for the coverage report.</summary>
    public static class Abilities
    {
        private const RegexOptions I = RegexOptions.IgnoreCase;

        private static readonly string[] Words =
        {
            "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
            "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty",
        };

        /// <summary>"a" / "an" / "one" → 1, "three" → 3, "4" → 4, "X" → -1; null when it isn't a number.</summary>
        public static int? Number(string s)
        {
            s = s.Trim().ToLowerInvariant();
            if (s is "a" or "an" or "another") return 1;
            if (s == "x") return -1;
            int i = Array.IndexOf(Words, s);
            if (i >= 0) return i;
            return int.TryParse(s, out int n) ? n : null;
        }

        private const string Num = @"(a|an|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|twenty|\d+|X)";

        /// <summary>The card's own name, its short name ("Omnath" for "Omnath, Locus of Mana") and "this creature" become "~".</summary>
        public static string Tilde(string text, string name)
        {
            string t = text;
            if (name.Length > 0)
            {
                t = t.Replace(name, "~");
                int comma = name.IndexOf(',');
                if (comma > 0) t = Regex.Replace(t, $@"\b{Regex.Escape(name[..comma])}\b", "~");
            }
            return Regex.Replace(t, @"\bthis (creature|artifact|enchantment|land|permanent|Equipment|Vehicle|planeswalker|card|token|Aura|spell|Class|Saga)\b", "~", I);
        }

        /// <summary>The abilities of one face's rules text.</summary>
        public static List<Ability> Read(string rulesText, string name, bool isPlaneswalker = false)
        {
            var result = new List<Ability>();
            var lines = Tilde(rulesText, name).Replace("\r", "").Split('\n')
                                              .Select(raw => Regex.Replace(raw, @"\s*\([^)]*\)", "").Trim()).ToList();
            for (int li = 0; li < lines.Count; li++)
            {
                string line = lines[li];
                if (line.Length == 0 || line.StartsWith("\"") || line.StartsWith("“") || line.StartsWith("•")) continue;

                var equip = Regex.Match(line, @"^Equip(?: [^{—]*?)?\s*((?:\{[^}]+\})+)", I);
                if (equip.Success)
                {
                    var a = new Ability { Kind = AbilityKind.Equip, Text = line, CostText = equip.Groups[1].Value, EffectText = "Attach to target creature you control.", ManaCost = equip.Groups[1].Value, Timing = "Equip only as a sorcery." };
                    a.Effects.Add(new Effect(EffectKind.Attach, EffectOn.Target, Text: "Attach to target creature you control"));
                    result.Add(a);
                    continue;
                }
                var levelUp = Regex.Match(line, @"^Level up\s*((?:\{[^}]+\})+)", I);
                if (levelUp.Success)
                {
                    var a = new Ability { Text = line, CostText = levelUp.Groups[1].Value, EffectText = "Put a level counter on ~.", ManaCost = levelUp.Groups[1].Value, Timing = "Level up only as a sorcery." };
                    a.Effects.Add(new Effect(EffectKind.Counters, EffectOn.Self, 1, What: "Level", Text: "Put a level counter on ~"));
                    result.Add(a);
                    continue;
                }
                var cycling = Regex.Match(line, @"^((?:\w+ )?(\w*)cycling)\s*((?:\{[^}]+\})+)", I);
                if (cycling.Success)
                {
                    string kind = cycling.Groups[1].Value;
                    bool plain = kind.Equals("cycling", StringComparison.OrdinalIgnoreCase);
                    var a = new Ability { Kind = AbilityKind.Cycling, From = Zone.Hand, Text = line, CostText = cycling.Groups[3].Value, EffectText = "Discard ~: " + (plain ? "Draw a card." : $"Search your library for a card ({kind})."), ManaCost = cycling.Groups[3].Value };
                    a.Effects.Add(plain ? new Effect(EffectKind.Draw, EffectOn.You, 1, Text: "Draw a card") : new Effect(EffectKind.Search, EffectOn.You, Text: $"Search your library ({kind})"));
                    result.Add(a);
                    continue;
                }

                // An ability word or name in front: "Channel — {1}{G}, Discard ~: …", "Hunt for Heresy — {1}, {T}: …".
                string word = "";
                var named = Regex.Match(line, @"^([^:—]{2,40}?) — (.+)$");
                if (named.Success && ColonOf(named.Groups[2].Value) > 0) { word = named.Groups[1].Value.Trim(); line = named.Groups[2].Value; }

                int colon = ColonOf(line);
                if (colon <= 0) continue;
                string cost = line[..colon].Trim(), effect = line[(colon + 1)..].Trim();
                if (Regex.IsMatch(cost, @"^(When|Whenever|At|As|If|Chapter|Level)\b", I)) continue;

                // "Choose one —" and its modes on the lines below ("• Draw a card." …): one button per mode.
                var modes = new List<string>();
                var choose = Regex.Match(effect, @"Choose (one|two|three|one or more|one or both|any number)(?: that hasn't been chosen)?(?: —|\.)?$", I);
                if (choose.Success)
                    while (li + 1 < lines.Count && lines[li + 1].StartsWith("•")) modes.Add(lines[++li].TrimStart('•', ' '));

                var loyalty = Regex.Match(cost, @"^([+−\-–]?)(\d+|X)$");
                if (loyalty.Success && isPlaneswalker)
                {
                    foreach (var (text, eff) in Variants(line, effect, choose, modes))
                    {
                        var a = new Ability { Kind = AbilityKind.Loyalty, Text = text, CostText = cost.Replace('−', '-').Replace('–', '-'), EffectText = eff, Word = word };
                        bool minus = loyalty.Groups[1].Value is "−" or "-" or "–";
                        if (loyalty.Groups[2].Value.Equals("X", StringComparison.OrdinalIgnoreCase)) { a.LoyaltyX = true; a.Loyalty = minus ? -1 : 1; }
                        else a.Loyalty = int.Parse(loyalty.Groups[2].Value) * (minus ? -1 : 1);
                        ReadEffects(eff, a);
                        result.Add(a);
                    }
                    continue;
                }
                if (!LooksLikeCost(cost)) continue;
                foreach (var (text, eff) in Variants(line, effect, choose, modes))
                {
                    var ab = new Ability { Kind = AbilityKind.Activated, Text = text, CostText = cost, EffectText = eff, Word = word };
                    if (word.Equals("Exhaust", StringComparison.OrdinalIgnoreCase)) ab.Timing = "Exhaust: once per game.";
                    if (word.Equals("Boast", StringComparison.OrdinalIgnoreCase)) ab.Timing = "Boast: only if this creature attacked this turn, once each turn.";
                    ReadCosts(cost, ab);
                    ReadEffects(eff, ab);
                    if (Regex.IsMatch(eff, @"\bReturn ~ from your graveyard\b", I)) ab.From = Zone.Graveyard;
                    result.Add(ab);
                }
            }
            return result;
        }

        /// <summary>The ability once, or (modal "Choose one —") once per mode.</summary>
        private static IEnumerable<(string Text, string Effect)> Variants(string line, string effect, Match choose, List<string> modes)
        {
            bool one = choose.Success && choose.Groups[1].Value.Equals("one", StringComparison.OrdinalIgnoreCase);
            if (!choose.Success || modes.Count == 0 || !one)
            {
                yield return (modes.Count > 0 ? line + " " + string.Join(" ", modes.Select(m => "• " + m)) : line, effect + (modes.Count > 0 && !one ? " " + string.Join(" ", modes) : ""));
                yield break;
            }
            string head = effect[..choose.Index].Trim();
            foreach (var m in modes)
                yield return (line.Replace(choose.Value, "").Trim() + " " + m, (head.Length > 0 ? head + " " : "") + m);
        }

        /// <summary>The colon between cost and effect (not one inside quotes).</summary>
        private static int ColonOf(string line)
        {
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch is '"' or '“' or '”') quoted = !quoted;
                else if (ch == ':' && !quoted) return i;
            }
            return -1;
        }

        private static bool LooksLikeCost(string cost) =>
            cost.Contains('{') || Regex.IsMatch(cost, @"^(Sacrifice|Pay|Discard|Remove|Exile|Tap|Untap|Return|Put|Mill|Reveal|Collect|Waterbend)\b", I);

        private static void ReadCosts(string cost, Ability a)
        {
            foreach (var part0 in Regex.Split(cost, @",\s*"))
            {
                string part = part0.Trim();
                if (part.Length == 0) continue;
                if (Regex.IsMatch(part, @"^(\{[^}]+\})+$"))
                {
                    foreach (Match m in Regex.Matches(part, @"\{[^}]+\}"))
                    {
                        if (m.Value == "{T}") a.Tap = true;
                        else if (m.Value == "{Q}") a.Untap = true;
                        else if (m.Value == "{E}") a.Energy++;
                        else a.ManaCost += m.Value;
                    }
                    continue;
                }
                Match g;
                if (Regex.IsMatch(part, @"^Sacrifice ~$", I)) a.SacrificeSelf = true;
                else if (Regex.IsMatch(part, @"^Discard ~$", I)) { a.DiscardSelf = true; a.From = Zone.Hand; }
                else if (Regex.IsMatch(part, @"^Reveal ~ from your hand$", I)) a.From = Zone.Hand;
                else if (Regex.IsMatch(part, @"^Exile ~ from your graveyard$", I)) { a.ExileSelf = true; a.From = Zone.Graveyard; }
                else if (Regex.IsMatch(part, @"^Exile ~$", I)) a.ExileSelf = true;
                else if (Regex.IsMatch(part, @"^Return ~ to its owner's hand$", I)) a.ReturnSelfToHand = true;
                else if ((g = Regex.Match(part, @"^Pay (\d+) life$", I)).Success) a.Life += int.Parse(g.Groups[1].Value);
                else if (Regex.IsMatch(part, @"^Pay half your life$", I)) a.HalfLife = true;
                else if (Regex.IsMatch(part, @"^rounded (up|down)$", I)) { }
                else if ((g = Regex.Match(part, $@"^Pay {Num} \{{E\}}$", I)).Success) a.Energy += Math.Max(1, Number(g.Groups[1].Value) ?? 1);
                else if (Regex.IsMatch(part, @"^Exert ~$", I)) a.Exert = true;
                else if ((g = Regex.Match(part, $@"^Put {Num} (\S+) counters? on ~$", I)).Success) { a.PutCount = Number(g.Groups[1].Value) ?? 1; a.PutKind = g.Groups[2].Value; }
                else if ((g = Regex.Match(part, $@"^Mill {Num} cards?$", I)).Success) a.MillCost = Number(g.Groups[1].Value) ?? 1;
                else if (Regex.IsMatch(part, @"^Exile the top card of your library$", I)) a.ExileTopCost = 1;
                else if (Regex.IsMatch(part, @"^Tap enchanted (creature|land|permanent|artifact)$", I)) a.TapEnchanted = true;
                else if ((g = Regex.Match(part, $@"^Exile the top {Num} cards of your library$", I)).Success) a.ExileTopCost = Number(g.Groups[1].Value) ?? 1;
                else if ((g = Regex.Match(part, $@"^Remove {Num} (\S+ )?counters? from among (.+?) you control$", I)).Success)
                    a.Choices.Add(new CostChoice(ChoiceKind.RemoveCounter, Count(g.Groups[1]), Filter(g.Groups[3].Value), Text: part, Counter: g.Groups[2].Value.Trim()));
                else if ((g = Regex.Match(part, @"^Remove (a|an|one|\d+|two|three) counters? from ~$", I)).Success) { a.RemoveCount = Number(g.Groups[1].Value) ?? 1; a.RemoveKind = "*"; }
                else if ((g = Regex.Match(part, @"^Remove all (\S+) counters from ~$", I)).Success) { a.RemoveCount = 999; a.RemoveKind = g.Groups[1].Value; }
                else if ((g = Regex.Match(part, $@"^Remove {Num} (\S+ )?counters? from (?:a|an|another) (.+?) you control$", I)).Success)
                    a.Choices.Add(new CostChoice(ChoiceKind.RemoveCounter, Count(g.Groups[1]), Filter(g.Groups[3].Value), Text: part, Counter: g.Groups[2].Value.Trim()));
                else if ((g = Regex.Match(part, $@"^Put {Num} (\S+) counters? on (?:a|an|another) (.+?) you control$", I)).Success)
                    a.Choices.Add(new CostChoice(ChoiceKind.PutCounter, 1, Filter(g.Groups[3].Value), Text: part, Counter: g.Groups[2].Value));
                else if ((g = Regex.Match(part, $@"^Exile (?:{Num} )?(another |other )?(.+?) you control$", I)).Success && !part.Contains("graveyard"))
                    a.Choices.Add(new CostChoice(ChoiceKind.ExileFromBattlefield, Count(g.Groups[1]), Filter(g.Groups[3].Value), g.Groups[2].Success, Text: part));
                else if ((g = Regex.Match(part, @"^Pay ((?:\{E\})+)$", I)).Success) a.Energy += g.Groups[1].Value.Length / 3;
                else if ((g = Regex.Match(part, @"^Waterbend ((?:\{[^}]+\})+)$", I)).Success) a.ManaCost += g.Groups[1].Value;
                else if ((g = Regex.Match(part, $@"^Remove {Num} (\S+) counters? from ~(?: and sacrifice it)?$", I)).Success)
                {
                    a.RemoveCount = Number(g.Groups[1].Value) ?? 1;
                    a.RemoveKind = g.Groups[2].Value;
                    if (part.EndsWith("sacrifice it", StringComparison.OrdinalIgnoreCase)) a.SacrificeSelf = true;
                }
                else if ((g = Regex.Match(part, $@"^Sacrifice (?:{Num} )?(another |other )?(.+)$", I)).Success)
                    a.Choices.Add(new CostChoice(ChoiceKind.Sacrifice, Count(g.Groups[1]), Filter(g.Groups[3].Value), g.Groups[2].Success, Text: part));
                else if ((g = Regex.Match(part, $@"^Tap (?:{Num} )?(another |other )?untapped (.+?) you control$", I)).Success)
                    a.Choices.Add(new CostChoice(ChoiceKind.Tap, Count(g.Groups[1]), Filter(g.Groups[3].Value), g.Groups[2].Success, Text: part));
                else if ((g = Regex.Match(part, $@"^Exile (?:{Num} )?(another |other )?(.+?) from your graveyard$", I)).Success)
                    a.Choices.Add(new CostChoice(ChoiceKind.ExileFromGraveyard, Count(g.Groups[1]), Filter(g.Groups[3].Value), g.Groups[2].Success, Text: part));
                else if ((g = Regex.Match(part, $@"^Discard {Num} (.+?)( at random)?$", I)).Success)
                    a.Choices.Add(new CostChoice(ChoiceKind.Discard, Math.Max(1, Number(g.Groups[1].Value) ?? 1), Filter(g.Groups[2].Value), Random: g.Groups[3].Success, Text: part));
                else if (Regex.IsMatch(part, @"^Discard your hand$", I))
                    a.Choices.Add(new CostChoice(ChoiceKind.Discard, 999, "card", Text: part));
                else if ((g = Regex.Match(part, $@"^Return {Num} (.+?) you control to (?:its|their) owners?'s? hand$", I)).Success)
                    a.Choices.Add(new CostChoice(ChoiceKind.ReturnToHand, Math.Max(1, Number(g.Groups[1].Value) ?? 1), Filter(g.Groups[2].Value), Text: part));
                else a.OtherCosts.Add(part);
            }
        }

        /// <summary>"creatures and planeswalkers" → "creature or planeswalker"; "nonland permanents you control" → "nonland" (yours).</summary>
        private static string AllFilter(string what)
        {
            string w = Regex.Replace(what, @"\s+(you control|your opponents control|with .+|that .+|other than .+)$", "", I);
            w = Regex.Replace(w, @",\s*(and\s+)?|\s+and\s+", " or ", I);
            return Filter(w) + (what.Contains("you control", StringComparison.OrdinalIgnoreCase) ? "|yours" : "");
        }

        private static int Count(Group g) => g.Success ? Math.Max(1, Number(g.Value) ?? 1) : 1;

        /// <summary>"creature cards" → "creature"; "artifact or creature" stays; "untapped Elves" → "Elf".</summary>
        private static string Filter(string what)
        {
            string w = Regex.Replace(what.Trim(), @"\b(cards?|permanents?)\b", "", I).Trim();
            w = Regex.Replace(w, @"\b(\w+?)(ies)\b", "$1y");                       // Allies → Ally
            w = Regex.Replace(w, @"\b(Elv)es\b", "Elf", I);
            w = Regex.Replace(w, @"\b(\w{3,}?)s\b(?!\w)", m => m.Value.EndsWith("ss") ? m.Value : m.Groups[1].Value);
            return w.Length == 0 ? "card" : w;
        }

        /// <summary>Does a card fit a choice's filter ("creature", "artifact or creature", "Food", "nontoken creature", "card")?</summary>
        public static bool Fits(GameCard c, string filter)
        {
            string type = c.FaceType(c.BackUp ? 1 : 0) + " " + c.ShownName;
            foreach (var alt in Regex.Split(filter, @"\s+or\s+|,\s*", I))
            {
                bool all = true;
                foreach (var word in alt.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    string w = word.ToLowerInvariant();
                    if (w is "card" or "permanent" or "a" or "an") continue;
                    if (w == "nontoken") { if (c.IsToken) all = false; continue; }
                    if (w.StartsWith("non")) { if (type.Contains(w[3..], StringComparison.OrdinalIgnoreCase)) all = false; continue; }
                    if (!type.Contains(w, StringComparison.OrdinalIgnoreCase)) all = false;
                }
                if (all) return true;
            }
            return false;
        }

        /// <summary>Split an effect into its sentences ("Draw a card, then discard a card." → two; "You gain 1 life and draw a card" → two).</summary>
        private static IEnumerable<string> Sentences(string effect)
        {
            foreach (var s in Regex.Split(effect, @"(?<=\.)\s+(?=[A-Z])"))
                foreach (var part in Regex.Split(s.Trim().TrimEnd('.'), @",\s+then\s+|\.\s*Then\s+", I))
                {
                    string p = part.Trim();
                    if (p.Length == 0) continue;
                    // "~ deals 2 damage to any target and 3 damage to you" (Orcish Cannonade).
                    var hurt = Regex.Match(p, @"^(~ deals \w+ damage to .+?) and (\w+) damage to you$", I);
                    if (hurt.Success)
                    {
                        yield return hurt.Groups[1].Value;
                        yield return $"You lose {hurt.Groups[2].Value} life";
                        continue;
                    }
                    // "~ deals 3 damage to any target and you gain 3 life" (Lightning Helix).
                    var gain = Regex.Match(p, @"^(.+?) and you (gain|lose) (\w+) life$", I);
                    if (gain.Success && !Regex.IsMatch(gain.Groups[1].Value, @"^(You )?(draw|gain|lose)\b", I))
                    {
                        yield return gain.Groups[1].Value;
                        yield return $"You {gain.Groups[2].Value.ToLowerInvariant()} {gain.Groups[3].Value} life";
                        continue;
                    }
                    // "You gain 1 life and draw a card", "You draw a card and you lose 1 life".
                    var both = Regex.Match(p, @"^(?:You )?(draw|gain|lose)(.+?) and (?:you )?(draw|gain|lose)(.+)$", I);
                    if (both.Success)
                    {
                        yield return Normal(both.Groups[1].Value + both.Groups[2].Value);
                        yield return Normal(both.Groups[3].Value + both.Groups[4].Value);
                        continue;
                    }
                    yield return Regex.Replace(p, @"^You draw\b", "Draw", I);
                }
        }

        private static string Normal(string s)
        {
            s = s.Trim();
            if (s.StartsWith("draw", StringComparison.OrdinalIgnoreCase)) return "Draw" + s[4..];
            return "You " + s.ToLowerInvariant();
        }

        private static EffectOn OnOf(string who) =>
            who == "~" ? EffectOn.Self
            : Regex.IsMatch(who, @"^(enchanted|equipped) creature$", I) ? EffectOn.Enchanted
            : who.StartsWith("each creature you control", StringComparison.OrdinalIgnoreCase) ? EffectOn.EachYours
            : who.StartsWith("any target", StringComparison.OrdinalIgnoreCase) ? EffectOn.AnyTarget
            : who.StartsWith("you", StringComparison.OrdinalIgnoreCase) ? EffectOn.You
            : who.StartsWith("each opponent", StringComparison.OrdinalIgnoreCase) || who.Contains("player") || who.Contains("opponent") ? EffectOn.Opponents
            : who.StartsWith("target", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(who, @"^up to \w+ targets?", I) ? EffectOn.Target
            : EffectOn.None;

        private static void ReadEffects(string effect, Ability a)
        {
            Effect? prev = null;
            foreach (var s0 in Sentences(effect))
            {
                string s = s0;
                if (Regex.IsMatch(s, @"^(Activate|Any player may activate|This ability costs|Activate this ability only|You may activate)", I))
                {
                    a.Timing += (a.Timing.Length > 0 ? " " : "") + s + ".";
                    continue;
                }
                // "It deals 1 damage …", "Tap it", "It gains haste …": the target just picked, or the card itself.
                string it = prev?.On is EffectOn.Target or EffectOn.AnyTarget ? "target creature" : "~";
                if (IsNote(s)) { a.Effects.Add(new Effect(EffectKind.Note, Text: s)); continue; }       // before "It" becomes "~"
                s = Regex.Replace(s, @"^(It|They)\b", it);
                s = Regex.Replace(s, @"^(Tap|Untap|Sacrifice|Exile|Destroy) (it|them|that creature|those creatures)$", "$1 " + it, I);
                s = Regex.Replace(s, @"\bon (it|that creature)$", "on " + it, I);
                if (it == "target creature") s = Regex.Replace(s, @"^target creature gain\b", "target creature gains");
                var e = ReadEffect(s);
                a.Effects.Add(e);
                prev = e;
            }
        }

        private static readonly string Target = @"(~|target [^,.]+?|any target|each creature you control|each opponent|you|target player|target opponent|up to \w+ targets? [^,.]+?|enchanted creature|equipped creature)";

        /// <summary>One sentence of an effect.</summary>
        public static Effect ReadEffect(string s)
        {
            // "If you win the flip, return ~ to its owner's hand": done only when the coin says so.
            var flip = Regex.Match(s, @"^If you (win|lose) the flip, (.+)$", I);
            if (flip.Success)
            {
                var inner = ReadEffect(char.ToUpperInvariant(flip.Groups[2].Value[0]) + flip.Groups[2].Value[1..]);
                return inner with { Cond = flip.Groups[1].Value.ToLowerInvariant(), Text = s };
            }
            Match m;
            int N(int group) => Number(m.Groups[group].Value) ?? 1;
            s = Regex.Replace(s, @",\s+where X is .+$", "", I);                    // X is asked for when it's activated
            if ((m = Regex.Match(s, $@"^(?:Each player|Target player) draws {Num} cards?$", I)).Success)
                return new(EffectKind.Draw, EffectOn.You, Number(m.Groups[1].Value) ?? 1, Text: s);
            if ((m = Regex.Match(s, $@"^Draw {Num} cards?$", I)).Success) return new(EffectKind.Draw, EffectOn.You, N(1), Text: s);
            if ((m = Regex.Match(s, $@"^You gain {Num} life$", I)).Success) return new(EffectKind.GainLife, EffectOn.You, N(1), Text: s);
            if ((m = Regex.Match(s, $@"^You lose {Num} life$", I)).Success) return new(EffectKind.LoseLife, EffectOn.You, N(1), Text: s);
            if ((m = Regex.Match(s, $@"^(?:Each opponent|Target player|Target opponent) loses {Num} life(?: and you gain {Num} life)?$", I)).Success)
                return new(EffectKind.OpponentsLoseLife, EffectOn.Opponents, N(1), What: m.Groups[2].Success ? m.Groups[2].Value : "", Text: s);
            if ((m = Regex.Match(s, $@"^Put {Num} (\S+) counters? on each of up to \w+ target (.+)$", I)).Success)
                return new(EffectKind.Counters, EffectOn.Target, N(1), What: m.Groups[2].Value, Text: s);
            if ((m = Regex.Match(s, $@"^Put {Num} (\S+) counters? on {Target}$", I)).Success)
                return new(EffectKind.Counters, OnOf(m.Groups[3].Value), N(1), What: m.Groups[2].Value, Text: s);
            s = Regex.Replace(s, @"^(Up to (one|two|three|four|five|X|\d+)|two|three|four|five|X|\d+) target (creatures?)( each)?\b", "target $3", I);       // "Up to two target creatures each get": you pick one, the rest are yours
            if ((m = Regex.Match(s, @"^(~|target [^,.]*?creature[^,.]*?|each creature you control|creatures you control|enchanted creature|equipped creature) gets? ([+\-−]\w+)/([+\-−]\w+)(?: and gains? ([^.]+?))? until end of turn$", I)).Success)
            {
                int Signed(string v) { v = v.Replace('−', '-'); int sign = v.StartsWith("-") ? -1 : 1; return (Number(v.TrimStart('+', '-')) is int k && k >= 0 ? k : 0) * sign; }
                string on = m.Groups[1].Value.StartsWith("creatures", StringComparison.OrdinalIgnoreCase) ? "each creature you control" : m.Groups[1].Value;
                return new(EffectKind.Pump, OnOf(on), What: m.Groups[4].Value, P: Signed(m.Groups[2].Value), T: Signed(m.Groups[3].Value), Text: s);
            }
            if ((m = Regex.Match(s, @"^(~|target [^,.]*?creature[^,.]*?|creatures you control|enchanted creature|equipped creature) gains? ([^.]+?) until end of turn$", I)).Success &&
                Keywords.ListOf(m.Groups[2].Value).Any())
                return new(EffectKind.GainKeywords, OnOf(m.Groups[1].Value.StartsWith("creatures", StringComparison.OrdinalIgnoreCase) ? "each creature you control" : m.Groups[1].Value),
                           What: string.Join(", ", Keywords.ListOf(m.Groups[2].Value)), Text: s);
            if ((m = Regex.Match(s, @"^(~|target [^,.]*?creature[^,.]*?|enchanted creature) gains? ([a-z ,]+?)$", I)).Success && Keywords.ListOf(m.Groups[2].Value).Any())
                return new(EffectKind.GainKeywords, OnOf(m.Groups[1].Value), What: string.Join(", ", Keywords.ListOf(m.Groups[2].Value)), Text: s);
            if ((m = Regex.Match(s, @"^(~|target [^,.]*?creature[^,.]*?|enchanted creature) can't be blocked this turn$", I)).Success)
                return new(EffectKind.GainKeywords, OnOf(m.Groups[1].Value), What: "Can't be blocked", Text: s);
            if ((m = Regex.Match(s, @"^(~|target [^,.]*?creatures?[^,.]*?) can't block this turn$", I)).Success)
                return new(EffectKind.GainKeywords, OnOf(m.Groups[1].Value), What: "Can't block", Text: s);
            if ((m = Regex.Match(s, @"^Regenerate (~|target [^,.]+|enchanted creature|equipped creature)$", I)).Success)
                return new(EffectKind.Regenerate, OnOf(m.Groups[1].Value), Text: s);
            if ((m = Regex.Match(s, $@"^(?:Target player |Each player |You )?mills? {Num} cards?$", I)).Success) return new(EffectKind.Mill, EffectOn.You, N(1), Text: s);
            if (Regex.IsMatch(s, @"^Mill a card$", I)) return new(EffectKind.Mill, EffectOn.You, 1, Text: s);
            if ((m = Regex.Match(s, $@"^Scry {Num}$", I)).Success) return new(EffectKind.Scry, EffectOn.You, N(1), Text: s);
            if ((m = Regex.Match(s, $@"^Surveil {Num}$", I)).Success) return new(EffectKind.Surveil, EffectOn.You, N(1), Text: s);
            if ((m = Regex.Match(s, $@"^(?:Look at|Reveal) the top {Num} cards? of your library", I)).Success) return new(EffectKind.LookTop, EffectOn.You, N(1), Text: s);
            if (Regex.IsMatch(s, @"^(?:Look at|Reveal) the top card of your library", I)) return new(EffectKind.LookTop, EffectOn.You, 1, Text: s);
            if ((m = Regex.Match(s, $@"^Exile the top {Num} cards? of your library", I)).Success) return new(EffectKind.ExileTop, EffectOn.You, N(1), Text: s);
            if (Regex.IsMatch(s, @"^Exile the top card of your library", I)) return new(EffectKind.ExileTop, EffectOn.You, 1, Text: s);
            if (Regex.IsMatch(s, @"^Search your library for", I)) return new(EffectKind.Search, EffectOn.You, Text: s);
            if (Regex.IsMatch(s, @"^Investigate$", I)) return new(EffectKind.Token, EffectOn.You, 1, Token: new TokenSpec("", "", "", "Clue", false, ""), Text: s);
            if ((m = Regex.Match(s, $@"^Create {Num} (.+?) tokens?(?: with (.+))?$", I)).Success && ReadToken(m.Groups[2].Value, m.Groups[3].Value) is { } spec)
                return new(EffectKind.Token, EffectOn.You, N(1), Token: spec, Text: s);
            if ((m = Regex.Match(s, $@"^~ deals {Num} damage to each creature and each player$", I)).Success)
                return new(EffectKind.Damage, EffectOn.Everything, N(1), Text: s);
            if ((m = Regex.Match(s, $@"^~ deals {Num} damage to {Target}$", I)).Success)
                return new(EffectKind.Damage, OnOf(m.Groups[2].Value), N(1), Text: s);
            if ((m = Regex.Match(s, @"^You may tap or untap (target .+)$", I)).Success) return new(EffectKind.Tap, EffectOn.Target, What: "toggle", Text: s);
            if ((m = Regex.Match(s, @"^(Tap|Untap) (target [^.]+)$", I)).Success)
                return new(m.Groups[1].Value.Equals("Tap", StringComparison.OrdinalIgnoreCase) ? EffectKind.Tap : EffectKind.Untap, EffectOn.Target, Text: s);
            if ((m = Regex.Match(s, $@"^(Tap|Untap) {Target}$", I)).Success)
                return new(m.Groups[1].Value.Equals("Tap", StringComparison.OrdinalIgnoreCase) ? EffectKind.Tap : EffectKind.Untap, OnOf(m.Groups[2].Value), Text: s);
            if ((m = Regex.Match(s, @"^Return ~ from your graveyard to (your hand|the battlefield)( tapped)?$", I)).Success)
                return new(EffectKind.ReturnSelf, EffectOn.Self, What: (m.Groups[1].Value.Contains("hand") ? "Hand" : "Battlefield") + (m.Groups[2].Success ? " tapped" : ""), Text: s);
            if (Regex.IsMatch(s, @"\bcards?\b[^.]*? from (?:a|your|a single|target player's|an opponent's) graveyard\b", I) &&
                Regex.IsMatch(s, @"^(Return|Exile|Put|Choose) (up to \w+ )?target", I))
                return new(EffectKind.FromGraveyard, EffectOn.You, Text: s);
            if ((m = Regex.Match(s, $@"^Return {Target} to (?:its|their) owners?'s?'? hands?$", I)).Success)
                return new(EffectKind.ToHand, OnOf(m.Groups[1].Value), Text: s);
            if (Regex.IsMatch(s, @"^Sacrifice ~$", I)) return new(EffectKind.Sacrifice, EffectOn.Self, Text: s);
            if (Regex.IsMatch(s, @"^Exile ~$", I)) return new(EffectKind.Exile, EffectOn.Self, Text: s);
            if (Regex.IsMatch(s, @"^Exile ~, then return it to the battlefield transformed", I)) return new(EffectKind.Transform, EffectOn.Self, Text: s);
            if ((m = Regex.Match(s, @"^Destroy (target [^.]+)$", I)).Success && !s.Contains(" can't ")) return new(EffectKind.Destroy, EffectOn.Target, Text: s);
            if (Regex.IsMatch(s, @"^Exile (enchanted|equipped) creature$", I)) return new(EffectKind.Exile, EffectOn.Enchanted, Text: s);
            if ((m = Regex.Match(s, @"^Exile (target [^,.]+)$", I)).Success && !s.Contains("graveyard")) return new(EffectKind.Exile, EffectOn.Target, Text: s);
            if (Regex.IsMatch(s, @"^Transform ~$", I)) return new(EffectKind.Transform, EffectOn.Self, Text: s);
            if ((m = Regex.Match(s, @"^(Destroy|Exile) all (.+?)$", I)).Success && !s.Contains("graveyard") && !s.Contains("can't"))
                return new(m.Groups[1].Value.Equals("Destroy", StringComparison.OrdinalIgnoreCase) ? EffectKind.DestroyAll : EffectKind.ExileAll, EffectOn.None,
                           What: AllFilter(m.Groups[2].Value), Text: s);
            if ((m = Regex.Match(s, @"^Return all (.+?) to their owners' hands$", I)).Success)
                return new(EffectKind.BounceAll, EffectOn.None, What: AllFilter(m.Groups[1].Value), Text: s);
            if (Regex.IsMatch(s, @"^Proliferate$", I)) return new(EffectKind.Proliferate, EffectOn.You, Text: s);
            if (Regex.IsMatch(s, @"^(?:Then )?shuffle(?: your library)?$", I)) return new(EffectKind.Shuffle, EffectOn.You, Text: s);
            if ((m = Regex.Match(s, $@"^(?:then )?discard {Num} cards?$", I)).Success) return new(EffectKind.Discard, EffectOn.You, N(1), Text: s);
            if (Regex.IsMatch(s, @"^Discard your hand$", I)) return new(EffectKind.DiscardHand, EffectOn.You, Text: s);
            if ((m = Regex.Match(s, @"^You get ((?:\{E\})+)$", I)).Success) return new(EffectKind.Energy, EffectOn.You, m.Groups[1].Value.Length / 3, Text: s);
            if ((m = Regex.Match(s, $@"^Monstrosity {Num}$", I)).Success) return new(EffectKind.Monstrosity, EffectOn.Self, N(1), Text: s);
            if ((m = Regex.Match(s, $@"^Adapt {Num}$", I)).Success) return new(EffectKind.Adapt, EffectOn.Self, N(1), Text: s);
            if ((m = Regex.Match(s, @"^Level (\d+)$", I)).Success) return new(EffectKind.SetCounter, EffectOn.Self, int.Parse(m.Groups[1].Value), What: "Level", Text: s);
            if (Regex.IsMatch(s, @"^Flip a coin$", I)) return new(EffectKind.Coin, Text: s);
            if ((m = Regex.Match(s, @"^Roll (?:a|an) (?:(\w+)-sided die|d(\d+))$", I)).Success)
                return new(EffectKind.Die, N: m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : Number(m.Groups[1].Value) ?? 6, Text: s);
            if (Regex.IsMatch(s, @"\bat the beginning of the next end step$", I) && Regex.IsMatch(s, @"^(Sacrifice|Exile|Return)\b", I))
                return new(EffectKind.AtNextEndStep, Text: s);
            if (Regex.IsMatch(s, @"^Add ", I)) return new(EffectKind.Mana, EffectOn.You, Text: s);
            if ((m = Regex.Match(s, @"^All creatures get ([+\-−]\w+)/([+\-−]\w+) until end of turn$", I)).Success)
            {
                int Sg(string v) { v = v.Replace('−', '-'); int sign = v.StartsWith("-") ? -1 : 1; return (Number(v.TrimStart('+', '-')) is int k && k >= 0 ? k : 0) * sign; }
                return new(EffectKind.Pump, EffectOn.EachCreature, P: Sg(m.Groups[1].Value), T: Sg(m.Groups[2].Value), Text: s);
            }
            if ((m = Regex.Match(s, $@"^~ deals {Num} damage to each creature( with flying| without flying)?( and each player)?$", I)).Success)
                return new(EffectKind.Damage, EffectOn.EachCreature, N(1),
                           What: (m.Groups[2].Value.Trim().StartsWith("without") ? "-flying" : m.Groups[2].Success ? "flying" : "") + (m.Groups[3].Success ? "+players" : ""), Text: s);
            if (Regex.IsMatch(s, @"^(~ deals \w+ damage to each creature your opponents control|Creatures your opponents control (get|can't|gain|lose)|Each creature your opponents control)", I))
                return new(EffectKind.Opponents, EffectOn.Opponents, Text: s);
            if ((m = Regex.Match(s, $@"^(?:then )?draws? {Num} cards?$", I)).Success) return new(EffectKind.Draw, EffectOn.You, N(1), Text: s);
            if ((m = Regex.Match(s, $@"^(?:then )?discards? {Num} cards?$", I)).Success) return new(EffectKind.Discard, EffectOn.You, N(1), Text: s);
            if ((m = Regex.Match(s, $@"^You may discard {Num} cards?$", I)).Success) return new(EffectKind.Discard, EffectOn.You, N(1), Text: s);
            if ((m = Regex.Match(s, $@"^If you do, draw {Num} cards?$", I)).Success) return new(EffectKind.Draw, EffectOn.You, N(1), Text: s);
            if ((m = Regex.Match(s, $@"^You gain {Num} life for each creature you control$", I)).Success) return new(EffectKind.GainLife, EffectOn.You, N(1), What: "per creature", Text: s);
            if (Regex.IsMatch(s, @"^Discard all the cards in your hand$", I)) return new(EffectKind.DiscardHand, EffectOn.You, Text: s);
            if (Regex.IsMatch(s, @"^Destroy ~$", I)) return new(EffectKind.Destroy, EffectOn.Self, Text: s);
            if (Regex.IsMatch(s, @"^Shuffle ~ into its owner's library$", I)) return new(EffectKind.ShuffleIn, EffectOn.Self, Text: s);
            if (Regex.IsMatch(s, @"^Exile all graveyards$", I)) return new(EffectKind.ExileGraveyards, EffectOn.You, Text: s);
            if ((m = Regex.Match(s, $@"^Bolster {Num}$", I)).Success) return new(EffectKind.Bolster, EffectOn.You, N(1), Text: s);
            if (Regex.IsMatch(s, @"^Manifest the top card of your library$", I)) return new(EffectKind.Manifest, EffectOn.You, 1, Text: s);
            if (Regex.IsMatch(s, @"^Populate$", I)) return new(EffectKind.CopyToken, EffectOn.Target, What: "populate", Text: s);
            if ((m = Regex.Match(s, @"^(~|target [^,.]*?creature[^,.]*?) has base power and toughness (\d+)/(\d+) until end of turn$", I)).Success)
                return new(EffectKind.SetBase, OnOf(m.Groups[1].Value), P: int.Parse(m.Groups[2].Value), T: int.Parse(m.Groups[3].Value), Text: s);
            if ((m = Regex.Match(s, @"^Untap all (.+?)$", I)).Success)
                return new(EffectKind.UntapAll, EffectOn.None, What: AllFilter(m.Groups[1].Value), Text: s);
            if (Regex.IsMatch(s, @"^Put target [^,.]+ on top of its owner's library$", I)) return new(EffectKind.ToLibraryTop, EffectOn.Target, Text: s);
            if (Regex.IsMatch(s, @"^Create a token that's a copy of target [^,.]+$", I)) return new(EffectKind.CopyToken, EffectOn.Target, Text: s);
            if ((m = Regex.Match(s, @"^return (it|that card|~|target creature|the exiled card) to the battlefield( transformed)?(?: under (?:its|your|their) owner's control| under your control)?$", I)).Success)
                return new(EffectKind.ReturnToBattlefield, m.Groups[1].Value == "~" ? EffectOn.Self : EffectOn.Target, What: m.Groups[2].Success ? "transformed" : "", Text: s);
            if (Regex.IsMatch(s, @"\bat the beginning of the next turn's upkeep$", I)) return new(EffectKind.AtNextUpkeep, Text: s);
            if (IsNote(s)) return new(EffectKind.Note, Text: s);
            if (Regex.IsMatch(s, @"^(Target opponent|Each opponent|Target player|Each player|That player|Each other player|Its controller|Their controller)\b", I))
                return new(EffectKind.Opponents, EffectOn.Opponents, Text: s);
            return new(EffectKind.Reminder, Text: s);
        }

        /// <summary>Information or a choice that's yours, with nothing for the table to do ("It's still a land", "Put the rest into your graveyard").</summary>
        private static bool IsNote(string s) => Regex.IsMatch(s,
            @"^((It|~|target creature)('s| is) still a land|(It|~|target creature|They) can't be regenerated|You may choose new targets|You may choose the same mode|" +
            @"Put the rest (on the bottom|into your graveyard|back)|put them back in any order|Put (N|\w+) of them into your hand|You may reveal an? .+ from among them|" +
            @"You may put (an?|up to \w+) .+ from among them (into your hand|onto the battlefield)|After this (main )?phase, there is an additional|" +
            @"Until (the )?end of (your next )?turn, you may play|You may play th|Spend this mana only|Prevent |Take an extra turn|" +
            @"You may put an? (land|creature|artifact|permanent) card from your hand onto the battlefield|That creature gains haste|Copy target|Counter target|" +
            @"If (that|a) (creature|permanent).* would die this turn, exile it instead|If that spell is countered this way|If you search your library this way, shuffle|" +
            @"(Then )?that player shuffles|shuffles$|shuffle and put that card on top|Choose a (creature type|color|card name|nonland card name|basic land type|number)|" +
            @"Choose target|If you control a commander as you cast|Gain control of|(Its|That creature|target creature|~|They|Those creatures) (doesn't|don't) untap during|" +
            @"The Ring tempts you|Venture into the dungeon|Learn$|Clash with an opponent|Cipher$|Escalate|Strive|You may pay \{.+\} to end this effect|X can't be 0|" +
            @"You choose (a|an) .*card from it|Choose an opponent|(Target creature|target creature|~) (blocks|attacks) .* if able|~ can attack this turn as though|" +
            @"You may play an additional land|Switch .* power and toughness|~ becomes the creature type|(Target|target) land becomes the basic land type|" +
            @"The next time a source of your choice|If that spell would be put into your graveyard|~'re still lands|They're still lands|Exile that card$|" +
            @"You may cast (that card|the copy|it|the exiled card) without paying|You may cast the exiled card|When you next cast|~ becomes an artifact creature|Goad target|" +
            @"Beam me up|Transmute|Teamwork|Converge|Raid|Delirium)", RegexOptions.IgnoreCase);

        private static readonly string[] ColorWords = { "white", "blue", "black", "red", "green", "colorless" };

        /// <summary>"1/1 white Soldier creature" → its power, toughness, colors and types; null when it isn't a token description.</summary>
        private static TokenSpec? ReadToken(string desc, string with)
        {
            var words = desc.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            bool tapped = false;
            if (words.Count > 0 && words[0].Equals("tapped", StringComparison.OrdinalIgnoreCase)) { tapped = true; words.RemoveAt(0); }
            string p = "", t = "";
            if (words.Count > 0 && Regex.Match(words[0], @"^(\d+|X|\*)/(\d+|X|\*)$") is { Success: true } pt)
            {
                p = pt.Groups[1].Value; t = pt.Groups[2].Value;
                words.RemoveAt(0);
            }
            var colors = new List<string>();
            while (words.Count > 0 && (ColorWords.Contains(words[0].ToLowerInvariant()) || words[0].Equals("and", StringComparison.OrdinalIgnoreCase)))
            {
                if (!words[0].Equals("and", StringComparison.OrdinalIgnoreCase)) colors.Add(words[0].ToLowerInvariant());
                words.RemoveAt(0);
            }
            if (words.Count == 0 || words.Count > 6) return null;
            if (words.Any(w => w.Contains("copy", StringComparison.OrdinalIgnoreCase) || w.Contains("that", StringComparison.OrdinalIgnoreCase))) return null;
            return new TokenSpec(p, t, string.Join(" and ", colors), string.Join(" ", words), tapped, with.Trim());
        }

        // ── The coverage report ──────────────────────────────────────────
        public sealed class Coverage
        {
            public int Cards { get; set; }
            public int CardsWithAbilities { get; set; }
            public int Abilities { get; set; }
            /// <summary>Of <see cref="Abilities"/>: instants and sorceries (each mode of a modal one counts).</summary>
            public int Spells { get; set; }
            public int Understood { get; set; }
            public int Partly { get; set; }
            /// <summary>The wordings not read, most common first: the sentence with numbers as N and the name as ~.</summary>
            public List<(string Wording, int Count, string Example)> Missing { get; } = new();
            /// <summary>Costs not read, most common first.</summary>
            public List<(string Wording, int Count, string Example)> MissingCosts { get; } = new();
        }

        /// <summary>Read every card (name, type line, rules text), one per Oracle card, and count what's understood.</summary>
        public static Coverage Measure(IEnumerable<(string Name, string TypeLine, string Text)> cards, int top = 60)
        {
            var cov = new Coverage();
            var missing = new Dictionary<string, (int Count, string Example)>();
            var missingCosts = new Dictionary<string, (int Count, string Example)>();
            static string Norm(string s) => Regex.Replace(Regex.Replace(s, @"\b(\d+|one|two|three|four|five|six|seven|X)\b", "N", I), @"\{[^}]+\}", "{M}");
            foreach (var (name, type, text) in cards)
            {
                cov.Cards++;
                var names = CardFacesSplit(name);
                var texts = text.Replace("\r\n", "\n").Split("\n//\n");
                var types = CardFacesSplit(type);
                bool any = false;
                for (int i = 0; i < texts.Length; i++)
                {
                    string faceName = i < names.Length ? names[i] : names[0];
                    string faceType = i < types.Length ? types[i] : types[0];
                    bool spell = Regex.IsMatch(faceType, @"\b(Instant|Sorcery)\b", I);
                    var read = Read(texts[i], faceName, faceType.Contains("Planeswalker", StringComparison.OrdinalIgnoreCase));
                    if (spell) { var sp = ReadSpell(texts[i], faceName); cov.Spells += sp.Count; read.AddRange(sp); }
                    foreach (var a in read)
                    {
                        if (a.Effects.All(e => e.Kind == EffectKind.Mana) && a.Kind == AbilityKind.Activated) continue;   // mana: the mana reader's
                        any = true;
                        cov.Abilities++;
                        if (a.Understood) cov.Understood++;
                        else if (a.PartlyUnderstood) cov.Partly++;
                        foreach (var e in a.Effects.Where(e => e.Kind == EffectKind.Reminder))
                        {
                            string k = Norm(e.Text);
                            missing[k] = missing.TryGetValue(k, out var v) ? (v.Count + 1, v.Example) : (1, $"{faceName}: {a.Text}");
                        }
                        foreach (var c in a.OtherCosts)
                        {
                            string k = Norm(c);
                            missingCosts[k] = missingCosts.TryGetValue(k, out var v) ? (v.Count + 1, v.Example) : (1, $"{faceName}: {a.Text}");
                        }
                    }
                }
                if (any) cov.CardsWithAbilities++;
            }
            cov.Missing.AddRange(missing.OrderByDescending(kv => kv.Value.Count).Take(top).Select(kv => (kv.Key, kv.Value.Count, kv.Value.Example)));
            cov.MissingCosts.AddRange(missingCosts.OrderByDescending(kv => kv.Value.Count).Take(top / 2).Select(kv => (kv.Key, kv.Value.Count, kv.Value.Example)));
            return cov;
        }

        private static string[] CardFacesSplit(string v) => (v ?? "").Split("//", StringSplitOptions.TrimEntries);

        private static readonly Regex SpellLineSkip = new(
            @"^(Kicker|Multikicker|Flashback|Buyback|Overload|Entwine|Escape|Foretell|Madness|Retrace|Jump-start|Replicate|Spectacle|Surge|Splice|" +
            @"Convoke|Delve|Improvise|Affinity|Casualty|Bargain|Gift|Cleave|Plot|Disturb|Harmonize|Warp|Squad|Demonstrate|Evoke|Emerge|Prowl|" +
            @"Rebound|Storm|Cascade|Split second|Suspend|Miracle|Ripple|Fuse|Aftermath|Awaken|Conspire|Echo|Freerunning|Blitz|Dash|" +
            @"This spell costs|This spell can't be countered|You may cast this spell|You can't cast this spell|Cast this spell only|" +
            @"~ costs|~ can't be countered|You may cast ~|You can't cast ~|Cast ~ only|" +
            @"(?:\w+ )?\w*cycling|Cycling|Enchant|Equip|Escalate|Cipher|Strive|Devoid|Overload|Tiered|Offspring|Spree)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// An instant's or sorcery's text as it resolves: one "ability" with its
        /// effects, or (Choose one —) one per mode. Keyword lines (Flash,
        /// Kicker, Flashback …) are left out; "As an additional cost to cast
        /// this spell, sacrifice a creature" becomes a cost to choose.
        /// </summary>
        public static List<Ability> ReadSpell(string rulesText, string name)
        {
            var result = new List<Ability>();
            var lines = Tilde(rulesText, name).Replace("\r", "").Split('\n')
                                              .Select(raw => Regex.Replace(raw, @"\s*\([^)]*\)", "").Trim())
                                              .Where(l => l.Length > 0).ToList();
            string extraCost = "";
            var body = new List<string>();
            var modes = new List<string>();
            string modeHead = "";
            bool chooseOne = false;
            int chooseCount = 1;
            foreach (var line in lines)
            {
                if (line.StartsWith("•")) { modes.Add(line.TrimStart('•', ' ')); continue; }
                var add = Regex.Match(line, @"^As an additional cost to cast (?:this spell|~), (.+?)\.$", I);
                if (add.Success) { extraCost = add.Groups[1].Value; continue; }
                if (SpellLineSkip.IsMatch(line)) continue;
                if (Keywords.Printed(line).Count > 0 && !line.Contains(':')) continue;               // "Flash", "Flying" (a keyword line)
                var choose = Regex.Match(line, @"^(.*?)Choose (one or more|one or both|any number|one|two|three)(?: —|\.)?\s*(.*)$", I);
                if (choose.Success && (line.EndsWith("—") || choose.Groups[3].Value.Length == 0 || choose.Groups[1].Value.Length == 0))
                {
                    modeHead = choose.Groups[1].Value.Trim();
                    chooseOne = choose.Groups[2].Value.Equals("one", StringComparison.OrdinalIgnoreCase);
                    chooseCount = choose.Groups[2].Value.ToLowerInvariant() switch { "one" => 1, "two" or "one or both" => 2, "three" => 3, _ => 99 };
                    continue;
                }
                body.Add(line);
            }
            Ability Make(string text)
            {
                var a = new Ability { Kind = AbilityKind.Spell, From = Zone.Stack, Text = text, CostText = "", EffectText = text };
                if (extraCost.Length > 0)
                    ReadCosts(char.ToUpperInvariant(extraCost[0]) + extraCost[1..], a);
                ReadEffects(text, a);
                return a;
            }
            string main = string.Join(" ", body);
            if (modes.Count > 0 && chooseOne)
                foreach (var m in modes) result.Add(Make(((modeHead.Length > 0 ? modeHead + " " : "") + m + (main.Length > 0 ? " " + main : "")).Trim()));
            else if (modes.Count > 0)
                foreach (var m in modes)
                {
                    var mode = Make(((modeHead.Length > 0 ? modeHead + " " : "") + m).Trim());
                    mode.ChooseCount = chooseCount;
                    result.Add(mode);
                }
            else if (main.Length > 0)
                result.Add(Make(main));
            return result;
        }

        /// <summary>The abilities a card has now (its face that's up), mana abilities aside (the mana reader's).</summary>
        public static List<Ability> Of(GameCard c) =>
            c.FaceDown ? new List<Ability>()
                       : Read(c.ShownText, c.ShownName, c.FaceType(c.BackUp ? 1 : 0).Contains("Planeswalker", StringComparison.OrdinalIgnoreCase))
                             .Where(a => !(a.Kind == AbilityKind.Activated && a.Effects.Count > 0 && a.Effects.All(e => e.Kind == EffectKind.Mana))).ToList();
    }
}

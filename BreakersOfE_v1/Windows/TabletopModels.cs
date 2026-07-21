using BreakersOfE.Models;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Media;

namespace BreakersOfE.Windows
{
    public enum CommanderLocation
    {
        CommandZone, Battlefield, Graveyard, Exile, Hand, Library
    }

    public class LinkedExile
    {
        public string HostCardName { get; set; } = string.Empty;
        public bool HostIsYour { get; set; }
        public List<DeckCard> ExiledCards { get; set; } = new();
    }

    /// <summary>
    /// A temporary effect that lasts until end of turn.
    /// </summary>
    public class TempEffect
    {
        public string Label { get; set; } = string.Empty; // e.g. "+2/+2", "Flying"
        public int BonusPower { get; set; } = 0;      // for P/T boosts
        public int BonusToughness { get; set; } = 0;
    }

    public class BattlefieldCard
    {
        public DeckCard Card { get; set; } = null!;
        public double X { get; set; }
        public double Y { get; set; }
        public bool IsTapped { get; set; }
        public bool IsYour { get; set; }
        public bool IsLandZone { get; set; }
        public bool IsTransformed { get; set; } = false;
        public bool IsFaceDown { get; set; } = false;
        public Dictionary<string, int> Counters { get; set; } = new();
        public List<TempEffect> TempEffects { get; set; } = new();
        public System.Windows.Controls.Border? Visual { get; set; }
    }
}
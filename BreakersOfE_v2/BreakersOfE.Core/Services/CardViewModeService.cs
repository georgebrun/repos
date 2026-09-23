using System;

namespace BreakersOfE.Services
{
    public enum CardViewMode { Grid, Gallery }

    /// <summary>
    /// The Grid / Gallery switch above the left navigation. App-wide: every
    /// page that can show cards both ways (pool now, collection later) reads
    /// <see cref="Mode"/> when it opens and listens for <see cref="ModeChanged"/>.
    /// </summary>
    public static class CardViewModeService
    {
        public static CardViewMode Mode { get; private set; } = CardViewMode.Grid;

        /// <summary>Raised on the UI thread when the switch changes.</summary>
        public static event Action<CardViewMode>? ModeChanged;

        public static void Set(CardViewMode mode)
        {
            if (Mode == mode) return;
            Mode = mode;
            ModeChanged?.Invoke(mode);
        }
    }
}

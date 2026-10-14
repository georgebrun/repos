using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media;
using BreakersOfE.Models;
using BreakersOfE.Services;

namespace BreakersOfE.ViewModels
{
    /// <summary>
    /// One tile in the gallery. Wraps any pool card type (PoolCard, TokenCard,
    /// PlanarCard, …) via cached reflection, so the gallery works for every
    /// Card Pool sub-page without per-type code.
    ///
    /// The image is LAZY: nothing downloads or decodes until the tile is
    /// actually shown (the binding reads Image). Virtualization means only
    /// visible rows ever ask.
    /// </summary>
    public class GalleryItem : INotifyPropertyChanged
    {
        // Decode width for thumbnails — sharp across the whole size-slider range
        // while keeping memory per tile small.
        private const int DecodeWidth = 340;

        public object Card { get; }
        public string ScryfallId { get; }
        public string ImageUrl { get; }
        public string Name { get; }

        /// <summary>
        /// Under the picture: one column per finish the card comes in (or you
        /// have) — its price, and the count (− / + on Edit pages). Nothing is
        /// drawn on the picture itself.
        /// </summary>
        public List<FinishSlot> Slots { get; private set; } = new();

        /// <summary>MTGO / Arena tables ("mtgo" / "arena"), else null (paper).</summary>
        private readonly string? _game;

        /// <summary>Re-read the counts (after an edit on the Edit pages).</summary>
        public void RefreshCounts()
        {
            var fresh = BuildSlots(Card, _accessors.GetOrAdd(Card.GetType(), MakeAccessors), _game, this);
            // Same finishes: update in place (no flicker); else replace the columns.
            if (fresh.Count == Slots.Count && fresh.Select(f => f.Finish).SequenceEqual(Slots.Select(f => f.Finish)))
                for (int i = 0; i < fresh.Count; i++) Slots[i].Count = fresh[i].Count;
            else
            {
                Slots = fresh;
                OnPropertyChanged(nameof(Slots));
            }
        }

        /// <summary>Deck gallery: this card is the deck's commander.</summary>
        public bool IsCommander { get; private set; }

        /// <summary>Set checklist: a printing you don't own shows dimmed.</summary>
        private bool _isDimmed;
        public bool IsDimmed
        {
            get => _isDimmed;
            set { if (_isDimmed != value) { _isDimmed = value; OnPropertyChanged(); } }
        }

        // Set on every row rebuild (size slider / window width).
        public double TileWidth { get; set; }
        public double TileHeight { get; set; }
        /// <summary>Height of the price / count strip under the picture.</summary>
        public double StripHeight { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        private ImageSource? _image;
        private bool _loading;

        /// <summary>
        /// Reading this is what triggers the load — so only tiles the user can
        /// see ever download/decode.
        /// </summary>
        public ImageSource? Image
        {
            get
            {
                // "Card not found" (offline?) tries again when the tile is shown a minute later.
                if (_isNotFound && !_loading && DateTime.UtcNow - _failedAt > TimeSpan.FromMinutes(1))
                {
                    _image = null;
                    IsNotFound = false;                      // card back (no name overlay) while it tries
                }
                if (_image == null && !_loading) BeginLoad();
                return _image ?? CardPictures.CardBack;      // face down until the picture arrives
            }
        }

        /// <summary>No picture (offline, or none exists): "Scryfall Fail" shows, with the card's name on it.</summary>
        private bool _isNotFound;
        private DateTime _failedAt;
        public bool IsNotFound
        {
            get => _isNotFound;
            private set { if (_isNotFound != value) { _isNotFound = value; OnPropertyChanged(); } }
        }

        private GalleryItem(object card, string sid, string url, string name, string? game)
        {
            Card = card;
            ScryfallId = sid;
            ImageUrl = url;
            Name = name;
            _game = game;
        }

        private async void BeginLoad()
        {
            _loading = true;
            try
            {
                // Kept on disk when it's yours (or Settings keeps everything); else shown from the internet.
                var bmp = await ImageCacheService.GetPictureAsync(ScryfallId, ImageUrl, DecodeWidth);
                IsNotFound = bmp == null;
                if (bmp == null) _failedAt = DateTime.UtcNow;
                _image = bmp ?? CardPictures.NotFound;
                if (bmp != null) GalleryImageTracker.Track(this);
                OnPropertyChanged(nameof(Image));
            }
            catch (Exception ex)
            {
                // A damaged picture file (or a disk error) must not take the app down.
                System.Diagnostics.Debug.WriteLine($"Gallery picture {ScryfallId}: {ex.Message}");
                IsNotFound = true;
                _failedAt = DateTime.UtcNow;
                _image = CardPictures.NotFound;
                OnPropertyChanged(nameof(Image));
            }
            finally
            {
                _loading = false;
            }
        }

        /// <summary>
        /// Drops the decoded bitmap to cap memory. Silent (no PropertyChanged):
        /// if the tile scrolls back into view, the binding re-reads Image and it
        /// reloads — from disk when kept, else from the internet again.
        /// </summary>
        internal void ReleaseImage() => _image = null;

        // ── Factory: works for any card type via cached reflection ──────────
        private static readonly ConcurrentDictionary<Type, Accessors> _accessors = new();

        private sealed class Accessors
        {
            public PropertyInfo? Sid, Url, SmallUrl, Name, Usd, UsdFoil, UsdEtched, Tix;
            public PropertyInfo? Finish, ShownFinish, Price, PriceDisplay;         // collection-side rows
            public PropertyInfo? OwnedNonFoil, OwnedFoil, OwnedEtched, OwnedTotal; // pool / deck cards
            public PropertyInfo? IsNonFoil, IsFoil, IsEtched;                      // finishes the printing comes in
            public PropertyInfo? Quantity, FoilQuantity, EtchedQuantity;           // collection rows / deck lines
            public PropertyInfo? TotalQuantity;                                    // deck lines
            public PropertyInfo? MtgoId, MtgoFoilId;                               // online pool
        }

        private static Accessors MakeAccessors(Type t) => new Accessors
            {
                Sid = t.GetProperty("ScryfallId"),
                Url = t.GetProperty("ImageNormalUrl"),
                SmallUrl = t.GetProperty("ImageSmallUrl"),
                Name = t.GetProperty("Name"),
                Usd = t.GetProperty("PriceUsd"),
                UsdFoil = t.GetProperty("PriceUsdFoil"),
                UsdEtched = t.GetProperty("PriceUsdEtched"),
                Tix = t.GetProperty("PriceTix"),
                Finish = t.GetProperty("Finish"),
                ShownFinish = t.GetProperty("ShownFinish"),
                Price = t.GetProperty("Price"),
                PriceDisplay = t.GetProperty("PriceDisplay"),
                OwnedNonFoil = t.GetProperty("OwnedNonFoil"),
                OwnedFoil = t.GetProperty("OwnedFoil"),
                OwnedEtched = t.GetProperty("OwnedEtched"),
                OwnedTotal = t.GetProperty("OwnedTotal"),
                IsNonFoil = t.GetProperty("IsNonFoil"),
                IsFoil = t.GetProperty("IsFoil"),
                IsEtched = t.GetProperty("IsEtched"),
                Quantity = t.GetProperty("Quantity"),
                FoilQuantity = t.GetProperty("FoilQuantity"),
                EtchedQuantity = t.GetProperty("EtchedQuantity"),
                TotalQuantity = t.GetProperty("TotalQuantity"),
                MtgoId = t.GetProperty("MtgoId"),
                MtgoFoilId = t.GetProperty("MtgoFoilId"),
            };

        /// <param name="game">"mtgo" / "arena" for the online tables, else null (paper).</param>
        public static GalleryItem FromCard(object card, string? game = null)
        {
            var a = _accessors.GetOrAdd(card.GetType(), MakeAccessors);

            string sid = a.Sid?.GetValue(card) as string ?? "";
            string url = a.Url?.GetValue(card) as string ?? "";
            if (string.IsNullOrEmpty(url))
                url = a.SmallUrl?.GetValue(card) as string ?? "";
            string name = a.Name?.GetValue(card) as string ?? "";

            var item = new GalleryItem(card, sid, url, name, game)
            {
                // Deck gallery: the commander's tile is outlined in gold, like its
                // row in the grid (flag or Commander category).
                IsCommander = card is Models.DeckCard dc &&
                              (dc.IsCommander || dc.Category == Models.DeckCardCategory.Commander),
            };
            item.Slots = BuildSlots(card, a, game, item);
            return item;
        }

        private static int Int(PropertyInfo? p, object o) => p?.GetValue(o) is int i ? i : 0;
        private static bool Bool(PropertyInfo? p, object o) => p?.GetValue(o) is bool b && b;
        private static decimal? Dec(PropertyInfo? p, object o) => p?.GetValue(o) as decimal?;
        private static string Money(decimal? v) => v.HasValue ? $"${v.Value:F2}" : "—";

        /// <summary>
        /// The columns under a tile, matching what the tile stands for:
        ///   deck lines → per finish: copies in the deck, and its price;
        ///   collection, Trade Binder, Want List, online rows → one column: the
        ///     row's own finish, quantity and price (− / + work on that row);
        ///   pool cards → per finish it comes in: copies you own, and its price.
        /// A finish you have copies of always gets its column, even when the card
        /// data says it doesn't come in it.
        /// </summary>
        private static List<FinishSlot> BuildSlots(object card, Accessors a, string? game, GalleryItem owner)
        {
            var slots = new List<FinishSlot>();

            // Deck lines
            if (a.TotalQuantity != null)
            {
                int owned = Int(a.OwnedTotal, card);
                string tip = $"Copies in this deck (you own {owned})";
                void Deck(string finish, bool exists, int qty, decimal? price)
                {
                    if (exists || qty > 0)
                        slots.Add(new FinishSlot(owner, finish, qty, Money(price), tip, rowTile: false, unlisted: !exists));
                }
                bool nf = Bool(a.IsNonFoil, card), f = Bool(a.IsFoil, card), e = Bool(a.IsEtched, card);
                if (!nf && !f && !e) nf = true;
                Deck(CardFinish.NonFoil, nf, Int(a.Quantity, card), Dec(a.Usd, card));
                Deck(CardFinish.Foil, f, Int(a.FoilQuantity, card), Dec(a.UsdFoil, card));
                Deck(CardFinish.Etched, e, Int(a.EtchedQuantity, card), Dec(a.UsdEtched, card));
                return slots;
            }

            // Collection-side rows: one row = one finish
            if (a.Finish != null && a.Quantity != null)
            {
                string finish = CardFinish.Normalize(
                    a.ShownFinish?.GetValue(card) as string is { Length: > 0 } shown ? shown : a.Finish.GetValue(card) as string);
                string price = a.PriceDisplay?.GetValue(card) as string
                               ?? (a.Price != null ? Money(Dec(a.Price, card)) : "");
                // Known finishes (paper rows, filled from the pool): is the row's one of them?
                bool unlisted = false;
                if (game == null && a.IsNonFoil != null && a.IsFoil != null && a.IsEtched != null)
                {
                    bool rNf = Bool(a.IsNonFoil, card), rF = Bool(a.IsFoil, card), rE = Bool(a.IsEtched, card);
                    if (rNf || rF || rE)
                        unlisted = finish == CardFinish.Foil ? !rF : finish == CardFinish.Etched ? !rE : !rNf;
                }
                slots.Add(new FinishSlot(owner, finish, Int(a.Quantity, card), price, "Quantity", rowTile: true, unlisted));
                return slots;
            }

            // Pool cards
            int ownNf = Int(a.OwnedNonFoil, card), ownF = Int(a.OwnedFoil, card), ownE = Int(a.OwnedEtched, card);
            const string ownTip = "Copies you own";
            if (game == Models.OnlineGame.Arena)
            {
                slots.Add(new FinishSlot(owner, CardFinish.NonFoil, ownNf, "", ownTip, rowTile: false));
                return slots;
            }
            if (game == Models.OnlineGame.Mtgo)
            {
                bool hasNf = a.MtgoId?.GetValue(card) != null, hasF = a.MtgoFoilId?.GetValue(card) != null;
                if (!hasNf && !hasF) hasNf = true;
                string tix = Dec(a.Tix, card) is decimal t ? $"{t:0.##} tix" : "—";
                if (hasNf || ownNf > 0) slots.Add(new FinishSlot(owner, CardFinish.NonFoil, ownNf, tix, ownTip, rowTile: false));
                if (hasF || ownF > 0) slots.Add(new FinishSlot(owner, CardFinish.Foil, ownF, "—", ownTip, rowTile: false));
                return slots;
            }

            bool pricesKnown = a.Usd != null;              // tokens, planes … have no prices
            string P(PropertyInfo? p) => pricesKnown ? Money(Dec(p, card)) : "";
            bool cNf = Bool(a.IsNonFoil, card), cF = Bool(a.IsFoil, card), cE = Bool(a.IsEtched, card);
            if (!cNf && !cF && !cE) cNf = true;
            if (cNf || ownNf > 0) slots.Add(new FinishSlot(owner, CardFinish.NonFoil, ownNf, P(a.Usd), ownTip, rowTile: false, unlisted: !cNf));
            if (cF || ownF > 0) slots.Add(new FinishSlot(owner, CardFinish.Foil, ownF, P(a.UsdFoil), ownTip, rowTile: false, unlisted: !cF));
            if (cE || ownE > 0) slots.Add(new FinishSlot(owner, CardFinish.Etched, ownE, P(a.UsdEtched), ownTip, rowTile: false, unlisted: !cE));
            return slots;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>
    /// One column under a gallery tile: a finish, its price (one copy), and
    /// how many there are (owned, in the deck, or the row's quantity).
    /// </summary>
    public class FinishSlot : INotifyPropertyChanged
    {
        public GalleryItem Owner { get; }
        /// <summary>nonfoil / foil / etched.</summary>
        public string Finish { get; }
        /// <summary>"F" foil, "E" etched, "" non-foil.</summary>
        public string Mark => Finish == CardFinish.Foil ? "F" : Finish == CardFinish.Etched ? "E" : "";
        public bool IsGold => Mark.Length > 0;
        /// <summary>
        /// You have copies in a finish Scryfall doesn't list for this printing:
        /// usually the wrong set or number picked (amber: needs a look).
        /// </summary>
        public bool IsUnlisted { get; }
        /// <summary>One copy's price ("$1.73", "—" when there's none); empty for tables without prices.</summary>
        public string PriceText { get; }
        /// <summary>A collection-side row's tile: − / + act on that row (no finish to pick).</summary>
        public bool RowTile { get; }
        private readonly string _countTip;

        private int _count;
        public int Count
        {
            get => _count;
            set
            {
                if (_count == value) return;
                _count = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CountText));
                OnPropertyChanged(nameof(PriceTip));
            }
        }
        public string CountText => $"{Count}{Mark}";
        public string CountTip => IsUnlisted
            ? $"{_countTip}\nScryfall doesn't list this printing in {CardFinish.Display(Finish)}. Check the set and collector number on the card."
            : _countTip;

        /// <summary>"2 × $0.25 = $0.50", or "$0.25 each".</summary>
        public string PriceTip
        {
            get
            {
                if (!PriceText.StartsWith("$") || !decimal.TryParse(PriceText.TrimStart('$'),
                        System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var each))
                    return PriceText;
                return Count > 0 ? $"{Count} × {PriceText} = ${Count * each:F2}" : $"{PriceText} each";
            }
        }

        public FinishSlot(GalleryItem owner, string finish, int count, string price, string countTip, bool rowTile, bool unlisted = false)
        {
            IsUnlisted = unlisted;
            Owner = owner;
            Finish = finish;
            _count = count;
            PriceText = price ?? "";
            _countTip = countTip;
            RowTile = rowTile;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>One virtualized row of N tiles (N recalculated from width).</summary>
    public class GalleryRow
    {
        public List<GalleryItem> Items { get; }
        public GalleryRow(List<GalleryItem> items) => Items = items;
    }

    /// <summary>
    /// Caps how many decoded thumbnails stay in memory. Scrolling through
    /// thousands of cards would otherwise keep every bitmap alive. Oldest
    /// loaded images are released first; they reload from disk if seen again.
    /// </summary>
    internal static class GalleryImageTracker
    {
        private const int MaxLoaded = 600;
        private static readonly Queue<GalleryItem> _loaded = new();

        public static void Track(GalleryItem item)
        {
            _loaded.Enqueue(item);
            while (_loaded.Count > MaxLoaded)
            {
                var old = _loaded.Dequeue();
                if (!old.IsSelected) old.ReleaseImage();
            }
        }
    }
}
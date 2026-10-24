using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using BreakersOfE.Services;
using Wpf.Ui.Controls;

namespace BreakersOfE.Views.Dialogs
{
    /// <summary>
    /// What can I build?: your commanders and partner pairs (or, for 60-card,
    /// color combinations) ranked by how much of a deck your free cards make
    /// (DeckBuildService.WhatCanIBuild / WhatCanIBuild60). <see cref="Chosen"/>
    /// is the one to build (Build This One or double-click).
    /// </summary>
    public partial class WhatCanIBuildWindow : FluentWindow
    {
        private CancellationTokenSource? _cts;
        /// <summary>60-card: the format's legality key (null: Commander).</summary>
        private readonly string? _format;

        /// <summary>The idea picked (Build This One), or null.</summary>
        public BuildIdea? Chosen { get; private set; }

        /// <param name="format">60-card: the format (key and name); null for Commander.</param>
        public WhatCanIBuildWindow(Window? owner, (string Key, string Name)? format = null)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;
            _format = format?.Key;
            if (format is { } f)
            {
                // 60-card: color combinations instead of commanders.
                WindowTitleBar.Title = TitleText.Text = $"What Can I Build? — 60-card {f.Name}";
                HintText.Text = $"Color combinations, best first, from your free cards legal in {f.Name}: spell copies (up to 4 each) against the deck's spell slots, " +
                                "how far they go toward the guide's roles, playsets, and the creature type most of them share (a tribe to build around). " +
                                "Only your collection is read. Build This One ticks those colors.";
                InUseCheck.Visibility = Visibility.Collapsed;
                NameColumn.Header = "Colors";
                CopyColumn.Visibility = Visibility.Collapsed;
                SpellsColumn.Header = "Spell copies";
                LandsColumn.Header = "Dual lands";
                NotesColumn.Header = "Playsets · tribe";
                NotesColumn.Width = new System.Windows.Controls.DataGridLength(260);
            }
            KeyDown += (_, e) => { if (e.Key == Key.F1) HelpWindow.Open("build"); };
            Loaded += async (_, _) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            _cts?.Cancel();
            var cts = _cts = new CancellationTokenSource();
            bool inUse = InUseCheck.IsChecked == true;
            BusyBar.Visibility = BtnStop.Visibility = Visibility.Visible;
            InUseCheck.IsEnabled = false;
            var progress = new Progress<string>(s => { if (!cts.IsCancellationRequested) StatusText.Text = s; });
            try
            {
                string? format = _format;
                var ideas = await Task.Run(() => format != null
                    ? DeckBuildService.WhatCanIBuild60(format, cts.Token, progress)
                    : DeckBuildService.WhatCanIBuild(inUse, cts.Token, progress), cts.Token);
                IdeasGrid.ItemsSource = ideas;
                StatusText.Text = ideas.Count == 0
                    ? (inUse ? "You don't own any commanders." : "No commanders with a free copy. Tick the box above to include ones in decks.")
                    : format != null ? "Pick one and Build This One (or double-click it)."
                    : $"{ideas.Count:N0} commander{(ideas.Count == 1 ? "" : "s")} and pairs. Pick one and Build This One (or double-click it).";
                if (ideas.Count > 0) IdeasGrid.SelectedIndex = 0;
            }
            catch (OperationCanceledException)
            {
                if (cts == _cts) StatusText.Text = "Stopped.";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Couldn't check your commanders: {ex.Message}";
            }
            finally
            {
                if (cts == _cts)
                {
                    BusyBar.Visibility = BtnStop.Visibility = Visibility.Collapsed;
                    InUseCheck.IsEnabled = true;
                }
            }
        }

        private async void InUseCheck_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private void Stop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        private void IdeasGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
            BtnUse.IsEnabled = IdeasGrid.SelectedItem is BuildIdea;

        private void IdeasGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (IdeasGrid.SelectedItem is BuildIdea) Use_Click(sender, e);
        }

        private void Use_Click(object sender, RoutedEventArgs e)
        {
            if (IdeasGrid.SelectedItem is not BuildIdea idea) return;
            Chosen = idea;
            DialogResult = true;
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts?.Cancel();
            base.OnClosed(e);
        }
    }
}

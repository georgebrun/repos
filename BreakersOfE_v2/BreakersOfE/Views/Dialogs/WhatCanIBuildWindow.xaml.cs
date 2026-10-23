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
    /// What can I build?: your commanders ranked by how much of a deck your
    /// free cards make for them (DeckBuildService.WhatCanIBuild). <see cref="ChosenName"/>
    /// is the one to build (Build This One or double-click).
    /// </summary>
    public partial class WhatCanIBuildWindow : FluentWindow
    {
        private CancellationTokenSource? _cts;

        public string? ChosenName { get; private set; }

        public WhatCanIBuildWindow(Window? owner)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;
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
                var ideas = await Task.Run(() => DeckBuildService.WhatCanIBuild(inUse, cts.Token, progress), cts.Token);
                IdeasGrid.ItemsSource = ideas;
                StatusText.Text = ideas.Count == 0
                    ? (inUse ? "You don't own any commanders." : "No commanders with a free copy. Tick the box above to include ones in decks.")
                    : $"{ideas.Count:N0} commander{(ideas.Count == 1 ? "" : "s")}. Pick one and Build This One (or double-click it).";
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
            ChosenName = idea.Name;
            DialogResult = true;
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts?.Cancel();
            base.OnClosed(e);
        }
    }
}

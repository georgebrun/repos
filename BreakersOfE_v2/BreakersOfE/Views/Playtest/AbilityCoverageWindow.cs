using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BreakersOfE.Data;
using BreakersOfE.Playtest;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Views.Playtest
{
    /// <summary>
    /// ⚙ Table → Coverage…: the ability reader run over the whole card pool
    /// (one card per Oracle card): how many activated abilities it reads in
    /// full, in part, or not at all, and the wordings it can't read yet, most
    /// common first (Copy puts the report on the clipboard).
    /// </summary>
    internal sealed class AbilityCoverageWindow : Window
    {
        private readonly TextBlock _summary = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) };
        private readonly TextBox _report = new()
        {
            IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 12, TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 440,
        };
        private readonly Button _copy = new() { Content = "Copy", Padding = new Thickness(16, 4, 16, 4), IsEnabled = false, Margin = new Thickness(0, 0, 8, 0) };

        internal AbilityCoverageWindow(Window owner)
        {
            Owner = owner;
            Title = "Ability Coverage";
            Width = 900;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
            SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

            var root = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
            root.Children.Add(new TextBlock { Text = "Ability Coverage", FontSize = 18, FontWeight = FontWeights.SemiBold });
            var hint = new TextBlock
            {
                Text = "Every activated ability, instant and sorcery in the card pool (every card, one of each; not just the ones you own), read the way the table reads them. " +
                       "\"Read in full\": the table does all of it. \"In part\": it does some and logs the rest. " +
                       "Below: the wordings it can't read yet, most common first (numbers as N, mana as {M}, the card's name as ~).",
                FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            root.Children.Add(hint);
            _summary.Text = "Reading the card pool…";
            root.Children.Add(_summary);
            root.Children.Add(_report);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            _copy.Click += (_, _) =>
            {
                try { Clipboard.SetText(_summary.Text + Environment.NewLine + Environment.NewLine + _report.Text); _copy.Content = "Copied"; }
                catch { _copy.Content = "Couldn't copy"; }
            };
            buttons.Children.Add(_copy);
            var close = new Button { Content = "Close", Padding = new Thickness(16, 4, 16, 4), IsCancel = true };
            close.Click += (_, _) => Close();
            buttons.Children.Add(close);
            root.Children.Add(buttons);
            Content = root;
            Loaded += async (_, _) => await Run();
        }

        private async Task Run()
        {
            try
            {
                var cov = await Task.Run(() =>
                {
                    using var db = new AppDbContext();
                    var cards = db.PoolCards.AsNoTracking()
                                  .Select(p => new { p.OracleId, p.Name, p.TypeLine, p.OracleText })
                                  .ToList()
                                  .GroupBy(p => string.IsNullOrEmpty(p.OracleId) ? p.Name : p.OracleId)
                                  .Select(g => g.First())
                                  .Select(p => (p.Name ?? "", p.TypeLine ?? "", p.OracleText ?? ""));
                    return Abilities.Measure(cards, 80);
                });
                double Pct(int n) => cov.Abilities == 0 ? 0 : 100.0 * n / cov.Abilities;
                int none = cov.Abilities - cov.Understood - cov.Partly;
                _summary.Text = $"{cov.Cards:N0} cards; {cov.CardsWithAbilities:N0} have something to read: {cov.Abilities:N0} in all " +
                                $"({cov.Abilities - cov.Spells:N0} activated abilities, mana abilities aside; {cov.Spells:N0} instants and sorceries, each mode counted).\n" +
                                $"Read in full: {cov.Understood:N0} ({Pct(cov.Understood):0.0}%)   In part: {cov.Partly:N0} ({Pct(cov.Partly):0.0}%)   " +
                                $"Not at all: {none:N0} ({Pct(none):0.0}%)";
                var sb = new StringBuilder();
                sb.AppendLine("EFFECTS NOT READ YET (count · wording · an example)");
                foreach (var (wording, count, example) in cov.Missing)
                    sb.AppendLine($"{count,6} · {wording}{Environment.NewLine}         e.g. {example}");
                sb.AppendLine();
                sb.AppendLine("COSTS NOT READ YET (yours to pay)");
                foreach (var (wording, count, example) in cov.MissingCosts)
                    sb.AppendLine($"{count,6} · {wording}{Environment.NewLine}         e.g. {example}");
                _report.Text = sb.ToString();
                _copy.IsEnabled = true;
            }
            catch (Exception ex)
            {
                _summary.Text = "Couldn't read the card pool: " + ex.Message;
            }
        }
    }
}

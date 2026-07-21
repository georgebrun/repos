using BreakersOfE.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace BreakersOfE.Windows
{
    public partial class DeckImportReportWindow : Window
    {
        public DeckImportReportWindow(List<DeckImportReportRow> report)
        {
            InitializeComponent();
            ReportGrid.ItemsSource = report;

            int total = report.Count;
            int newRows = report.Count(r => r.Status == "New Row");
            int merged = report.Count(r => r.Status == "Merged");
            int totalNF = report.Sum(r => r.NonFoilAdded);
            int totalF = report.Sum(r => r.FoilAdded);

            LblHeader.Text =
                $"Import complete — {total} card type{(total == 1 ? "" : "s")} processed";

            LblSummary.Text =
                $"New collection rows: {newRows}   " +
                $"Merged into existing: {merged}   " +
                $"Non-Foil added: {totalNF}   " +
                $"Foil added: {totalF}";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
            => Close();
    }
}
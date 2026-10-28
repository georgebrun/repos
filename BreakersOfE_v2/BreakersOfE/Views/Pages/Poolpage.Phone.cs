using System;
using System.Windows;

namespace BreakersOfE.Views.Pages
{
    // View → Trade Binder and View → Want List: the 📱 Phone… button and the
    // Phone Lists window offered once (the Edit pages have their own).
    // Part of PoolPage (same class).
    public partial class PoolPage
    {
        /// <summary>View → Trade Binder or Want List (not the tables inside Edit pages).</summary>
        private bool IsPhoneListTable => _layoutPrefix.Length == 0 && _currentTag is "TradeBinder" or "WantList";

        private void BtnPhone_Click(object sender, RoutedEventArgs e) =>
            Dialogs.PhoneListsWindow.Open(Window.GetWindow(this));

        /// <summary>Once, while Phone lists are off: show the window when the page has opened.</summary>
        private void OfferPhoneLists() =>
            Dispatcher.BeginInvoke(new Action(() => { if (IsLoaded && IsPhoneListTable) Dialogs.PhoneListsWindow.OfferOnce(Window.GetWindow(this)); }),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
}

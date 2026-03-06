using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Sigil.Models;

namespace Sigil.Views;

public partial class SteamImportDialog : Window
{
    public SteamImportDialog(IEnumerable<SteamAccount> accounts)
    {
        InitializeComponent();
        AccountsList.ItemsSource = accounts.ToList();
        AccountsList.SelectedIndex = 0;
    }

    public SteamAccount? SelectedAccount => DialogResult == true ? AccountsList.SelectedItem as SteamAccount : null;

    private void OnImport(object sender, RoutedEventArgs e)
    {
        if (AccountsList.SelectedItem == null)
        {
            MessageBox.Show("Select a Steam account to import.", "Sigil");
            return;
        }

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

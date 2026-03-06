using System.Windows;
using Sigil.Models;

namespace Sigil.Views;

public partial class AddAccountDialog : Window
{
    public AddAccountDialog()
    {
        InitializeComponent();
    }

    public AccountProvider? SelectedProvider { get; private set; }

    private void OnJagex(object sender, RoutedEventArgs e)
    {
        SelectedProvider = AccountProvider.Jagex;
        DialogResult = true;
        Close();
    }

    private void OnSteam(object sender, RoutedEventArgs e)
    {
        SelectedProvider = AccountProvider.Steam;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

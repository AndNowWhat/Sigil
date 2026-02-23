using System.Windows;
using System.Windows.Controls;
using Sigil.Models;

namespace Sigil.Views;

public partial class ProxyDialog : Window
{
    public ProxyConfig Result { get; private set; }

    public ProxyDialog(ProxyConfig existing)
    {
        InitializeComponent();
        Result = existing;

        EnabledCheck.IsChecked = existing.Enabled;
        HostBox.Text = existing.Host;
        PortBox.Text = existing.Port.ToString();
        UsernameBox.Text = existing.Username ?? string.Empty;
        PasswordBox.Password = existing.Password ?? string.Empty;

        // Select type combo
        TypeCombo.SelectedIndex = existing.Type == ProxyType.Http ? 1 : 0;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var type = ProxyType.Socks5;
        if (TypeCombo.SelectedItem is ComboBoxItem item && item.Tag?.ToString() == "Http")
            type = ProxyType.Http;

        if (!int.TryParse(PortBox.Text.Trim(), out var port))
            port = 1080;

        Result = new ProxyConfig
        {
            Enabled = EnabledCheck.IsChecked == true,
            Host = HostBox.Text.Trim(),
            Port = port,
            Type = type,
            Username = string.IsNullOrWhiteSpace(UsernameBox.Text) ? null : UsernameBox.Text.Trim(),
            Password = string.IsNullOrWhiteSpace(PasswordBox.Password) ? null : PasswordBox.Password
        };

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

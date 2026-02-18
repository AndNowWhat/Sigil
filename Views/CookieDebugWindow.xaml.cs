using System.Windows;

namespace Sigil.Views;

public partial class CookieDebugWindow : Window
{
    public CookieDebugWindow(string log)
    {
        InitializeComponent();
        LogBox.Text = log;
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(LogBox.Text);
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

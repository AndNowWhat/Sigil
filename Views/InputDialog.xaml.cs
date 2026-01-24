using System.Windows;

namespace Sigil.Views;

public partial class InputDialog : Window
{
    public InputDialog(string prompt, string title)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputBox.Focus();
    }

    public string? Response => DialogResult == true ? InputBox.Text : null;

    private void OnOk(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

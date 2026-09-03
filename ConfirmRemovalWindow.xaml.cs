using System.Windows;

namespace AniTV;

public partial class ConfirmRemovalWindow : Window
{
    public ConfirmRemovalWindow(string title) { InitializeComponent(); AnimeName.Text = title; Loaded += (_, _) => CancelButton.Focus(); }
    void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}

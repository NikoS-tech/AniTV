using System.Windows;

namespace AniTV;

public partial class ConfirmDownloadDeleteWindow : Window
{
    public ConfirmDownloadDeleteWindow(string name) { InitializeComponent(); EpisodeName.Text=name; Loaded+=(_,_)=>CancelButton.Focus(); }
    void Confirm_Click(object sender,RoutedEventArgs e) => DialogResult=true;
}

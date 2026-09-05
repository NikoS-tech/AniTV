using AniTV;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

internal static class EpisodeSelectionChecks
{
    public static void Run()
    {
        var episodes = Enumerable.Range(1, 3).Select(n => new VostEpisode($"{n} серия", new Uri($"https://example.org/{n}"), new Uri($"https://example.org/{n}"), null)).ToList();
        var box = new ComboBox { ItemsSource = episodes, DisplayMemberPath = "DisplayName", IsSynchronizedWithCurrentItem = false };
        box.SelectedIndex = 0;
        episodes[0].IsWatched = true;
        episodes[0].PropertyChanged += (_, _) => { };
        box.SelectedIndex = 1;
        if (!ReferenceEquals(box.SelectedItem, episodes[1])) throw new Exception("WPF selection did not move after watched mutation");
        episodes[1].IsDownloaded = true;
        box.SelectedIndex = 2;
        box.SelectedIndex = 0;
        if (!ReferenceEquals(box.SelectedItem, episodes[0])) throw new Exception("WPF selection failed on return to previous episode");
        box.SelectedItem = episodes[2];
        if(box.SelectedIndex != 2) throw new Exception("WPF manual item selection failed");
        Console.WriteLine("PASS: WPF episode selection survives watched/downloaded mutations and return navigation");
    }
}

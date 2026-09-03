using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AniTV;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var app = new App(); app.InitializeComponent();
        var window = new MainWindow();
        if (args.Contains("--live-hls"))
        {
            using var vlc = new LibVLCSharp.Shared.LibVLC("--vout=dummy", "--aout=dummy", "--network-caching=1500");
            using var player = new LibVLCSharp.Shared.MediaPlayer(vlc);
            var source = new AnimeSource { Provider="best", Id="2296", PageUrl="https://anime1.best/anime1-online/2296-krestjanin-devjatsot-devjanosto-devjatogo-urovnja-1-sezon-z10.html" };
            var episodes = new AnimeBestProvider().GetEpisodesAsync(source).GetAwaiter().GetResult();
            using var media = new LibVLCSharp.Shared.Media(vlc, episodes[0].StandardUrl);
            media.AddOption(":http-referrer=https://anime1.best/");
            player.Play(media);
            var timeout = DateTime.UtcNow.AddSeconds(35);
            while (player.Time < 1000 && DateTime.UtcNow < timeout) Thread.Sleep(200);
            if (player.Time < 1000 || player.Length <= 0) throw new Exception("HLS playback did not start");
            Console.WriteLine($"PASS: HLS playback clock {player.Time} / {player.Length}");
            player.Time = 60000;
            timeout=DateTime.UtcNow.AddSeconds(15);
            while (player.Time < 55000 && DateTime.UtcNow < timeout) Thread.Sleep(200);
            if (player.Time < 55000) throw new Exception("HLS seek failed");
            Console.WriteLine("PASS: HLS seeks to 60 seconds");
            player.Stop();
        }
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS: " + name); }
        var border = (Border)Field("PlayerVideoBorder")!;
        var detailCover = (Image)Field("DetailCover")!;
        Check(detailCover.Stretch == Stretch.Uniform && ((Border)detailCover.Parent).Height == 345 && ((Border)detailCover.Parent).VerticalAlignment == VerticalAlignment.Top,
            "Preview poster keeps its whole aspect ratio and does not stretch the dialog height");
        var detailPanel = (Border)Field("DetailPanel")!;
        var detailSidebar = (StackPanel)Field("DetailSidebar")!;
        Check(Grid.GetColumn((UIElement)Field("FavoriteButton")!) == 0 && Grid.GetColumn((UIElement)Field("WatchButton")!) == 1 && Grid.GetColumn((UIElement)Field("DetailProgress")!) == 2, "Preview actions are favorite, watch, then checkpoint");
        Check(Grid.GetRow((UIElement)Field("DetailDescriptionScroll")!) == 1, "Description scroll is below the fixed action header");
        Check(detailSidebar.Children.Contains((UIElement)Field("DetailMeta")!) && detailSidebar.Children.Contains((UIElement)Field("DetailGenres")!), "Metadata and genre chips are below the poster");
        Check(Field("DetailGenres") is ItemsControl { ItemTemplate: not null }, "Genres use chip item templates");
        detailCover.Source = new DrawingImage(new GeometryDrawing(Brushes.Purple,null,new RectangleGeometry(new Rect(0,0,1000,2500))));
        ((TextBlock)Field("DetailTitle")!).Text = "Короткое название";
        detailPanel.Measure(new Size(900,700));
        Check(detailPanel.DesiredSize.Height < 500,"Short preview shrinks instead of filling its maximum height");
        Check(border.Child is null, "VideoView starts detached from catalog");
        for (var i = 0; i < 3; i++)
        {
            Call("AttachVideoSurface");
            Check(border.Child is not null, "Video surface attaches");
            var surface = border.Child;
            var overlay = ((FrameworkElement)Field("VideoOverlayRoot")!).Parent;
            Call("AttachVideoSurface");
            Check(ReferenceEquals(surface, border.Child) && ReferenceEquals(overlay, ((FrameworkElement)Field("VideoOverlayRoot")!).Parent), "Episode switch preserves native surface and overlay parent");
            Call("StopPlayerSession");
            Check(border.Child is null && Field("currentMedia") is null && Field("activeEpisode") is null, "Closing detaches view and clears playback");
            Call("ToggleFullscreen");
            Check(!(bool)Field("isFullscreen")!, "Fullscreen cannot open in catalog");
            Call("PlayPause_Click", window, new RoutedEventArgs());
            Check(!(bool)Field("isPlaying")!, "Catalog clicks cannot restart playback");
        }
        var spinner = new ContentControl { Style = (Style)app.FindResource("LoadingSpinner") };
        var scrollBar = new System.Windows.Controls.Primitives.ScrollBar {
            Style = (Style)app.FindResource(typeof(System.Windows.Controls.Primitives.ScrollBar)),
            Orientation = Orientation.Vertical
        };
        scrollBar.ApplyTemplate();
        var scrollTrack = (System.Windows.Controls.Primitives.Track)scrollBar.Template.FindName("PART_Track", scrollBar);
        Check(scrollTrack.IsDirectionReversed, "Vertical scrollbar increases from top to bottom");
        scrollBar.Orientation = Orientation.Horizontal;
        Check(!scrollTrack.IsDirectionReversed, "Horizontal scrollbar increases from left to right");
        Check(spinner.ApplyTemplate(), "Animated loader template instantiates");
        Check(((SolidColorBrush)((Grid)Field("PlayerLoading")!).Background).Color.A == 255, "Loading backdrop is opaque during output rebuild");
        Check(window.WindowStyle == WindowStyle.None, "Main window uses custom caption");
        Call("ApplyFullscreenShell", true);
        Check(((RowDefinition)Field("DesktopCaptionRow")!).Height.Value == 0, "Fullscreen hides desktop caption");
        Call("ApplyFullscreenShell", false);
        Check(((RowDefinition)Field("DesktopCaptionRow")!).Height.Value == 42, "Windowed mode restores caption");
        var dialog = new ConfirmRemovalWindow("Тайтл с длинным названием для проверки подтверждения");
        Check(dialog.WindowStyle == WindowStyle.None && ((Button)dialog.FindName("CancelButton")).IsDefault && ((Button)dialog.FindName("CancelButton")).IsCancel, "Styled dialog preserves safe default and Escape cancellation");
        dialog.Close();
        Check(window.Icon is not null, "Application window has the AniTV icon");
        Check(window.Icon is System.Windows.Media.Imaging.BitmapSource { PixelWidth: 256, PixelHeight: 256 }, "Taskbar receives a 256px image rather than the ICO's first 16px frame");
        var testAnime = new Anime { Id = -1, Title = "Test" };
        var testProgress = (WatchProgress)Call("ProgressFor", testAnime)!;
        testProgress.Record("ep2", "2 серия", 2, 42, 1200);
        Call("Decorate", testAnime);
        Check(testAnime.ProgressLabel.Length > 0, "Started title displays progress");
        testProgress.HiddenFromContinue = true;
        Call("Decorate", testAnime);
        Check(testAnime.ProgressLabel == "" && testProgress.PositionSeconds == 42, "Removed title hides catalog progress without deleting checkpoint");
        testProgress.Record("ep2", "2 серия", 2, 43, 1200);
        Call("Decorate", testAnime);
        Check(testAnime.ProgressLabel.Length > 0, "Resuming restores visible progress");
        window.Close();
        app.Shutdown();
        Console.WriteLine("UI lifecycle smoke checks passed.");
    }
}

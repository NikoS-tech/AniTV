using System.Windows;
using System.Windows.Input;

namespace AniTV;

public partial class MainWindow
{
    FrameworkElement videoOverlayContent = null!;
    bool playerOpen;
    bool windowClosing;
    int playbackGeneration;
    CancellationTokenSource playbackCancellation = new();
    bool PlayerActive => playerOpen && !windowClosing && activeEpisode is not null;

    void DispatchPlayback(Action action)
    {
        var generation = System.Threading.Volatile.Read(ref playbackGeneration);
        if (Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (PlayerActive && !changingSource && generation == playbackGeneration) action();
        });
    }

    void AttachVideoSurface()
    {
        playbackCancellation.Cancel(); playbackCancellation.Dispose(); playbackCancellation = new();
        // Switching episodes reuses the existing native surface and overlay window.
        // Reassigning Content here reparents the overlay while VLC rebuilds its output.
        if (playerOpen) return;
        playerOpen = true;
        if (PlayerVideoBorder.Child is null) PlayerVideoBorder.Child = VideoPlayer;
        VideoPlayer.Content = videoOverlayContent;
        videoOverlayContent.IsHitTestVisible = true;
        VideoPlayer.Visibility = Visibility.Visible;
        VideoPlayer.IsHitTestVisible = true;
        VideoPlayer.MediaPlayer = mediaPlayer;
    }

    void StopPlayerSession()
    {
        mediaRequest?.Cancel();
        CaptureProgress();
        if (isFullscreen) ToggleFullscreen();
        playerOpen = false;
        playbackCancellation.Cancel();
        playbackGeneration++;
        seekVersion++;
        isSeeking = false;
        seekTarget = -1;
        activeSeekSlider = null;
        playbackTimer.Stop(); controlsTimer.Stop();
        Mouse.Capture(null); Mouse.OverrideCursor = null;
        FullscreenChrome.Visibility = Visibility.Collapsed;
        PlayerLoading.Visibility = Visibility.Collapsed;
        videoOverlayContent.IsHitTestVisible = false;
        VideoPlayer.IsHitTestVisible = false;
        VideoPlayer.Content = null;
        VideoPlayer.Visibility = Visibility.Collapsed;
        VideoPlayer.MediaPlayer = null;
        // Unload VideoView as well as its separate transparent WPF overlay window.
        PlayerVideoBorder.Child = null;
        PlayerOverlay.Visibility = Visibility.Collapsed;
        DetailsOverlay.Visibility = Visibility.Collapsed;
        mediaPlayer.Stop();
        mediaPlayer.Media = null;
        currentMedia?.Dispose(); currentMedia = null;
        activeAnime = null; activeEpisode = null;
        isPlaying = false; videoStarted = false;
        syncingSelectors = true;
        EpisodeBox.ItemsSource = FullscreenEpisodeBox.ItemsSource = null;
        syncingSelectors = false;
        episodes = [];
        SearchBox.Focus();
    }
}

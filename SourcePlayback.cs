using System.Windows;
using System.Windows.Controls;

namespace AniTV;

public partial class MainWindow
{
    AnimeSource? playbackSource;
    string activeSourceKey = "";
    CancellationTokenSource? mediaRequest;

    async Task<IReadOnlyList<VostEpisode>> FetchInitialEpisodes(Anime anime, CancellationToken token)
    {
        var preferred = ProgressFor(anime).LastSourceKey;
        Exception? failure = null;
        foreach (var source in anime.Sources.Where(s => string.IsNullOrEmpty(preferred) || s.Key == preferred))
        {
            try
            {
                var list = await FetchEpisodes(anime, source, token);
                token.ThrowIfCancellationRequested();
                if (list.Count == 0) continue;
                playbackSource = source; return list;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) { failure = ex; }
        }
        if (failure is not null) throw new InvalidOperationException("Доступные источники не ответили.", failure);
        return [];
    }

    async Task<IReadOnlyList<VostEpisode>> FetchEpisodes(Anime anime, AnimeSource source, CancellationToken token)
    {
        var list = source.Provider == "best" ? await best.GetEpisodesAsync(source, token) : await vost.GetEpisodesAsync(source.Id, token);
        if (source.Provider == "vost") SourceMatching.MigrateEpisodeKeys(ProgressFor(anime), list);
        source.Available = list.Count;
        ProgressFor(anime).ObserveSource(source, list.Select(e => e.Key));
        MarkDownloaded(anime,list);
        return list;
    }
    void UpdateSourceSelectors()
    {
        syncingSelectors = true;
        SourceBox.ItemsSource = FullscreenSourceBox.ItemsSource = selected?.Sources;
        SourceBox.SelectedItem = FullscreenSourceBox.SelectedItem = playbackSource;
        SourceBox.Visibility = FullscreenSourceBox.Visibility = selected?.Sources.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        syncingSelectors = false;
    }
    async Task PrepareEpisodeAsync(VostEpisode episode, double resume = 0)
    {
        mediaRequest?.Cancel(); mediaRequest?.Dispose(); mediaRequest = new();
        var token = mediaRequest.Token;
        CaptureProgress(); changingSource = true; playbackTimer.Stop();
        if (isPlaying) mediaPlayer.Pause();
        QualityBox.IsEnabled = FullscreenQualityBox.IsEnabled = false;
        PlayerLoading.Visibility = Visibility.Visible; PlayerLoadingText.Text = "Загружаем качества видео…";
        try
        {
            var qualities = await best.GetQualitiesAsync(episode, token);
            token.ThrowIfCancellationRequested();
            if (PlayerOverlay.Visibility != Visibility.Visible || windowClosing) return;
            syncingSelectors = true;
            QualityBox.ItemsSource = FullscreenQualityBox.ItemsSource = qualities;
            var quality = PlaybackChoice.Maximum(qualities);
            QualityBox.SelectedItem = FullscreenQualityBox.SelectedItem = quality;
            syncingSelectors = false;
            activeSourceKey = playbackSource?.Key ?? "";
            PlayEpisode(episode, resume);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            PlayerLoadingText.Text = "Видео недоступно: " + ex.Message;
            isPlaying = false; PlayPauseButton.Content = FullscreenPlayPauseButton.Content = "▶";
            syncingSelectors = true;
            EpisodeBox.SelectedItem = FullscreenEpisodeBox.SelectedItem = activeEpisode;
            syncingSelectors = false;
            selectedEpisode = activeEpisode;
        }
        finally
        {
            if (!token.IsCancellationRequested) { changingSource = false; QualityBox.IsEnabled = FullscreenQualityBox.IsEnabled = true; }
        }
    }
    async void SourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (syncingSelectors || sender is not ComboBox { SelectedItem: AnimeSource source } || selected is null || source.Key == playbackSource?.Key) return;
        CaptureProgress();
        mediaRequest?.Cancel(); mediaRequest?.Dispose(); mediaRequest = new();
        var token = mediaRequest.Token;
        var anime = selected;
        var previousSource = playbackSource;
        var key = activeEpisode?.Key;
        changingSource = true; playbackTimer.Stop(); if (isPlaying) mediaPlayer.Pause();
        PlayerLoading.Visibility = Visibility.Visible; PlayerLoadingText.Text = "Загружаем источник…";
        SourceBox.IsEnabled = FullscreenSourceBox.IsEnabled = EpisodeBox.IsEnabled = FullscreenEpisodeBox.IsEnabled = QualityBox.IsEnabled = FullscreenQualityBox.IsEnabled = false;
        try
        {
            var list = await FetchEpisodes(anime, source, token);
            token.ThrowIfCancellationRequested();
            if (selected != anime || PlayerOverlay.Visibility != Visibility.Visible) return;
            if (list.Count == 0) throw new InvalidOperationException("На этом источнике нет прямых HLS-серий.");
            var index = list.ToList().FindIndex(ep => ep.Key == key);
            if (index < 0) throw new InvalidOperationException("Текущая серия отсутствует на выбранном источнике.");
            episodes = list; playbackSource = source; ObserveEpisodes(anime, list);
            foreach (var ep in list) ep.IsWatched = ProgressFor(anime).Watched.Contains(ep.Key);
            syncingSelectors = true;
            EpisodeBox.ItemsSource = FullscreenEpisodeBox.ItemsSource = episodes;
            syncingSelectors = false;
            selectedEpisode = episodes[index];
            SyncEpisodeSelectors(selectedEpisode);
            UpdateSourceSelectors(); ProviderText.Text = source.Name;
            // Do not transfer timestamps between differently edited releases.
            var resume = ProgressFor(anime).SourcePositions.GetValueOrDefault(source.Key + "/" + episodes[index].Key);
            await PrepareEpisodeAsync(episodes[index], resume);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            playbackSource = previousSource; UpdateSourceSelectors();
            PlayerLoadingText.Text = ex.Message; isPlaying = false; PlayPauseButton.Content = FullscreenPlayPauseButton.Content = "▶";
        }
        finally
        {
            SourceBox.IsEnabled = FullscreenSourceBox.IsEnabled = EpisodeBox.IsEnabled = FullscreenEpisodeBox.IsEnabled = QualityBox.IsEnabled = FullscreenQualityBox.IsEnabled = true;
            changingSource = false;
        }
    }
}

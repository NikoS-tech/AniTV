using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace AniTV;

public partial class MainWindow : Window
{
    readonly AnimeVostProvider vost = new();
    readonly AnimeBestProvider best = new();
    MultiSourceCatalog? mergedCatalog;
    readonly ObservableCollection<Anime> items = [];
    readonly ObservableCollection<GenreFilterItem> genreFilters = [];
    GenreDefinition? activeGenre;
    readonly string statePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniTV", "state.json");
    UserState state = new(); Anime? selected; IReadOnlyList<VostEpisode> episodes = []; VostEpisode? selectedEpisode; bool isPlaying; bool changingSource; bool syncingSelectors; bool syncingVolume; bool videoStarted; bool isSeeking; long seekTarget = -1; int seekVersion; DateTime seekStartedAt; Slider? activeSeekSlider; readonly DispatcherTimer playbackTimer = new() { Interval = TimeSpan.FromMilliseconds(500) }; readonly DispatcherTimer controlsTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    readonly LibVLC libVlc; readonly MediaPlayer mediaPlayer; Media? currentMedia;
    WindowState stateBeforeFullscreen; WindowStyle styleBeforeFullscreen; ResizeMode resizeBeforeFullscreen; Rect boundsBeforeFullscreen; bool topmostBeforeFullscreen; bool isFullscreen;
    public MainWindow()
    {
        var vlcPath = Path.Combine(AppContext.BaseDirectory, "libvlc", Environment.Is64BitProcess ? "win-x64" : "win-x86");
        Core.Initialize(vlcPath); InitializeComponent();
        SourceInitialized += (_, _) => InstallWindowSizingHook();
        // BitmapImage(ICO) selects its first 16px frame; use a high-resolution
        // window image independently of the multi-size Explorer/shortcut icon.
        var windowIcon = new BitmapImage();
        windowIcon.BeginInit();
        windowIcon.UriSource = new Uri("pack://application:,,,/AniTV;component/Assets/anitv-taskbar.png");
        windowIcon.DecodePixelWidth = 256;
        windowIcon.CacheOption = BitmapCacheOption.OnLoad;
        windowIcon.EndInit();
        windowIcon.Freeze();
        Icon = windowIcon;
        libVlc = new LibVLC("--network-caching=1500", "--http-referrer=https://v13.vost.pw/", "--http-user-agent=AniTV/1.0");
        mediaPlayer = new MediaPlayer(libVlc);
        videoOverlayContent = VideoOverlayRoot;
        VideoPlayer.Content = null; VideoPlayer.Visibility = Visibility.Collapsed; PlayerVideoBorder.Child = null;
        mediaPlayer.Playing += (_, _) => DispatchPlayback(() => { videoStarted = true; isPlaying = true; PlayPauseButton.Content = FullscreenPlayPauseButton.Content = "Ⅱ"; playbackTimer.Start(); CaptureProgress(); });
        mediaPlayer.Buffering += (_, e) => DispatchPlayback(() => { if (!videoStarted && e.Cache < 100) { PlayerLoading.Visibility = Visibility.Visible; PlayerLoadingText.Text = "Буферизация…"; } });
        mediaPlayer.TimeChanged += (_, e) => DispatchPlayback(() => { if (e.Time > 0) { videoStarted = true; if (!isSeeking) PlayerLoading.Visibility = Visibility.Collapsed; else if (Math.Abs(e.Time - seekTarget) < 2500) CompleteSeekAfterMinimumDelay(seekVersion); UpdatePlaybackDisplay(); } });
        mediaPlayer.EncounteredError += (_, _) => DispatchPlayback(() => { PlayerLoading.Visibility = Visibility.Visible; PlayerLoadingText.Text = "Не удалось воспроизвести поток. Попробуйте другое качество."; });
        mediaPlayer.EndReached += (_, _) => DispatchPlayback(VideoPlayer_MediaEnded);
        var qualities = new[] { "HD  ·  720p", "SD  ·  480p" }; QualityBox.ItemsSource = FullscreenQualityBox.ItemsSource = qualities; QualityBox.SelectedIndex = FullscreenQualityBox.SelectedIndex = 0;
        PositionSlider.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(PositionSlider_MouseDown), true); PositionSlider.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(PositionSlider_MouseUp), true); FullscreenPositionSlider.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(PositionSlider_MouseDown), true); FullscreenPositionSlider.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(PositionSlider_MouseUp), true);
        controlsTimer.Tick += (_, _) => { controlsTimer.Stop(); if (isFullscreen) SetFullscreenControls(false); };
        AnimeGrid.ItemsSource = items; GenreFilters.ItemsSource = genreFilters;
        genreFilters.Add(new GenreFilterItem { IsSelected=true });
        var orderedGenres=CatalogGenres.All.OrderBy(g=>g.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
        for (var i=0;i<orderedGenres.Count;i++) genreFilters.Add(new GenreFilterItem { Genre=orderedGenres[i], ColorIndex=i });
        playbackTimer.Tick += PlaybackTimer_Tick;
        Closing += (_, _) => { CancelCatalogLoading(); StopPlayerSession(); windowClosing = true; };
        Closed += (_, _) => { mediaPlayer.Stop(); VideoPlayer.MediaPlayer = null; currentMedia?.Dispose(); mediaPlayer.Dispose(); libVlc.Dispose(); };
        Loaded += async (_, _) => { mediaPlayer.Volume = (int)VolumeSlider.Value; LoadState(); await LoadAnime(); await RefreshTrackedAsync(); };
    }

    async Task LoadAnime(string? query = null, GenreDefinition? genre = null)
    {
        CancelCatalogLoading();
        catalogCancellation = new CancellationTokenSource();
        catalogQuery = query; activeGenre = genre;
        best.SetMetadataCache(state.BestMetadata);
        mergedCatalog = new MultiSourceCatalog(vost, best, state, genre);
        catalogPager = string.IsNullOrWhiteSpace(query) ? new CatalogPager(mergedCatalog.FetchPageAsync, false) : null;
        items.Clear(); CatalogScroll.ScrollToTop();
        catalogInitialPending = true;
        if (catalogPager is not null)
            foreach (var id in (genre is null ? state.HomeCatalogIds : state.GenreCatalogIds.GetValueOrDefault(genre.Name) ?? []).Distinct().Take(50))
                if (state.Titles.TryGetValue(id, out var cached)) { Decorate(cached); items.Add(cached); }
        await LoadCatalogBatchAsync(true);
    }
    async void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key != Key.Enter) return; libraryMode = false; SelectGenre(null); GenrePanel.Visibility=Visibility.Visible; PageTitle.Text = string.IsNullOrWhiteSpace(SearchBox.Text) ? "Последние обновления" : $"Поиск: {SearchBox.Text}"; Subtitle.Text = "Результаты AnimeVost и AnimeBest"; await LoadAnime(SearchBox.Text); }
    async void Home_Click(object sender, RoutedEventArgs e) { libraryMode = false; GenrePanel.Visibility=Visibility.Visible; SearchBox.Clear(); PageTitle.Text = "Последние обновления"; Subtitle.Text = "Новые серии AnimeVost и AnimeBest"; SelectGenre(null); await LoadAnime(); }
    async void GenreFilter_Click(object sender, RoutedEventArgs e)
    {
        GenrePopup.IsOpen=false;
        if (sender is not Button { Tag: GenreFilterItem item } || item.Genre == activeGenre) return;
        libraryMode=false; SearchBox.Clear(); SelectGenre(item.Genre);
        PageTitle.Text = item.Genre is null ? "Последние обновления" : "Жанр: " + item.Name;
        Subtitle.Text = item.Genre is null ? "Новые серии AnimeVost и AnimeBest" : "Весь каталог AnimeVost и AnimeBest";
        await LoadAnime(null,item.Genre);
    }
    void FilterButton_Click(object sender, RoutedEventArgs e) => GenrePopup.IsOpen=!GenrePopup.IsOpen;
    void SelectGenre(GenreDefinition? genre) { activeGenre=genre; GenreFilterButton.Content="Жанр: " + (genre?.Name ?? "Все") + "  ▾"; foreach(var item in genreFilters) item.IsSelected=item.Genre==genre; }
    void Favorites_Click(object sender, RoutedEventArgs e) { CancelCatalogLoading(); libraryMode = false; GenrePanel.Visibility=Visibility.Collapsed; PageTitle.Text = "Избранное"; Subtitle.Text = "Сохранённые тайтлы"; var saved = state.Titles.Values.Where(a => state.Favorites.Contains(a.Id)).ToList(); items.Clear(); foreach (var anime in saved) { Decorate(anime); items.Add(anime); } EmptyPanel.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed; }
    async void Refresh_Click(object sender, RoutedEventArgs e) { if (libraryMode) await RefreshTrackedAsync(); else { var genre=string.IsNullOrWhiteSpace(SearchBox.Text) ? activeGenre : null; await LoadAnime(SearchBox.Text,genre); await RefreshTrackedAsync(); } }
    void Anime_Click(object sender, RoutedEventArgs e) { selected = (sender as Button)?.Tag as Anime; if (selected is null) return; DetailCover.Source = Uri.TryCreate(selected.CoverUrl, UriKind.Absolute, out var cover) ? new BitmapImage(cover) : null; DetailTitle.Text = selected.Title; DetailNative.Text = selected.NativeTitle; DetailMeta.Text = selected.Meta + " · " + selected.ReleaseLabel + " · " + selected.SourcesLabel; DetailGenres.ItemsSource = selected.GenresText.Split(new[] { ',', ';', '/' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).Select(GenreChip.Create).ToArray(); DetailDescriptionScroll.ScrollToTop(); DetailDescription.Text = selected.Description; FavoriteButton.Content = selected.FavoriteLabel; Decorate(selected); UpdateDetailCheckpoint(ProgressFor(selected)); WatchButton.Content = ProgressFor(selected).Started.Count > 0 ? "▶  Продолжить" : "▶  Смотреть"; DetailsOverlay.Visibility = Visibility.Visible; state.RecentlyViewed.Remove(selected.Id); state.RecentlyViewed.Insert(0, selected.Id); SaveState(); }
    void Favorite_Click(object sender, RoutedEventArgs e) { if (selected is null) return; selected.IsFavorite = !selected.IsFavorite; if (selected.IsFavorite) state.Favorites.Add(selected.Id); else state.Favorites.Remove(selected.Id); FavoriteButton.Content = selected.FavoriteLabel; SaveState(); }
    async void Watch_Click(object sender, RoutedEventArgs e)
        => await StartWatchingAsync();

    async void ContinueDirect_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (!WatchButton.IsEnabled || sender is not Button { Tag: Anime anime } button) return;
        selected = anime;
        DetailsOverlay.Visibility = Visibility.Collapsed;
        button.IsEnabled = false;
        try { await StartWatchingAsync(true); }
        finally { button.IsEnabled = true; }
    }

    async Task StartWatchingAsync(bool direct = false)
    {
        if (selected is null) return;
        WatchButton.IsEnabled = false; WatchButton.Content = "Ищем серии…";
        try
        {
            CaptureProgress();
            SourceMatching.EnsureSource(selected);
            var openingAnime = selected;
            mediaRequest?.Cancel(); mediaRequest?.Dispose(); mediaRequest = new();
            var openingToken = mediaRequest.Token;
            
            episodes = await FetchInitialEpisodes(openingAnime, openingToken);
            openingToken.ThrowIfCancellationRequested();
            if (selected != openingAnime || (!direct && DetailsOverlay.Visibility != Visibility.Visible) || windowClosing) return;
            if (episodes.Count == 0) { MessageBox.Show("Для этого тайтла пока нет доступных серий.", "AniTV"); return; }
            ObserveEpisodes(selected, episodes);
            var progress = ProgressFor(selected);
            var (index, resume) = PlaybackChoice.Resume(progress, playbackSource!.Key, episodes);
            foreach (var ep in episodes) ep.IsWatched = progress.Watched.Contains(ep.Key);
            PlayerTitle.Text = FullscreenTitle.Text = selected.Title; ProviderText.Text = playbackSource!.Name;
            UpdateSourceSelectors();
            syncingSelectors = true; EpisodeBox.ItemsSource = FullscreenEpisodeBox.ItemsSource = episodes; syncingSelectors = false;
            SyncEpisodeSelectors(episodes[index]);
            PlayerOverlay.Visibility = Visibility.Visible; await PrepareEpisodeAsync(episodes[index], resume);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { MessageBox.Show("Не удалось получить видео: " + ex.Message, "AniTV", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { WatchButton.IsEnabled = true; WatchButton.Content = "▶  Смотреть"; }
    }
    void PlayEpisode(VostEpisode episode, double resumeSeconds = 0)
    {
        CaptureProgress(); playbackGeneration++; changingSource = true; videoStarted = false;
        PlayerLoadingText.Text = resumeSeconds > 0 ? "Возобновляем просмотр…" : "Подключаемся к видео…";
        PlayerLoading.Visibility = Visibility.Visible;
        playbackTimer.Stop(); mediaPlayer.Stop(); currentMedia?.Dispose(); AttachVideoSurface();
        activeAnime = selected; activeEpisode = selectedEpisode = episode; SyncEpisodeSelectors(episode); activeEnded = false; pendingResumeSeconds = resumeSeconds; isSeeking = false; seekTarget = -1; seekVersion++;
        var url = (QualityBox.SelectedItem as StreamQuality)?.Url ?? episode.HdUrl; currentMedia = new Media(libVlc, url); currentMedia.AddOption(":http-referrer=" + episode.Referrer); currentMedia.AddOption(":network-caching=1500");
        if (resumeSeconds > 0) currentMedia.AddOption(":start-time=" + resumeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        PositionSlider.Value = 0; TimeText.Text = "00:00 / 00:00"; PlayerLoading.Visibility = Visibility.Visible; PlayerLoadingText.Text = resumeSeconds > 0 ? "Возобновляем просмотр…" : "Подключаемся к видео…"; mediaPlayer.Play(currentMedia); isPlaying = true; PlayPauseButton.Content = "Ⅱ"; changingSource = false;
    }
    async void EpisodeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (syncingSelectors || sender is not ComboBox box) return;
        var index = box.SelectedIndex;
        if (index < 0 || index >= episodes.Count) return;
        await SelectEpisodeAsync(index);
    }
    void QualityBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || syncingSelectors || changingSource || sender is not ComboBox box || EpisodeBox.SelectedItem is not VostEpisode episode || PlayerOverlay.Visibility != Visibility.Visible) return; syncingSelectors = true; if (box == QualityBox) FullscreenQualityBox.SelectedIndex = box.SelectedIndex; else QualityBox.SelectedIndex = box.SelectedIndex; syncingSelectors = false; var position = mediaPlayer.Time; PlayEpisode(episode, Math.Max(0, position / 1000d));
    }
    async Task SelectEpisodeAsync(int index)
    {
        if (index < 0 || index >= episodes.Count) return;
        var target = episodes[index];
        selectedEpisode = target;
        SyncEpisodeSelectors(target);
        await PrepareEpisodeAsync(target);
    }
    void SyncEpisodeSelectors(VostEpisode episode)
    {
        var index = episodes.ToList().FindIndex(item => ReferenceEquals(item, episode) || item.Key == episode.Key);
        if (index < 0) return;
        syncingSelectors = true;
        EpisodeBox.SelectedIndex = FullscreenEpisodeBox.SelectedIndex = index;
        syncingSelectors = false;
    }
    async Task MoveEpisodeAsync(int direction)
    {
        var current = selectedEpisode ?? EpisodeBox.SelectedItem as VostEpisode ?? activeEpisode;
        var index = EpisodeNavigation.AdjacentIndex(episodes, current, direction);
        if (index >= 0) await SelectEpisodeAsync(index);
    }
    async void PreviousEpisode_Click(object sender, RoutedEventArgs e) => await MoveEpisodeAsync(-1);
    async void NextEpisode_Click(object sender, RoutedEventArgs e) => await MoveEpisodeAsync(1);
    void PlayPause_Click(object sender, RoutedEventArgs e) { if (!PlayerActive) return; CaptureProgress(); if (isPlaying) mediaPlayer.Pause(); else mediaPlayer.Play(); isPlaying = !isPlaying; var icon = isPlaying ? "Ⅱ" : "▶"; PlayPauseButton.Content = icon; FullscreenPlayPauseButton.Content = icon; }
    void VideoSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (!PlayerActive) return; if (e.ClickCount >= 2) ToggleFullscreen(); else PlayPause_Click(sender, e); }
    void ToggleFullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();
    void ToggleFullscreen()
    {
        if (!PlayerActive && !isFullscreen) return;
        if (!isFullscreen)
        {
            stateBeforeFullscreen = WindowState; styleBeforeFullscreen = WindowStyle; resizeBeforeFullscreen = ResizeMode; boundsBeforeFullscreen = RestoreBounds; topmostBeforeFullscreen = Topmost;
            ApplyFullscreenShell(true);
            Grid.SetRow(PlayerVideoHost, 0); Grid.SetRowSpan(PlayerVideoHost, 3); PlayerVideoHost.Margin = new Thickness(0); PlayerVideoBorder.CornerRadius = new CornerRadius(0); SetFullscreenControls(false);
            WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true;
            var screen = CurrentMonitorBounds(); Left=screen.Left; Top=screen.Top; Width=screen.Width; Height=screen.Height; isFullscreen = true;
        }
        else
        {
            Topmost = topmostBeforeFullscreen; WindowState = WindowState.Normal; WindowStyle = styleBeforeFullscreen; ResizeMode = resizeBeforeFullscreen;
            Left=boundsBeforeFullscreen.Left; Top=boundsBeforeFullscreen.Top; Width=boundsBeforeFullscreen.Width; Height=boundsBeforeFullscreen.Height;
            if(stateBeforeFullscreen==WindowState.Maximized) WindowState=WindowState.Maximized;
            ApplyFullscreenShell(false);
            controlsTimer.Stop(); Mouse.OverrideCursor = null; FullscreenChrome.Visibility = Visibility.Collapsed; Grid.SetRowSpan(PlayerVideoHost, 1); Grid.SetRow(PlayerVideoHost, 1); PlayerHeader.Visibility = Visibility.Visible; PlayerControls.Visibility = Visibility.Visible; PlayerVideoHost.Margin = new Thickness(22,16,22,8); PlayerVideoBorder.CornerRadius = new CornerRadius(14); isFullscreen = false; FullscreenButton.Content = "⛶  Полный экран";
        }
    }
    void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; } else if (e.Key == Key.Escape && isFullscreen) { ToggleFullscreen(); e.Handled = true; } else if (e.Key == Key.Space && PlayerOverlay.Visibility == Visibility.Visible) { PlayPause_Click(sender, e); e.Handled = true; } }
    void Window_MouseMove(object sender, MouseEventArgs e) => RevealFullscreenControls();
    void VideoSurface_MouseMove(object sender, MouseEventArgs e) => RevealFullscreenControls();
    void RevealFullscreenControls() { if (!PlayerActive || !isFullscreen) return; SetFullscreenControls(true); controlsTimer.Stop(); controlsTimer.Start(); }
    void SetFullscreenControls(bool visible) { PlayerHeader.Visibility = Visibility.Collapsed; PlayerControls.Visibility = Visibility.Collapsed; FullscreenChrome.Visibility = visible ? Visibility.Visible : Visibility.Collapsed; Mouse.OverrideCursor = visible ? null : Cursors.None; }
    void ClosePlayer_Click(object sender, RoutedEventArgs e) { StopPlayerSession(); if (libraryMode) ShowLibrary(); }
    async void VideoPlayer_MediaEnded()
    {
        CaptureProgress(true);
        var next = EpisodeNavigation.AdjacentIndex(episodes, selectedEpisode ?? EpisodeBox.SelectedItem as VostEpisode ?? activeEpisode, 1);
        if (next >= 0) await SelectEpisodeAsync(next);
        else { isPlaying = false; PlayPauseButton.Content = FullscreenPlayPauseButton.Content = "▶"; }
    }
    void PlaybackTimer_Tick(object? sender, EventArgs e) { UpdatePlaybackDisplay(); if (DateTime.UtcNow - lastCheckpoint >= TimeSpan.FromSeconds(5)) CaptureProgress(); }
    void UpdatePlaybackDisplay() { if (mediaPlayer.Length <= 0) return; if (!isSeeking) { PositionSlider.Maximum = FullscreenPositionSlider.Maximum = mediaPlayer.Length / 1000d; PositionSlider.Value = FullscreenPositionSlider.Value = mediaPlayer.Time / 1000d; } var text = $"{FormatTime(TimeSpan.FromMilliseconds(mediaPlayer.Time))} / {FormatTime(TimeSpan.FromMilliseconds(mediaPlayer.Length))}"; TimeText.Text = FullscreenTimeText.Text = text; }
    void PositionSlider_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || mediaPlayer.Length <= 0 || sender is not Slider slider) return; isSeeking = true; activeSeekSlider = slider;
        var point = e.GetPosition(slider); var ratio = Math.Clamp(point.X / Math.Max(1, slider.ActualWidth), 0, 1); slider.Value = ratio * slider.Maximum;
    }
    void PositionSlider_MouseUp(object sender, MouseButtonEventArgs e) { if (e.ChangedButton != MouseButton.Left || !isSeeking || sender is not Slider slider) return; var point = e.GetPosition(slider); var ratio = Math.Clamp(point.X / Math.Max(1, slider.ActualWidth), 0, 1); slider.Value = ratio * slider.Maximum; activeSeekSlider = slider; CommitSeek(); }
    void CommitSeek()
    {
        if (!PlayerActive || mediaPlayer.Length <= 0) { isSeeking = false; return; }
        var target = (long)((activeSeekSlider?.Value ?? PositionSlider.Value) * 1000); seekTarget = target; seekStartedAt = DateTime.UtcNow; var version = ++seekVersion; PlayerLoadingText.Text = "Перемотка…"; PlayerLoading.Visibility = Visibility.Visible;
        var cancellation = playbackCancellation.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                mediaPlayer.Position = Math.Clamp((float)target / Math.Max(1, mediaPlayer.Length), 0f, 1f);
                await Task.Delay(8000, cancellation);
                DispatchPlayback(() => { if (isSeeking && version == seekVersion) { isSeeking = false; seekTarget = -1; PlayerLoading.Visibility = Visibility.Collapsed; UpdatePlaybackDisplay(); } });
            }
            catch (OperationCanceledException) { }
        });
    }
    async void CompleteSeekAfterMinimumDelay(int version) { var wait = TimeSpan.FromMilliseconds(450) - (DateTime.UtcNow - seekStartedAt); if (wait > TimeSpan.Zero) await Task.Delay(wait); if (!isSeeking || version != seekVersion) return; isSeeking = false; seekTarget = -1; PlayerLoading.Visibility = Visibility.Collapsed; UpdatePlaybackDisplay(); }
    void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if(syncingVolume) return;
        syncingVolume=true;
        try
        {
            if(sender==FullscreenVolumeSlider && VolumeSlider is not null) VolumeSlider.Value=e.NewValue;
            else if(sender==VolumeSlider && FullscreenVolumeSlider is not null) FullscreenVolumeSlider.Value=e.NewValue;
            if(mediaPlayer is not null) mediaPlayer.Volume=(int)e.NewValue;
        }
        finally { syncingVolume=false; }
    }
    static string FormatTime(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"hh\:mm\:ss") : value.ToString(@"mm\:ss");
    void CloseDetails_Click(object s, RoutedEventArgs e) { mediaRequest?.Cancel(); DetailsOverlay.Visibility = Visibility.Collapsed; }
    void Overlay_MouseDown(object s, MouseButtonEventArgs e) { mediaRequest?.Cancel(); DetailsOverlay.Visibility = Visibility.Collapsed; }
    void Details_MouseDown(object s, MouseButtonEventArgs e) => e.Handled = true;
}

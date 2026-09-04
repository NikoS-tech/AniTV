using System.IO;
using System.Text.Json;
using System.Windows;

namespace AniTV;

public partial class MainWindow
{
    Anime? activeAnime;
    VostEpisode? activeEpisode;
    double pendingResumeSeconds;
    bool activeEnded;
    bool libraryMode;
    bool completedMode;
    bool refreshingLibrary;
    DateTime lastCheckpoint = DateTime.MinValue;

    WatchProgress ProgressFor(Anime anime)
    {
        SourceMatching.EnsureSource(anime);
        if (!state.Progress.TryGetValue(anime.Id, out var progress))
            state.Progress[anime.Id] = progress = new WatchProgress { KnownAvailable = anime.AvailableEpisodes ?? 0 };
        state.Titles[anime.Id] = anime;
        return progress;
    }

    void Decorate(Anime anime)
    {
        var progress = ProgressFor(anime);
        anime.IsFavorite = state.Favorites.Contains(anime.Id);
        anime.IsCompleted = progress.IsComplete(anime);
        anime.CanRemoveFromContinue = libraryMode && progress.Started.Count > 0 && !progress.HiddenFromContinue;
        anime.CanContinueFromLibrary = anime.CanRemoveFromContinue && !anime.IsCompleted;
        foreach (var source in anime.Sources) progress.ObserveSource(source);
        anime.HasNewEpisodes = anime.Sources.Any(s => s.HasNewEpisodes);
        anime.ProgressLabel = progress.HiddenFromContinue || progress.Started.Count == 0 ? "" : $"▶ {progress.LastEpisodeName} · {TimeSpan.FromSeconds(progress.PositionSeconds):mm\\:ss}\nМакс. серия: {progress.MaxEpisodeNumber} · ✓ {progress.Watched.Count}";
        anime.Refresh();
        if (selected?.Id == anime.Id) DetailMeta.Text = anime.Meta + " · " + anime.ReleaseLabel + "\n" + anime.SourcesLabel;
    }

    void ObserveEpisodes(Anime anime, IReadOnlyList<VostEpisode> list)
    {
        ProgressFor(anime).Observe(list.Select(x => x.Key));
        anime.AvailableEpisodes = Math.Max(anime.AvailableEpisodes ?? 0, list.Select(x => x.Key).Distinct().Count());
        if (anime.TotalIsExact && anime.Episodes is > 0 && list.Count >= anime.Episodes) anime.Status = "FINISHED";
        Decorate(anime);
    }

    void CaptureProgress(bool ended = false)
    {
        if (activeAnime is null || activeEpisode is null || activeEnded || changingSource || isSeeking || !videoStarted) return;
        var position = Math.Max(0, mediaPlayer.Time / 1000d);
        var duration = Math.Max(0, mediaPlayer.Length / 1000d);
        if (pendingResumeSeconds > 0)
        {
            if (position < pendingResumeSeconds - 2) return;
            pendingResumeSeconds = 0;
        }
        var progress = ProgressFor(activeAnime);
        progress.LastSourceKey = activeSourceKey;
        progress.SourcePositions[activeSourceKey + "/" + activeEpisode.Key] = ended ? duration : position;
        progress.Record(activeEpisode.Key, activeEpisode.Name, activeEpisode.Number, ended ? duration : position, duration, ended, activeSourceKey);
        activeEnded = ended;
        Decorate(activeAnime);
        RefreshEpisodeLabels();
        SaveState();
        lastCheckpoint = DateTime.UtcNow;
    }

    void RefreshEpisodeLabels()
    {
        if (activeAnime is null) return;
        var progress = ProgressFor(activeAnime);
        var changed = false;
        foreach (var episode in episodes)
        {
            var watched = progress.Watched.Contains(episode.Key);
            if (episode.IsWatched != watched) { episode.IsWatched = watched; changed = true; }
        }
        if (changed) { EpisodeBox.Items.Refresh(); FullscreenEpisodeBox.Items.Refresh(); }
        UpdateDetailCheckpoint(progress);
    }

    void UpdateDetailCheckpoint(WatchProgress progress)
    {
        DetailProgress.Text = progress.Started.Count == 0 ? "" : $"{progress.LastEpisodeName} · {FormatTime(TimeSpan.FromSeconds(progress.PositionSeconds))}";
        DetailProgress.ToolTip = progress.Started.Count == 0 ? null : $"Максимальная серия: {progress.MaxEpisodeNumber}\nПросмотрено: {progress.Watched.Count}\nИсточник: {selected?.Sources.FirstOrDefault(s => s.Key == progress.LastSourceKey)?.Name ?? "—"}";
    }

    async void Library_Click(object sender, RoutedEventArgs e)
    {
        GenrePanel.Visibility = Visibility.Collapsed;
        CancelCatalogLoading();
        completedMode = false;
        libraryMode = true;
        ShowLibrary();
        await RefreshTrackedAsync();
    }

    void ShowLibrary()
    {
        ShowCatalogContent();
        PageTitle.Text = completedMode ? "Просмотрено" : "Продолжить просмотр";
        Subtitle.Text = completedMode ? "Полностью просмотренные сезоны · Сначала последние по дате просмотра" : "Последняя серия и позиция · ✓ — просмотренные серии · НОВИНКА — новая, ещё не начатая серия";
        var titles = state.Titles.Values.Where(a => state.Progress.TryGetValue(a.Id, out var p) && (completedMode ? p.IsComplete(a) : p.Started.Count > 0 && !p.HiddenFromContinue && !p.IsComplete(a)))
            .OrderByDescending(a => state.Progress[a.Id].LastWatchedAt).ToList();
        items.Clear();
        foreach (var anime in titles) { Decorate(anime); items.Add(anime); }
        EmptyPanel.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = $"{(completedMode ? "Просмотрено" : "Начатых тайтлов")}: {items.Count}";
    }

    async void Completed_Click(object sender, RoutedEventArgs e)
    {
        GenrePanel.Visibility = Visibility.Collapsed;
        CancelCatalogLoading();
        libraryMode = true; completedMode = true;
        CatalogScroll.ScrollToTop();
        ShowLibrary();
        await RefreshTrackedAsync();
    }

    async Task RefreshTrackedAsync()
    {
        if (refreshingLibrary) return;
        refreshingLibrary = true;
        var failed = 0;
        try
        {
            var titles = state.Titles.Values.Where(a => state.Favorites.Contains(a.Id) || (state.Progress.TryGetValue(a.Id, out var p) && p.Started.Count > 0)).ToList();
            using var slots = new SemaphoreSlim(2);
            await Task.WhenAll(titles.Select(async anime =>
            {
                await slots.WaitAsync();
                try
                {
                    SourceMatching.EnsureSource(anime);
                    foreach (var source in anime.Sources.ToList())
                    {
                        try { var list = await FetchEpisodes(anime, source, CancellationToken.None); if (list.Count > 0) ObserveEpisodes(anime, list); }
                        catch { failed++; }
                    }
                }
                catch { failed++; }
                finally { slots.Release(); }
            }));
            SaveState();
            if (libraryMode) { ShowLibrary(); if (failed > 0) StatusText.Text += $" · Не удалось обновить: {failed}. Сохранённый прогресс доступен."; }
        }
        finally { refreshingLibrary = false; }
    }

    void RemoveFromContinue_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (!libraryMode || sender is not System.Windows.Controls.Button { Tag: Anime anime } ||
            !state.Progress.TryGetValue(anime.Id, out var progress)) return;
        var confirmation = new ConfirmRemovalWindow(anime.Title) { Owner = this };
        if (confirmation.ShowDialog() != true) return;
        state.Progress[anime.Id] = new WatchProgress { KnownAvailable = anime.AvailableEpisodes ?? 0 };
        foreach (var source in anime.Sources) state.Progress[anime.Id].ObserveSource(source);
        if (!SaveState()) { state.Progress[anime.Id] = progress; return; }
        Decorate(anime);
        ShowLibrary();
    }

    void LoadState()
    {
        foreach (var path in new[] { statePath, statePath + ".bak" })
        {
            if (!File.Exists(path)) continue;
            try
            {
                state = JsonSerializer.Deserialize<UserState>(File.ReadAllText(path)) ?? new();
                if (state.SchemaVersion < 2)
                {
                    var migrationBackup = statePath + ".before-sources.bak";
                    if (!File.Exists(migrationBackup)) File.Copy(path, migrationBackup);
                    foreach (var anime in state.Titles.Values) SourceMatching.EnsureSource(anime);
                    state.SchemaVersion = 2;
                }
                if (state.SchemaVersion < 3)
                {
                    var backup = statePath + ".before-dedup.bak";
                    if (!File.Exists(backup)) File.Copy(path, backup);
                    CatalogDeduplication.Reconcile(state);
                    state.SchemaVersion = 3;
                }
                if (state.SchemaVersion < 4)
                {
                    var backup = statePath + ".before-numeric-match.bak";
                    if (!File.Exists(backup)) File.Copy(path, backup);
                    CatalogDeduplication.Reconcile(state);
                    state.SchemaVersion = 4;
                }
                return;
            }
            catch { StatusText.Text = "Не удалось прочитать сохранения; проверяется резервная копия."; }
        }
        state = new();
    }

    bool SaveState()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            var temporary = statePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state));
            if (File.Exists(statePath)) File.Replace(temporary, statePath, statePath + ".bak");
            else File.Move(temporary, statePath);
            return true;
        }
        catch (Exception ex) { StatusText.Text = "Не удалось сохранить прогресс: " + ex.Message; return false; }
    }
}

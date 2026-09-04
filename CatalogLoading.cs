using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AniTV;

public partial class MainWindow
{
    CancellationTokenSource? catalogCancellation;
    CatalogPager? catalogPager;
    string? catalogQuery;
    bool catalogLoading, catalogFailed;
    bool catalogInitialPending;
    Task<List<Anime>?>? catalogPrefetch;

    void CancelCatalogLoading()
    {
        catalogCancellation?.Cancel();
        catalogCancellation?.Dispose();
        catalogCancellation = null;
        catalogPager = null;
        catalogPrefetch = null;
        catalogInitialPending = false;
        catalogLoading = catalogFailed = false;
        LoadingPanel.Visibility = CatalogMoreSpinner.Visibility = CatalogRetry.Visibility = Visibility.Collapsed;
    }

    async Task LoadCatalogBatchAsync(bool initial)
    {
        initial |= catalogInitialPending;
        if (catalogLoading || catalogCancellation is null || (!initial && catalogPager?.HasMore != true && catalogPrefetch is null)) return;
        var session = catalogCancellation;
        var token = session.Token;
        var pager = catalogPager;
        catalogLoading = true;
        catalogFailed = false;
        CatalogRetry.Visibility = EmptyPanel.Visibility = Visibility.Collapsed;
        if (initial) LoadingPanel.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        else CatalogMoreSpinner.Visibility = Visibility.Visible;
        StatusText.Text = initial ? (items.Count == 0 ? "Загружаем каталог…" : "Сохранённый каталог · Обновляем серии и статусы…") : $"Показано: {items.Count} · Загружаем ещё 20…";
        try
        {
            var prefetched = !initial && catalogPrefetch is { } pending ? await pending : null;
            token.ThrowIfCancellationRequested();
            var batch = prefetched ?? (pager is null ? await mergedCatalog!.SearchAsync(catalogQuery ?? "", token) : await pager.TakeAsync(initial ? 50 : 20, token));
            if (session != catalogCancellation) return;
            catalogPrefetch = null;
            if (initial) items.Clear();
            catalogInitialPending = false;
            foreach (var anime in batch) { Decorate(anime); if (!items.Any(a => a.Id == anime.Id)) items.Add(anime); }
            if (initial && pager is not null && string.IsNullOrEmpty(mergedCatalog?.Warning))
            {
                var order = items.Select(a => a.Id).Take(50).ToList();
                if (activeGenre is null) { state.HomeCatalogIds = order; state.HomeCatalogUpdatedAt = DateTimeOffset.UtcNow; }
                else state.GenreCatalogIds[activeGenre.Name] = order;
            }
            EmptyPanel.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = $"Показано: {items.Count} · AnimeVost + AnimeBest" + (pager is { HasMore: false } ? " · Все тайтлы загружены" : "") + (string.IsNullOrEmpty(mergedCatalog?.Warning) ? "" : " · " + mergedCatalog.Warning);
            SaveState();
            if (pager?.HasMore == true) catalogPrefetch = PrefetchCatalogAsync(pager, session);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (session != catalogCancellation) return;
            catalogFailed = true;
            StatusText.Text = $"Показано: {items.Count} · Не удалось загрузить тайтлы. Проверьте подключение и повторите.";
            CatalogRetry.Visibility = Visibility.Visible;
        }
        finally
        {
            if (session == catalogCancellation)
            {
                catalogLoading = false;
                LoadingPanel.Visibility = CatalogMoreSpinner.Visibility = Visibility.Collapsed;
            }
        }
    }

    async Task<List<Anime>?> PrefetchCatalogAsync(CatalogPager pager, CancellationTokenSource session)
    {
        try
        {
            var batch = await pager.TakeAsync(20, session.Token);
            if (session != catalogCancellation) return null;
            foreach (var anime in items) Decorate(anime);
            SaveState();
            return batch;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception) { return null; } // Foreground loading will retry and display any error.
    }

    async void CatalogScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource == CatalogScroll && e.VerticalChange > 0) await LoadNearCatalogEndAsync();
    }

    async void CatalogScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta < 0) await LoadNearCatalogEndAsync();
    }

    async Task LoadNearCatalogEndAsync()
    {
        if (catalogLoading || catalogFailed || libraryMode || downloadsMode || (catalogPager?.HasMore != true && catalogPrefetch is null) ||
            PlayerOverlay.Visibility == Visibility.Visible || DetailsOverlay.Visibility == Visibility.Visible) return;
        if (CatalogScroll.ScrollableHeight - CatalogScroll.VerticalOffset <= 400)
            await LoadCatalogBatchAsync(false);
    }

    async void CatalogRetry_Click(object sender, RoutedEventArgs e) => await LoadCatalogBatchAsync(items.Count == 0);
}

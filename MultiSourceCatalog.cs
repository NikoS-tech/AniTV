namespace AniTV;

public sealed class MultiSourceCatalog(AnimeVostProvider vost, AnimeBestProvider best, UserState state, GenreDefinition? genre = null)
{
    readonly HashSet<string> seenSources = [];
    readonly Dictionary<string, Anime> sourceIndex = [];
    readonly Dictionary<(int Year, string Format, TitleFingerprint Name), HashSet<Anime>> titleIndex = [];
    bool indexed;
    public int ComparisonCount { get; private set; }
    void EnsureIndex()
    {
        if (indexed) return;
        foreach (var anime in state.Titles.Values) Index(anime);
        indexed = true;
    }
    void Index(Anime anime)
    {
        SourceMatching.EnsureSource(anime);
        foreach (var source in anime.Sources) sourceIndex[source.Key] = anime;
        var cache = SourceMatching.Cached(anime);
        if (cache.Year is not int year || cache.Format.Length == 0) return;
        foreach (var name in cache.Fingerprints)
        {
            var key = (year, cache.Format, name);
            if (!titleIndex.TryGetValue(key, out var titles)) titleIndex[key] = titles = [];
            titles.Add(anime);
        }
    }
    IEnumerable<Anime> Candidates(Anime incoming)
    {
        var cache = SourceMatching.Cached(incoming);
        if (cache.Year is not int year || cache.Format.Length == 0) return [];
        return cache.Fingerprints.SelectMany(name => titleIndex.TryGetValue((year,cache.Format,name), out var titles) ? titles : Enumerable.Empty<Anime>()).Distinct();
    }
    void RemoveTitleKeys(Anime anime)
    {
        var cache = SourceMatching.Cached(anime);
        if (cache.Year is not int year) return;
        foreach (var name in cache.Fingerprints)
        {
            var key = (year,cache.Format,name);
            if (titleIndex.TryGetValue(key,out var titles)) { titles.Remove(anime); if(titles.Count==0) titleIndex.Remove(key); }
        }
    }
    bool vostEnded, bestEnded;
    public string Warning { get; private set; } = "";
    public async Task<List<Anime>> FetchPageAsync(int page, CancellationToken token)
    {
        var v = FetchSafe(() => vostEnded ? Task.FromResult(new List<Anime>()) : genre is null ? vost.GetCatalogPageAsync(page, token) : vost.GetGenrePageAsync(genre.VostSlug,page,token), token);
        var b = FetchSafe(() => bestEnded ? Task.FromResult(new List<Anime>()) : genre is null ? best.GetCatalogPageAsync(page, token) : best.GetGenrePageAsync(genre.BestPath,page,token), token);
        await Task.WhenAll(v, b);
        token.ThrowIfCancellationRequested();
        if (v.Result.Error is not null && b.Result.Error is not null) throw new InvalidOperationException("Оба источника недоступны.");
        if (v.Result.Error is not null) Warning = "AnimeVost недоступен; повторите обновление позже";
        if (b.Result.Error is not null) Warning = "AnimeBest недоступен; повторите обновление позже";
        var vr = NewSources(v.Result.Rows); var br = NewSources(b.Result.Rows);
        vostEnded |= vr.Count == 0; bestEnded |= br.Count == 0;
        return vr.ZipLongest(br).Select(Resolve).DistinctBy(a => a.Id).ToList();
    }
    List<Anime> NewSources(List<Anime> rows) => rows.Where(a => { SourceMatching.EnsureSource(a); return seenSources.Add(a.Sources[0].Key); }).ToList();
    public async Task<List<Anime>> SearchAsync(string query, CancellationToken token)
    {
        var v = FetchSafe(() => vost.GetCatalogAsync(query, token), token);
        var b = FetchSafe(() => best.SearchAsync(query, token), token);
        await Task.WhenAll(v,b);
        token.ThrowIfCancellationRequested();
        if (v.Result.Error is not null && b.Result.Error is not null) throw new InvalidOperationException("Оба источника недоступны.");
        Warning = v.Result.Error is not null ? "AnimeVost недоступен" : b.Result.Error is not null ? "AnimeBest недоступен" : "";
        return v.Result.Rows.Concat(b.Result.Rows).Select(Resolve).DistinctBy(a => a.Id).ToList();
    }
    public Anime Resolve(Anime incoming)
    {
        SourceMatching.EnsureSource(incoming);
        EnsureIndex();
        var source = incoming.Sources[0];
        sourceIndex.TryGetValue(source.Key, out var existing);
        if (existing is null)
        {
            var candidates = Candidates(incoming).Where(a => !a.Sources.Any(s => s.Provider == source.Provider))
                .Where(a => { ComparisonCount++; return SourceMatching.SameTitle(a,incoming); }).Take(2).ToList();
            if (candidates.Count == 1) existing = candidates[0];
        }
        if (existing is null) { state.Titles[incoming.Id] = incoming; Index(incoming); return incoming; }
        var index = existing.Sources.FindIndex(s => s.Key == source.Key);
        if (index < 0) existing.Sources.Add(source); else existing.Sources[index] = source;
        sourceIndex[source.Key] = existing;
        existing.AvailableEpisodes = existing.Sources.Max(s => s.Available);
        // Prefer the original site's metadata, preserving stable canonical IDs and saved progress.
        if (existing.Sources[0].Key == source.Key)
        {
            if (existing.Title != incoming.Title || existing.RomanizedTitle != incoming.RomanizedTitle || existing.NativeTitle != incoming.NativeTitle || existing.Year != incoming.Year || existing.Format != incoming.Format)
            {
                RemoveTitleKeys(existing);
                existing.Title = incoming.Title; existing.RomanizedTitle = incoming.RomanizedTitle; existing.NativeTitle = incoming.NativeTitle;
                existing.Year = incoming.Year; existing.Format = incoming.Format;
                Index(existing);
            }
            existing.PageUrl = incoming.PageUrl;
            existing.Status = incoming.Status; existing.Episodes = incoming.Episodes; existing.TotalIsExact = incoming.TotalIsExact;
            existing.CoverUrl = incoming.CoverUrl; existing.Description = incoming.Description;
        }
        existing.Refresh();
        return existing;
    }
    static async Task<(List<Anime> Rows, Exception? Error)> FetchSafe(Func<Task<List<Anime>>> fetch, CancellationToken token)
    {
        try { return (await fetch(), null); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) { return ([],e); }
    }
}

static class CatalogInterleave
{
    public static IEnumerable<T> ZipLongest<T>(this IReadOnlyList<T> a, IReadOnlyList<T> b)
    {
        for (int i=0; i<Math.Max(a.Count,b.Count); i++) { if(i<a.Count) yield return a[i]; if(i<b.Count) yield return b[i]; }
    }
}

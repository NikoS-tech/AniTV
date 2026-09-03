namespace AniTV;

public sealed class CatalogPager(Func<int, CancellationToken, Task<List<Anime>>> fetchPage, bool stopOnRepeatedPage = true)
{
    readonly Queue<Anime> pending = new();
    readonly HashSet<int> seen = [];
    int nextPage = 1;
    bool ended;
    public bool HasMore => pending.Count > 0 || !ended;

    public async Task<List<Anime>> TakeAsync(int count, CancellationToken token = default)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        while (pending.Count < count && !ended)
        {
            token.ThrowIfCancellationRequested();
            var page = await fetchPage(nextPage, token);
            token.ThrowIfCancellationRequested();
            nextPage++;
            var added = 0;
            foreach (var anime in page)
                if (seen.Add(anime.Id)) { pending.Enqueue(anime); added++; }
            ended = page.Count == 0 || (stopOnRepeatedPage && added == 0);
        }
        token.ThrowIfCancellationRequested();
        var result = new List<Anime>();
        while (result.Count < count && pending.TryDequeue(out var anime)) result.Add(anime);
        return result;
    }
}

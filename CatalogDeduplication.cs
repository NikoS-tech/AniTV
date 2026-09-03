namespace AniTV;

public static class CatalogDeduplication
{
    // Reconcile existing saves, not only newly discovered titles. Ambiguous matches stay separate.
    public static int Reconcile(UserState state)
    {
        foreach (var anime in state.Titles.Values) SourceMatching.EnsureSource(anime);
        var count = 0;
        foreach (var incoming in state.Titles.Values.Where(a => a.Sources.Count == 1).ToList())
        {
            if (!state.Titles.ContainsKey(incoming.Id)) continue;
            var candidates = state.Titles.Values.Where(a => a.Id != incoming.Id &&
                !a.Sources.Any(s => incoming.Sources.Any(t => t.Provider == s.Provider)) &&
                SourceMatching.SameTitle(a, incoming)).Take(2).ToList();
            if (candidates.Count != 1) continue;
            var candidate = candidates[0];
            // Require a unique match in both directions; do not arbitrarily join a franchise.
            if (state.Titles.Values.Count(a => a.Id != candidate.Id &&
                !a.Sources.Any(s => candidate.Sources.Any(t => t.Provider == s.Provider)) &&
                SourceMatching.SameTitle(a,candidate)) != 1) continue;
            var keep = incoming.Id > 0 ? incoming : candidate;
            var remove = ReferenceEquals(keep,incoming) ? candidate : incoming;
            Merge(state,keep,remove); count++;
        }
        return count;
    }

    static void Merge(UserState state, Anime keep, Anime remove)
    {
        keep.Sources.AddRange(remove.Sources.Where(s => keep.Sources.All(t => t.Key != s.Key)));
        keep.AvailableEpisodes = keep.Sources.Max(s => s.Available);
        if (state.Favorites.Remove(remove.Id)) state.Favorites.Add(keep.Id);
        state.RecentlyViewed = state.RecentlyViewed.Select(id => id == remove.Id ? keep.Id : id).Distinct().ToList();
        state.Progress.TryGetValue(keep.Id, out var a);
        state.Progress.TryGetValue(remove.Id, out var b);
        if (a is not null) PreserveCheckpoint(a,keep);
        if (b is not null) PreserveCheckpoint(b,remove);
        if (b is not null)
        {
            if (a is null) state.Progress[keep.Id] = b;
            else
            {
                var latest = b.LastWatchedAt > a.LastWatchedAt ? b : a;
                var other = ReferenceEquals(latest,a) ? b : a;
                latest.Started.UnionWith(other.Started);
                latest.Watched.UnionWith(other.Watched);
                latest.KnownEpisodes.UnionWith(other.KnownEpisodes);
                latest.NewEpisodes.UnionWith(other.NewEpisodes);
                latest.NewEpisodes.ExceptWith(latest.Started);
                latest.MaxEpisodeNumber = Math.Max(a.MaxEpisodeNumber,b.MaxEpisodeNumber);
                latest.KnownAvailable = Math.Max(a.KnownAvailable,b.KnownAvailable);
                latest.HasBaseline |= other.HasBaseline;
                foreach (var pair in other.SourcePositions) latest.SourcePositions.TryAdd(pair.Key,pair.Value);
                foreach (var pair in other.SourceWatched)
                {
                    if (!latest.SourceWatched.TryGetValue(pair.Key,out var watched)) latest.SourceWatched[pair.Key]=watched=[];
                    watched.UnionWith(pair.Value);
                }
                foreach (var pair in other.SourceReleases) latest.SourceReleases.TryAdd(pair.Key,pair.Value);
                state.Progress[keep.Id] = latest;
            }
            state.Progress.Remove(remove.Id);
        }
        state.Titles.Remove(remove.Id);
        keep.Refresh();
    }
    static void PreserveCheckpoint(WatchProgress progress, Anime anime)
    {
        if (string.IsNullOrEmpty(progress.LastSourceKey))
            progress.LastSourceKey = anime.Sources.First(s => s.Id == anime.SourceId).Key;
        if (!string.IsNullOrEmpty(progress.LastEpisodeKey))
            progress.SourcePositions.TryAdd(progress.LastSourceKey + "/" + progress.LastEpisodeKey,progress.PositionSeconds);
    }
}

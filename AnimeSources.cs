using System.Text.RegularExpressions;

namespace AniTV;

public sealed class AnimeSource
{
    public string Provider { get; set; } = "vost";
    public string Id { get; set; } = "";
    public string PageUrl { get; set; } = "";
    public int? Available { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool HasNewEpisodes { get; set; }
    public string DisplayLabel => $"{Name}: {Available?.ToString() ?? "?"} эп." + (HasNewEpisodes ? " · НОВИНКА" : "");
    public string Key => Provider + ":" + Id;
    public string Name => Provider == "best" ? "AnimeBest" : "AnimeVost";
    public override string ToString() => Name;
}

public sealed record StreamQuality(string Name, Uri Url)
{
    public override string ToString() => Name;
}

public static class SourceMatching
{
    public const int AlgorithmVersion = 1;
    public static TitleComparisonCache Cached(Anime anime)
    {
        var input = System.Text.Json.JsonSerializer.Serialize(new object?[] { anime.Title, anime.RomanizedTitle, anime.NativeTitle, anime.Year, anime.Format });
        if (anime.ComparisonCache is { } saved && saved.Version == AlgorithmVersion && saved.Input == input) return saved;
        return anime.ComparisonCache = new TitleComparisonCache {
            Version = AlgorithmVersion, Input = input, Year = anime.Year,
            Format = Format(anime.Format), PrimaryNumbers = TitleFingerprint.Create(anime.Title).Numbers,
            Fingerprints = Fingerprints(anime).Distinct().ToList()
        };
    }
    public static void EnsureSource(Anime anime)
    {
        if (anime.Sources.Count == 0)
            anime.Sources.Add(new AnimeSource { Provider = anime.Id < 0 ? "best" : "vost", Id = anime.SourceId,
                PageUrl = anime.PageUrl, Available = anime.AvailableEpisodes });
    }

    static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant().Replace('ё', 'е'), @"[^\p{L}\p{N}]", "");
    static string Format(string value)
    {
        if (Regex.IsMatch(value, @"^(ТВ|TV)(?:$|[\s(])", RegexOptions.IgnoreCase)) return "TV";
        if (Regex.IsMatch(value, @"^(Фильм|Movie)(?:$|[\s(])", RegexOptions.IgnoreCase)) return "MOVIE";
        if (Regex.IsMatch(value, @"^OVA(?:$|[\s(])", RegexOptions.IgnoreCase)) return "OVA";
        return Normalize(value);
    }
    public static bool SameTitle(Anime a, Anime b)
    {
        var aa = Cached(a); var bb = Cached(b);
        if (aa.Year is null || bb.Year is null || aa.Year != bb.Year || aa.Format.Length == 0 || aa.Format != bb.Format) return false;
        if (aa.PrimaryNumbers.Length > 0 && bb.PrimaryNumbers.Length > 0 && aa.PrimaryNumbers != bb.PrimaryNumbers) return false;
        return aa.Fingerprints.Intersect(bb.Fingerprints).Any();
    }
    static IEnumerable<TitleFingerprint> Fingerprints(Anime anime) => new[] { anime.Title, anime.RomanizedTitle, anime.NativeTitle }
        .SelectMany(s => s.Split([" / ", " | "], StringSplitOptions.RemoveEmptyEntries))
        .Select(TitleFingerprint.Create).Where(f => f.Text.Length > 0 || f.Numbers.Length > 0);
    public static void MigrateEpisodeKeys(WatchProgress p, IEnumerable<VostEpisode> episodes)
    {
        foreach (var ep in episodes)
        {
            foreach (var set in new[] { p.Started, p.Watched, p.KnownEpisodes, p.NewEpisodes })
                if (set.Remove(ep.LegacyKey)) set.Add(ep.Key);
            foreach (var set in p.SourceWatched.Values)
                if (set.Remove(ep.LegacyKey)) set.Add(ep.Key);
            if (p.LastEpisodeKey == ep.LegacyKey) p.LastEpisodeKey = ep.Key;
            foreach (var pair in p.SourcePositions.Where(x => x.Key.EndsWith("/" + ep.LegacyKey, StringComparison.Ordinal)).ToList())
            {
                p.SourcePositions.TryAdd(pair.Key[..^(ep.LegacyKey.Length)] + ep.Key, pair.Value);
                p.SourcePositions.Remove(pair.Key);
            }
        }
    }
}

public sealed class TitleComparisonCache
{
    public int Version { get; set; }
    public string Input { get; set; } = "";
    public int? Year { get; set; }
    public string Format { get; set; } = "";
    public string PrimaryNumbers { get; set; } = "";
    public List<TitleFingerprint> Fingerprints { get; set; } = [];
}

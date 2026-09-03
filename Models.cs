using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace AniTV;

public sealed class Anime : INotifyPropertyChanged
{
    public int Id { get; set; }
    public string SourceId { get; set; } = "";
    public List<AnimeSource> Sources { get; set; } = [];
    public TitleComparisonCache? ComparisonCache { get; set; }
    [JsonIgnore] public string SourcesLabel => string.Join("\n", Sources.Select(s => s.DisplayLabel));
    public string PageUrl { get; set; } = "";
    public string Title { get; set; } = "";
    public string NativeTitle { get; set; } = "";
    public string RomanizedTitle { get; set; } = "";
    public string CoverUrl { get; set; } = "";
    public string BannerUrl { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "";
    public string Format { get; set; } = "";
    public int? Episodes { get; set; }
    public int? AvailableEpisodes { get; set; }
    public bool TotalIsExact { get; set; }
    public int? Score { get; set; }
    public int? Year { get; set; }
    public string GenresText { get; set; } = "";
    public string Meta => string.Join("  •  ", new[] { Year?.ToString(), Format, AvailableEpisodes is null ? null : $"{AvailableEpisodes} из {(TotalIsExact ? Episodes?.ToString() : "?")} эп." }.Where(x => !string.IsNullOrWhiteSpace(x)));
    [JsonIgnore] public string ReleaseLabel => Status == "FINISHED" ? "ВЕСЬ СЕЗОН" : Status == "RELEASING" ? "ВЫХОДИТ" : "СТАТУС НЕИЗВЕСТЕН";
    [JsonIgnore] public string ReleaseColor => Status == "FINISHED" ? "#246A58" : "#59438B";
    [JsonIgnore] public string ProgressLabel { get; set; } = "";
    [JsonIgnore] public bool HasNewEpisodes { get; set; }
    [JsonIgnore] public bool IsCompleted { get; set; }
    [JsonIgnore] public bool CanRemoveFromContinue { get; set; }
    [JsonIgnore] public bool CanContinueFromLibrary { get; set; }
    public void Refresh() => OnPropertyChanged("");
    bool favorite;
    [JsonIgnore] public bool IsFavorite { get => favorite; set { favorite = value; OnPropertyChanged(); OnPropertyChanged(nameof(FavoriteLabel)); } }
    [JsonIgnore] public string FavoriteLabel => IsFavorite ? "♥  В избранном" : "♡  В избранное";
    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new(n));
}

public sealed record GenreChip(string Name, string Background, string Border, string Foreground)
{
    public static GenreChip CreateFilter(string name, int index)
    {
        // Golden-angle hues keep neighbouring filters visually distinct; the index is unique in the filter list.
        var hue = (index * 137.508) % 360;
        var saturation = 0.50 + (index % 3) * 0.06;
        var value = 0.48 + (index % 2) * 0.05;
        static string Color(double h, double s, double v, double scale)
        {
            v=Math.Clamp(v*scale,0,1); var c=v*s; var x=c*(1-Math.Abs(h/60%2-1)); var m=v-c;
            var (r,g,b)=h switch { <60=>(c,x,0d), <120=>(x,c,0d), <180=>(0d,c,x), <240=>(0d,x,c), <300=>(x,0d,c), _=>(c,0d,x) };
            return $"#{(int)((r+m)*255):X2}{(int)((g+m)*255):X2}{(int)((b+m)*255):X2}";
        }
        return new(name,Color(hue,saturation,value,.48),Color(hue,saturation,value,1.25),"#F1F3F8");
    }

    public static GenreChip Create(string name)
    {
        var key = name.Trim().ToLowerInvariant().Replace('ё','е');
        var palette = new[] {
            ("#173D3A", "#36877A", "#9DE5D4"), ("#35254D", "#8560B5", "#DBC0FF"),
            ("#482839", "#B65C87", "#FFC0DC"), ("#49391D", "#A98940", "#F5DA8E"),
            ("#203A50", "#4C87AD", "#A8DFFF"), ("#492B28", "#B76C59", "#FFC5AE"),
            ("#263F2C", "#598E66", "#B7E8BC"), ("#30314A", "#7375AD", "#CACBFF") };
        var index = key switch { "приключения" => 0, "фэнтези" => 1, "романтика" => 2, "комедия" => 3,
            "фантастика" => 4, "боевик" or "экшен" => 5, "повседневность" => 6, "драма" => 7, _ => -1 };
        if (index < 0) { uint hash=2166136261; foreach(var c in key) hash=unchecked((hash^c)*16777619); index=(int)(hash%(uint)palette.Length); }
        var colors=palette[index]; return new(name.Trim(),colors.Item1,colors.Item2,colors.Item3);
    }
}

public sealed class UserState
{
    public List<int> HomeCatalogIds { get; set; } = [];
    public DateTimeOffset? HomeCatalogUpdatedAt { get; set; }
    public Dictionary<string, CatalogMetadata> BestMetadata { get; set; } = [];
    public Dictionary<string, List<int>> GenreCatalogIds { get; set; } = [];
    public int SchemaVersion { get; set; }
    public HashSet<int> Favorites { get; set; } = [];
    public List<int> RecentlyViewed { get; set; } = [];
    public Dictionary<int, Anime> Titles { get; set; } = [];
    public Dictionary<int, WatchProgress> Progress { get; set; } = [];
}

public sealed class CatalogMetadata
{
    public Anime Title { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class WatchProgress
{
    public bool IsComplete(Anime anime) => anime.Status == "FINISHED" && anime.TotalIsExact && anime.Episodes is > 0 and <= 100000
        && Watched.Contains("episode:" + anime.Episodes.Value);
    public string LastSourceKey { get; set; } = "";
    public Dictionary<string, double> SourcePositions { get; set; } = [];
    public Dictionary<string, HashSet<string>> SourceWatched { get; set; } = [];
    public Dictionary<string, SourceRelease> SourceReleases { get; set; } = [];
    public void ObserveSource(AnimeSource source, IEnumerable<string>? keys = null)
    {
        if (keys is null && source.Available is null) return;
        var count = source.Available ?? 0;
        if (keys is null && SourceReleases.TryGetValue(source.Key, out var previous) && previous.Count == count)
        { source.HasNewEpisodes = previous.New.Except(Started).Any(); return; }
        var snapshot = keys?.ToHashSet() ?? Enumerable.Range(1, Math.Clamp(count,0,100000)).Select(i => "episode:" + i).ToHashSet();
        if (!SourceReleases.TryGetValue(source.Key,out var release))
        {
            release = new SourceRelease(); SourceReleases[source.Key] = release;
            release.New.UnionWith(snapshot.Intersect(NewEpisodes));
            if (KnownAvailable > 0) release.New.UnionWith(snapshot.Where(k => k.StartsWith("episode:") && int.TryParse(k[8..],out var n) && n > KnownAvailable));
        }
        else release.New.UnionWith(snapshot.Except(release.Known));
        release.Known.UnionWith(snapshot); release.Current = snapshot; release.Count = count;
        release.New.IntersectWith(snapshot); release.New.ExceptWith(Started);
        source.HasNewEpisodes = release.New.Count > 0;
    }
    public string LastEpisodeKey { get; set; } = "";
    public string LastEpisodeName { get; set; } = "";
    public double PositionSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public int MaxEpisodeNumber { get; set; }
    public DateTimeOffset LastWatchedAt { get; set; }
    public HashSet<string> Started { get; set; } = [];
    public HashSet<string> Watched { get; set; } = [];
    public HashSet<string> KnownEpisodes { get; set; } = [];
    public HashSet<string> NewEpisodes { get; set; } = [];
    public int KnownAvailable { get; set; }
    public bool HasBaseline { get; set; }
    public bool HiddenFromContinue { get; set; }

    public void Observe(IEnumerable<string> episodeKeys)
    {
        var ordered = episodeKeys.Distinct().ToList();
        var keys = ordered.ToHashSet();
        if (HasBaseline) NewEpisodes.UnionWith(keys.Except(KnownEpisodes).Except(Started));
        else if (KnownAvailable > 0) NewEpisodes.UnionWith(ordered.Skip(KnownAvailable).Except(Started));
        KnownEpisodes.UnionWith(keys);
        KnownAvailable = KnownEpisodes.Count;
        HasBaseline = true;
    }
    public void Record(string key, string name, int number, double position, double duration, bool ended = false, string? sourceKey = null)
    {
        HiddenFromContinue = false;
        LastEpisodeKey = key; LastEpisodeName = name;
        PositionSeconds = Math.Max(0, position); DurationSeconds = Math.Max(0, duration);
        MaxEpisodeNumber = Math.Max(MaxEpisodeNumber, number);
        LastWatchedAt = DateTimeOffset.UtcNow;
        Started.Add(key); NewEpisodes.Remove(key);
        foreach (var release in SourceReleases.Values) release.New.Remove(key);
        if (ended || (duration > 0 && position >= duration * 0.8))
        {
            Watched.Add(key);
            if (!string.IsNullOrEmpty(sourceKey))
            {
                if (!SourceWatched.TryGetValue(sourceKey,out var watched)) SourceWatched[sourceKey] = watched = [];
                watched.Add(key);
            }
        }
    }
}

public sealed class SourceRelease
{
    public int Count { get; set; }
    public HashSet<string> Known { get; set; } = [];
    public HashSet<string> Current { get; set; } = [];
    public HashSet<string> New { get; set; } = [];
}

public static class PlaybackChoice
{
    public static StreamQuality Maximum(IReadOnlyList<StreamQuality> qualities) => qualities.OrderByDescending(q => int.TryParse(System.Text.RegularExpressions.Regex.Match(q.Name,@"\d+").Value,out var n) ? n : 0).First();
    public static (int Index, double Position) Resume(WatchProgress p, string source, IReadOnlyList<VostEpisode> episodes)
    {
        var index = episodes.ToList().FindIndex(e => e.Key == p.LastEpisodeKey);
        if(index < 0) return (0,0);
        var same = p.LastSourceKey == source || string.IsNullOrEmpty(p.LastSourceKey);
        var watched = p.SourceWatched.TryGetValue(source,out var set) ? set.Contains(p.LastEpisodeKey) : same && p.Watched.Contains(p.LastEpisodeKey);
        if(same && watched && index+1<episodes.Count && episodes[index].Number>0 && episodes[index+1].Number==episodes[index].Number+1) return (index+1,0);
        return (index,same ? p.PositionSeconds : p.SourcePositions.GetValueOrDefault(source+"/"+episodes[index].Key));
    }
}

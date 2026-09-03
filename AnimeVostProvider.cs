using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AniTV;

public sealed record VostTitle(string Id, string Name, Uri PageUrl);
public sealed record VostEpisode(string Name, Uri StandardUrl, Uri HdUrl, Uri? PreviewUrl)
{
    public string LegacyKey => System.IO.Path.GetFileNameWithoutExtension(StandardUrl.AbsolutePath);
    public string Key => Number > 0 ? "episode:" + Number : "special:" + Referrer + Name;
    public string Referrer { get; init; } = "https://v13.vost.pw/";
    public bool IsHls { get; init; }
    public int Number => int.TryParse(Regex.Match(Name, @"^\s*(\d+)\s*(?:серия|эпизод)?\s*$", RegexOptions.IgnoreCase).Groups[1].Value, out var n) ? n : 0;
    public bool IsWatched { get; set; }
    public string DisplayName => (IsWatched ? "✓  " : "") + Name;
    public override string ToString() => Name;
}

public sealed partial class AnimeVostProvider
{
    static readonly Uri Site = new("https://v13.vost.pw/");
    static readonly Uri PlaylistEndpoint = new("https://api.animevost.org/v1/playlist");
    readonly HttpClient http = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All });

    public AnimeVostProvider()
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AniTV/1.0 (authorized AnimeVost client)");
        http.DefaultRequestHeaders.Referrer = Site;
        http.Timeout = TimeSpan.FromSeconds(20);
    }

    public async Task<List<Anime>> GetCatalogAsync(string? search = null, CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(search) ? Site : new Uri(Site, $"index.php?do=search&subaction=search&story={Uri.EscapeDataString(search.Trim())}");
        if (!string.IsNullOrWhiteSpace(search)) return await GetCatalogPageAsync(url, cancellationToken);
        return await new CatalogPager(GetCatalogPageAsync).TakeAsync(50, cancellationToken);
    }

    public async Task<List<Anime>> GetCatalogPageAsync(int pageNumber, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1) throw new ArgumentOutOfRangeException(nameof(pageNumber));
        try { return await GetCatalogPageAsync(pageNumber == 1 ? Site : new Uri(Site, $"page/{pageNumber}/"), cancellationToken); }
        catch (HttpRequestException ex) when (pageNumber > 1 && ex.StatusCode == HttpStatusCode.NotFound) { return []; }
    }

    public async Task<List<Anime>> GetGenrePageAsync(string slug, int pageNumber, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1) throw new ArgumentOutOfRangeException(nameof(pageNumber));
        var path = $"zhanr/{slug}/" + (pageNumber == 1 ? "" : $"page/{pageNumber}/");
        try { return await GetCatalogPageAsync(new Uri(Site, path), cancellationToken); }
        catch (HttpRequestException ex) when (pageNumber > 1 && ex.StatusCode == HttpStatusCode.NotFound) { return []; }
    }

    async Task<List<Anime>> GetCatalogPageAsync(Uri url, CancellationToken cancellationToken)
    {
        var html = await http.GetStringAsync(url, cancellationToken);
        var result = new List<Anime>();
        foreach (Match match in CatalogCardRegex().Matches(html))
        {
            var idText = match.Groups["id"].Value;
            if (!int.TryParse(idText, out var id) || result.Any(x => x.Id == id)) continue;
            var rawTitle = CleanText(match.Groups["title"].Value);
            var release = ParseRelease(rawTitle, match.Groups["block"].Value.Contains("/ongoing/"));
            rawTitle = EpisodeSuffixRegex().Replace(rawTitle, "").Trim();
            var titleParts = rawTitle.Split(" / ", 2, StringSplitOptions.TrimEntries);
            var block = match.Groups["block"].Value;
            string Field(string label) { var m = Regex.Match(block, $@"<strong>{Regex.Escape(label)}\s*</strong>\s*(?<v>[^<]+)", RegexOptions.IgnoreCase); return m.Success ? CleanText(m.Groups["v"].Value) : ""; }
            var descriptionMatch = Regex.Match(block, @"Описание:\s*</strong>\s*(?<v>.*?)(?:</p>|</td>)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var yearText = Field("Год выхода:"); int? year = int.TryParse(Regex.Match(yearText, @"\d{4}").Value, out var y) ? y : null;
            var episodesText = Field("Количество серий:"); int? count = int.TryParse(Regex.Match(episodesText, @"^\d+").Value, out var c) ? c : null;
            var image = WebUtility.HtmlDecode(match.Groups["image"].Value);
            var page = WebUtility.HtmlDecode(match.Groups["url"].Value);
            result.Add(new Anime { Id = id, SourceId = idText, PageUrl = page, Title = titleParts[0], RomanizedTitle = titleParts.Length > 1 ? titleParts[1] : "", NativeTitle = titleParts.Length > 1 ? titleParts[1] : "", CoverUrl = new Uri(Site, image).AbsoluteUri, Description = descriptionMatch.Success ? CleanText(descriptionMatch.Groups["v"].Value) : "Описание пока отсутствует.", Format = Field("Тип:"), Episodes = count, Year = year, GenresText = Field("Жанр:") });
            result[^1].AvailableEpisodes = release.Available;
            result[^1].Episodes = release.Total ?? count;
            result[^1].TotalIsExact = release.Exact;
            result[^1].Status = release.Status;
        }
        return result;
    }

    public static (int? Available, int? Total, bool Exact, string Status) ParseRelease(string title, bool ongoing)
    {
        var m = Regex.Match(title, @"\[(?:\d+\s*[-–]\s*)?(?<available>\d+)\s+из\s+(?<total>\d+)(?<plus>\+)?", RegexOptions.IgnoreCase);
        if (!m.Success) return (null, null, false, ongoing ? "RELEASING" : "UNKNOWN");
        var available = int.Parse(m.Groups["available"].Value);
        var total = int.Parse(m.Groups["total"].Value);
        var exact = !m.Groups["plus"].Success;
        return (available, total, exact, exact && available >= total ? "FINISHED" : "RELEASING");
    }

    public async Task<VostTitle?> FindTitleAsync(IEnumerable<string> names, CancellationToken cancellationToken = default)
    {
        foreach (var name in names.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
        {
            var url = new Uri(Site, $"index.php?do=search&subaction=search&story={Uri.EscapeDataString(name)}");
            var html = await http.GetStringAsync(url, cancellationToken);
            var matches = TitleLinkRegex().Matches(html);
            foreach (Match match in matches)
            {
                var path = WebUtility.HtmlDecode(match.Groups["url"].Value);
                var id = match.Groups["id"].Value;
                var pageUrl = Uri.TryCreate(path, UriKind.Absolute, out var absolute) ? absolute : new Uri(Site, path);
                var title = SlugToTitle(match.Groups["slug"].Value);
                return new VostTitle(id, title, pageUrl);
            }
        }
        return null;
    }

    public async Task<IReadOnlyList<VostEpisode>> GetEpisodesAsync(string titleId, CancellationToken cancellationToken = default)
    {
        using var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = titleId });
        using var request = new HttpRequestMessage(HttpMethod.Post, PlaylistEndpoint) { Content = body };
        request.Headers.Referrer = Site;
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var rows = JsonSerializer.Deserialize<List<PlaylistRow>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        return rows.Where(x => Uri.TryCreate(x.Std, UriKind.Absolute, out _) && Uri.TryCreate(x.Hd, UriKind.Absolute, out _))
            .Select(x => new VostEpisode(x.Name ?? "Серия", new Uri(x.Std!), new Uri(x.Hd!), Uri.TryCreate(x.Preview, UriKind.Absolute, out var p) ? p : null)).OrderBy(x => x.Number).ToList();
    }

    static string SlugToTitle(string slug) => WebUtility.HtmlDecode(slug.Replace('-', ' '));
    static string CleanText(string value) => WebUtility.HtmlDecode(Regex.Replace(value, "<.*?>", " ")).Replace("  ", " ").Trim();
    sealed class PlaylistRow { public string? Name { get; set; } public string? Hd { get; set; } public string? Std { get; set; } public string? Preview { get; set; } }

    [GeneratedRegex("(?:href=[\\\"'])(?<url>(?:https://v13\\.vost\\.pw)?/tip/[^\\\"']+?/(?<id>\\d+)-(?<slug>[^\\\"'/]+)\\.html)(?:[\\\"'])", RegexOptions.IgnoreCase)]
    private static partial Regex TitleLinkRegex();

    [GeneratedRegex("<div\\s+class=[\\\"']shortstory[\\\"'](?<block>[\\s\\S]*?<h2>[\\s\\S]*?<a\\s+href=[\\\"'](?<url>https?://[^\\\"']+/tip/[^\\\"']+?/(?<id>\\d+)-[^\\\"']+?\\.html)[\\\"'][^>]*>(?<title>[\\s\\S]*?)</a>[\\s\\S]*?<img[^>]+src=[\\\"'](?<image>[^\\\"']+)[\\\"'][\\s\\S]*?<div\\s+class=[\\\"']shortstoryFuter[\\\"'][\\s\\S]*?</div>)", RegexOptions.IgnoreCase)]
    private static partial Regex CatalogCardRegex();

    [GeneratedRegex("\\s*\\[[^\\]]*(?:серия|из)[^\\]]*\\](?:\\s*\\[[^\\]]*\\])*$", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeSuffixRegex();
}

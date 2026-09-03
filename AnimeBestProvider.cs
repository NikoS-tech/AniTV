using System.Net;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AniTV;

public sealed class AnimeBestProvider
{
    readonly HttpClient http = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(20) };
    static readonly Uri Site = new("https://anime1.best/");
    Dictionary<string, CatalogMetadata> metadata = [];
    public void SetMetadataCache(Dictionary<string, CatalogMetadata> cache) => metadata = cache;
    public AnimeBestProvider() { http.DefaultRequestHeaders.UserAgent.ParseAdd("AniTV/1.0"); http.DefaultRequestHeaders.Referrer = Site; }
    public AnimeBestProvider(HttpClient client) { http = client; }
    static string Clean(string text) => WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(text, @"<script\b[^>]*>[\s\S]*?</script>|<style\b[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase), "<[^>]+>", " ")).Trim();
    static string Match(string html, string pattern) => Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value;

    public async Task<List<Anime>> GetCatalogPageAsync(int page, CancellationToken token = default)
    {
        try { return await ReadCatalog(new Uri(Site, page == 1 ? "/" : $"/page/{page}/"), token); }
        catch (HttpRequestException ex) when (page > 1 && ex.StatusCode == HttpStatusCode.NotFound) { return []; }
    }
    public async Task<List<Anime>> GetGenrePageAsync(string path, int page, CancellationToken token = default)
    {
        var suffix = path.Trim('/') + "/" + (page == 1 ? "" : $"page/{page}/");
        try { return await ReadCatalog(new Uri(Site, suffix), token); }
        catch (HttpRequestException ex) when (page > 1 && ex.StatusCode == HttpStatusCode.NotFound) { return []; }
    }
    public Task<List<Anime>> SearchAsync(string query, CancellationToken token) => ReadCatalog(new Uri(Site, "index.php?do=search&subaction=search&story=" + Uri.EscapeDataString(query)), token);
    async Task<List<Anime>> ReadCatalog(Uri url, CancellationToken token)
    {
        var html = await http.GetStringAsync(url, token);
        var cards = Regex.Matches(html, @"<h2\b[^>]*>\s*<a[^>]+href=""([^""]+\.html)""[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(m => (Url: new Uri(Site, WebUtility.HtmlDecode(m.Groups[1].Value)), Title: Clean(m.Groups[2].Value)))
            .DistinctBy(c => c.Url).ToList();
        using var slots = new SemaphoreSlim(4);
        var result = await Task.WhenAll(cards.Select(async card =>
        {
            token.ThrowIfCancellationRequested();
            CatalogMetadata? saved;
            lock (metadata) metadata.TryGetValue(card.Url.AbsoluteUri, out saved);
            if (saved is not null && FromCachedCard(saved, card.Title, DateTimeOffset.UtcNow) is { } cached)
                return cached;
            await slots.WaitAsync(token);
            try
            {
                var anime = ParseTitle(await http.GetStringAsync(card.Url, token), card.Url);
                token.ThrowIfCancellationRequested();
                if (anime is not null)
                    lock (metadata) metadata[card.Url.AbsoluteUri] = new() { Title = anime, UpdatedAt = DateTimeOffset.UtcNow };
                return anime is null ? null : Clone(anime);
            }
            finally { slots.Release(); }
        }));
        return result.Where(a => a is not null).Cast<Anime>().ToList();
    }
    static Anime Clone(Anime anime) => JsonSerializer.Deserialize<Anime>(JsonSerializer.Serialize(anime))!;
    public static Anime? FromCachedCard(CatalogMetadata saved, string title, DateTimeOffset now)
    {
        // Refresh descriptive metadata periodically; episode counts always come from the current listing.
        if (now < saved.UpdatedAt || now - saved.UpdatedAt > TimeSpan.FromDays(7)) return null;
        var name = Regex.Replace(title, @"\s*\[[^\]]*\]\s*$", "");
        if (!string.Equals(name, saved.Title.Title, StringComparison.Ordinal)) return null;
        var release = AnimeVostProvider.ParseRelease(title, false);
        if (release.Available is null) return null; // Unrecognised listing: fetch full metadata safely.
        var anime = Clone(saved.Title);
        anime.AvailableEpisodes = release.Available; anime.Episodes = release.Total;
        anime.TotalIsExact = release.Exact; anime.Status = release.Status;
        foreach (var source in anime.Sources) source.Available = release.Available;
        return anime;
    }
    public static Anime? ParseTitle(string html, Uri url)
    {
        var idText = Match(url.AbsolutePath, @"/(\d+)-");
        if (!int.TryParse(idText, out var id)) return null;
        var title = Clean(Match(html, @"<h1\b[^>]*>(.*?)</h1>"));
        if (string.IsNullOrWhiteSpace(title)) return null;
        string Field(string label) => Clean(Match(html, @"<strong>" + label + @"\s*</strong>\s*</div>\s*<div[^>]*>(.*?)</div>"));
        var year = Regex.Match(Field("Вышел:"), @"\d{4}").Value;
        var release = AnimeVostProvider.ParseRelease(title, html.Contains("fbadge-ongoing"));
        var aliases = Clean(Match(html, @"class=""finfo-text1[^""]*""[^>]*>(.*?)</div>"));
        var cover = WebUtility.HtmlDecode(Match(html, @"<meta\s+property=""og:image""\s+content=""([^""]+)"""));
        if (cover.Length == 0) cover = WebUtility.HtmlDecode(Match(html, @"<meta\s+itemprop=""url""\s+content=""([^""]+)"""));
        var anime = new Anime { Id = -id, SourceId = idText, PageUrl = url.AbsoluteUri,
            Title = Regex.Replace(title, @"\s*\[[^\]]*\]\s*$", ""), RomanizedTitle = aliases.Split(" / ")[0],
            NativeTitle = aliases, Year = int.TryParse(year, out var y) ? y : null,
            Format = Field("Тип:"), GenresText = Field("Жанр:"),
            Description = Clean(Match(html, @"<meta\s+name=""description""\s+content=""([^""]+)""")),
            CoverUrl = Uri.TryCreate(Site, cover, out var image) && cover.Length > 0 ? image.AbsoluteUri : "",
            AvailableEpisodes = release.Available, Episodes = release.Total, TotalIsExact = release.Exact, Status = release.Status };
        SourceMatching.EnsureSource(anime);
        return anime;
    }
    public async Task<IReadOnlyList<VostEpisode>> GetEpisodesAsync(AnimeSource source, CancellationToken token = default)
    {
        var html = await http.GetStringAsync(source.PageUrl, token);
        return ParseEpisodes(html);
    }
    public static IReadOnlyList<VostEpisode> ParseEpisodes(string html)
    {
        // Only the public Playerjs playlist, never execute arbitrary scripts from the site.
        var list = Match(html, @"new\s+Playerjs\s*\(\s*\{[\s\S]*?\bfile\s*:\s*(\[[\s\S]*?\])\s*\}");
        if (list.Length == 0) return [];
        using var json = JsonDocument.Parse(list);
        return json.RootElement.EnumerateArray().Where(row => row.TryGetProperty("file", out _)).Select(row =>
        {
            var file = row.GetProperty("file").GetString();
            if (!Uri.TryCreate(file, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.AbsolutePath.EndsWith(".m3u8")) return null;
            return new VostEpisode(row.GetProperty("title").GetString() ?? "Серия", uri, uri, null) { IsHls = true, Referrer = Site.AbsoluteUri };
        }).Where(e => e is not null).Cast<VostEpisode>().OrderBy(e => e.Number).ToList();
    }
    public async Task<IReadOnlyList<StreamQuality>> GetQualitiesAsync(VostEpisode episode, CancellationToken token)
    {
        if (!episode.IsHls) return [new("720p", episode.HdUrl), new("480p", episode.StandardUrl)];
        var text = await http.GetStringAsync(episode.StandardUrl, token);
        return ParseQualities(text, episode.StandardUrl);
    }
    public static IReadOnlyList<StreamQuality> ParseQualities(string text, Uri master)
    {
        if (!text.TrimStart().StartsWith("#EXTM3U")) throw new InvalidDataException("Сервер вернул не HLS-плейлист.");
        var result = new List<StreamQuality> { new("Авто", master) };
        var lines = text.Split('\n').Select(l => l.Trim()).ToArray();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("#EXT-X-STREAM-INF:")) continue;
            var height = Match(lines[i], @"RESOLUTION=\d+x(\d+)");
            var next = lines.Skip(i + 1).FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));
            if (height.Length > 0 && next is not null && Uri.TryCreate(master, next, out var uri) && uri.Scheme == "https") result.Add(new(height + "p", uri));
        }
        return result.DistinctBy(q => q.Name).ToList();
    }
}

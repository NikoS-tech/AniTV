using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AniTV;

public sealed class AniListService
{
    readonly HttpClient http = new() { BaseAddress = new Uri("https://graphql.anilist.co/") };
    const string Query = """
    query ($page:Int,$search:String) { Page(page:$page,perPage:24) { media(type:ANIME,search:$search,sort:POPULARITY_DESC,isAdult:false) { id title { english romaji native } coverImage { extraLarge } bannerImage description(asHtml:false) status format episodes averageScore seasonYear genres } } }
    """;
    public async Task<List<Anime>> GetAsync(string? search = null)
    {
        var payload = JsonSerializer.Serialize(new { query = Query, variables = new { page = 1, search = string.IsNullOrWhiteSpace(search) ? null : search.Trim() } });
        using var response = await http.PostAsync("", new StringContent(payload, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var list = new List<Anime>();
        foreach (var m in doc.RootElement.GetProperty("data").GetProperty("Page").GetProperty("media").EnumerateArray())
        {
            string? S(JsonElement e, string n) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            int? I(JsonElement e, string n) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;
            var titles = m.GetProperty("title");
            var genres = m.GetProperty("genres").EnumerateArray().Select(x => x.GetString()).Where(x => x is not null);
            list.Add(new Anime { Id = m.GetProperty("id").GetInt32(), Title = S(titles,"english") ?? S(titles,"romaji") ?? "Без названия", RomanizedTitle = S(titles,"romaji") ?? "", NativeTitle = S(titles,"native") ?? "", CoverUrl = S(m.GetProperty("coverImage"),"extraLarge") ?? "", BannerUrl = S(m,"bannerImage") ?? "", Description = Clean(S(m,"description") ?? "Описание пока отсутствует."), Status = S(m,"status") ?? "", Format = S(m,"format")?.Replace('_',' ') ?? "", Episodes = I(m,"episodes"), Score = I(m,"averageScore"), Year = I(m,"seasonYear"), GenresText = string.Join("  •  ", genres!) });
        }
        return list;
    }
    static string Clean(string value) => Regex.Replace(value.Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase), "<.*?>", "").Trim();
}

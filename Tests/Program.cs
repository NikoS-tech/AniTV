using AniTV;
using System.Text.Json;

var count = 0;
await SourceTests.Run(args);
void Check(bool result, string name) { if (!result) throw new Exception(name); Console.WriteLine("PASS: " + name); count++; }
var progress = new WatchProgress();
progress.Observe(new[] { "ep1", "ep2", "ep3" });
Check(progress.NewEpisodes.Count == 0, "First discovery is not a new release");
progress.Record("ep3", "3 серия", 3, 95, 100);
progress.Record("ep2", "2 серия", 2, 24, 100);
Check(progress.LastEpisodeKey == "ep2" && progress.PositionSeconds == 24, "Resume moves backwards from episode 3 to 2");
Check(progress.MaxEpisodeNumber == 3, "Highest started episode stays 3");
Check(progress.Watched.SetEquals(new[] { "ep3" }), "Watched episodes independent of last and maximum");
progress.Observe(new[] { "ep1", "ep2", "ep3", "ep4", "ep5" });
Check(progress.NewEpisodes.SetEquals(new[] { "ep4", "ep5" }), "New episodes are detected by stable identity");
progress.Record("ep4", "4 серия", 4, 0, 100);
Check(!progress.NewEpisodes.Contains("ep4") && progress.NewEpisodes.Contains("ep5"), "Starting a new episode clears only its own badge");
progress.Record("ep2", "2 серия", 2, 100, 100, true);
Check(progress.Watched.SetEquals(new[] { "ep2", "ep3" }) && progress.MaxEpisodeNumber == 4, "End marks only that episode watched");
var state = new UserState { Progress = { [3960] = progress }, Titles = { [3960] = new Anime { Id = 3960, SourceId = "3960", Title = "Тест" } } };
var restored = JsonSerializer.Deserialize<UserState>(JsonSerializer.Serialize(state))!;
Check(restored.Progress[3960].LastEpisodeKey == "ep2" && restored.Progress[3960].PositionSeconds == 100 && restored.Titles[3960].SourceId == "3960", "Restart preserves library and exact checkpoint");
Check(restored.Progress[3960].Watched.SetEquals(progress.Watched) && restored.Progress[3960].NewEpisodes.SetEquals(progress.NewEpisodes), "Restart preserves watched/new sets");
Check(JsonSerializer.Deserialize<UserState>("{\"Favorites\":[123],\"RecentlyViewed\":[123]}")!.Progress.Count == 0, "Old save schema loads without loss");
var release = AnimeVostProvider.ParseRelease("Название [1-9 из 12]", true);
Check(release.Available == 9 && release.Total == 12 && release.Status == "RELEASING", "Ongoing release count");
Check(AnimeVostProvider.ParseRelease("Название [1-12 из 12]", false).Status == "FINISHED", "Finished season badge");
Check(AnimeVostProvider.ParseRelease("Название [1-12 из 12+]", false).Status == "RELEASING", "Open-ended season not incorrectly finished");
Check(AnimeVostProvider.ParseRelease("Название", false).Status == "UNKNOWN", "Missing source data does not invent status");
var baseline = new WatchProgress { KnownAvailable = 2 };
baseline.Observe(new[] { "a", "b", "c" });
Check(baseline.NewEpisodes.SetEquals(new[] { "c" }), "Catalog baseline detects a release before first playlist fetch");
progress.HiddenFromContinue = true;
var hidden = JsonSerializer.Deserialize<WatchProgress>(JsonSerializer.Serialize(progress))!;
Check(hidden.HiddenFromContinue && hidden.PositionSeconds == progress.PositionSeconds && hidden.Watched.SetEquals(progress.Watched), "Removing from continue persists without deleting watch history");
hidden.Observe(new[] { "ep1", "ep2", "ep3", "ep4", "ep5", "ep6" });
Check(hidden.HiddenFromContinue, "Background refresh does not return hidden titles");
hidden.Record("ep2", "2 серия", 2, 20, 100);
Check(!hidden.HiddenFromContinue && hidden.MaxEpisodeNumber == 4, "Playback returns title to continue without losing maximum episode");
Console.WriteLine($"{count} tests passed.");
var pager = new CatalogPager((page, _) => Task.FromResult(Enumerable.Range((page - 1) * 13 + 1, 13)
    .Where(id => id <= 76).Select(id => new Anime { Id = id }).ToList()));
Check((await pager.TakeAsync(50)).Count == 50, "Initial batch contains 50");
Check((await pager.TakeAsync(20)).Select(a => a.Id).SequenceEqual(Enumerable.Range(51, 20)), "Scroll adds the next 20 without skipping page overflow");
Check((await pager.TakeAsync(20)).Count == 6 && !pager.HasMore, "Final batch stops pagination");
var repeated = new CatalogPager((_, _) => Task.FromResult(new List<Anime> { new() { Id = 1 } }));
Check((await repeated.TakeAsync(50)).Count == 1 && !repeated.HasMore, "Repeated pages do not loop");
var reversedEpisodes = new[] { 3, 2, 1 }.Select(n => new VostEpisode($"{n} серия", new Uri($"https://example.org/{n}"), new Uri($"https://example.org/{n}"), null)).ToList();
Check(EpisodeNavigation.AdjacentIndex(reversedEpisodes, reversedEpisodes[1], 1) == 0, "Next episode follows the greater episode number even in reversed source order");
Check(EpisodeNavigation.AdjacentIndex(reversedEpisodes, reversedEpisodes[1], -1) == 2, "Previous episode follows the smaller episode number even in reversed source order");
var labelChanges = 0;
reversedEpisodes[1].PropertyChanged += (_, e) => { if(e.PropertyName == nameof(VostEpisode.DisplayName)) labelChanges++; };
reversedEpisodes[1].IsWatched = true;
reversedEpisodes[1].IsDownloaded = true;
Check(labelChanges == 2 && reversedEpisodes[1].DisplayName.Contains("✓") && reversedEpisodes[1].DisplayName.Contains("↓"), "Episode badges update without refreshing selector items");
var detachedCurrent = new VostEpisode("2 серия", new Uri("https://mirror.example.org/2"), new Uri("https://mirror.example.org/2"), null);
Check(EpisodeNavigation.AdjacentIndex(reversedEpisodes, detachedCurrent, 1) == 0, "Episode navigation survives replacement instances by stable key");
if (args.Contains("--live-catalog"))
{
    var livePager = new CatalogPager(new AnimeVostProvider().GetCatalogPageAsync);
    var titles = await livePager.TakeAsync(50);
    Check(titles.Count == 50 && titles.Select(x => x.Id).Distinct().Count() == 50,
        "Home loads exactly 50 unique titles from AnimeVost");
    var more = await livePager.TakeAsync(20);
    Check(more.Count == 20 && titles.Concat(more).Select(a => a.Id).Distinct().Count() == 70, "Live scroll adds 20 unique titles");
}

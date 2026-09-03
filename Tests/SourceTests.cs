using AniTV;
using System.Net.Http;

static class SourceTests
{
    sealed class CachedCatalogHandler : HttpMessageHandler
    {
        public int ListRequests, DetailRequests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string html;
            if (request.RequestUri!.AbsolutePath.EndsWith(".html"))
            {
                DetailRequests++;
                html = "<h1>Тест (1 сезон) [1-3 из 12]</h1>";
            }
            else
            {
                ListRequests++;
                html = $"<h2><a href=\"https://anime1.best/1-test.html\">Тест (1 сезон) [1-{(ListRequests == 1 ? 3 : 4)} из 12]</a></h2>";
            }
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(html) });
        }
    }
    public static async Task Run(string[] args)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS: " + name); }
        Check(CatalogGenres.All.Count >= 20 && CatalogGenres.All.Select(g=>g.Name).Distinct().Count()==CatalogGenres.All.Count, "Genre catalog has unique common filters");
        Check(CatalogGenres.All.All(g=>g.VostSlug.Length>0 && g.BestPath.Length>0), "Each genre maps both providers");
        Check(CatalogGenres.All.Select((g,i)=>GenreChip.CreateFilter(g.Name,i).Background).Distinct().Count()==CatalogGenres.All.Count, "Every catalog genre has a unique chip color");
        var cacheNow = DateTimeOffset.UtcNow;
        var cachedBest = new CatalogMetadata { UpdatedAt = cacheNow, Title = new Anime { Id = -1, Title = "Тест (1 сезон)", Year = 2026,
            Sources = [new AnimeSource {Provider="best", Id="1", Available=3}], AvailableEpisodes=3 } };
        var freshBest = AnimeBestProvider.FromCachedCard(cachedBest, "Тест (1 сезон) [1-4 из 12]", cacheNow)!;
        Check(freshBest.AvailableEpisodes == 4 && freshBest.Sources[0].Available == 4 && freshBest.Status == "RELEASING", "Cached metadata uses live release count per source");
        Check(cachedBest.Title.AvailableEpisodes == 3 && cachedBest.Title.Sources[0].Available == 3, "Cached provider metadata is detached from merged titles");
        Check(AnimeBestProvider.FromCachedCard(cachedBest,"Тест (1 сезон) [1-12 из 12]",cacheNow)!.Status == "FINISHED", "Cached title updates completed status from listing");
        Check(AnimeBestProvider.FromCachedCard(cachedBest,"Тест (1 сезон) [1-4 из 12]",cacheNow.AddDays(8)) is null, "Expired metadata requires full refresh");
        Check(AnimeBestProvider.FromCachedCard(cachedBest,"Другой сезон [1-4 из 12]",cacheNow) is null, "Renamed title requires full refresh");
        Check(AnimeBestProvider.FromCachedCard(cachedBest,"Тест (1 сезон)",cacheNow) is null, "Unknown release syntax falls back to full page");
        var cacheState = new UserState { HomeCatalogIds=[3,1,2], HomeCatalogUpdatedAt=cacheNow, BestMetadata = { ["test"] = cachedBest } };
        var roundtripCache = System.Text.Json.JsonSerializer.Deserialize<UserState>(System.Text.Json.JsonSerializer.Serialize(cacheState))!;
        Check(roundtripCache.HomeCatalogIds.SequenceEqual(new[]{3,1,2}) && roundtripCache.BestMetadata["test"].UpdatedAt == cacheNow, "Catalog order and metadata cache survive restart");
        using var bestHandler = new CachedCatalogHandler();
        var cachedProvider = new AnimeBestProvider(new HttpClient(bestHandler));
        var providerCache = new Dictionary<string,CatalogMetadata>();
        cachedProvider.SetMetadataCache(providerCache);
        var firstBest = await cachedProvider.GetCatalogPageAsync(1);
        var nextBest = await cachedProvider.GetCatalogPageAsync(1);
        Check(firstBest.Count == 1 && nextBest.Count == 1 && bestHandler.DetailRequests == 1 && bestHandler.ListRequests == 2, "Second catalog load avoids known title detail requests");
        Check(nextBest[0].AvailableEpisodes == 4, "Warm catalog still updates new episodes");
        var completedTitle = new Anime {Status="FINISHED",TotalIsExact=true,Episodes=3};
        var completedProgress = new WatchProgress {Watched=["episode:1","episode:2"]};
        Check(!completedProgress.IsComplete(completedTitle), "Earlier episodes without finale do not complete title");
        completedProgress.Watched.Clear();
        completedProgress.Record("episode:3", "3 серия", 3, 79, 100);
        Check(!completedProgress.IsComplete(completedTitle), "Starting finale below threshold does not complete title");
        completedProgress.Record("episode:3", "3 серия", 3, 80, 100);
        Check(completedProgress.IsComplete(completedTitle), "Watched finale alone completes finished season");
        completedProgress.Record("episode:1", "1 серия", 1, 10, 100);
        Check(completedProgress.IsComplete(completedTitle), "Rewatching earlier episode preserves completion");
        completedTitle.Status="RELEASING";
        Check(!completedProgress.IsComplete(completedTitle), "Caught-up ongoing title stays in continue");
        completedTitle.Status="FINISHED"; completedTitle.TotalIsExact=false;
        Check(!completedProgress.IsComplete(completedTitle), "Unknown total cannot mark title complete");
        Check(GenreChip.Create("фэнтези").Background != GenreChip.Create("приключения").Background, "Different main genres have distinct colors");
        Check(GenreChip.Create(" ФЭНТЕЗИ ").Background == GenreChip.Create("фэнтези").Background, "Genre color is stable across casing and whitespace");
        Check(GenreChip.Create("мистика").Background == GenreChip.Create("мистика").Background, "Other genres have deterministic colors");
        var releaseProgress = new WatchProgress { KnownAvailable=3 };
        var sourceA = new AnimeSource {Provider="vost",Id="a",Available=3};
        var sourceB = new AnimeSource {Provider="best",Id="b",Available=3};
        releaseProgress.ObserveSource(sourceA); releaseProgress.ObserveSource(sourceB);
        sourceB.Available=4; releaseProgress.ObserveSource(sourceB); releaseProgress.ObserveSource(sourceA);
        Check(sourceB.HasNewEpisodes && !sourceA.HasNewEpisodes,"New release attributed only to the source with a new episode");
        releaseProgress.Record("episode:4","4 серия",4,1,100,false,sourceB.Key);
        releaseProgress.ObserveSource(sourceB);
        Check(!sourceB.HasNewEpisodes,"Starting the episode clears the source badge");
        var playbackProgress = new WatchProgress {LastSourceKey=sourceA.Key};
        playbackProgress.Record("episode:2","2 серия",2,95,100,false,sourceA.Key);
        var choices = Enumerable.Range(1,3).Select(n=>new VostEpisode(n+" серия",new Uri("https://example.org/"+n+".mp4"),new Uri("https://example.org/hd"+n+".mp4"),null)).ToList();
        Check(PlaybackChoice.Resume(playbackProgress,sourceA.Key,choices)==(2,0),"Watched episode resumes at the next episode on the same source");
        Check(PlaybackChoice.Resume(playbackProgress,sourceB.Key,choices).Index==1,"Different source does not advance the episode");
        Check(PlaybackChoice.Resume(playbackProgress,sourceA.Key,choices.Take(2).ToList()).Index==1,"Missing next episode does not change source or jump");
        Check(PlaybackChoice.Maximum(new[] {new StreamQuality("Авто",new Uri("https://example.org/auto")),new StreamQuality("720p",new Uri("https://example.org/720")),new StreamQuality("1080p",new Uri("https://example.org/1080"))}).Name=="1080p","Playback automatically selects highest explicit resolution");
        Check(TitleFingerprint.Create("Second Life") == TitleFingerprint.Create("2 Life"), "Number words normalize without roles");
        Check(TitleFingerprint.Create("Название (второй сезон)") == TitleFingerprint.Create("Название II"), "Roman and word numbers match");
        Check(TitleFingerprint.Create("Name 4th Season") == TitleFingerprint.Create("Name IV"), "English ordinals normalize");
        Check(TitleFingerprint.Create("Крестьянин девятьсот девяносто девятого уровня") == TitleFingerprint.Create("Крестьянин 999 уровня"), "Compound Russian numbers normalize");
        Check(TitleFingerprint.Create("Name twenty-first") == TitleFingerprint.Create("Name 21"), "Compound English numbers normalize");
        Check(TitleFingerprint.Create("Name 2 part 1") != TitleFingerprint.Create("Name 1 part 2"), "Number order is preserved");
        Check(TitleFingerprint.Create("Mix Life").Text == "mixlife", "Roman-like letters inside words are preserved");
        Check(TitleFingerprint.Create("LV999").Numbers == "999", "Attached Arabic numbers are extracted");
        Check(TitleFingerprint.Create("Название: (II), — тест.!?") == TitleFingerprint.Create("название 2 тест"), "Punctuation and brackets are ignored");
        foreach (var name in new[] { "Мир отомэ-игр - это тяжёлый мир для мобов", "Военная хроника маленькой девочки", "Клеватесс: Король демонических зверей, младенец и герой-нежить", "Старик из деревни становится Святым мечом" })
        {
            var left = new Anime { Id=1, SourceId="1", Title=name+" (второй сезон)", Year=2026, Format="ТВ" };
            var right = new Anime { Id=-2, SourceId="2", Title=name+" (2 сезон)", Year=2026, Format="ТВ (12 эп.), 25 мин." };
            Check(SourceMatching.SameTitle(left,right), "Word/numeric season: "+name);
            right.Title=name+" (1 сезон)";
            Check(!SourceMatching.SameTitle(left,right), "Season 1 and 2 remain separate: "+name);
        }
        Check(SourceMatching.SameTitle(new Anime { Title="Жизнь в альтернативном мире с нуля (четвёртый сезон)", Year=2026, Format="ТВ" },new Anime { Title="Жизнь в альтернативном мире с нуля (4 сезон)", Year=2026, Format="ТВ" }), "ReZero fourth season matches");
        var saved = new UserState {
            Titles = { [10]=new Anime {Id=10,SourceId="10",Title="Название (второй сезон)",Year=2026,Format="ТВ"}, [-20]=new Anime {Id=-20,SourceId="20",Title="Название (2 сезон)",Year=2026,Format="ТВ"} },
            Favorites=[-20], RecentlyViewed=[-20,10],
            Progress = { [10]=new WatchProgress {LastEpisodeKey="episode:3",PositionSeconds=80,MaxEpisodeNumber=3,Started=["episode:3"],Watched=["episode:3"],LastWatchedAt=DateTimeOffset.UtcNow.AddHours(-1)}, [-20]=new WatchProgress {LastEpisodeKey="episode:2",PositionSeconds=42,MaxEpisodeNumber=2,Started=["episode:2"],LastWatchedAt=DateTimeOffset.UtcNow,HiddenFromContinue=true} }
        };
        Check(CatalogDeduplication.Reconcile(saved)==1 && saved.Titles.Count==1 && saved.Titles[10].Sources.Count==2,"Persisted duplicates are merged");
        Check(saved.Progress[10].LastEpisodeKey=="episode:2" && saved.Progress[10].PositionSeconds==42 && saved.Progress[10].MaxEpisodeNumber==3 && saved.Progress[10].Watched.Contains("episode:3") && saved.Progress[10].HiddenFromContinue,"Merge preserves latest resume, watched union, max and hidden flag");
        Check(saved.Favorites.SetEquals([10]) && saved.RecentlyViewed.SequenceEqual([10]) && saved.Progress[10].SourcePositions.Count==2,"Merge preserves favorites, recents and both source positions");
        Check(CatalogDeduplication.Reconcile(saved)==0,"Saved dedup is idempotent");
        if (args.Contains("--saved-dedup"))
        {
            var user = System.Text.Json.JsonSerializer.Deserialize<UserState>(File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AniTV","state.json")))!;
            Console.WriteLine("READ-ONLY saved catalog merge count: "+CatalogDeduplication.Reconcile(user));
            foreach (var pair in new[] { (3904,-2271), (3800,-1746), (3920,-5256), (3912,-2289), (3916,-2292) })
                Check(user.Titles.ContainsKey(pair.Item1) && !user.Titles.ContainsKey(pair.Item2) && user.Titles[pair.Item1].Sources.Count==2,"Actual saved pair merges: "+pair.Item1);
        }
        var v = new Anime { Id=3960, SourceId="3960", Title="Крестьянин девятьсот девяносто девятого уровня", Year=2026, Format="ТВ", RomanizedTitle="Lv999 no Murabito" };
        var b = new Anime { Id=-2296, SourceId="2296", Title=v.Title+" (1 сезон)", Year=2026, Format="ТВ (12 эп.), 25 мин." };
        Check(!SourceMatching.SameTitle(v,b), "Missing number is not silently invented");
        b.Title = v.Title+" (2 сезон)";
        Check(!SourceMatching.SameTitle(v,b), "Different seasons stay separate");
        b.Title = v.Title+" (1 сезон)"; b.Year=2025;
        Check(!SourceMatching.SameTitle(v,b), "Different years do not merge"); b.Year=2026;
        b.Title=v.Title;
        var state = new UserState(); var hub = new MultiSourceCatalog(new(), new(), state);
        var canonical = hub.Resolve(v); var merged = hub.Resolve(b);
        Check(ReferenceEquals(canonical,merged) && merged.Sources.Count == 2 && merged.Id==3960, "Two source identities share a stable title ID");
        Check(ReferenceEquals(hub.Resolve(b),canonical) && canonical.Sources.Count==2, "Repeated source does not duplicate");
        var comparisons = hub.ComparisonCount;
        for(var i=0;i<50;i++) hub.Resolve(b);
        Check(hub.ComparisonCount==comparisons,"Known source IDs bypass title comparisons");
        var cached = SourceMatching.Cached(canonical);
        Check(ReferenceEquals(cached,SourceMatching.Cached(canonical)),"Unchanged metadata reuses cached normalization");
        var restoredState=System.Text.Json.JsonSerializer.Deserialize<UserState>(System.Text.Json.JsonSerializer.Serialize(state))!;
        var restoredCache=restoredState.Titles[canonical.Id].ComparisonCache;
        var restoredHub=new MultiSourceCatalog(new(),new(),restoredState);
        var restoredTitle=restoredHub.Resolve(b);
        Check(restoredHub.ComparisonCount==0 && restoredTitle.Sources.Count==2 && ReferenceEquals(restoredCache,SourceMatching.Cached(restoredTitle)),"Restart restores source links and fingerprint cache without comparisons");
        canonical.Title += " новое название";
        Check(!ReferenceEquals(cached,SourceMatching.Cached(canonical)),"Changed metadata invalidates normalization cache");
        canonical.ComparisonCache!.Version=0;
        var stale=canonical.ComparisonCache;
        Check(!ReferenceEquals(stale,SourceMatching.Cached(canonical)),"Algorithm version invalidates stale fingerprints");
        var unmatched=new Anime { Id=-900000,SourceId="900000",Title="Совершенно другой тайтл",Year=2026,Format="ТВ" };
        hub.Resolve(unmatched); comparisons=hub.ComparisonCount;
        hub.Resolve(unmatched);
        Check(comparisons==hub.ComparisonCount,"Known unmatched title is not compared repeatedly");
        var old = new WatchProgress { LastEpisodeKey="file_3", Watched=["file_3"], Started=["file_3"], PositionSeconds=150, MaxEpisodeNumber=3 };
        var ep = new VostEpisode("3 серия",new Uri("https://example.org/file_3.mp4"),new Uri("https://example.org/hd3.mp4"),null);
        SourceMatching.MigrateEpisodeKeys(old,[ep]);
        Check(old.LastEpisodeKey=="episode:3" && old.Watched.Contains("episode:3") && old.PositionSeconds==150, "Legacy file keys migrate without losing resume");
        old.Observe(["episode:1","episode:2","episode:3"]); old.NewEpisodes.Clear();
        old.Observe(["episode:1","episode:2","episode:3"]);
        Check(old.NewEpisodes.Count==0, "Second source does not manufacture new episodes");
        var playlist=AnimeBestProvider.ParseEpisodes("new Playerjs({id: 'p', file: [{\"title\":\"1 серия\",\"file\":\"https://example.org/1/index.m3u8\"},{\"title\":\"2 серия\",\"file\":\"https://example.org/2/index.m3u8\"}]});");
        Check(playlist.Count==2 && playlist[0].Key!=playlist[1].Key, "HLS index filenames do not collide between episodes");
        var qualities=AnimeBestProvider.ParseQualities("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1,RESOLUTION=1920x1080\n./1080/index.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=1,RESOLUTION=1280x720\n./720/index.m3u8",new Uri("https://example.org/hls/index.m3u8"));
        Check(qualities.Count==3 && qualities[1].Url.AbsoluteUri=="https://example.org/hls/1080/index.m3u8", "HLS qualities use manifest URLs, including 1080");
        if (args.Contains("--live-cache"))
        {
            var liveCacheProvider = new AnimeBestProvider();
            var liveCache = new Dictionary<string,CatalogMetadata>();
            liveCacheProvider.SetMetadataCache(liveCache);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var coldRows = await liveCacheProvider.GetCatalogPageAsync(1);
            var coldMs = timer.ElapsedMilliseconds;
            timer.Restart();
            var warmRows = await liveCacheProvider.GetCatalogPageAsync(1);
            Console.WriteLine($"Live AnimeBest: cold={coldMs}ms, warm={timer.ElapsedMilliseconds}ms, titles={warmRows.Count}");
            Check(coldRows.Count > 0 && coldRows.Select(a=>a.Id).SequenceEqual(warmRows.Select(a=>a.Id)), "Live cached catalog preserves order and title IDs");
            Check(warmRows.All(a=>a.Sources.Count==1 && a.Sources[0].Available==a.AvailableEpisodes), "Live cached catalog keeps source counts consistent");
        }
        if (!args.Contains("--live-best")) return;
        var best = new AnimeBestProvider();
        var rows = await best.GetCatalogPageAsync(1);
        foreach(var a in rows) Console.WriteLine($"BEST {a.SourceId} | {a.Title} | {a.Year} | {a.Format} | {a.RomanizedTitle} | {a.CoverUrl}");
        Check(rows.Count==16 && rows.All(a => a.Year is > 1900 && a.CoverUrl.Length>0), "Live AnimeBest metadata and posters parse");
        var title=rows.First(a=>a.SourceId=="2296");
        var episodes=await best.GetEpisodesAsync(title.Sources[0]);
        Check(episodes.Count>=11,"Live AnimeBest direct HLS episodes parse");
        var q=await best.GetQualitiesAsync(episodes[0],default);
        Check(q.Any(x=>x.Name=="1080p") && q.Any(x=>x.Name=="720p"),"Live HLS resolutions parse");
        var liveHub=new MultiSourceCatalog(new(),best,new());
        var pager=new CatalogPager(liveHub.FetchPageAsync,false);
        var first=await pager.TakeAsync(50); var more=await pager.TakeAsync(20);
        Check(first.Count==50 && more.Count==20 && first.Concat(more).Select(a=>a.Id).Distinct().Count()==70,"Merged catalog returns 50 + 20 unique titles");
        var common=first.Concat(more).First(a=>a.Sources.Any(s=>s.Provider=="best" && s.Id=="2296"));
        Check(common.Sources.Count==2,"Live Lv999 is a single card with two sources");
        Console.WriteLine("Live source warning: "+liveHub.Warning);
    }
}

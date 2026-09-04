using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace AniTV;

public partial class MainWindow
{
    readonly ObservableCollection<EpisodeDownload> downloads=[];
    readonly FfmpegDownloadService downloadService=new();
    readonly DispatcherTimer downloadNoticeTimer=new(){Interval=TimeSpan.FromSeconds(3)};
    string DownloadRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),"AniTV");

    void InitializeDownloads() { DownloadsList.ItemsSource=downloads; LoadDownloadedFiles(); downloadNoticeTimer.Tick+=(_,_)=>{downloadNoticeTimer.Stop();DownloadNotice.Visibility=Visibility.Collapsed;}; }
    void CancelDownloads() { foreach(var item in downloads.Where(item=>item.CanCancel)) item.Cancellation.Cancel(); }

    async void DownloadCurrent_Click(object sender, RoutedEventArgs e)
    {
        if(selected is null || selectedEpisode is null) return;
        if(!downloadService.IsAvailable) { ShowDownloadNotice("FFmpeg отсутствует в этой сборке"); return; }
        var anime=selected; var episode=selectedEpisode;
        ShowDownloadNotice($"Подготавливаем: {episode.Name}…");
        try
        {
            var quality=PlaybackChoice.Maximum(await best.GetQualitiesAsync(episode,CancellationToken.None));
            var added=QueueDownload(anime,episode,quality,episode==activeEpisode?Math.Max(0,mediaPlayer.Length/1000d):0);
            ShowDownloadNotice(added?$"Добавлено: {episode.Name} · {quality.Name}":"Серия уже загружена или находится в очереди");
        }
        catch(Exception ex) { ShowDownloadNotice("Не удалось подготовить загрузку: "+ex.Message); }
    }

    async void DownloadAll_Click(object sender,RoutedEventArgs e)
    {
        if(selected is null || episodes.Count==0) return;
        if(!downloadService.IsAvailable) { ShowDownloadNotice("FFmpeg отсутствует в этой сборке"); return; }
        ShowDownloadNotice("Подготавливаем серии к загрузке…");
        var added=await QueueAllAsync(selected,episodes);
        ShowDownloadNotice(added>0?$"В очередь добавлено серий: {added}. Параллельно загружаются до трёх.":"Новых серий для загрузки не найдено");
    }

    async void DownloadTitle_Click(object sender,RoutedEventArgs e)
    {
        if(sender is not Button button || selected is null) return;
        if(!downloadService.IsAvailable) { button.Content="FFmpeg не найден"; return; }
        var anime=selected; var original=button.Content; button.IsEnabled=false; button.Content="Подготавливаем…";
        try
        {
            SourceMatching.EnsureSource(anime);
            var preferred=ProgressFor(anime).LastSourceKey;
            var source=anime.Sources.FirstOrDefault(item=>item.Key==preferred) ?? anime.Sources.First();
            var list=await FetchEpisodes(anime,source,CancellationToken.None);
            var added=await QueueAllAsync(anime,list);
            button.Content=added>0?$"Добавлено: {added}":"Уже скачано";
            await Task.Delay(1800);
        }
        catch { button.Content="Ошибка загрузки"; await Task.Delay(1800); }
        finally { button.Content=original; button.IsEnabled=true; }
    }

    async Task<int> QueueAllAsync(Anime anime,IReadOnlyList<VostEpisode> list)
    {
        var added=0;
        foreach(var episode in list)
        {
            try { var quality=PlaybackChoice.Maximum(await best.GetQualitiesAsync(episode,CancellationToken.None)); if(QueueDownload(anime,episode,quality,episode==activeEpisode?Math.Max(0,mediaPlayer.Length/1000d):0)) added++; }
            catch { }
        }
        return added;
    }

    bool QueueDownload(Anime anime,VostEpisode episode,StreamQuality quality,double duration)
    {
        var folder=Path.Combine(DownloadRoot,SafeName(anime.Title));
        var number=episode.Number>0?episode.Number.ToString("D2"):SafeName(episode.Name);
        var output=Path.Combine(folder,$"{number} серия - {SafeName(quality.Name)}.mp4");
        if(File.Exists(output) || downloads.Any(item=>string.Equals(item.OutputPath,output,StringComparison.OrdinalIgnoreCase) && (item.CanOpen || item.CanCancel))) return false;
        var item=new EpisodeDownload {Title=anime.Title,Episode=episode.Name,Quality=quality.Name,Url=quality.Url,Referrer=episode.Referrer,OutputPath=output,DurationSeconds=duration};
        downloads.Insert(0,item); DownloadsEmpty.Visibility=Visibility.Collapsed; _=RunDownloadAsync(item,episode); return true;
    }

    async Task RunDownloadAsync(EpisodeDownload item,VostEpisode episode)
    {
        await downloadService.DownloadAsync(item);
        if(!item.IsComplete) return;
        episode.IsDownloaded=true; EpisodeBox.Items.Refresh(); FullscreenEpisodeBox.Items.Refresh(); LoadDownloadedFiles();
    }

    void MarkDownloaded(Anime anime,IEnumerable<VostEpisode> list)
    {
        var folder=Path.Combine(DownloadRoot,SafeName(anime.Title));
        foreach(var episode in list)
        {
            var prefix=episode.Number>0?$"{episode.Number:D2} серия -":SafeName(episode.Name);
            episode.IsDownloaded=Directory.Exists(folder) && Directory.EnumerateFiles(folder,prefix+"*.mp4").Any();
        }
    }

    void LoadDownloadedFiles()
    {
        if(!Directory.Exists(DownloadRoot)) return;
        foreach(var file in Directory.EnumerateFiles(DownloadRoot,"*.mp4",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc))
        {
            if(downloads.Any(item=>string.Equals(item.OutputPath,file,StringComparison.OrdinalIgnoreCase))) continue;
            var title=Directory.GetParent(file)?.Name ?? "Локальное видео";
            downloads.Add(new EpisodeDownload {Title=title,Episode=Path.GetFileNameWithoutExtension(file),Quality="Локальный файл",OutputPath=file,Progress=100,IsComplete=true,Status="Скачано"});
        }
    }

    void ShowDownloadNotice(string text)
    {
        DownloadNoticeText.Text=text; DownloadNotice.Visibility=Visibility.Visible;
        downloadNoticeTimer.Stop(); downloadNoticeTimer.Start();
    }

    void Downloads_Click(object sender, RoutedEventArgs e) => ShowDownloadsPage();
    void ShowDownloadsPage()
    {
        CancelCatalogLoading(); DetailsOverlay.Visibility=Visibility.Collapsed;
        LoadDownloadedFiles();
        CatalogScroll.Visibility=Visibility.Collapsed; DownloadsPage.Visibility=Visibility.Visible;
        LoadingPanel.Visibility=EmptyPanel.Visibility=GenrePanel.Visibility=Visibility.Collapsed;
        DownloadsEmpty.Visibility=downloads.Count==0?Visibility.Visible:Visibility.Collapsed;
        PageTitle.Text="Загрузки"; Subtitle.Text="Скачанные серии и активные задания";
        StatusText.Text=$"Файлов: {downloads.Count(item=>item.CanOpen)} · Активных загрузок: {downloads.Count(item=>item.CanCancel)}";
    }
    void ShowCatalogContent()
    {
        DownloadsPage.Visibility=Visibility.Collapsed; CatalogScroll.Visibility=Visibility.Visible;
    }
    void DownloadCancel_Click(object sender, RoutedEventArgs e) { if(sender is Button {Tag:EpisodeDownload item}) item.Cancellation.Cancel(); }
    void DownloadOpen_Click(object sender, RoutedEventArgs e)
    {
        if(sender is not Button {Tag:EpisodeDownload item} || !item.CanOpen) return;
        Process.Start(new ProcessStartInfo(item.OutputPath){UseShellExecute=true});
    }
    void DownloadFolder_Click(object sender, RoutedEventArgs e)
    {
        if(sender is not Button {Tag:EpisodeDownload item}) return;
        Directory.CreateDirectory(Path.GetDirectoryName(item.OutputPath)!);
        Process.Start(new ProcessStartInfo("explorer.exe",$"/select,\"{item.OutputPath}\""){UseShellExecute=true});
    }
    void DownloadDelete_Click(object sender,RoutedEventArgs e)
    {
        if(sender is not Button {Tag:EpisodeDownload item} || !item.CanOpen) return;
        var confirmation=new ConfirmDownloadDeleteWindow(item.DisplayTitle){Owner=this};
        if(confirmation.ShowDialog()!=true) return;
        try
        {
            File.Delete(item.OutputPath); downloads.Remove(item);
            var folder=Path.GetDirectoryName(item.OutputPath); if(folder is not null && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
            if(selected is not null) {MarkDownloaded(selected,episodes); EpisodeBox.Items.Refresh(); FullscreenEpisodeBox.Items.Refresh();}
            DownloadsEmpty.Visibility=downloads.Count==0?Visibility.Visible:Visibility.Collapsed;
            StatusText.Text=$"Файлов: {downloads.Count(entry=>entry.CanOpen)} · Активных загрузок: {downloads.Count(entry=>entry.CanCancel)}";
        }
        catch(Exception ex) { MessageBox.Show("Не удалось удалить файл: "+ex.Message,"AniTV",MessageBoxButton.OK,MessageBoxImage.Warning); }
    }
    static string SafeName(string value) { var invalid=Regex.Escape(new string(Path.GetInvalidFileNameChars())); var result=Regex.Replace(value,$"[{invalid}]"," ").Trim().TrimEnd('.'); if(string.IsNullOrWhiteSpace(result)) return "Без названия"; return result.Length>90?result[..90].Trim():result; }
}

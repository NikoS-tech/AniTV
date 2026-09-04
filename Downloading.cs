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
    bool videoHiddenForDownloads;

    void InitializeDownloads() { DownloadsList.ItemsSource=downloads; downloadNoticeTimer.Tick+=(_,_)=>{downloadNoticeTimer.Stop();DownloadNotice.Visibility=Visibility.Collapsed;}; }
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
        var anime=selected; var added=0; var skipped=0;
        ShowDownloadNotice("Подготавливаем серии к загрузке…");
        foreach(var episode in episodes)
        {
            try
            {
                var qualities=await best.GetQualitiesAsync(episode,CancellationToken.None);
                var quality=PlaybackChoice.Maximum(qualities);
                if(QueueDownload(anime,episode,quality,episode==activeEpisode?Math.Max(0,mediaPlayer.Length/1000d):0)) added++; else skipped++;
            }
            catch { skipped++; }
        }
        ShowDownloadNotice(added>0?$"В очередь добавлено серий: {added}. Параллельно загружаются до трёх.":"Новых серий для загрузки не найдено");
    }

    bool QueueDownload(Anime anime,VostEpisode episode,StreamQuality quality,double duration)
    {
        var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),"AniTV",SafeName(anime.Title));
        var number=episode.Number>0?episode.Number.ToString("D2"):SafeName(episode.Name);
        var output=Path.Combine(folder,$"{number} серия - {SafeName(quality.Name)}.mp4");
        if(File.Exists(output) || downloads.Any(item=>string.Equals(item.OutputPath,output,StringComparison.OrdinalIgnoreCase) && (item.CanOpen || item.CanCancel))) return false;
        var item=new EpisodeDownload {Title=anime.Title,Episode=episode.Name,Quality=quality.Name,Url=quality.Url,Referrer=episode.Referrer,OutputPath=output,DurationSeconds=duration};
        downloads.Insert(0,item); DownloadsEmpty.Visibility=Visibility.Collapsed; _=downloadService.DownloadAsync(item); return true;
    }

    void ShowDownloadNotice(string text)
    {
        DownloadNoticeText.Text=text; DownloadNotice.Visibility=Visibility.Visible;
        downloadNoticeTimer.Stop(); downloadNoticeTimer.Start();
    }

    void Downloads_Click(object sender, RoutedEventArgs e) => ShowDownloads();
    void ShowDownloads()
    {
        DownloadsEmpty.Visibility=downloads.Count==0?Visibility.Visible:Visibility.Collapsed;
        if(playerOpen && PlayerVideoBorder.Child is not null)
        {
            videoHiddenForDownloads=true;
            SuspendVideoSurface();
            controlsTimer.Stop(); Mouse.OverrideCursor=null;
        }
        DownloadsOverlay.Visibility=Visibility.Visible;
    }
    void HideDownloads()
    {
        DownloadsOverlay.Visibility=Visibility.Collapsed;
        if(videoHiddenForDownloads && playerOpen)
        {
            ResumeVideoSurface();
            if(isFullscreen) RevealFullscreenControls();
        }
        videoHiddenForDownloads=false;
    }
    void CloseDownloads_Click(object sender, RoutedEventArgs e) => HideDownloads();
    void DownloadsOverlay_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { if(e.OriginalSource==DownloadsOverlay) HideDownloads(); }
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
    static string SafeName(string value) { var invalid=Regex.Escape(new string(Path.GetInvalidFileNameChars())); var result=Regex.Replace(value,$"[{invalid}]"," ").Trim().TrimEnd('.'); if(string.IsNullOrWhiteSpace(result)) return "Без названия"; return result.Length>90?result[..90].Trim():result; }
    static string UniquePath(string folder,string file) { Directory.CreateDirectory(folder); var path=Path.Combine(folder,file); if(!File.Exists(path)) return path; var name=Path.GetFileNameWithoutExtension(file); var extension=Path.GetExtension(file); for(var i=2;;i++){path=Path.Combine(folder,$"{name} ({i}){extension}");if(!File.Exists(path))return path;} }
}

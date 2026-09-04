using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace AniTV;

public partial class MainWindow
{
    readonly ObservableCollection<EpisodeDownload> downloads=[];
    readonly FfmpegDownloadService downloadService=new();

    void InitializeDownloads() => DownloadsList.ItemsSource=downloads;
    void CancelDownloads() { foreach(var item in downloads.Where(item=>item.CanCancel)) item.Cancellation.Cancel(); }

    async void DownloadCurrent_Click(object sender, RoutedEventArgs e)
    {
        if(selected is null || selectedEpisode is null) return;
        var qualities=(QualityBox.ItemsSource as IEnumerable<StreamQuality>)?.ToList() ?? [];
        if(qualities.Count==0) { MessageBox.Show("Качества видео ещё не загружены.","AniTV"); return; }
        if(!downloadService.IsAvailable) { MessageBox.Show("Компонент FFmpeg отсутствует в этой сборке.","AniTV",MessageBoxButton.OK,MessageBoxImage.Warning); return; }
        var quality=PlaybackChoice.Maximum(qualities);
        var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),"AniTV",SafeName(selected.Title));
        var number=selectedEpisode.Number>0 ? selectedEpisode.Number.ToString("D2") : SafeName(selectedEpisode.Name);
        var output=UniquePath(folder,$"{number} серия - {SafeName(quality.Name)}.mp4");
        var item=new EpisodeDownload { Title=selected.Title,Episode=selectedEpisode.Name,Quality=quality.Name,Url=quality.Url,Referrer=selectedEpisode.Referrer,OutputPath=output,DurationSeconds=Math.Max(0,mediaPlayer.Length/1000d) };
        downloads.Insert(0,item); ShowDownloads();
        await downloadService.DownloadAsync(item);
    }

    void Downloads_Click(object sender, RoutedEventArgs e) => ShowDownloads();
    void ShowDownloads() { DownloadsEmpty.Visibility=downloads.Count==0?Visibility.Visible:Visibility.Collapsed; DownloadsOverlay.Visibility=Visibility.Visible; }
    void CloseDownloads_Click(object sender, RoutedEventArgs e) => DownloadsOverlay.Visibility=Visibility.Collapsed;
    void DownloadsOverlay_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { if(e.OriginalSource==DownloadsOverlay) DownloadsOverlay.Visibility=Visibility.Collapsed; }
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

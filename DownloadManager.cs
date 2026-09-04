using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace AniTV;

public sealed class EpisodeDownload : INotifyPropertyChanged
{
    string status = "В очереди";
    double progress;
    bool isRunning;
    bool isComplete;
    bool isPaused;
    public required string Title { get; init; }
    public required string Episode { get; init; }
    public required string Quality { get; init; }
    public Uri? Url { get; init; }
    public string Referrer { get; init; } = "";
    public required string OutputPath { get; init; }
    public double DurationSeconds { get; set; }
    public CancellationTokenSource Cancellation { get; } = new();
    internal Process? RunningProcess { get; set; }
    public string DisplayTitle => $"{Title} · {Episode}";
    public string FileName => Path.GetFileName(OutputPath);
    public string FileSizeLabel => File.Exists(OutputPath) ? FormatSize(new FileInfo(OutputPath).Length) : "—";
    public string Status { get => status; set { status=value; Changed(); Changed(nameof(CanDismiss)); Changed(nameof(CanShowFolder)); } }
    public double Progress { get => progress; set { progress=Math.Clamp(value,0,100); Changed(); } }
    public bool IsRunning { get => isRunning; set { isRunning=value; Changed(); Changed(nameof(CanCancel)); Changed(nameof(CanPauseOrResume)); Changed(nameof(CanDismiss)); Changed(nameof(CanShowFolder)); } }
    public bool IsComplete { get => isComplete; set { isComplete=value; Changed(); Changed(nameof(CanOpen)); Changed(nameof(CanShowFolder)); Changed(nameof(FileSizeLabel)); } }
    public bool IsPaused { get => isPaused; set { isPaused=value; Changed(); Changed(nameof(PauseLabel)); Changed(nameof(CanPauseOrResume)); } }
    public bool CanCancel => IsRunning || Status == "В очереди";
    public bool CanOpen => IsComplete && File.Exists(OutputPath);
    public bool CanShowFolder => CanOpen || CanCancel;
    public bool CanPauseOrResume => IsRunning;
    public bool CanDismiss => !IsRunning && !IsComplete && Status != "В очереди";
    public string PauseLabel => IsPaused ? "Продолжить" : "Пауза";
    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed([CallerMemberName] string? name=null) => PropertyChanged?.Invoke(this,new(name));
    static string FormatSize(long bytes) => bytes >= 1L<<30 ? $"{bytes/(double)(1L<<30):0.##} ГБ" : $"{bytes/(double)(1L<<20):0} МБ";
}

public sealed class FfmpegDownloadService
{
    readonly SemaphoreSlim queue = new(3,3);
    public string ExecutablePath { get; }
    public bool IsAvailable => File.Exists(ExecutablePath);

    public FfmpegDownloadService()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory,"ffmpeg","ffmpeg.exe");
        ExecutablePath = File.Exists(bundled) ? bundled : FindOnPath("ffmpeg.exe") ?? bundled;
    }

    public async Task DownloadAsync(EpisodeDownload item)
    {
        var acquired=false;
        try
        {
            await queue.WaitAsync(item.Cancellation.Token);
            acquired=true;
            item.IsRunning=true; item.Status="Подготовка…";
            Directory.CreateDirectory(Path.GetDirectoryName(item.OutputPath)!);
            var partial=item.OutputPath+".part";
            if(File.Exists(partial)) File.Delete(partial);
            var start = new ProcessStartInfo(ExecutablePath) { UseShellExecute=false, RedirectStandardOutput=true, RedirectStandardError=true, CreateNoWindow=true };
            if(item.Url is null) throw new InvalidOperationException("Для загрузки не задан адрес видео.");
            foreach(var argument in new[]{"-y","-hide_banner","-loglevel","info","-nostats","-progress","pipe:1","-headers",$"Referer: {item.Referrer}\r\n","-i",item.Url.AbsoluteUri,"-map","0","-c","copy","-movflags","+faststart","-f","mp4",partial}) start.ArgumentList.Add(argument);
            using var process = new Process { StartInfo=start,EnableRaisingEvents=true };
            process.Start(); item.RunningProcess=process; item.Status="Скачивание…";
            using var registration=item.Cancellation.Token.Register(() => { try { if(!process.HasExited) process.Kill(true); } catch { } });
            var errorTask=ReadDiagnosticsAsync(process,item,item.Cancellation.Token);
            while(await process.StandardOutput.ReadLineAsync(item.Cancellation.Token) is { } line)
            {
                if(line.StartsWith("out_time_us=",StringComparison.Ordinal) && long.TryParse(line[12..],NumberStyles.Integer,CultureInfo.InvariantCulture,out var microseconds) && item.DurationSeconds>0)
                    item.Progress=microseconds/1_000_000d/item.DurationSeconds*100;
            }
            var error=await errorTask;
            await process.WaitForExitAsync(item.Cancellation.Token);
            if(process.ExitCode!=0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"FFmpeg завершился с кодом {process.ExitCode}." : error.Trim());
            File.Move(partial,item.OutputPath,true); item.Progress=100; item.IsComplete=true; item.Status="Готово";
        }
        catch(OperationCanceledException) { item.Status="Отменено"; DeletePartial(item.OutputPath); }
        catch(Exception ex) { item.Status="Ошибка: "+ShortError(ex.Message); DeletePartial(item.OutputPath); }
        finally { item.RunningProcess=null; item.IsPaused=false; item.IsRunning=false; if(acquired) queue.Release(); }
    }

    static void DeletePartial(string output) { try { var path=output+".part"; if(File.Exists(path)) File.Delete(path); } catch { } }
    static async Task<string> ReadDiagnosticsAsync(Process process,EpisodeDownload item,CancellationToken token)
    {
        var lines=new Queue<string>();
        while(await process.StandardError.ReadLineAsync(token) is { } line)
        {
            var match=Regex.Match(line,@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
            if(match.Success && double.TryParse(match.Groups[3].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var seconds))
                item.DurationSeconds=int.Parse(match.Groups[1].Value)*3600+int.Parse(match.Groups[2].Value)*60+seconds;
            lines.Enqueue(line); if(lines.Count>12) lines.Dequeue();
        }
        return string.Join(Environment.NewLine,lines);
    }
    static string ShortError(string value) { var line=value.Split('\n',StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? value; return line.Length>150 ? line[..150]+"…" : line; }
    static string? FindOnPath(string name) => (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator).Select(path=>Path.Combine(path,name)).FirstOrDefault(File.Exists);

    public bool TogglePause(EpisodeDownload item)
    {
        var process=item.RunningProcess; if(process is null || process.HasExited) return false;
        var result=item.IsPaused?NtResumeProcess(process.Handle):NtSuspendProcess(process.Handle);
        if(result!=0) return false;
        item.IsPaused=!item.IsPaused; item.Status=item.IsPaused?"Приостановлено":"Скачивание…"; return true;
    }

    [DllImport("ntdll.dll")] static extern int NtSuspendProcess(IntPtr processHandle);
    [DllImport("ntdll.dll")] static extern int NtResumeProcess(IntPtr processHandle);
}

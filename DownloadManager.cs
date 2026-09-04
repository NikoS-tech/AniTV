using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;

namespace AniTV;

public sealed class EpisodeDownload : INotifyPropertyChanged
{
    string status = "В очереди";
    double progress;
    bool isRunning;
    bool isComplete;
    public required string Title { get; init; }
    public required string Episode { get; init; }
    public required string Quality { get; init; }
    public required Uri Url { get; init; }
    public required string Referrer { get; init; }
    public required string OutputPath { get; init; }
    public double DurationSeconds { get; init; }
    public CancellationTokenSource Cancellation { get; } = new();
    public string DisplayTitle => $"{Title} · {Episode}";
    public string FileName => Path.GetFileName(OutputPath);
    public string Status { get => status; set { status=value; Changed(); } }
    public double Progress { get => progress; set { progress=Math.Clamp(value,0,100); Changed(); } }
    public bool IsRunning { get => isRunning; set { isRunning=value; Changed(); Changed(nameof(CanCancel)); } }
    public bool IsComplete { get => isComplete; set { isComplete=value; Changed(); Changed(nameof(CanOpen)); } }
    public bool CanCancel => IsRunning || Status == "В очереди";
    public bool CanOpen => IsComplete && File.Exists(OutputPath);
    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed([CallerMemberName] string? name=null) => PropertyChanged?.Invoke(this,new(name));
}

public sealed class FfmpegDownloadService
{
    readonly SemaphoreSlim queue = new(1,1);
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
            foreach(var argument in new[]{"-y","-hide_banner","-loglevel","warning","-progress","pipe:1","-headers",$"Referer: {item.Referrer}\r\n","-i",item.Url.AbsoluteUri,"-map","0","-c","copy","-movflags","+faststart","-f","mp4",partial}) start.ArgumentList.Add(argument);
            using var process = new Process { StartInfo=start,EnableRaisingEvents=true };
            process.Start(); item.Status="Скачивание…";
            using var registration=item.Cancellation.Token.Register(() => { try { if(!process.HasExited) process.Kill(true); } catch { } });
            var errorTask=process.StandardError.ReadToEndAsync(item.Cancellation.Token);
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
        finally { item.IsRunning=false; if(acquired) queue.Release(); }
    }

    static void DeletePartial(string output) { try { var path=output+".part"; if(File.Exists(path)) File.Delete(path); } catch { } }
    static string ShortError(string value) { var line=value.Split('\n',StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? value; return line.Length>150 ? line[..150]+"…" : line; }
    static string? FindOnPath(string name) => (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator).Select(path=>Path.Combine(path,name)).FirstOrDefault(File.Exists);
}

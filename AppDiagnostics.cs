using System.IO;

namespace AniTV;

public static class AppDiagnostics
{
    static readonly object gate = new();
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniTV", "logs");
    public static void Write(string operation, Exception? error = null)
    {
        try
        {
            lock(gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                var path=Path.Combine(DirectoryPath,"application.log");
                if(File.Exists(path) && new FileInfo(path).Length>1_000_000)
                    File.Move(path,path+".previous",true);
                // Do not persist URLs, titles, paths or exception messages containing access tokens.
                File.AppendAllText(path,$"{DateTimeOffset.UtcNow:O} {operation} {error?.GetType().Name} {error?.HResult:X8}{Environment.NewLine}");
            }
        }
        catch { /* Failure to log must not interrupt recovery. */ }
    }
}

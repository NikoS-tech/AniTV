using System.IO;
using System.Windows;
namespace AniTV;
public partial class App : Application
{
    FileStream? instanceLock;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var dataDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AniTV");
        try
        {
            Directory.CreateDirectory(dataDirectory);
            instanceLock=new FileStream(Path.Combine(dataDirectory,"instance.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {
            AppDiagnostics.Write("instance.lock.failed",ex);
            MessageBox.Show("AniTV уже запущен или папка данных недоступна. Закройте другой экземпляр и повторите запуск.","AniTV");
            Shutdown(1); return;
        }
        DispatcherUnhandledException += (_, args) => AppDiagnostics.Write("dispatcher.unhandled",args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => AppDiagnostics.Write("process.unhandled",args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => AppDiagnostics.Write("task.unobserved",args.Exception);
        try { AppDiagnostics.Write("application.start"); MainWindow = new MainWindow(); MainWindow.Show(); }
        catch (Exception ex)
        {
            AppDiagnostics.Write("startup.failed",ex);
            MessageBox.Show($"Ошибка запуска AniTV. Журнал: {AppDiagnostics.DirectoryPath}\n\n{ex.Message}", "AniTV", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        instanceLock?.Dispose();
        base.OnExit(e);
    }
}

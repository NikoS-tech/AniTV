using System.IO;
using System.Windows;
namespace AniTV;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try { MainWindow = new MainWindow(); MainWindow.Show(); }
        catch (Exception ex)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "startup-error.log");
            File.WriteAllText(path, ex.ToString());
            MessageBox.Show($"Ошибка запуска AniTV. Подробности: {path}\n\n{ex.Message}", "AniTV", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}

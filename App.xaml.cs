using System.Windows;

namespace BackgroundStudio;

public partial class App : Application
{
    private void App_Startup(object sender, StartupEventArgs e)
    {
        var window = new MainWindow();
        window.Show();
        window.AddCommandLineFiles(e.Args);
    }
}

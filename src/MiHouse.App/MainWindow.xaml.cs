using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace MiHouse.App;

/// <summary>
/// Hosts the application shell.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow(ILogger<MainWindow> logger)
    {
        InitializeComponent();

        AppWindow.Resize(new SizeInt32(1280, 840));
        logger.LogInformation("Main window created");
    }
}

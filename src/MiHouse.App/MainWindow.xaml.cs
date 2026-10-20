using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using MiHouse.App.ViewModels;
using MiHouse.App.Views;
using MiHouse.Core.Auth;
using MiHouse.Core.Devices;
using MiHouse.Core.Session;
using MiHouse.Core.Spec;
using Windows.Graphics;

namespace MiHouse.App;

public sealed partial class MainWindow : Window
{
    private const int _defaultWidth = 1280;
    private const int _defaultHeight = 840;

    private readonly IAuthService _auth;
    private readonly ICredentialStore _credentials;
    private readonly IMiHomeCloudClient _cloud;
    private readonly ISpecCatalog _specs;

    public MainWindow(ILogger<MainWindow> logger, IAuthService auth, ICredentialStore credentials,
        IMiHomeCloudClient cloud, ISpecCatalog specs)
    {
        _auth = auth;
        _credentials = credentials;
        _cloud = cloud;
        _specs = specs;
        InitializeComponent();
        SizeToDefaultLogicalSize();
        Closed += OnClosed;
        ShowLogin();
        logger.LogInformation("Main window created");
    }

    /// <summary>
    /// Sizes the window in logical pixels; <see cref="AppWindow.Resize"/> works in physical
    /// pixels, so the value must be scaled by the monitor DPI.
    /// </summary>
    private void SizeToDefaultLogicalSize()
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(handle) / 96.0;
        AppWindow.Resize(new SizeInt32(
            (int)(_defaultWidth * scale),
            (int)(_defaultHeight * scale)));
    }

    private void ShowLogin()
    {
        StopCurrentPage();
        var viewModel = new LoginViewModel(_auth, _credentials);
        viewModel.SignedIn += ShowDevices;
        RootFrame.Content = new LoginPage(viewModel);
    }

    private void ShowDevices(MiHomeSession session)
    {
        StopCurrentPage();
        var viewModel = new DevicesViewModel(_cloud, _specs, _credentials, session);
        viewModel.SignedOut += ShowLogin;
        RootFrame.Content = new DevicesPage(viewModel);
    }

    private void StopCurrentPage()
    {
        if (RootFrame.Content is LoginPage login)
        {
            login.ViewModel.SignedIn -= ShowDevices;
            login.ViewModel.Stop();
        }
        else if (RootFrame.Content is DevicesPage devices)
        {
            devices.ViewModel.SignedOut -= ShowLogin;
            devices.ViewModel.Stop();
        }
    }

    private void OnClosed(object sender, WindowEventArgs args) => StopCurrentPage();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}

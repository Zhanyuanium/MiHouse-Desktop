using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MiHouse.App.ViewModels;
using MiHouse.App.Views;
using MiHouse.Core.Auth;
using MiHouse.Core.Devices;
using MiHouse.Core.Lan;
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
    private readonly IDeviceSecretStore _secrets;
    private readonly ILanTransport _lan;
    private readonly SnapshotStore _snapshots;

    public MainWindow(ILogger<MainWindow> logger, IAuthService auth, ICredentialStore credentials,
        IMiHomeCloudClient cloud, ISpecCatalog specs, IDeviceSecretStore secrets, ILanTransport lan,
        SnapshotStore snapshots)
    {
        _auth = auth;
        _credentials = credentials;
        _cloud = cloud;
        _specs = specs;
        _secrets = secrets;
        _lan = lan;
        _snapshots = snapshots;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(WindowTitleBar);
        WindowTitleBar.Loaded += (_, _) =>
        {
            UpdateTitleBarInset();
            WindowTitleBar.XamlRoot.Changed += (_, _) => UpdateTitleBarInset();
        };
        WindowTitleBar.SizeChanged += (_, _) => UpdateTitleBarInset();
        AppWindow.Changed += (_, _) => DispatcherQueue.TryEnqueue(UpdateTitleBarInset);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
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

    private void UpdateTitleBarInset()
    {
        // SDK 2.3.9 TitleBar.UpdatePadding assigns physical RightInset directly
        // to this XAML column. Correct only the inset, keeping the native template,
        // drag regions and caption buttons. Remove when the SDK converts for DPI.
        if (WindowTitleBar.XamlRoot is { } root && VisualTreeHelper.GetChildrenCount(WindowTitleBar) > 0 &&
            VisualTreeHelper.GetChild(WindowTitleBar, 0) is Grid layout &&
            layout.FindName("RightPaddingColumn") is ColumnDefinition padding)
        {
            padding.Width = new GridLength(AppWindow.TitleBar.RightInset / root.RasterizationScale);
        }
    }

    private void ShowLogin()
    {
        StopCurrentPage();
        _secrets.Clear();
        WindowTitleBar.DataContext = null;
        HomeSwitcher.Visibility = Visibility.Collapsed;
        var viewModel = new LoginViewModel(_auth, _credentials);
        viewModel.SignedIn += ShowDevices;
        RootFrame.Content = new LoginPage(viewModel);
    }

    private void ShowDevices(MiHomeSession session)
    {
        StopCurrentPage();
        var controller = new DeviceController(_cloud, _specs, session, _lan, _secrets);
        var viewModel = new DevicesViewModel(_cloud, controller, _credentials, session, _snapshots);
        WindowTitleBar.DataContext = viewModel;
        HomeSwitcher.Visibility = Visibility.Visible;
        viewModel.SignedOut += ShowLogin;
        var page = new DevicesPage(viewModel);
        page.NavigationPaneWidthChanged += OnNavigationPaneWidthChanged;
        RootFrame.Content = page;
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
            devices.NavigationPaneWidthChanged -= OnNavigationPaneWidthChanged;
            devices.ViewModel.SignedOut -= ShowLogin;
            devices.ViewModel.Stop();
        }
    }

    // Equal 8 px insets only make sense against the expanded pane; the 56 px compact
    // rail would otherwise squash the switcher to 40 px, so keep the default width.
    private void OnNavigationPaneWidthChanged(object? sender, double width) => HomeSwitcher.Width = Math.Max(192, width - 16);

    private void OnClosed(object sender, WindowEventArgs args) => StopCurrentPage();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}

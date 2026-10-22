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
    private readonly DeviceClassification _classification;

    public MainWindow(ILogger<MainWindow> logger, IAuthService auth, ICredentialStore credentials,
        IMiHomeCloudClient cloud, ISpecCatalog specs, IDeviceSecretStore secrets, ILanTransport lan,
        SnapshotStore snapshots, DeviceClassification classification)
    {
        _auth = auth;
        _credentials = credentials;
        _cloud = cloud;
        _specs = specs;
        _secrets = secrets;
        _lan = lan;
        _snapshots = snapshots;
        _classification = classification;
        InitializeComponent();
        Title = AppResources.DisplayName;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(WindowTitleBar);
        WindowTitleBar.Loaded += (_, _) =>
        {
            UpdateTitleBarInset();
            WindowTitleBar.XamlRoot.Changed += (_, _) => UpdateTitleBarInset();
        };
        WindowTitleBar.SizeChanged += (_, _) => UpdateTitleBarInset();
        HomeSwitcher.Loaded += (_, _) => UpdateTitleBarNavigation();
        HomeSwitcher.SizeChanged += (_, _) => UpdateTitleBarNavigation();
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
        UpdateTitleBarNavigation();
    }

    private void ShowLogin()
    {
        StopCurrentPage();
        _secrets.Clear();
        WindowTitleBar.DataContext = null;
        HomeSwitcher.Visibility = Visibility.Collapsed;
        WindowTitleBar.IsPaneToggleButtonVisible = false;
        var viewModel = new LoginViewModel(_auth, _credentials);
        viewModel.SignedIn += ShowDevices;
        RootFrame.Content = new LoginPage(viewModel);
    }

    private void ShowDevices(MiHomeSession session)
    {
        StopCurrentPage();
        var controller = new DeviceController(_cloud, _specs, session, _lan, _secrets);
        var viewModel = new DevicesViewModel(_cloud, controller, _credentials, session, _snapshots, _classification);
        WindowTitleBar.DataContext = viewModel;
        HomeSwitcher.Visibility = Visibility.Visible;
        WindowTitleBar.IsPaneToggleButtonVisible = true;
        viewModel.SignedOut += ShowLogin;
        var page = new DevicesPage(viewModel);
        page.NavigationLayoutChanged += OnNavigationLayoutChanged;
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
            devices.NavigationLayoutChanged -= OnNavigationLayoutChanged;
            devices.ViewModel.SignedOut -= ShowLogin;
            devices.ViewModel.Stop();
        }
    }

    private void OnTitleBarPaneToggleRequested(TitleBar sender, object args)
    {
        if (RootFrame.Content is DevicesPage page) { page.ToggleNavigationPane(); }
    }

    private void OnNavigationLayoutChanged(object? sender, EventArgs args) => DispatcherQueue.TryEnqueue(UpdateTitleBarNavigation);

    private bool _updatingNavigation;
    private void UpdateTitleBarNavigation()
    {
        if (_updatingNavigation || RootFrame.Content is not DevicesPage page || !HomeSwitcher.IsLoaded || HomeSwitcher.XamlRoot is null) { return; }
        if (VisualTreeHelper.GetChildrenCount(WindowTitleBar) > 0 && VisualTreeHelper.GetChild(WindowTitleBar, 0) is FrameworkElement root &&
            root.FindName("PART_PaneToggleButton") is Button toggle)
        {
            _updatingNavigation = true;
            try
            {
                UIElement visualRoot = HomeSwitcher.XamlRoot.Content;
                Thickness inset = page.NavigationItemInset;
                double toggleLeft = toggle.TransformToVisual(visualRoot).TransformPoint(new Windows.Foundation.Point()).X;
                toggle.Width = Math.Max(0, page.NavigationRailWidth - inset.Left - inset.Right);
                toggle.Height = RefreshButton.Height;
                toggle.VerticalAlignment = VerticalAlignment.Center;
                toggle.Margin = new Thickness(toggle.Margin.Left + page.NavigationRailLeft + inset.Left - toggleLeft, 0, inset.Right, 0);
                root.UpdateLayout();

                // Keep both header edges anchored to live NavigationView geometry,
                // independent of the title bar's own template padding or DPI.
                double contentLeft = page.NavigationCollapsedContentLeft;
                double homeLeft = HomeSwitcher.TransformToVisual(visualRoot).TransformPoint(new Windows.Foundation.Point()).X;
                HomeSwitcher.Margin = new Thickness(HomeSwitcher.Margin.Left + contentLeft - homeLeft, 0, 0, 0);
                HomeSwitcher.Width = Math.Max(0, page.NavigationHeaderRightEdge - contentLeft);
            }
            finally { _updatingNavigation = false; }
            string label = page.IsNavigationPaneOpen ? "收起导航" : "展开导航";
            ToolTipService.SetToolTip(toggle, label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, label);
        }
    }

    private void OnClosed(object sender, WindowEventArgs args) => StopCurrentPage();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}

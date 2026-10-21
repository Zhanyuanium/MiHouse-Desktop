using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MiHouse.App.ViewModels;
using MiHouse.Core.Spec;
using Windows.System;

namespace MiHouse.App.Views;

public sealed partial class DevicesPage : Page
{
    private bool _isActionDialogOpen;
    private bool _navigationRebuildQueued;
    private InputActivationListener? _activationListener;
    private readonly HomePage _homePage;
    private readonly RoomPage _roomPage;
    private readonly ActivityPage _activityPage;
    private readonly SettingsPage _settingsPage;
    private readonly ScenesPage _scenesPage;
    private Control? _returnFocus;
    private RoomChipItem[] _roomChipItems = [];
    private bool _isSynchronizingRoomSelection;

    public DevicesPage(DevicesViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        _homePage = new HomePage { DataContext = viewModel };
        _roomPage = new RoomPage { DataContext = viewModel };
        _activityPage = new ActivityPage { DataContext = viewModel };
        _settingsPage = new SettingsPage { DataContext = viewModel };
        _scenesPage = new ScenesPage { DataContext = viewModel.ScenePanel };
        _homePage.GroupOpened += (_, group) => NavigateWithEntrance(() => ViewModel.ShowClass(group));
        _homePage.ScenesOpened += (_, _) => NavigateWithEntrance(() => ViewModel.NavigateScenesCommand.Execute(null));
        _roomPage.DeviceOpened += OnDeviceOpened;
        PresentPage();
    }

    public DevicesViewModel ViewModel { get; }
    private Thickness _navigationItemInset;
    private bool _presentationReady;
    private bool _navigationRequested;
    private bool? _paneTransitionOpen;
    internal event EventHandler? NavigationLayoutChanged;
    internal bool IsNavigationPaneOpen => ShellNavigation.IsPaneOpen;
    internal double NavigationRailLeft => ShellNavigation.TransformToVisual(XamlRoot.Content).TransformPoint(new Windows.Foundation.Point()).X;
    internal double NavigationRailWidth => ShellNavigation.CompactPaneLength;
    internal double NavigationCollapsedContentLeft
    {
        get
        {
            // Include the native content surface's live border, not a pixel offset.
            for (DependencyObject? parent = VisualTreeHelper.GetParent(NavigationContent); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            {
                if (parent is Grid { Name: "ContentGrid" } surface)
                {
                    double contentInset = NavigationContent.TransformToVisual(surface).TransformPoint(new Windows.Foundation.Point()).X;
                    return NavigationRailLeft + NavigationRailWidth + contentInset;
                }
            }
            return NavigationRailLeft + NavigationRailWidth;
        }
    }
    internal Thickness NavigationItemInset => _navigationItemInset;
    internal double NavigationHeaderRightEdge
    {
        get
        {
            var root = XamlRoot.Content;
            if (IsNavigationPaneOpen && ActualWidth >= 700 && FindNavigationSurface(NativeHome) is { ActualWidth: > 0 } surface)
            { return surface.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(surface.ActualWidth, 0)).X; }
            return NavigationRailLeft + ShellNavigation.OpenPaneLength - _navigationItemInset.Right;
        }
    }
    internal void ToggleNavigationPane() => ShellNavigation.IsPaneOpen = !ShellNavigation.IsPaneOpen;

    private void OnNavigationItemLoaded(object sender, RoutedEventArgs e)
    {
        // Read the native highlight's inset; the pane width remains the actual
        // OpenPaneLength even when the pane is currently compact or overlayed.
        if (FindNavigationSurface(NativeHome) is { } surface) { _navigationItemInset = surface.Margin; }
        UpdateRoomNavigation();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.ConfirmAction = ConfirmActionAsync;
        ViewModel.ConfirmScene = ConfirmSceneAsync;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.Rooms.CollectionChanged += OnRoomsChanged;
        ViewModel.ScenePanel.SetHome(ViewModel.SelectedHome?.Home);
        if (XamlRoot is not null)
        {
            _activationListener = InputActivationListener.GetForWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId);
            _activationListener.InputActivationChanged += OnInputActivationChanged;
            ViewModel.SetPollingActive(_activationListener.State == InputActivationState.Activated);
        }

        RebuildNavigation();
        await ViewModel.RefreshCommand.ExecuteAsync(null);
        // Initial content is presented without an entrance, including cached data.
        _presentationReady = true;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.ConfirmAction = null;
        ViewModel.ConfirmScene = null;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.Rooms.CollectionChanged -= OnRoomsChanged;
        if (_activationListener is not null)
        {
            _activationListener.InputActivationChanged -= OnInputActivationChanged;
            _activationListener = null;
        }

        ViewModel.Stop();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DevicesViewModel.SelectedDevice))
        {
            UpdateSheetPlacement();
            if (ViewModel.HasSelection && ActualWidth < 1200)
            {
                DispatcherQueue.TryEnqueue(() => { if (ViewModel.HasSelection) { CloseDetailsButton.Focus(FocusState.Programmatic); } });
            }
        }

        if (e.PropertyName == nameof(DevicesViewModel.CurrentPage))
        {
            PresentPage();
            UpdateRoomNavigation();
        }

        if (e.PropertyName is nameof(DevicesViewModel.CurrentPage) or nameof(DevicesViewModel.ActiveRoom))
        {
            SelectNavigation();
        }

    }

    private void PresentPage()
    {
        Page page = ViewModel.CurrentPage switch
        {
            ShellPageKind.Room => _roomPage,
            ShellPageKind.Activity => _activityPage,
            ShellPageKind.Settings => _settingsPage,
            ShellPageKind.Scenes => _scenesPage,
            _ => _homePage
        };
        if (!ReferenceEquals(ScreenFrame.Content, page))
        {
            ScreenFrame.Content = page;
            if (_presentationReady && _navigationRequested && page.Content is PageLayout layout) { layout.PlayEntrance(); }
        }
    }

    private void NavigateWithEntrance(Action navigate)
    {
        _navigationRequested = true;
        try { navigate(); }
        finally { _navigationRequested = false; }
    }

    private void OnRoomsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_navigationRebuildQueued) { return; }
        _navigationRebuildQueued = true;
        DispatcherQueue.TryEnqueue(() => { _navigationRebuildQueued = false; if (IsLoaded) { RebuildNavigation(); } });
    }

    private void RebuildNavigation()
    {
        if (ShellNavigation is null)
        {
            return;
        }

        UpdateRoomNavigation();
        _roomChipItems = ViewModel.Rooms.Select(room => new RoomChipItem(room)).ToArray();
        RoomChips.ItemsSource = _roomChipItems;
        bool roomsExpanded = NativeRooms.IsExpanded;
        NativeRooms.IsExpanded = false;
        NativeRooms.MenuItems.Clear();
        foreach (RoomGroup room in ViewModel.Rooms)
        { NativeRooms.MenuItems.Add(new NavigationViewItem { Content = room.Name, Tag = room }); }
        NativeRooms.IsExpanded = roomsExpanded;
        SelectNavigation();
    }

    private void OnNavigationInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        NavigateWithEntrance(() =>
        {
            switch (args.InvokedItemContainer?.Tag)
            {
                case RoomGroup room: ViewModel.ShowRoom(room); break;
                case "Home": ViewModel.NavigateHomeCommand.Execute(null); break;
                case "Scenes": ViewModel.NavigateScenesCommand.Execute(null); break;
                case "Activity": ViewModel.NavigateActivityCommand.Execute(null); break;
                case "Settings": ViewModel.NavigateSettingsCommand.Execute(null); break;
            }
        });
        if (ActualWidth < 700 && args.InvokedItemContainer is NavigationViewItem { SelectsOnInvoked: true }) { sender.IsPaneOpen = false; }
    }

    private void OnNavigationPaneChanged(NavigationView sender, object args)
    {
        _paneTransitionOpen = null;
        UpdateRoomNavigation();
    }

    private void OnNavigationPaneOpening(NavigationView sender, object args)
    {
        _paneTransitionOpen = true;
        UpdateRoomNavigation();
    }

    private void OnNavigationPaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args)
    {
        _paneTransitionOpen = false;
        UpdateRoomNavigation();
    }

    private static FrameworkElement? FindNavigationSurface(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement { Name: "LayoutRoot", Margin.Right: > 0 } surface) { return surface; }
            if (FindNavigationSurface(child) is { } found) { return found; }
        }
        return null;
    }

    private void SelectNavigation()
    {
        ShellNavigation.SelectedItem = ViewModel.CurrentPage switch
        {
            ShellPageKind.Scenes => NativeScenes,
            ShellPageKind.Activity => NativeActivity,
            ShellPageKind.Settings => NativeSettings,
            ShellPageKind.Room => NativeRooms.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(item => ReferenceEquals(item.Tag, ViewModel.ActiveRoom)) ?? NativeHome,
            _ => NativeHome
        };
        foreach (RoomGroup room in ViewModel.Rooms)
        {
            room.IsActive = ViewModel.CurrentPage == ShellPageKind.Room && ReferenceEquals(room, ViewModel.ActiveRoom);
        }

        foreach (RoomChipItem chip in _roomChipItems)
        {
            chip.IsActive = ViewModel.CurrentPage == ShellPageKind.Room && ReferenceEquals(chip.Room, ViewModel.ActiveRoom);
        }

        _isSynchronizingRoomSelection = true;
        try { RoomChips.SelectedItem = _roomChipItems.FirstOrDefault(chip => chip.IsActive); }
        finally { _isSynchronizingRoomSelection = false; }

        QueueActiveRoomIntoView();
    }

    private void OnRoomSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isSynchronizingRoomSelection && e.AddedItems.FirstOrDefault() is RoomChipItem selected)
        {
            RoomGroup room = selected.Room;
            foreach (RoomChipItem chip in _roomChipItems) { chip.IsActive = ReferenceEquals(chip.Room, room); }
            QueueActiveRoomIntoView();
            NavigateWithEntrance(() => ViewModel.ShowRoom(room));
        }
    }

    private void OnRoomChipLoaded(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RoomChipItem chip && RoomChips.ContainerFromItem(chip) is ListViewItem item)
        { Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, chip.Name); }
        QueueActiveRoomIntoView();
    }
    private void OnRoomScrollLoaded(object sender, RoutedEventArgs e) => QueueActiveRoomIntoView();
    private void OnRoomStripSizeChanged(object sender, SizeChangedEventArgs e)
    {
        QueueActiveRoomIntoView();
    }
    private void OnRoomScrollSizeChanged(object sender, SizeChangedEventArgs e) => QueueActiveRoomIntoView();
    private void OnRoomScrollChanged(object? sender, ScrollViewerViewChangedEventArgs e) => UpdateRoomScrollAffordance();
    private void OnPreviousRooms(object sender, RoutedEventArgs e) => ScrollRooms(-1);
    private void OnNextRooms(object sender, RoutedEventArgs e) => ScrollRooms(1);
    private void ScrollRooms(int direction) => RoomScroll.ChangeView(
        Math.Clamp(RoomScroll.HorizontalOffset + direction * Math.Max(64, RoomScroll.ViewportWidth * 0.75), 0, RoomScroll.ScrollableWidth), null, null, true);

    private void QueueActiveRoomIntoView()
    {
        if (!IsLoaded || RoomStrip.Visibility != Visibility.Visible) { return; }
        RoomChipItem? active = _roomChipItems.FirstOrDefault(chip => chip.IsActive);
        if (active is not null && RoomChips.ContainerFromItem(active) is FrameworkElement container && container.ActualWidth > 0)
        {
            container.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, HorizontalAlignmentRatio = 0.5 });
            double left = container.TransformToVisual(RoomChips).TransformPoint(new Windows.Foundation.Point()).X;
            double right = left + container.ActualWidth;
            double offset = RoomScroll.HorizontalOffset;
            if (left < offset + 12) { offset = left - 12; }
            else if (right > offset + RoomScroll.ViewportWidth - 12) { offset = right - RoomScroll.ViewportWidth + 12; }
            RoomScroll.ChangeView(Math.Clamp(offset, 0, RoomScroll.ScrollableWidth), null, null, true);
        }

        UpdateRoomScrollAffordance();
    }

    private void UpdateRoomScrollAffordance()
    {
        PreviousRooms.IsEnabled = RoomScroll.HorizontalOffset > 1;
        NextRooms.IsEnabled = RoomScroll.HorizontalOffset < RoomScroll.ScrollableWidth - 1;
        RoomLeftFade.Visibility = PreviousRooms.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        RoomRightFade.Visibility = NextRooms.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        PreviousRooms.Opacity = PreviousRooms.IsEnabled ? 1 : 0.3;
        NextRooms.Opacity = NextRooms.IsEnabled ? 1 : 0.3;
    }


    private void UpdateRoomNavigation()
    {
        if (RoomStrip is null || ShellNavigation is null || NativeRooms is null)
        {
            return;
        }

        bool paneOpen = _paneTransitionOpen ?? ShellNavigation.IsPaneOpen;
        NavigationLayoutChanged?.Invoke(this, EventArgs.Empty);
        NativeRooms.Visibility = paneOpen ? Visibility.Visible : Visibility.Collapsed;
        RoomSeparator.Visibility = NativeRooms.Visibility;

        bool roomContext = ViewModel.CurrentPage is ShellPageKind.Home or ShellPageKind.Room;
        RoomStrip.Margin = new Thickness(16, 0, 16, 0);
        bool showStrip = roomContext && !paneOpen;
        NavigationRow.Height = new GridLength(showStrip ? RoomStrip.Height : 0);
        RoomStrip.Visibility = showStrip ? Visibility.Visible : Visibility.Collapsed;
        if (RoomStrip.Visibility == Visibility.Visible) { QueueActiveRoomIntoView(); }
    }

    private void OnDeviceOpened(object? sender, DeviceRowViewModel device)
    {
        _returnFocus = FocusManager.GetFocusedElement(XamlRoot) as Control;
        ViewModel.SelectedDevice = device;
    }

    private Task<bool> ConfirmActionAsync(ActionRowViewModel action, CancellationToken token) =>
        ShowConfirmationAsync(action.DisplayName, action.ConfirmationMessage, token);

    private Task<bool> ConfirmSceneAsync(SceneRowViewModel scene, CancellationToken token) =>
        ShowConfirmationAsync(scene.Name, $"确定要执行「{scene.Name}」吗？\n家庭：{ViewModel.SelectedHome?.Name}", token);

    private async Task<bool> ShowConfirmationAsync(string title, string message, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsLoaded || XamlRoot is null || _isActionDialogOpen)
        {
            throw new InvalidOperationException("无法显示确认对话框，操作未执行");
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "执行",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        _isActionDialogOpen = true;
        try
        {
            using CancellationTokenRegistration registration = token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
            ContentDialogResult result = await dialog.ShowAsync();
            token.ThrowIfCancellationRequested();
            return result == ContentDialogResult.Primary;
        }
        finally
        {
            _isActionDialogOpen = false;
        }
    }

    private void OnInputActivationChanged(InputActivationListener sender, InputActivationListenerActivationChangedEventArgs args)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            ViewModel.SetPollingActive(IsLoaded && sender.State == InputActivationState.Activated);
        }
        else
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (IsLoaded && ReferenceEquals(sender, _activationListener))
                {
                    ViewModel.SetPollingActive(sender.State == InputActivationState.Activated);
                }
            });
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DetailsHost is null)
        {
            return;
        }

        UpdateSheetPlacement();
        UpdateRoomNavigation();
    }

    private void OnDetailsHostSizeChanged(object sender, SizeChangedEventArgs e) => UpdateSheetPlacement();

    private void UpdateSheetPlacement()
    {
        if (DeviceSheet is null || ScreenFrame is null)
        {
            return;
        }

        bool docked = ActualWidth >= 1200;
        DeviceSheet.Width = ActualWidth < 700 ? double.NaN : Math.Min(420, Math.Max(240, DetailsHost.ActualWidth));
        DeviceSheet.HorizontalAlignment = ActualWidth < 700 ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        Grid.SetColumn(DeviceSheet, docked ? 1 : 0);
        Grid.SetColumnSpan(DeviceSheet, docked ? 1 : 2);
        SheetScrim.Visibility = ViewModel.HasSelection && !docked ? Visibility.Visible : Visibility.Collapsed;
        ScreenFrame.IsHitTestVisible = !ViewModel.HasSelection || docked;
        ScreenFrame.IsEnabled = !ViewModel.HasSelection || docked;
        ScreenFrame.IsTabStop = !ViewModel.HasSelection || docked;
    }

    private void OnSheetScrimTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        ViewModel.CloseDetailsCommand.Execute(null);
        RestoreFocus();
    }

    private void OnCloseDetails(object sender, RoutedEventArgs e) => RestoreFocus();
    private void RestoreFocus()
    {
        if (_returnFocus?.Focus(FocusState.Programmatic) != true)
        {
            ShellNavigation.Focus(FocusState.Programmatic);
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && !_isActionDialogOpen && ViewModel.HasSelection)
        {
            ViewModel.CloseDetailsCommand.Execute(null);
            RestoreFocus();
            e.Handled = true;
        }
    }
}

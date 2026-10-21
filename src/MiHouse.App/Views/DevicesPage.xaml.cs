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
        _homePage.GroupOpened += (_, group) => ViewModel.ShowClass(group);
        _roomPage.DeviceOpened += OnDeviceOpened;
        PresentPage();
    }

    public DevicesViewModel ViewModel { get; }
    internal event EventHandler<double>? NavigationPaneWidthChanged;

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
        UpdateNavigationToolTip();
        await ViewModel.RefreshCommand.ExecuteAsync(null);
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
        }
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
        switch (args.InvokedItemContainer?.Tag)
        {
            case RoomGroup room: ViewModel.ShowRoom(room); break;
            case "Home": ViewModel.NavigateHomeCommand.Execute(null); break;
            case "Scenes": ViewModel.NavigateScenesCommand.Execute(null); break;
            case "Activity": ViewModel.NavigateActivityCommand.Execute(null); break;
            case "Settings": ViewModel.NavigateSettingsCommand.Execute(null); break;
        }
        if (ActualWidth < 700 && args.InvokedItemContainer is NavigationViewItem { SelectsOnInvoked: true }) { sender.IsPaneOpen = false; }
    }

    private void OnNavigationPaneChanged(NavigationView sender, object args)
    {
        UpdateRoomNavigation();
        UpdateNavigationToolTip();
    }

    private void UpdateNavigationToolTip()
    {
        // generic.xaml's TogglePaneButton has no tooltip string resource. Keep its
        // native template and localize the existing button after native pane updates.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (FindToggleButton(ShellNavigation) is not { } button) { return; }
            string label = ShellNavigation.IsPaneOpen ? "收起导航" : "展开导航";
            ToolTipService.SetToolTip(button, label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        });
    }

    private static Button? FindToggleButton(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is Button { Name: "TogglePaneButton" } button) { return button; }
            if (FindToggleButton(child) is { } found) { return found; }
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
            ViewModel.ShowRoom(room);
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
        AlignRoomStripWithPaneToggle();
        UpdateNavigationToolTip();
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

        bool expanded = ShellNavigation.IsPaneOpen && ActualWidth >= 700;
        NavigationPaneWidthChanged?.Invoke(this, ShellNavigation.IsPaneOpen ? ShellNavigation.OpenPaneLength : ShellNavigation.CompactPaneLength);
        NativeRooms.Visibility = expanded || ShellNavigation.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        RoomSeparator.Visibility = NativeRooms.Visibility;

        bool roomContext = ViewModel.CurrentPage is ShellPageKind.Home or ShellPageKind.Room;
        RoomStrip.Margin = new Thickness(ActualWidth < 700 ? 56 : 16, RoomStrip.Margin.Top, 16, 0);
        NavigationRow.Height = new GridLength(expanded ? 0 : RoomStrip.Height + RoomStrip.Margin.Top);
        RoomStrip.Visibility = roomContext && !expanded ? Visibility.Visible : Visibility.Collapsed;
        DispatcherQueue.TryEnqueue(AlignRoomStripWithPaneToggle);
        if (RoomStrip.Visibility == Visibility.Visible) { QueueActiveRoomIntoView(); }
    }

    private void AlignRoomStripWithPaneToggle()
    {
        if (!IsLoaded || RoomStrip.Parent is not UIElement parent || FindToggleButton(ShellNavigation) is not { IsLoaded: true } toggle || toggle.ActualHeight <= 0) { return; }
        // The native button includes template padding outside its visible surface.
        // Align with that surface (and its glyph), not the larger control bounds.
        FrameworkElement surface = VisualTreeHelper.GetChildrenCount(toggle) > 0 && VisualTreeHelper.GetChild(toggle, 0) is FrameworkElement root ? root : toggle;
        double height = (double)Resources["PaneToggleButtonHeight"];
        double center = surface.TransformToVisual(parent).TransformPoint(new Windows.Foundation.Point(0, surface.ActualHeight / 2)).Y;
        double top = Math.Max(0, center - height / 2);
        RoomStrip.Height = height;
        var margin = RoomStrip.Margin;
        if (Math.Abs(margin.Top - top) > 0.01) { RoomStrip.Margin = new Thickness(margin.Left, top, margin.Right, 0); }
        bool expanded = ShellNavigation.IsPaneOpen && ActualWidth >= 700;
        NavigationRow.Height = new GridLength(expanded ? 0 : RoomStrip.Height + top);
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

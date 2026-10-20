using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MiHouse.App.ViewModels;
using Windows.System;

namespace MiHouse.App.Views;

public sealed partial class DevicesPage : Page
{
    private bool _isActionDialogOpen;

    public DevicesPage(DevicesViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        ((Microsoft.UI.Xaml.Data.CollectionViewSource)Resources["GroupedDevices"]).Source = viewModel.Rooms;
    }

    public DevicesViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.ConfirmAction = ConfirmActionAsync;
        await ViewModel.RefreshCommand.ExecuteAsync(null);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.ConfirmAction = null;
        ViewModel.Stop();
    }

    private async Task<bool> ConfirmActionAsync(ActionRowViewModel action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsLoaded || XamlRoot is null)
        {
            throw new InvalidOperationException("无法显示确认对话框，操作未执行");
        }

        if (_isActionDialogOpen)
        {
            throw new InvalidOperationException("请先完成当前操作的确认");
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = action.Name,
            Content = new TextBlock { Text = action.ConfirmationMessage, TextWrapping = TextWrapping.Wrap },
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

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        DetailsSplitView.DisplayMode = e.NewSize.Width < 880 ? SplitViewDisplayMode.Overlay : SplitViewDisplayMode.Inline;
        DetailsSplitView.OpenPaneLength = Math.Max(240, Math.Min(360, e.NewSize.Width - 48));
    }

    private void OnPaneOpened(SplitView sender, object args)
    {
        if (sender.DisplayMode == SplitViewDisplayMode.Overlay)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (DetailsSplitView.DisplayMode == SplitViewDisplayMode.Overlay &&
                    DetailsSplitView.IsPaneOpen && ViewModel.HasSelection)
                {
                    CloseDetailsButton.Focus(FocusState.Programmatic);
                }
            });
        }
    }

    private void OnPaneClosed(SplitView sender, object args)
    {
        if (sender.DisplayMode == SplitViewDisplayMode.Overlay && !sender.IsPaneOpen && ViewModel.HasSelection)
        {
            ViewModel.CloseDetailsCommand.Execute(null);
        }
    }

    private void OnCloseDetails(object sender, RoutedEventArgs e) => DeviceList.Focus(FocusState.Programmatic);

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && !_isActionDialogOpen && ViewModel.HasSelection)
        {
            ViewModel.CloseDetailsCommand.Execute(null);
            DeviceList.Focus(FocusState.Programmatic);
            e.Handled = true;
        }
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MiHouse.App.ViewModels;

namespace MiHouse.App.Views;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
    }
    public event EventHandler<DeviceClassGroupViewModel>? GroupOpened;
    private void OnGroupCardClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DeviceClassCardViewModel card) { GroupOpened?.Invoke(this, card.Group); }
    }
    private async void OnGroupPowerRequested(object? sender, bool desired)
    {
        if ((sender as FrameworkElement)?.DataContext is DeviceClassCardViewModel card) { await card.Group.SetPowerAsync(desired); }
    }
}

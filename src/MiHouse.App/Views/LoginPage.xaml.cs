using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using MiHouse.App.ViewModels;
using Windows.System;

namespace MiHouse.App.Views;

public sealed partial class LoginPage : Page
{
    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public LoginViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateQrImage();
        QrButton.Focus(FocusState.Programmatic);
        await ViewModel.InitializeCommand.ExecuteAsync(null);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        QrImage.Source = null;
        ViewModel.Stop();
    }

    private void OnShowPassword(object sender, RoutedEventArgs e) =>
        DispatcherQueue.TryEnqueue(() => AccountBox.Focus(FocusState.Programmatic));

    private void OnShowQr(object sender, RoutedEventArgs e) =>
        DispatcherQueue.TryEnqueue(() => QrButton.Focus(FocusState.Programmatic));

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            ViewModel.ClearCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Enter && ViewModel.IsPasswordMode)
        {
            if (ViewModel.SubmitPasswordCommand.CanExecute(null))
            {
                ViewModel.SubmitPasswordCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoginViewModel.QrImageUrl))
        {
            UpdateQrImage();
        }
    }

    private void UpdateQrImage()
    {
        QrImage.Source = null;
        string? attemptedUrl = ViewModel.QrImageUrl;
        if (attemptedUrl is null)
        {
            return;
        }

        if (!Uri.TryCreate(attemptedUrl, UriKind.Absolute, out Uri? uri))
        {
            ViewModel.ReportQrImageFailure(attemptedUrl);
            return;
        }

        // A new source captures each attempt, even if two tickets reuse the same URL.
        var image = new BitmapImage();
        image.ImageFailed += (_, _) =>
        {
            if (IsLoaded && ReferenceEquals(QrImage.Source, image))
            {
                ViewModel.ReportQrImageFailure(attemptedUrl);
            }
        };
        QrImage.Source = image;
        image.UriSource = uri;
    }
}

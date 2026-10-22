using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using MiHouse.App.Diagnostics;
using MiHouse.App.ViewModels;
using Windows.Storage.Streams;
using Windows.System;

namespace MiHouse.App.Views;

public sealed partial class LoginPage : Page
{
    private static readonly TimeSpan _qrImageTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _images;
    private CancellationTokenSource? _qrImageLoad;

    public LoginPage(LoginViewModel viewModel, HttpClient images)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _images = images;
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
        CancelQrImageLoad();
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

    private void UpdateQrImage() => _ = LoadQrImageAsync(ViewModel.QrImageUrl);

    /// <summary>
    /// Downloads the QR image with the app's own <see cref="HttpClient"/> and hands the decoded
    /// stream to the image element.
    /// </summary>
    /// <remarks>
    /// Assigning <see cref="BitmapImage.UriSource"/> downloads through urlmon/WinINet, which has no
    /// timeout of its own and performs the system certificate-revocation check: when the issuing
    /// CA's CRL/OCSP endpoints are unreachable, the first HTTPS request in the process blocks until
    /// the network timeouts expire (measured at 60 s for account.xiaomi.com's GoDaddy chain). The
    /// .NET client used here does not, and its 10 s budget turns a stalled download into a visible
    /// failure instead of a blank square.
    /// </remarks>
    private async Task LoadQrImageAsync(string? attemptedUrl)
    {
        CancelQrImageLoad();
        QrImage.Source = null;

        if (attemptedUrl is null)
        {
            return;
        }

        if (!Uri.TryCreate(attemptedUrl, UriKind.Absolute, out Uri? uri))
        {
            ViewModel.ReportQrImageFailure(attemptedUrl);
            return;
        }

        var cancellation = new CancellationTokenSource(_qrImageTimeout);
        _qrImageLoad = cancellation;
        try
        {
            byte[] bytes = await _images.GetByteArrayAsync(uri, cancellation.Token).ConfigureAwait(true);
            cancellation.Token.ThrowIfCancellationRequested();

            var image = new BitmapImage
            {
                DecodePixelType = DecodePixelType.Logical,
                DecodePixelWidth = 240,
            };

            using (var stream = new InMemoryRandomAccessStream())
            {
                using (var writer = new DataWriter(stream))
                {
                    writer.WriteBytes(bytes);
                    await writer.StoreAsync().AsTask(cancellation.Token);
                    await writer.FlushAsync().AsTask(cancellation.Token);
                    writer.DetachStream();
                }

                stream.Seek(0);
                await image.SetSourceAsync(stream).AsTask(cancellation.Token);
            }

            cancellation.Token.ThrowIfCancellationRequested();
            QrImage.Source = image;
            ViewModel.ReportQrImageReady(attemptedUrl);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Superseded by a newer ticket, the page unloaded, or the download timed out.
            if (ReferenceEquals(_qrImageLoad, cancellation) && IsLoaded)
            {
                ViewModel.ReportQrImageFailure(attemptedUrl);
            }
        }
        catch (Exception exception)
        {
            CrashLog.Write("LoginPage.QrImage", handled: true, exception);
            if (ReferenceEquals(_qrImageLoad, cancellation) && IsLoaded)
            {
                ViewModel.ReportQrImageFailure(attemptedUrl);
            }
        }
        finally
        {
            if (ReferenceEquals(_qrImageLoad, cancellation))
            {
                _qrImageLoad = null;
            }

            cancellation.Dispose();
        }
    }

    private void CancelQrImageLoad()
    {
        CancellationTokenSource? current = _qrImageLoad;
        _qrImageLoad = null;
        current?.Cancel();
    }
}

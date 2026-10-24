using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Windows.Storage;
using Windows.System;

namespace MiHouse.App.Views;

public sealed partial class AboutDialog : ContentDialog
{
    private XamlRoot? _dialogRoot;

    public AboutDialog()
    {
        InitializeComponent();
        Title = AppInfo.AboutTitle;
        AppName.Text = AppInfo.AboutDisplayName;
        AppDescription.Text = AppInfo.Description;
        VersionText.Text = string.IsNullOrWhiteSpace(AppInfo.BuildRevision)
            ? AppInfo.Version
            : $"{AppInfo.Version} · {AppInfo.BuildRevision}";
        PublisherText.Text = AppInfo.Publisher;
        LicenseText.Text = AppInfo.License;
        CopyrightText.Text = AppInfo.Copyright;
        if (AppInfo.LicenseTextPath is null)
        {
            LinksText.Inlines.Remove(LicenseLink);
            LinksText.Inlines.Remove(LicenseSeparator);
        }

        if (AppInfo.ThirdPartyNoticesPath is null)
        {
            LinksText.Inlines.Remove(NoticesLink);
            LinksText.Inlines.Remove(NoticesSeparator);
        }
    }

    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        _dialogRoot = XamlRoot;
        _dialogRoot.Changed += OnRootChanged;
        UpdateContentBounds();
        if (GetTemplateChild("CloseButton") is Button closeButton)
        {
            closeButton.HorizontalAlignment = HorizontalAlignment.Right;
            closeButton.MinWidth = 120;
            closeButton.Focus(FocusState.Programmatic);
        }
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        if (_dialogRoot is not null)
        {
            _dialogRoot.Changed -= OnRootChanged;
            _dialogRoot = null;
        }
    }

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateContentBounds();

    private void UpdateContentBounds()
    {
        if (_dialogRoot is not null)
        {
            double contentWidth = Math.Max(0, Math.Min(600, _dialogRoot.Size.Width - 96));
            ContentScroller.MinWidth = contentWidth;
            ContentScroller.MaxWidth = contentWidth;
            ContentScroller.MaxHeight = Math.Max(80, _dialogRoot.Size.Height - 240);
        }
    }

    private void OnIconOpened(object sender, RoutedEventArgs e) => FallbackIcon.Visibility = Visibility.Collapsed;

    private void OnIconFailed(object sender, ExceptionRoutedEventArgs e)
    {
        AppIcon.Visibility = Visibility.Collapsed;
        FallbackIcon.Visibility = Visibility.Visible;
    }

    private async void OnLicenseClicked(Hyperlink sender, HyperlinkClickEventArgs args) =>
        await OpenFileAsync(AppInfo.LicenseTextPath);

    private async void OnNoticesClicked(Hyperlink sender, HyperlinkClickEventArgs args) =>
        await OpenFileAsync(AppInfo.ThirdPartyNoticesPath);

    private async Task OpenFileAsync(string? path)
    {
        LaunchStatus.Visibility = Visibility.Collapsed;
        try
        {
            if (path is not null)
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(path);
                if (await Launcher.LaunchFileAsync(file))
                {
                    return;
                }
            }
        }
        catch (Exception)
        {
            // Files can disappear after startup, and Windows may not have a default handler.
        }

        ShowLaunchStatus("无法打开文档，请检查文件是否存在，并为此文件类型设置默认应用。");
    }

    private async void OnRepositoryClicked(Hyperlink sender, HyperlinkClickEventArgs args)
    {
        LaunchStatus.Visibility = Visibility.Collapsed;
        try
        {
            if (await Launcher.LaunchUriAsync(new Uri(AppInfo.RepositoryUrl)))
            {
                return;
            }
        }
        catch (Exception)
        {
            // A missing or unavailable browser should not dismiss the About dialog.
        }

        ShowLaunchStatus("无法打开源代码页面，请检查默认浏览器设置后重试。");
    }

    private void ShowLaunchStatus(string message)
    {
        LaunchStatus.Text = message;
        LaunchStatus.Visibility = Visibility.Visible;
    }
}

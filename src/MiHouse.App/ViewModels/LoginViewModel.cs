using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiHouse.Core.Auth;
using MiHouse.Core.Session;

namespace MiHouse.App.ViewModels;

public enum QrLoginState
{
    Generating,
    Waiting,
    Expired,
    Failed,
    Confirmed
}

[Microsoft.UI.Xaml.Data.Bindable]
public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _auth;
    private readonly ICredentialStore _credentials;
    private CancellationTokenSource? _lifetime;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial string? QrImageUrl { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsPolling))]
    public partial QrLoginState State { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQrMode))]
    public partial bool IsPasswordMode { get; set; }

    [ObservableProperty]
    public partial string Account { get; set; }

    [ObservableProperty]
    public partial string Password { get; set; }

    public LoginViewModel(IAuthService auth, ICredentialStore credentials)
    {
        _auth = auth;
        _credentials = credentials;
        Account = string.Empty;
        Password = string.Empty;
    }

    public event Action<MiHomeSession>? SignedIn;

    public bool IsQrMode => !IsPasswordMode;
    public bool IsPolling => State == QrLoginState.Waiting && IsQrMode;
    public bool IsEmpty => QrImageUrl is null;
    public string StatusText => State switch
    {
        QrLoginState.Generating => "正在生成二维码",
        QrLoginState.Waiting => "请用米家 App 扫码",
        QrLoginState.Expired => "二维码已过期，点击刷新",
        QrLoginState.Confirmed => "登录成功",
        _ => Error ?? "二维码加载失败，请重试"
    };

    [RelayCommand]
    private async Task InitializeAsync()
    {
        CancellationToken token = RenewLifetime();
        IsBusy = true;
        try
        {
            MiHomeSession? session = await _credentials.LoadAsync(token);
            token.ThrowIfCancellationRequested();
            if (session is not null)
            {
                SignedIn?.Invoke(session);
                return;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            Error = $"无法读取已保存的登录信息：{ex.Message}";
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsBusy = false;
            }
        }

        await RefreshQrAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RefreshQrAsync()
    {
        CancellationToken token = RenewLifetime();
        IsPasswordMode = false;
        Error = null;
        QrImageUrl = null;
        State = QrLoginState.Generating;
        IsBusy = true;
        try
        {
            QrLoginTicket ticket = await _auth.StartQrLoginAsync(token);
            token.ThrowIfCancellationRequested();
            QrImageUrl = ticket.ImageUrl;
            token.ThrowIfCancellationRequested();
            State = QrLoginState.Waiting;

            // IsBusy stays set until the page reports the image as shown or failed, so the progress
            // ring covers the whole download instead of disappearing over an empty square.
            while (DateTimeOffset.UtcNow < ticket.ExpiresAt)
            {
                MiHomeSession? session = await _auth.PollQrLoginAsync(ticket, token);
                token.ThrowIfCancellationRequested();
                if (session is not null)
                {
                    await CompleteAsync(session, token);
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }

            State = QrLoginState.Expired;
        }
        catch (TimeoutException)
        {
            if (!token.IsCancellationRequested)
            {
                State = QrLoginState.Expired;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                State = QrLoginState.Failed;
                Error = ex.Message;
            }
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsBusy = false;
            }
        }
    }

    [RelayCommand]
    private void ShowPassword()
    {
        Stop();
        IsPasswordMode = true;
        QrImageUrl = null;
        Error = null;
        Password = string.Empty;
        OnPropertyChanged(nameof(IsPolling));
    }

    [RelayCommand]
    private async Task SubmitPasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(Account) || string.IsNullOrEmpty(Password))
        {
            Error = "请输入账号和密码";
            return;
        }

        string password = Password;
        CancellationToken token = RenewLifetime();
        IsBusy = true;
        Error = null;
        try
        {
            MiHomeSession session = await _auth.LoginWithPasswordAsync(Account.Trim(), password, token);
            token.ThrowIfCancellationRequested();
            await CompleteAsync(session, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                // Includes MiAuthException's unsupported-2FA explanation verbatim.
                Error = ex.Message;
            }
        }
        finally
        {
            Password = string.Empty;
            if (!token.IsCancellationRequested)
            {
                IsBusy = false;
            }
        }
    }

    [RelayCommand]
    private void Clear()
    {
        Stop();
        Account = string.Empty;
        Password = string.Empty;
        Error = null;
        if (IsQrMode)
        {
            State = QrLoginState.Expired;
        }
    }

    public void Stop()
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        IsBusy = false;
        Password = string.Empty;
    }

    public void ReportQrImageFailure(string attemptedUrl)
    {
        if (!IsQrMode || string.IsNullOrEmpty(QrImageUrl) ||
            !string.Equals(QrImageUrl, attemptedUrl, StringComparison.Ordinal))
        {
            return;
        }

        Stop();
        QrImageUrl = null;
        Error = "二维码图片加载失败，请刷新后重试";
        State = QrLoginState.Failed;
    }

    /// <summary>
    /// Clears the busy indicator once the QR image for <paramref name="attemptedUrl"/> is visible.
    /// </summary>
    public void ReportQrImageReady(string attemptedUrl)
    {
        if (!IsQrMode || string.IsNullOrEmpty(QrImageUrl) ||
            !string.Equals(QrImageUrl, attemptedUrl, StringComparison.Ordinal))
        {
            return;
        }

        IsBusy = false;
    }

    private CancellationToken RenewLifetime()
    {
        Stop();
        _lifetime = new CancellationTokenSource();
        return _lifetime.Token;
    }

    private async Task CompleteAsync(MiHomeSession session, CancellationToken token)
    {
        await _credentials.SaveAsync(session, token);
        token.ThrowIfCancellationRequested();
        Password = string.Empty;
        State = QrLoginState.Confirmed;
        SignedIn?.Invoke(session);
    }

    partial void OnQrImageUrlChanged(string? value) => OnPropertyChanged(nameof(IsEmpty));
}

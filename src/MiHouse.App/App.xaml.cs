using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using MiHouse.App.Security;
using MiHouse.Core.Auth;
using MiHouse.Core.Cloud;
using MiHouse.Core.Devices;
using MiHouse.Core.Spec;

namespace MiHouse.App;

/// <summary>
/// Composition root for the desktop application.
/// </summary>
public partial class App : Application
{
    private const string _userAgent =
        "Android-7.1.1-1.0.0-ONEPLUS A3010-136-ABCDEF1234567 APP/xiaomi.smarthome APPV/62830";

    private Window? _window;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
    }

    /// <summary>
    /// Gets the application service provider.
    /// </summary>
    public IServiceProvider Services { get; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = Services.GetRequiredService<MainWindow>();
        _window.Activate();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder
            .AddDebug()
            .SetMinimumLevel(LogLevel.Information));

        // API calls are bounded; the auth client needs headroom for the 120-second
        // long-poll, whose per-request budget is enforced by the auth service itself.
        services.AddSingleton(_ => CreateHttpClient(TimeSpan.FromSeconds(30)));
        services.AddKeyedSingleton("auth", (_, _) => CreateHttpClient(TimeSpan.FromSeconds(300)));

        services.AddSingleton<IAuthService>(provider =>
            new MiHomeAuthService(provider.GetRequiredKeyedService<HttpClient>("auth")));
        services.AddSingleton<ICredentialStore, DpapiCredentialStore>();
        services.AddSingleton<IMiHomeCloudClient>(provider =>
            new MiHomeCloudClient(provider.GetRequiredService<HttpClient>()));
        services.AddSingleton<ISpecCatalog>(provider =>
            new SpecCatalog(provider.GetRequiredService<HttpClient>()));

        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    private static HttpClient CreateHttpClient(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgent);
        return client;
    }
}

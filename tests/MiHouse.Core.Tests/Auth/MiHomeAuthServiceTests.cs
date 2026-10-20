using System.Diagnostics;
using System.Net;
using System.Text;
using MiHouse.Core.Auth;

namespace MiHouse.Core.Tests.Auth;

public sealed class MiHomeAuthServiceTests : IDisposable
{
    private const string _prefix = "&&&START&&&";
    private const string _serviceLoginBody =
        _prefix +
        """{"code":70016,"qs":"?sid=xiaomiio&_json=true","callback":"https://sts.api.io.mi.com/sts","_sign":"sign-1","serviceParam":"param-1"}""";

    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), "MiHouse.Core.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StartQrLoginExtractsTicketFromPrefixedResponses()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.Uri.AbsolutePath == "/longPolling/loginUrl")
            {
                return Json(
                    _prefix +
                    """{"qr":"https://qrcode.example/qr.png","lp":"https://account.xiaomi.com/longPolling/login?ticket=1","timeout":300}""");
            }

            throw new InvalidOperationException($"Unexpected request: {request.Uri}");
        });

        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);
        var before = DateTimeOffset.UtcNow;

        var ticket = await service.StartQrLoginAsync(CancellationToken.None);

        var after = DateTimeOffset.UtcNow;
        Assert.Equal("https://qrcode.example/qr.png", ticket.ImageUrl);
        Assert.Equal("https://account.xiaomi.com/longPolling/login?ticket=1", ticket.PollUrl);
        Assert.InRange(ticket.ExpiresAt, before.AddSeconds(300), after.AddSeconds(300));

        var loginRequest = Assert.Single(handler.Requests);
        var loginQuery = ParseQuery(loginRequest.Uri);
        Assert.Equal(4, loginQuery.Count);
        Assert.Equal("xiaomiio", loginQuery["sid"]);
        Assert.Equal("240", loginQuery["_qrsize"]);
        Assert.Equal("false", loginQuery["_hasLogo"]);
        Assert.Equal("true", loginQuery["_json"]);
    }

    [Fact]
    public async Task PasswordLoginUsesInjectedDeviceIdentityPath()
    {
        var deviceIdPath = Path.Combine(_tempDirectory, "device-id");
        var defaultPath = DeviceIdentity.DefaultFilePath();
        var defaultExisted = File.Exists(defaultPath);
        var defaultLastWrite = defaultExisted ? File.GetLastWriteTimeUtc(defaultPath) : default;

        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.Uri.AbsolutePath == "/pass/serviceLogin")
            {
                return Json(_serviceLoginBody);
            }

            if (request.Uri.AbsolutePath == "/pass/serviceLoginAuth2")
            {
                return Json(
                    _prefix +
                    """{"result":"ok","ssecurity":"ssecurity-value","userId":"987654321","cUserId":"cuser","passToken":"passtoken","location":"https://sts.api.io.mi.com/sts?sid=xiaomiio"}""");
            }

            if (request.Uri.Host == "sts.api.io.mi.com")
            {
                return Json("ok", "serviceToken=password-token; Path=/");
            }

            throw new InvalidOperationException($"Unexpected request: {request.Uri}");
        });

        using var httpClient = new HttpClient(handler);
        var service = new MiHomeAuthService(httpClient, new DeviceIdentity(deviceIdPath));

        _ = await service.LoginWithPasswordAsync("alice@example.com", "secret", CancellationToken.None);

        Assert.True(File.Exists(deviceIdPath));
        var deviceId = (await File.ReadAllTextAsync(deviceIdPath)).Trim();
        Assert.Matches("^[a-z]{16}$", deviceId);
        Assert.Equal(deviceId, new DeviceIdentity(deviceIdPath).GetOrCreate());

        var serviceLogin = Assert.Single(
            handler.Requests,
            request => request.Uri.AbsolutePath == "/pass/serviceLogin");
        Assert.True(serviceLogin.Headers.TryGetValue("Cookie", out var cookie));
        Assert.Contains($"deviceId={deviceId}", cookie);

        Assert.Equal(defaultExisted, File.Exists(defaultPath));
        if (defaultExisted)
        {
            Assert.Equal(defaultLastWrite, File.GetLastWriteTimeUtc(defaultPath));
        }
    }

    [Fact]
    public async Task PollReturnsNullUntilConfirmedThenBuildsSession()
    {
        var polls = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.Uri.AbsolutePath == "/longPolling/login")
            {
                polls++;
                return polls == 1
                    ? Json(_prefix + """{"code":70016}""")
                    : Json(
                        _prefix +
                        """{"code":0,"ssecurity":"ssecurity-value","userId":123456789,"cUserId":"cuser","passToken":"passtoken","location":"https://sts.api.io.mi.com/sts?sid=xiaomiio"}""");
            }

            if (request.Uri.Host == "sts.api.io.mi.com")
            {
                return Json(
                    "ok",
                    "yetAnotherServiceToken=other; Path=/; Secure",
                    "serviceToken=service-token-value; Path=/; Secure; HttpOnly");
            }

            throw new InvalidOperationException($"Unexpected request: {request.Uri}");
        });

        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);
        var ticket = new QrLoginTicket(
            "https://qrcode.example/qr.png",
            "https://account.xiaomi.com/longPolling/login?ticket=1",
            DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.Null(await service.PollQrLoginAsync(ticket, CancellationToken.None));

        var session = await service.PollQrLoginAsync(ticket, CancellationToken.None);

        Assert.NotNull(session);
        Assert.Equal("123456789", session.UserId);
        Assert.Equal("service-token-value", session.ServiceToken);
        Assert.Equal("ssecurity-value", session.Ssecurity);
        Assert.Equal("cn", session.Region);

        var stsRequest = Assert.Single(handler.Requests, request => request.Uri.Host == "sts.api.io.mi.com");
        Assert.Equal("https://sts.api.io.mi.com/sts?sid=xiaomiio", stsRequest.Uri.AbsoluteUri);
        Assert.Equal(
            "userId=123456789; cUserId=cuser; passToken=passtoken",
            stsRequest.Headers["Cookie"]);
    }

    [Fact]
    public async Task PollTimeoutBeforeExpiryReturnsNull()
    {
        var handler = new StubHttpMessageHandler(_ => throw new TaskCanceledException("HTTP timeout"));
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);
        var ticket = new QrLoginTicket(
            "https://qrcode.example/qr.png",
            "https://account.xiaomi.com/longPolling/login?ticket=1",
            DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.Null(await service.PollQrLoginAsync(ticket, CancellationToken.None));
    }

    [Fact]
    public async Task PollExpiredTicketThrowsWithoutHttpCall()
    {
        var handler = new StubHttpMessageHandler(
            _ => throw new InvalidOperationException("An HTTP call was not expected."));
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);
        var ticket = new QrLoginTicket(
            "https://qrcode.example/qr.png",
            "https://account.xiaomi.com/longPolling/login?ticket=1",
            DateTimeOffset.UtcNow.AddSeconds(-1));

        await Assert.ThrowsAsync<TimeoutException>(
            () => service.PollQrLoginAsync(ticket, CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PollBudgetIsBoundedByRemainingTicketLifetime()
    {
        var handler = new WaitingHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);
        var ticket = new QrLoginTicket(
            "https://qrcode.example/qr.png",
            "https://account.xiaomi.com/longPolling/login?ticket=1",
            DateTimeOffset.UtcNow.AddSeconds(1));

        var stopwatch = Stopwatch.StartNew();
        var exception = await Record.ExceptionAsync(
            async () => _ = await service.PollQrLoginAsync(ticket, CancellationToken.None));
        stopwatch.Stop();

        Assert.Equal(1, handler.CallCount);
        Assert.True(exception is null or TimeoutException, $"Unexpected exception: {exception}");
        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromMilliseconds(500),
            $"The poll returned before the remaining ticket lifetime elapsed: {stopwatch.Elapsed}.");
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"The poll used the full 120 s budget instead of the ticket lifetime: {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task PasswordLoginSendsExpectedFormAndReturnsSession()
    {
        const string Password = "secret";
        const string ExpectedHash = "5EBE2294ECD0E0F08EAB7690D2A6EE69";

        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.Uri.AbsolutePath == "/pass/serviceLogin")
            {
                return Json(_serviceLoginBody);
            }

            if (request.Uri.AbsolutePath == "/pass/serviceLoginAuth2")
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.NotNull(request.Body);

                var form = ParseForm(request.Body);
                Assert.Equal("xiaomiio", form["sid"]);
                Assert.Equal(ExpectedHash, form["hash"]);
                Assert.Equal("https://sts.api.io.mi.com/sts", form["callback"]);
                Assert.Equal("?sid=xiaomiio&_json=true", form["qs"]);
                Assert.Equal("alice@example.com", form["user"]);
                Assert.Equal("true", form["_json"]);
                Assert.Equal("sign-1", form["_sign"]);
                Assert.Equal(7, form.Count);

                return Json(
                    _prefix +
                    """{"result":"ok","ssecurity":"ssecurity-value","userId":"987654321","cUserId":"cuser","passToken":"passtoken","location":"https://sts.api.io.mi.com/sts?sid=xiaomiio"}""");
            }

            if (request.Uri.Host == "sts.api.io.mi.com")
            {
                return Json("ok", "serviceToken=password-token; Path=/");
            }

            throw new InvalidOperationException($"Unexpected request: {request.Uri}");
        });

        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);

        var session = await service.LoginWithPasswordAsync(
            "alice@example.com",
            Password,
            CancellationToken.None);

        Assert.Equal("987654321", session.UserId);
        Assert.Equal("password-token", session.ServiceToken);
        Assert.Equal("ssecurity-value", session.Ssecurity);
        Assert.Equal("cn", session.Region);
    }

    [Fact]
    public async Task PasswordChallengeThrowsMiAuthExceptionForTwoFactor()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.Uri.AbsolutePath == "/pass/serviceLogin")
            {
                return Json(_serviceLoginBody);
            }

            if (request.Uri.AbsolutePath == "/pass/serviceLoginAuth2")
            {
                return Json(
                    _prefix +
                    """{"result":"ok","notificationUrl":"https://account.xiaomi.com/2fa","ssecurity":"s","userId":"1","cUserId":"c","passToken":"p","location":"https://sts.api.io.mi.com/sts?sid=xiaomiio"}""");
            }

            throw new InvalidOperationException($"Unexpected request: {request.Uri}");
        });

        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);

        var exception = await Assert.ThrowsAsync<MiAuthException>(
            () => service.LoginWithPasswordAsync("alice@example.com", "secret", CancellationToken.None));

        Assert.Contains("2FA", exception.Message);
        Assert.DoesNotContain(handler.Requests, request => request.Uri.Host == "sts.api.io.mi.com");
    }

    [Fact]
    public async Task RejectedPasswordLoginExposesServerCodeAndMessage()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.Uri.AbsolutePath == "/pass/serviceLogin")
            {
                return Json(_serviceLoginBody);
            }

            if (request.Uri.AbsolutePath == "/pass/serviceLoginAuth2")
            {
                return Json(
                    _prefix +
                    """{"result":"error","code":70003,"description":"Invalid username or password"}""");
            }

            throw new InvalidOperationException($"Unexpected request: {request.Uri}");
        });

        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);

        var exception = await Assert.ThrowsAsync<MiAuthException>(
            () => service.LoginWithPasswordAsync("alice@example.com", "secret", CancellationToken.None));

        Assert.Equal(70003, exception.Code);
        Assert.Equal("Invalid username or password", exception.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private MiHomeAuthService CreateService(HttpClient httpClient)
        => new(httpClient, new DeviceIdentity(Path.Combine(_tempDirectory, "device-id")));

    private static HttpResponseMessage Json(string body, params string[] setCookies)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        foreach (var cookie in setCookies)
        {
            response.Headers.TryAddWithoutValidation("Set-Cookie", cookie);
        }

        return response;
    }

    private static Dictionary<string, string> ParseQuery(Uri uri)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            var name = WebUtility.UrlDecode(separator >= 0 ? pair[..separator] : pair);
            var value = separator >= 0 ? WebUtility.UrlDecode(pair[(separator + 1)..]) : string.Empty;
            result[name] = value;
        }

        return result;
    }

    private static Dictionary<string, string> ParseForm(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            var name = WebUtility.UrlDecode(separator >= 0 ? pair[..separator] : pair);
            var value = separator >= 0 ? WebUtility.UrlDecode(pair[(separator + 1)..]) : string.Empty;
            result[name] = value;
        }

        return result;
    }

    private sealed class WaitingHttpMessageHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("The request should only finish by cancellation.");
        }
    }
}

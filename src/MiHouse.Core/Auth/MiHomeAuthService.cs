using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MiHouse.Core.Session;

namespace MiHouse.Core.Auth;

/// <summary>
/// Logs in to the Mi Home cloud through the Xiaomi account service.
/// </summary>
public sealed class MiHomeAuthService : IAuthService
{
    private const string _jsonPrefix = "&&&START&&&";
    private const string _serviceId = "xiaomiio";
    private const string _stsCallback = "https://sts.api.io.mi.com/sts";
    private const string _defaultQueryString = "?sid=xiaomiio&_json=true";
    private const string _loginUrl = "https://account.xiaomi.com/longPolling/loginUrl";
    private const string _deviceCookie = "sdkVersion=3.8.6; deviceId={0}";
    private const int _pollTimeoutSeconds = 120;

    private static readonly Uri _serviceLoginUri =
        new($"https://account.xiaomi.com/pass/serviceLogin?sid={_serviceId}&_json=true");

    private static readonly Uri _serviceLoginAuth2Uri =
        new("https://account.xiaomi.com/pass/serviceLoginAuth2");

    private readonly HttpClient _httpClient;
    private readonly DeviceIdentity _identity;

    /// <summary>
    /// Initializes a new instance of the <see cref="MiHomeAuthService"/> class.
    /// </summary>
    /// <param name="httpClient">Client used for all account and STS requests.</param>
    /// <param name="stateDirectory">Directory that holds the persisted device identity.</param>
    public MiHomeAuthService(HttpClient httpClient, string stateDirectory)
        : this(httpClient, new DeviceIdentity(GetIdentityPath(stateDirectory)))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MiHomeAuthService"/> class.
    /// </summary>
    /// <param name="httpClient">Client used for all account and STS requests.</param>
    /// <param name="identity">Device identity persisted between runs.</param>
    internal MiHomeAuthService(HttpClient httpClient, DeviceIdentity identity)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    }

    /// <inheritdoc />
    public async Task<QrLoginTicket> StartQrLoginAsync(CancellationToken cancellationToken)
    {
        var query = new StringBuilder();
        AppendQuery(query, "sid", _serviceId);
        AppendQuery(query, "_qrsize", "240");
        AppendQuery(query, "_hasLogo", "false");
        AppendQuery(query, "_json", "true");

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{_loginUrl}?{query}"));
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = ParseJson(body);

        var imageUrl = RequiredString(result, "qr");
        var pollUrl = RequiredString(result, "lp");
        var timeout = ReadTimeoutSeconds(result);

        return new QrLoginTicket(imageUrl, pollUrl, DateTimeOffset.UtcNow.AddSeconds(timeout));
    }

    /// <inheritdoc />
    public async Task<MiHomeSession?> PollQrLoginAsync(QrLoginTicket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var now = DateTimeOffset.UtcNow;
        if (now >= ticket.ExpiresAt)
        {
            throw new TimeoutException("The QR login ticket expired before the user confirmed it.");
        }

        var remaining = ticket.ExpiresAt - now;
        var pollBudget = remaining < TimeSpan.FromSeconds(_pollTimeoutSeconds)
            ? remaining
            : TimeSpan.FromSeconds(_pollTimeoutSeconds);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(pollBudget);

        using var request = new HttpRequestMessage(HttpMethod.Get, ticket.PollUrl);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (DateTimeOffset.UtcNow >= ticket.ExpiresAt)
            {
                throw new TimeoutException("The QR login ticket expired before the user confirmed it.");
            }

            return null;
        }

        using (response)
        {
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = ParseJson(body);

            if (ReadInt64(result, "code") != 0)
            {
                return DateTimeOffset.UtcNow >= ticket.ExpiresAt
                    ? throw new TimeoutException("The QR login ticket expired before the user confirmed it.")
                    : null;
            }

            return await CompleteLoginAsync(result, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<MiHomeSession> LoginWithPasswordAsync(
        string account,
        string password,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(account);
        ArgumentNullException.ThrowIfNull(password);

        var login = await GetServiceLoginAsync(cancellationToken).ConfigureAwait(false);

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sid"] = _serviceId,
            ["hash"] = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(password))),
            ["callback"] = ReadNonEmptyString(login, "callback") ?? _stsCallback,
            ["qs"] = ReadNonEmptyString(login, "qs") ?? _defaultQueryString,
            ["user"] = account,
            ["_json"] = "true",
        };

        var sign = ReadNonEmptyString(login, "_sign");
        if (!string.IsNullOrEmpty(sign))
        {
            fields["_sign"] = sign;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _serviceLoginAuth2Uri)
        {
            Content = new FormUrlEncodedContent(fields),
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = ParseJson(body);

        var resultText = ReadString(result, "result");
        if (!string.Equals(resultText, "ok", StringComparison.Ordinal))
        {
            throw CreateRejectedException(result);
        }

        if (!string.IsNullOrEmpty(ReadString(result, "notificationUrl")))
        {
            throw new MiAuthException(
                "This account requires two-factor authentication (2FA), which is not supported yet.");
        }

        return await CompleteLoginAsync(result, cancellationToken).ConfigureAwait(false);
    }

    private static string GetIdentityPath(string stateDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        return Path.Combine(stateDirectory, "device-id");
    }

    private static MiAuthException CreateRejectedException(JsonElement result)
    {
        var code = ReadInt64(result, "code");
        var description = ReadString(result, "description") ?? ReadString(result, "desc");

        if (string.IsNullOrEmpty(description))
        {
            description = code.HasValue
                ? $"Xiaomi login failed with code {code.Value.ToString(CultureInfo.InvariantCulture)}."
                : "Xiaomi login failed.";
        }

        return new MiAuthException(description, code);
    }

    private async Task<JsonElement> GetServiceLoginAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _serviceLoginUri);
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            string.Format(CultureInfo.InvariantCulture, _deviceCookie, _identity.GetOrCreate()));

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseJson(body);
    }

    private async Task<MiHomeSession> CompleteLoginAsync(JsonElement result, CancellationToken cancellationToken)
    {
        var userId = RequiredString(result, "userId");
        var cUserId = RequiredString(result, "cUserId");
        var passToken = RequiredString(result, "passToken");
        var ssecurity = RequiredString(result, "ssecurity");
        var location = RequiredString(result, "location");

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(location, UriKind.Absolute));
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            $"userId={userId}; cUserId={cUserId}; passToken={passToken}");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // The STS body is normally "ok"; reading it also releases the connection promptly.
        _ = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var serviceToken = ReadCookie(response, "serviceToken")
            ?? throw new MiAuthException("The Xiaomi STS response did not contain a serviceToken cookie.");

        return new MiHomeSession(userId, serviceToken, ssecurity, "cn");
    }

    private static void AppendQuery(StringBuilder query, string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        if (query.Length > 0)
        {
            query.Append('&');
        }

        query
            .Append(Uri.EscapeDataString(name))
            .Append('=')
            .Append(Uri.EscapeDataString(value));
    }

    private static JsonElement ParseJson(string body)
    {
        var json = body.Trim();

        if (json.StartsWith(_jsonPrefix, StringComparison.Ordinal))
        {
            json = json[_jsonPrefix.Length..];
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null,
        };
    }

    private static string? ReadNonEmptyString(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return string.IsNullOrEmpty(value)
            ? throw new MiAuthException($"The Xiaomi login response did not contain '{propertyName}'.")
            : value;
    }

    private static long? ReadInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(
                property.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var number) => number,
            _ => null,
        };
    }

    private static double ReadTimeoutSeconds(JsonElement element)
    {
        if (element.TryGetProperty("timeout", out var property))
        {
            if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var number))
            {
                return number;
            }

            if (property.ValueKind == JsonValueKind.String
                && double.TryParse(
                    property.GetString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out number))
            {
                return number;
            }
        }

        throw new MiAuthException("The Xiaomi login response did not contain a timeout.");
    }

    private static string? ReadCookie(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var headers))
        {
            return null;
        }

        foreach (var header in headers)
        {
            var end = header.IndexOf(';');
            var pair = end >= 0 ? header.AsSpan(0, end) : header.AsSpan();
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            if (!pair[..separator].Trim().Equals(name.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = pair[(separator + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }

            if (!value.IsEmpty)
            {
                return value.ToString();
            }
        }

        return null;
    }
}

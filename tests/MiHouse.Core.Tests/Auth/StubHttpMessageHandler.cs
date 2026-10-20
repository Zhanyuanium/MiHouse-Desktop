using System.Net;

namespace MiHouse.Core.Tests.Auth;

internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    IReadOnlyDictionary<string, string> Headers,
    string? Body);

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<RecordedRequest, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<RecordedRequest, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        var recorded = new RecordedRequest(request.Method, request.RequestUri!, headers, body);
        Requests.Add(recorded);

        return _responder(recorded);
    }
}

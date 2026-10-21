using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiHouse.Core.Spec;

/// <summary>
/// Resolves device models through the published miot-spec index and caches the
/// resulting definitions in memory and, when configured, on disk.
/// </summary>
public sealed class SpecCatalog : ISpecCatalog
{
    private const string _indexUrl = "https://miot-spec.org/miot-spec-v2/instances?status=released";
    private const string _instanceUrlPrefix = "https://miot-spec.org/miot-spec-v2/instance?type=";
    private const string _indexCacheKey = "instances?status=released";
    private const string _indexCacheTimestampKey = "instances?status=released:fetched-at";

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private static readonly TimeSpan _indexCacheLifetime = TimeSpan.FromHours(24);

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _indexGate = new(1, 1);
    private readonly ConcurrentDictionary<string, SpecInstance> _specs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _specGates = new(StringComparer.Ordinal);
    private readonly SpecDiskCache? _diskCache;
    private readonly TimeProvider _timeProvider;
    private Dictionary<string, string>? _modelTypes;

    /// <summary>
    /// Creates a catalog that issues miot-spec requests through <paramref name="httpClient"/>.
    /// </summary>
    /// <param name="httpClient">Client used for miot-spec requests.</param>
    /// <param name="diskCache">
    /// Optional on-disk cache for raw spec documents; <see langword="null"/> disables disk caching.
    /// </param>
    /// <param name="timeProvider">Clock used to evaluate the instance-index cache lifetime.</param>
    public SpecCatalog(
        HttpClient httpClient,
        SpecDiskCache? diskCache = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _diskCache = diskCache;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<SpecInstance?> GetForModelAsync(string model, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        var modelTypes = await GetModelTypesAsync(cancellationToken).ConfigureAwait(false);
        if (!modelTypes.TryGetValue(model, out var urn))
        {
            return null;
        }

        return await GetOrFetchSpecAsync(urn, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<SpecInstance?> GetAsync(string urn, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(urn);
        return GetOrFetchSpecAsync(urn, cancellationToken);
    }

    private async Task<Dictionary<string, string>> GetModelTypesAsync(CancellationToken cancellationToken)
    {
        var modelTypes = Volatile.Read(ref _modelTypes);
        if (modelTypes is not null)
        {
            return modelTypes;
        }

        await _indexGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            modelTypes = _modelTypes;
            if (modelTypes is not null)
            {
                return modelTypes;
            }

            Dictionary<string, string>? staleIndex = null;
            var cachedIndex = ReadCachedIndex();
            if (cachedIndex is { } cached)
            {
                try
                {
                    modelTypes = ParseModelTypes(cached.Json);
                }
                catch (MiSpecException)
                {
                    modelTypes = null;
                }

                if (modelTypes is not null)
                {
                    if (IsFresh(cached.FetchedAt))
                    {
                        Volatile.Write(ref _modelTypes, modelTypes);
                        return modelTypes;
                    }

                    staleIndex = modelTypes;
                }
            }

            string json;
            try
            {
                json = await FetchIndexJsonAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsNetworkFailure(ex, cancellationToken))
            {
                if (staleIndex is not null)
                {
                    Volatile.Write(ref _modelTypes, staleIndex);
                    return staleIndex;
                }

                throw;
            }

            modelTypes = ParseModelTypes(json);
            WriteCachedIndex(json);
            Volatile.Write(ref _modelTypes, modelTypes);
            return modelTypes;
        }
        finally
        {
            _indexGate.Release();
        }
    }

    private async Task<string> FetchIndexJsonAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(_indexUrl, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new MiSpecException(
                $"The miot-spec instance index request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new MiSpecException("The miot-spec instance index response was empty.");
        }

        return json;
    }

    private CachedIndex? ReadCachedIndex()
    {
        if (_diskCache is null)
        {
            return null;
        }

        var json = _diskCache.TryRead(_indexCacheKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        _ = DateTimeOffset.TryParse(
            _diskCache.TryRead(_indexCacheTimestampKey),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var fetchedAt);

        return new CachedIndex(json, fetchedAt);
    }

    private bool IsFresh(DateTimeOffset fetchedAt)
    {
        return _timeProvider.GetUtcNow() - fetchedAt < _indexCacheLifetime;
    }

    private void WriteCachedIndex(string json)
    {
        if (_diskCache is null)
        {
            return;
        }

        _diskCache.Write(_indexCacheKey, json);
        _diskCache.Write(
            _indexCacheTimestampKey,
            _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
    }

    private static Dictionary<string, string> ParseModelTypes(string json)
    {
        List<SpecIndexEntryDto>? entries;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            entries = root.ValueKind switch
            {
                JsonValueKind.Array => root.Deserialize<List<SpecIndexEntryDto>>(_serializerOptions),
                JsonValueKind.Object => root.Deserialize<SpecIndexDto>(_serializerOptions)?.Instances,
                _ => throw new MiSpecException("The miot-spec instance index response has an unexpected shape."),
            };
        }
        catch (JsonException ex)
        {
            throw new MiSpecException("The miot-spec instance index response is not valid JSON.", ex);
        }

        var best = new Dictionary<string, (string Type, int Version)>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries ?? [])
        {
            if (string.IsNullOrEmpty(entry.Model) || string.IsNullOrEmpty(entry.Type))
            {
                continue;
            }

            var version = entry.Version ?? 0;
            if (!best.TryGetValue(entry.Model, out var current) || version > current.Version)
            {
                best[entry.Model] = (entry.Type, version);
            }
        }

        var modelTypes = new Dictionary<string, string>(best.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (model, entry) in best)
        {
            modelTypes[model] = entry.Type;
        }

        return modelTypes;
    }

    private async Task<SpecInstance?> GetOrFetchSpecAsync(string urn, CancellationToken cancellationToken)
    {
        if (_specs.TryGetValue(urn, out var cached))
        {
            return cached;
        }

        var gate = _specGates.GetOrAdd(urn, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_specs.TryGetValue(urn, out cached))
            {
                return cached;
            }

            var spec = await FetchSpecAsync(urn, cancellationToken).ConfigureAwait(false);
            if (spec is not null)
            {
                _specs[urn] = spec;
            }

            return spec;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<SpecInstance?> FetchSpecAsync(string urn, CancellationToken cancellationToken)
    {
        if (TryReadCachedSpec(urn, out var cached))
        {
            return cached;
        }

        string? json;
        try
        {
            json = await FetchSpecJsonAsync(urn, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNetworkFailure(ex, cancellationToken))
        {
            if (TryReadCachedSpec(urn, out cached))
            {
                return cached;
            }

            throw;
        }

        if (json is null)
        {
            return null;
        }

        var spec = ParseSpec(urn, json);
        _diskCache?.Write(urn, json);
        return spec;
    }

    private async Task<string?> FetchSpecJsonAsync(string urn, CancellationToken cancellationToken)
    {
        var url = _instanceUrlPrefix + Uri.EscapeDataString(urn);
        using var response = await SendAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new MiSpecException(
                $"The miot-spec definition request for '{urn}' failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(json) ? null : json;
    }

    private bool TryReadCachedSpec(string urn, [NotNullWhen(true)] out SpecInstance? spec)
    {
        spec = null;
        var json = _diskCache?.TryRead(urn);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            spec = ParseSpec(urn, json);
            return true;
        }
        catch (MiSpecException)
        {
            return false;
        }
    }

    private static bool IsNetworkFailure(Exception exception, CancellationToken cancellationToken)
    {
        return exception switch
        {
            MiSpecException => true,
            HttpRequestException => true,
            IOException => true,
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            _ => false,
        };
    }

    private static SpecInstance ParseSpec(string urn, string json)
    {
        SpecInstanceDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<SpecInstanceDto>(json, _serializerOptions);
        }
        catch (JsonException ex)
        {
            throw new MiSpecException($"The miot-spec definition for '{urn}' is not valid JSON.", ex);
        }

        if (dto is null)
        {
            throw new MiSpecException($"The miot-spec definition for '{urn}' was empty.");
        }

        var services = new List<SpecService>(dto.Services?.Count ?? 0);
        foreach (var service in dto.Services ?? [])
        {
            services.Add(MapService(service));
        }

        return new SpecInstance(
            string.IsNullOrEmpty(dto.Type) ? urn : dto.Type,
            dto.Description ?? string.Empty,
            services);
    }

    private static SpecService MapService(SpecServiceDto dto)
    {
        var properties = new List<SpecProperty>(dto.Properties?.Count ?? 0);
        foreach (var property in dto.Properties ?? [])
        {
            properties.Add(MapProperty(property, "property"));
        }

        var actions = new List<SpecAction>(dto.Actions?.Count ?? 0);
        foreach (var action in dto.Actions ?? [])
        {
            actions.Add(MapAction(action, properties));
        }

        return new SpecService(
            RequireIid(dto.Iid, "service"),
            dto.Type ?? string.Empty,
            dto.Description ?? string.Empty,
            properties,
            actions,
            NormalizeComment(dto.Comment));
    }

    private static SpecAction MapAction(SpecActionDto dto, IReadOnlyList<SpecProperty> serviceProperties)
    {
        var inputs = new List<SpecProperty>(dto.In?.Count ?? 0);
        foreach (var element in dto.In ?? [])
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                SpecPropertyDto? input;
                try
                {
                    input = element.Deserialize<SpecPropertyDto>(_serializerOptions);
                }
                catch (JsonException ex)
                {
                    throw new MiSpecException("An action input of the miot-spec definition is malformed.", ex);
                }

                if (input is not null)
                {
                    inputs.Add(MapProperty(input, "action input"));
                }

                continue;
            }

            if (TryReadIidReference(element, out var referencedIid))
            {
                foreach (var property in serviceProperties)
                {
                    if (property.Iid == referencedIid)
                    {
                        inputs.Add(property);
                        break;
                    }
                }
            }
        }

        return new SpecAction(
            RequireIid(dto.Iid, "action"),
            dto.Type ?? string.Empty,
            dto.Description ?? string.Empty,
            inputs);
    }

    private static SpecProperty MapProperty(SpecPropertyDto dto, string kind)
    {
        var access = dto.Access ?? [];
        IReadOnlyList<SpecValueList>? values = dto.ValueList is { Count: > 0 }
            ? dto.ValueList.ConvertAll(value => new SpecValueList(
                value.Value ?? throw new MiSpecException("A value-list entry of the miot-spec definition is missing 'value'."),
                value.Description ?? string.Empty))
            : null;

        return new SpecProperty(
            RequireIid(dto.Iid, kind),
            dto.Type ?? string.Empty,
            dto.Description ?? string.Empty,
            dto.Format ?? string.Empty,
            access.Exists(entry => string.Equals(entry, "read", StringComparison.OrdinalIgnoreCase)),
            access.Exists(entry => string.Equals(entry, "write", StringComparison.OrdinalIgnoreCase)),
            access.Exists(entry => string.Equals(entry, "notify", StringComparison.OrdinalIgnoreCase)),
            values,
            MapRange(dto.ValueRange),
            NormalizeComment(dto.Comment));
    }

    private static SpecRange? MapRange(List<double>? range)
    {
        if (range is null || range.Count == 0)
        {
            return null;
        }

        if (range.Count != 3)
        {
            throw new MiSpecException(
                "A value-range of the miot-spec definition must contain exactly minimum, maximum and step.");
        }

        return new SpecRange(range[0], range[1], range[2]);
    }

    private static bool TryReadIidReference(JsonElement element, out int iid)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetInt32(out iid);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out iid);
        }

        iid = 0;
        return false;
    }

    private static int RequireIid(int? iid, string kind)
    {
        return iid ?? throw new MiSpecException($"A miot-spec {kind} is missing its numeric 'iid'.");
    }

    private static string? NormalizeComment(string? comment)
    {
        return string.IsNullOrWhiteSpace(comment) ? null : comment;
    }

    private async Task<HttpResponseMessage> SendAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new MiSpecException($"The miot-spec request to '{url}' failed: {ex.Message}", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MiSpecException($"The miot-spec request to '{url}' timed out.", ex);
        }
    }

    private readonly record struct CachedIndex(string Json, DateTimeOffset FetchedAt);
}

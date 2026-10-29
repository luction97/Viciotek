using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Viciotek.Application;

namespace Viciotek.Infrastructure.Covers;

public sealed class SteamGridDbException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

/// <summary>Optional HTTPS integration. No calls during normal startup.</summary>
public sealed class SteamGridDbClient : ISteamGridDbGames, IDisposable
{
    // Official API v2 dimensions. 300x450 is NOT supported and causes HTTP 400 for the entire request.
    public const string VerticalDimensions = "600x900,342x482,660x930";
    private const int MaxDownloadBytes = 30 * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _apiGate = new(1, 1);
    private readonly SemaphoreSlim _imageGate = new(2, 2);
    private readonly Dictionary<string, (DateTimeOffset Expires, JsonElement Data, int Weight)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private int _cacheBytes;
    private int _credentialRevision;
    private int _cachedRevision;
    private DateTimeOffset _nextRequest;
    private long _limitedUntilUtcTicks;
    private string? _apiKey;

    public SteamGridDbClient(string? apiKey, HttpMessageHandler? handler = null)
    {
        UpdateApiKey(apiKey);
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Viciotek/1.0 (Windows)");
    }

    public bool IsConfigured => _apiKey is not null;

    public void UpdateApiKey(string? apiKey)
    {
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        Interlocked.Increment(ref _credentialRevision);
    }

    public async Task<SteamGridGame?> GetSteamGameAsync(int appId, CancellationToken cancellationToken)
    {
        if (!IsConfigured || appId <= 0) return null;
        var data = await GetDataAsync($"games/steam/{appId}", cancellationToken).ConfigureAwait(false);
        return ParseGame(data);
    }

    public async Task<IReadOnlyList<SteamGridGame>> SearchGamesAsync(string term, CancellationToken cancellationToken)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(term)) return [];
        var data = await GetDataAsync("search/autocomplete/" + Uri.EscapeDataString(term.Trim()), cancellationToken).ConfigureAwait(false);
        return data.ValueKind == JsonValueKind.Array
            ? data.EnumerateArray().Select(ParseGame).OfType<SteamGridGame>().DistinctBy(game => game.Id).Take(100).ToList()
            : [];
    }

    private static SteamGridGame? ParseGame(JsonElement data) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty("id", out var id) && id.TryGetInt32(out var number) && number > 0 &&
        data.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString())
            ? new(number, name.GetString()!.Trim()) : null;

    public Task<IReadOnlyList<SteamGridGrid>> GetGridsAsync(int gameId, CancellationToken cancellationToken) =>
        GetGridsAsync(gameId, VerticalDimensions, cancellationToken);

    public async Task<IReadOnlyList<SteamGridGrid>> GetGridsAsync(int gameId, string dimensions, CancellationToken cancellationToken)
    {
        if (!IsConfigured || gameId <= 0) return [];
        var data = await GetDataAsync($"grids/game/{gameId}?dimensions={Uri.EscapeDataString(dimensions)}&types=static&nsfw=false&humor=false&limit=24",
            cancellationToken).ConfigureAwait(false);
        if (data.ValueKind != JsonValueKind.Array) return [];
        var result = new List<SteamGridGrid>();
        foreach (var grid in data.EnumerateArray())
        {
            var url = HttpsUrl(grid, "url");
            var width = Number(grid, "width");
            var height = Number(grid, "height");
            if (url is null || width <= 0 || height <= width || IsTrue(grid, "nsfw") || IsTrue(grid, "humor")) continue;
            result.Add(new(Number(grid, "id"), url, width, height, HttpsUrl(grid, "thumb")));
        }
        return result.OrderBy(grid => Math.Abs(grid.Width / (double)grid.Height - 2d / 3d))
            .ThenByDescending(grid => grid.Width).Take(24).ToList();
    }

    private static int Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static bool IsTrue(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? HttpsUrl(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo)
            ? uri.AbsoluteUri : null;

    private async Task<JsonElement> GetDataAsync(string relativeUrl, CancellationToken cancellationToken, bool useCache = true,
        string? verificationApiKey = null)
    {
        await _apiGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedRevision != _credentialRevision)
            {
                _cache.Clear();
                _cacheBytes = 0;
                _cachedRevision = _credentialRevision;
            }
            if (useCache && _cache.TryGetValue(relativeUrl, out var cached) && cached.Expires > DateTimeOffset.UtcNow) return cached.Data;
            if (IsRateLimited) throw CreateStatusException(HttpStatusCode.TooManyRequests);
            var delay = _nextRequest - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.steamgriddb.com/api/v2/" + relativeUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", verificationApiKey ?? _apiKey);
            try
            {
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                _nextRequest = DateTimeOffset.UtcNow.AddMilliseconds(250);
                ObserveRateLimit(response);
                if (response.StatusCode == HttpStatusCode.NotFound) return default;
                if (!response.IsSuccessStatusCode) throw CreateStatusException(response.StatusCode);
                var bytes = await ReadLimitedAsync(response, 2 * 1024 * 1024, timeout.Token).ConfigureAwait(false);
                using var document = JsonDocument.Parse(bytes);
                if (!document.RootElement.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True ||
                    !document.RootElement.TryGetProperty("data", out var data))
                    throw new SteamGridDbException("SteamGridDB devolvió una respuesta inesperada.");
                var copy = data.Clone();
                if (useCache)
                {
                    var weight = copy.GetRawText().Length * 2;
                    if (_cache.Remove(relativeUrl, out var previous)) _cacheBytes -= previous.Weight;
                    while (_cache.Count > 0 && (_cache.Count >= 64 || _cacheBytes + weight > 2 * 1024 * 1024))
                    {
                        var first = _cache.Keys.First();
                        _cacheBytes -= _cache[first].Weight;
                        _cache.Remove(first);
                    }
                    if (weight <= 2 * 1024 * 1024)
                    {
                        _cache[relativeUrl] = (DateTimeOffset.UtcNow.AddMinutes(5), copy, weight);
                        _cacheBytes += weight;
                    }
                }
                return copy;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new SteamGridDbException("SteamGridDB no respondió a tiempo."); }
            catch (HttpRequestException) { throw new SteamGridDbException("No se pudo conectar con SteamGridDB."); }
            catch (JsonException) { throw new SteamGridDbException("SteamGridDB devolvió una respuesta inesperada."); }
        }
        finally { _apiGate.Release(); }
    }

    public async Task<byte[]> DownloadImageAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            throw new SteamGridDbException("La dirección de la portada no es válida.");
        await _imageGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                if (IsRateLimited) throw CreateStatusException(HttpStatusCode.TooManyRequests);
                // Never send the API key to image/CDN hosts.
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                ObserveRateLimit(response);
                if (!response.IsSuccessStatusCode) throw CreateStatusException(response.StatusCode);
                return await ReadLimitedAsync(response, MaxDownloadBytes, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new SteamGridDbException("SteamGridDB no respondió a tiempo."); }
            catch (HttpRequestException) { throw new SteamGridDbException("No se pudo descargar la portada."); }
        }
        finally { _imageGate.Release(); }
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, int limit, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > limit) throw new SteamGridDbException("La respuesta descargada es demasiado grande.");
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read == 0) break;
            if (memory.Length + read > limit) throw new SteamGridDbException("La respuesta descargada es demasiado grande.");
            memory.Write(buffer, 0, read);
        }
        return memory.ToArray();
    }

    private static SteamGridDbException CreateStatusException(HttpStatusCode code) => new(code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "API Key inválida o sin autorización.",
        HttpStatusCode.TooManyRequests => "Límite de solicitudes alcanzado. Intentá más tarde.",
        HttpStatusCode.BadRequest => "SteamGridDB rechazó la búsqueda de portadas.",
        _ => "Servicio temporalmente no disponible."
    }, code);

    private bool IsRateLimited => Interlocked.Read(ref _limitedUntilUtcTicks) > DateTimeOffset.UtcNow.UtcTicks;

    private void ObserveRateLimit(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.TooManyRequests) return;
        var until = response.Headers.RetryAfter?.Date ?? DateTimeOffset.UtcNow.Add(
            response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1));
        long previous;
        do
        {
            previous = Interlocked.Read(ref _limitedUntilUtcTicks);
            if (previous >= until.UtcTicks) return;
        } while (Interlocked.CompareExchange(ref _limitedUntilUtcTicks, until.UtcTicks, previous) != previous);
    }

    public sealed record VerificationResult(bool Success, string Message);

    public Task<VerificationResult> VerifyAsync(CancellationToken cancellationToken) => VerifyAsync(_apiKey, cancellationToken);

    public async Task<VerificationResult> VerifyAsync(string? apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return new(false, "Configurá una clave de API.");
        try
        {
            var data = await GetDataAsync("search/autocomplete/a", cancellationToken, useCache: false,
                verificationApiKey: apiKey.Trim()).ConfigureAwait(false);
            if (data.ValueKind != JsonValueKind.Array) return new(false, "Servicio temporalmente no disponible.");
            return new(true, "Conexión correcta.");
        }
        catch (SteamGridDbException exception) { return new(false, exception.Message); }
    }

    public sealed record SteamGridGrid(int Id, string Url, int Width, int Height, string? ThumbnailUrl = null);

    public void Dispose()
    {
        _httpClient.Dispose();
        _apiGate.Dispose();
        _imageGate.Dispose();
    }
}

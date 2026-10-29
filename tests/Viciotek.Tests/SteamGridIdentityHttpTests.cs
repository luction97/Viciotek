using System.Net;
using System.Net.Http;
using Viciotek.Application;
using Viciotek.Domain;
using Viciotek.Infrastructure.Covers;

namespace Viciotek.Tests;

public sealed class SteamGridIdentityHttpTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("saved-fixture-key")]
    public async Task Verify_UsesNewlyEnteredKeyWithoutChangingSavedCredential(string? savedKey)
    {
        var credentials = new List<string?>();
        using var handler = new Handler(request =>
        {
            credentials.Add(request.Headers.Authorization?.Parameter);
            return Json("""{"success":true,"data":[]}""");
        });
        using var client = new SteamGridDbClient(savedKey, handler);
        Assert.True((await client.VerifyAsync("  entered-fixture-key  ", default)).Success);
        Assert.Equal(savedKey is not null, client.IsConfigured);
        await client.SearchGamesAsync("Hades", default);
        Assert.Equal("entered-fixture-key", credentials[0]);
        if (savedKey is null) Assert.Single(credentials);
        else Assert.Equal(savedKey, credentials[1]);
    }

    [Fact]
    public async Task DefaultGridsQuery_UsesOnlyDocumentedVerticalDimensions()
    {
        string? query = null;
        using var handler = new Handler(request =>
        {
            query = Uri.UnescapeDataString(request.RequestUri!.Query);
            return Json("""{"success":true,"data":[]}""");
        });
        using var client = new SteamGridDbClient("fixture-key", handler);
        await client.GetGridsAsync(123, default);
        Assert.Contains("dimensions=600x900,342x482,660x930", query);
        Assert.DoesNotContain("300x450", query);
    }

    [Fact]
    public async Task SteamLookup_UsesDocumentedEndpointAndBearerAuthentication()
    {
        using var handler = new Handler(request =>
        {
            Assert.Equal("https://www.steamgriddb.com/api/v2/games/steam/1145360", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("fixture-key", request.Headers.Authorization?.Parameter);
            return Json("""{"success":true,"data":{"id":123,"name":"Hades"}}""");
        });
        using var client = new SteamGridDbClient("fixture-key", handler);
        Assert.Equal("Hades", (await client.GetSteamGameAsync(1145360, default))?.Name);
    }

    [Fact]
    public async Task DuplicateConcurrentSearches_UseBoundedResponseCache()
    {
        using var handler = new Handler(_ => Json("""{"success":true,"data":[{"id":123,"name":"Hades"}]}"""));
        using var client = new SteamGridDbClient("fixture-key", handler);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.SearchGamesAsync("Hades", default)));
        Assert.Equal(1, handler.Count);
        Assert.All(results, result => Assert.Single(result));
        client.UpdateApiKey("changed-fixture-key");
        await client.SearchGamesAsync("Hades", default);
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task MissingSteamAssociation_FallsBackToText_WithoutMisusingSteamIdAsProviderId()
    {
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath.Contains("games/steam/")
            ? new(HttpStatusCode.NotFound)
            : Json("""{"success":true,"data":[{"id":123,"name":"Hades"}]}"""));
        using var client = new SteamGridDbClient("fixture-key", handler);
        var match = await new GameIdentitySearch(client).FindAsync(new Game { Name = "Hades", SteamAppId = 1145360 }, default);
        Assert.Equal(123, match.Confirmed?.Id);
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task RateLimit_StopsVariantsAndHonorsCooldown_WithoutRetries()
    {
        using var handler = new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromMinutes(1));
            return response;
        });
        using var client = new SteamGridDbClient("fixture-key", handler);
        var search = new GameIdentitySearch(client);
        var game = new Game { Name = "First Title", IdentityCandidates = ["Second Title", "Third Title"] };
        var exception = await Assert.ThrowsAsync<SteamGridDbException>(() => search.FindAsync(game, default));
        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        await Assert.ThrowsAsync<SteamGridDbException>(() => search.FindAsync(game, default));
        Assert.Equal(1, handler.Count);
        Assert.DoesNotContain("fixture-key", exception.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task HttpFailure_DoesNotBecomeEmptyResults(HttpStatusCode status)
    {
        using var handler = new Handler(_ => new(status));
        using var client = new SteamGridDbClient("fixture-key", handler);
        var exception = await Assert.ThrowsAsync<SteamGridDbException>(() => client.SearchGamesAsync("Hades", default));
        Assert.Equal(status, exception.StatusCode);
    }

    [Fact]
    public async Task Images_DoNotReceiveApiCredentials()
    {
        using var handler = new Handler(request =>
        {
            Assert.Null(request.Headers.Authorization);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
        });
        using var client = new SteamGridDbClient("fixture-key", handler);
        Assert.Equal(new byte[] { 1, 2, 3 }, await client.DownloadImageAsync("https://cdn.example.com/grid.png", default));
    }

    [Fact]
    public async Task CanceledLookup_DoesNotBecomeNetworkFailure()
    {
        using var handler = new Handler(_ => Json("""{"success":true,"data":[]}"""));
        using var client = new SteamGridDbClient("fixture-key", handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SearchGamesAsync("Hades", cancellation.Token));
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task Grids_KeepVerticalSafeImagesAndProviderThumbnail()
    {
        using var handler = new Handler(_ => Json("""
            {"success":true,"data":[
            {"id":1,"url":"https://cdn.example.com/full.png","thumb":"https://cdn.example.com/thumb.png","width":600,"height":900},
            {"id":2,"url":"https://cdn.example.com/wide.png","width":920,"height":430},
            {"id":3,"url":"https://cdn.example.com/nsfw.png","width":600,"height":900,"nsfw":true}]}
            """));
        using var client = new SteamGridDbClient("fixture-key", handler);
        var grid = Assert.Single(await client.GetGridsAsync(123, "600x900", default));
        Assert.Equal("https://cdn.example.com/thumb.png", grid.ThumbnailUrl);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Count++; return Task.FromResult(respond(request)); }
    }
}

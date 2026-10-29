using System.Net;
using System.Net.Http;
using Viciotek.Infrastructure.Covers;

namespace Viciotek.Tests;

public sealed class SteamGridDbClientTests
{
    private sealed class StatusHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        public StatusHandler(HttpStatusCode status) => _status = status;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent("""{"success":true,"data":[]}""") });
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public async Task VerifyAsync_ReportsAuthenticationAndServiceStatus(HttpStatusCode status, bool expectedSuccess)
    {
        using var client = new SteamGridDbClient("test-key", new StatusHandler(status));
        var result = await client.VerifyAsync(CancellationToken.None);
        Assert.Equal(expectedSuccess, result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task VerifyAsync_WithoutKey_DoesNotCallNetwork()
    {
        using var handler = new CountingHandler();
        using var client = new SteamGridDbClient(null, handler);
        var result = await client.VerifyAsync(CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task SearchGamesAsync_ParsesGameMatches()
    {
        using var handler = new JsonHandler("""{"success":true,"data":[{"id":123,"name":"Hades"},{"id":456,"name":"Hades II"}]}""");
        using var client = new SteamGridDbClient("test-key", handler);
        var games = await client.SearchGamesAsync("hades", CancellationToken.None);
        Assert.Equal(2, games.Count);
        Assert.Equal(123, games[0].Id);
        Assert.Equal("Hades", games[0].Name);
    }

    [Fact]
    public async Task GetGridsAsync_ParsesGridUrls()
    {
        using var handler = new JsonHandler("""{"success":true,"data":[{"id":1,"url":"https://cdn.example.com/a.jpg","width":600,"height":900},{"id":2,"url":"https://cdn.example.com/b.jpg","width":342,"height":482}]}""");
        using var client = new SteamGridDbClient("test-key", handler);
        var grids = await client.GetGridsAsync(123, "600x900,342x482", CancellationToken.None);
        Assert.Equal(2, grids.Count);
        Assert.Equal("https://cdn.example.com/a.jpg", grids[0].Url);
        Assert.Equal(600, grids[0].Width);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class JsonHandler : HttpMessageHandler
    {
        private readonly string _json;
        public JsonHandler(string json) => _json = json;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}

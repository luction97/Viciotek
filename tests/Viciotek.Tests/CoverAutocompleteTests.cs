using System.Net;
using System.Net.Http;
using Viciotek.Domain;
using Viciotek.Infrastructure.Covers;
using Viciotek.Presentation;

namespace Viciotek.Tests;

public sealed class CoverAutocompleteTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Interlocked.Increment(ref Calls); return send(request, token); }
    }
    private static HttpResponseMessage Result(int id, string name) => new(HttpStatusCode.OK)
        { Content = new StringContent($$"""{"success":true,"data":[{"id":{{id}},"name":"{{name}}"}]}""") };

    [Fact]
    public Task ChoosingSuggestion_CompletesNameHidesListAndDoesNotSearchAgain() => WpfTest.RunAsync(async () =>
    {
        using var handler = new Handler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("/search/")
            ? """{"success":true,"data":[{"id":1,"name":"First edition"},{"id":2,"name":"Second edition"}]}"""
            : """{"success":true,"data":[]}""") }));
        using var client = new SteamGridDbClient("fixture", handler);
        var vm = new CoverGalleryViewModel(client, new Game(), (_, _) => Task.FromResult(true), true);
        try
        {
            vm.SearchQuery = "edition";
            await vm.SearchAsync();
            Assert.True(vm.ShowSuggestions);
            await vm.SelectGameAsync(vm.Matches[1]);
            Assert.Equal("Second edition", vm.SearchQuery);
            Assert.False(vm.ShowSuggestions);
            await Task.Delay(450);
            Assert.Equal(2, handler.Calls);
            vm.SearchQuery = "another";
            await vm.SearchAsync();
            Assert.True(vm.ShowSuggestions);
        }
        finally { await vm.CloseAsync(); }
    });

    [Fact]
    public Task DismissedSuggestions_DoNotReappearWhenPendingResponseArrives() => WpfTest.RunAsync(async () =>
    {
        var reply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler((_, _) => reply.Task);
        using var client = new SteamGridDbClient("fixture", handler);
        var vm = new CoverGalleryViewModel(client, new Game(), (_, _) => Task.FromResult(true), true);
        try
        {
            vm.SearchQuery = "edition";
            var search = vm.SearchAsync();
            vm.HideSuggestions();
            reply.SetResult(Result(1, "Another edition"));
            await search;
            Assert.Single(vm.Matches);
            Assert.False(vm.ShowSuggestions);
        }
        finally { reply.TrySetResult(Result(1, "Another edition")); await vm.CloseAsync(); }
    });

    [Fact]
    public Task Typing_UsesThresholdAndCoalescesBurst() => WpfTest.RunAsync(async () =>
    {
        using var handler = new Handler((_, _) => Task.FromResult(Result(2, "God of War")));
        using var client = new SteamGridDbClient("fixture", handler);
        var vm = new CoverGalleryViewModel(client, new Game(), (_, _) => Task.FromResult(true), true);
        try
        {
            vm.SearchQuery = "go";
            await Task.Delay(450);
            Assert.Equal(0, handler.Calls);
            vm.SearchQuery = "god";
            vm.SearchQuery = "god o";
            vm.SearchQuery = "god of";
            await WpfTest.WaitUntilAsync(() => vm.Matches.Count == 1 && !vm.IsLoading);
            Assert.Equal(1, handler.Calls);
            Assert.Equal(2, vm.Matches[0].Id);
        }
        finally { await vm.CloseAsync(); }
    });

    [Fact]
    public Task LateResponse_CannotPublishStaleMatches() => WpfTest.RunAsync(async () =>
    {
        var oldReply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newReply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/old")) return oldReply.Task;
            newStarted.TrySetResult();
            return newReply.Task;
        });
        using var client = new SteamGridDbClient("fixture", handler);
        var vm = new CoverGalleryViewModel(client, new Game(), (_, _) => Task.FromResult(true), true);
        try
        {
            vm.SearchQuery = "old";
            var oldSearch = vm.SearchAsync();
            vm.SearchQuery = "new";
            var newSearch = vm.SearchAsync();
            oldReply.SetResult(Result(1, "Old edition"));
            await oldSearch;
            await newStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Empty(vm.Matches);
            Assert.Null(vm.SelectedGame);
            newReply.SetResult(Result(2, "New edition"));
            await newSearch;
            Assert.Equal(2, Assert.Single(vm.Matches).Id);
        }
        finally
        {
            oldReply.TrySetResult(Result(1, "Old edition"));
            newReply.TrySetResult(Result(2, "New edition"));
            await vm.CloseAsync();
        }
    });

    [Fact]
    public Task ChangingSuggestion_DiscardsPreviousGridResponse() => WpfTest.RunAsync(async () =>
    {
        var oldReply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/search/"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"success":true,"data":[{"id":1,"name":"First"},{"id":2,"name":"Second"}]}""") });
            if (request.RequestUri.AbsolutePath.EndsWith("/1")) return oldReply.Task;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"success":true,"data":[]}""") });
        });
        using var client = new SteamGridDbClient("fixture", handler);
        var vm = new CoverGalleryViewModel(client, new Game(), (_, _) => Task.FromResult(true), true);
        try
        {
            vm.SearchQuery = "query";
            await vm.SearchAsync();
            var first = vm.SelectGameAsync(vm.Matches[0]);
            var second = vm.SelectGameAsync(vm.Matches[1]);
            oldReply.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"success":true,"data":[]}""") });
            await Task.WhenAll(first, second);
            Assert.Equal(2, vm.SelectedGame?.Id);
            Assert.Empty(vm.Candidates);
        }
        finally
        {
            oldReply.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"success":true,"data":[]}""") });
            await vm.CloseAsync();
        }
    });

    [Fact]
    public Task Close_CancelsPendingAutomaticSearch() => WpfTest.RunAsync(async () =>
    {
        using var handler = new Handler((_, _) => Task.FromResult(Result(1, "Example")));
        using var client = new SteamGridDbClient("fixture", handler);
        var vm = new CoverGalleryViewModel(client, new Game(), (_, _) => Task.FromResult(true), true);
        vm.SearchQuery = "example";
        await vm.CloseAsync();
        await Task.Delay(450);
        Assert.Equal(0, handler.Calls);
    });
}

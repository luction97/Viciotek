using System.Net;
using System.Net.Http;
using Viciotek.Domain;
using Viciotek.Infrastructure.Covers;
using Viciotek.Presentation;

namespace Viciotek.Tests;

public sealed class CoverAutocompleteTests
{
    [Theory]
    [InlineData("God of War")]
    [InlineData("god of")]
    public Task Homonyms_ShowModernAndOriginalCovers_AndSaveTheChosenEdition(string query) => WpfTest.RunAsync(async () =>
    {
        CoverSelection? applied = null;
        using var handler = new Handler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.Host == "cdn.example.com" ? new ByteArrayContent(TestHelpers.CreatePng(160, 240)) : new StringContent(
                request.RequestUri.AbsolutePath.Contains("/search/")
                ? """{"success":true,"data":[{"id":2005,"name":"God of War","release_date":1111449600},{"id":2018,"name":"God of War","release_date":1524182400}]}"""
                : """{"success":true,"data":[{"id":1,"url":"https://cdn.example.com/full.png","width":600,"height":900}]}""")
        }));
        using var client = new SteamGridDbClient("fixture", handler);
        var vm = new CoverGalleryViewModel(client, new Game(), (selection, _) => { applied = selection; return Task.FromResult(true); }, true);
        try
        {
            vm.SearchQuery = query;
            await vm.SearchAsync();
            await vm.LoadMoreAsync();
            Assert.Equal(new int?[] { 2018, 2005 }, vm.Candidates.Select(candidate => candidate.Game!.ReleaseYear));
            Assert.Contains(vm.Matches, game => game.DisplayName == "God of War (2018)");
            Assert.Contains(vm.Matches, game => game.DisplayName == "God of War (2005)");
            Assert.Null(applied);
            vm.Selected = vm.Candidates[0];
            Assert.True(vm.ConfirmCommand.CanExecute(null));
            await vm.ConfirmAsync();
            Assert.Equal(2018, applied!.Game.Id);
            vm.Selected = vm.Candidates[1];
            await vm.ConfirmAsync();
            Assert.Equal(2005, applied!.Game.Id);
            await vm.SelectGameAsync(vm.Matches.Single(game => game.Id == 2018));
            Assert.Single(vm.Candidates);
            Assert.Equal(2018, vm.Candidates[0].Game!.Id);
            Assert.False(vm.ShowSuggestions);
        }
        finally { await vm.CloseAsync(); }
    });
    [Theory]
    [InlineData("spid")]
    [InlineData("MK")]
    public Task TypingPartialName_LoadsCoversWithoutSelectingASuggestion_AndDoesNotApply(string query) => WpfTest.RunAsync(async () =>
    {
        var applied = false;
        using var handler = new Handler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.AbsolutePath.EndsWith("cover.png") ? new ByteArrayContent(TestHelpers.CreatePng(160, 240)) : new StringContent(request.RequestUri.AbsolutePath.Contains("/search/")
                ? """{"success":true,"data":[{"id":5,"name":"First result"},{"id":6,"name":"Another result"}]}"""
                : """{"success":true,"data":[{"id":1,"url":"https://example.invalid/cover.png","width":600,"height":900}]}""")
        }));
        using var client = new SteamGridDbClient("fixture", handler);
        var vm = new CoverGalleryViewModel(client, new Game(), (selection, _) =>
        {
            Assert.Equal(5, selection.Game.Id);
            Assert.Equal(query, selection.SearchName);
            applied = true;
            return Task.FromResult(true);
        }, true);
        try
        {
            vm.SearchQuery = query;
            await WpfTest.WaitUntilAsync(() => vm.Candidates.Count == 1 && !vm.IsLoading);
            Assert.Equal(5, vm.SelectedGame?.Id);
            Assert.Equal(query, vm.SearchQuery);
            Assert.Contains(query, vm.Status);
            Assert.True(vm.ShowSuggestions);
            Assert.Null(vm.Selected);
            Assert.False(vm.ConfirmCommand.CanExecute(null));
            Assert.False(applied);
            vm.Selected = Assert.Single(vm.Candidates);
            await vm.ConfirmAsync();
            Assert.True(applied);
            await vm.SelectGameAsync(vm.Matches[1]);
            Assert.Equal(6, vm.SelectedGame?.Id);
            Assert.Equal("Another result", vm.SearchQuery);
            Assert.False(vm.ShowSuggestions);
            vm.SearchQuery = " ";
            Assert.Empty(vm.Candidates);
            Assert.Null(vm.SelectedGame);
        }
        finally { await vm.CloseAsync(); }
    });
    [Theory]
    [InlineData("M")]
    [InlineData("MK")]
    [InlineData("   ")]
    public Task AutomaticSearch_AcceptsShortNamesAndSkipsBlankInput(string query) => WpfTest.RunAsync(async () =>
    {
        string? requestedPath = null;
        using var handler = new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/search/")) requestedPath = request.RequestUri.AbsolutePath;
            return Task.FromResult(Result(1, "Mortal Kombat"));
        });
        using var client = new SteamGridDbClient("fixture", handler);
        var vm = new CoverGalleryViewModel(client, new Game(), (_, _) => Task.FromResult(true), true);
        try
        {
            vm.SearchQuery = query;
            if (string.IsNullOrWhiteSpace(query))
            {
                await Task.Delay(450);
                Assert.Equal(0, handler.Calls);
                Assert.Null(vm.Status);
            }
            else
            {
                await WpfTest.WaitUntilAsync(() => vm.Matches.Count == 1 && !vm.IsLoading);
                Assert.True(vm.ShowSuggestions);
                Assert.Equal(2, handler.Calls);
                Assert.EndsWith("/" + query, requestedPath);
                vm.SearchQuery = "";
                Assert.Null(vm.Status);
                Assert.Empty(vm.Matches);
            }
        }
        finally { await vm.CloseAsync(); }
    });

    [Fact]
    public Task TypingBelowThresholdAfterDebounceThenTypingAgain_DoesNotUseDisposedSource() => WpfTest.RunAsync(async () =>
    {
        using var handler = new Handler((_, _) => Task.FromResult(Result(1, "Spider-Man")));
        using var client = new SteamGridDbClient("fixture", handler);
        var vm = new CoverGalleryViewModel(client, new Game(), (_, _) => Task.FromResult(true), true);
        try
        {
            vm.SearchQuery = "spi";
            vm.SearchQuery = "sp";
            vm.SearchQuery = "spiderman";
            await WpfTest.WaitUntilAsync(() => vm.Matches.Count == 1 && !vm.IsLoading);
            Assert.True(vm.ShowSuggestions);
            vm.SearchQuery = "sp";
            vm.SearchQuery = "s";
            vm.SearchQuery = "";
            vm.SearchQuery = "spiderman";
            await WpfTest.WaitUntilAsync(() => vm.Matches.Count == 1 && !vm.IsLoading);
        }
        finally { await vm.CloseAsync(); }
    });

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Interlocked.Increment(ref Calls); return send(request, token); }
    }
    private static HttpResponseMessage Result(int id, string name) => new(HttpStatusCode.OK)
        { Content = new StringContent($$"""{"success":true,"data":[{"id":{{id}},"name":"{{name}}"}]}""") };

    [Fact]
    public Task ExactMatch_KeepsSuggestionsUntilExplicitSelection() => WpfTest.RunAsync(async () =>
    {
        using var handler = new Handler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.Contains("/search/") ? Result(1, "Spider-Man") :
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"success":true,"data":[]}""") }));
        using var client = new SteamGridDbClient("fixture", handler);
        var vm = new CoverGalleryViewModel(client, new Game(), (_, _) => Task.FromResult(true), true);
        try
        {
            vm.SearchQuery = "Spider-Man";
            await vm.SearchAsync();
            Assert.True(vm.ShowSuggestions);
            await vm.SelectGameAsync(Assert.Single(vm.Matches));
            Assert.False(vm.ShowSuggestions);
            var requests = handler.Calls;
            await Task.Delay(450);
            Assert.Equal(requests, handler.Calls);
            Assert.False(vm.ShowSuggestions);
            vm.SearchQuery = "Spider-Ma";
            await vm.SearchAsync();
            Assert.True(vm.ShowSuggestions);
        }
        finally { await vm.CloseAsync(); }
    });

    [Fact]
    public Task CloseAfterDeletingBelowThreshold_DoesNotCancelDisposedSource() => WpfTest.RunAsync(async () =>
    {
        using var handler = new Handler((_, _) => Task.FromResult(Result(1, "Spider-Man")));
        using var client = new SteamGridDbClient("fixture", handler);
        var vm = new CoverGalleryViewModel(client, new Game(), (_, _) => Task.FromResult(true), true);
        vm.SearchQuery = "spi";
        vm.SearchQuery = "sp";
        vm.SearchQuery = "";
        await vm.CloseAsync();
        await vm.CloseAsync();
        await Task.Delay(450);
        Assert.Equal(0, handler.Calls);
    });

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
            Assert.Equal(3, handler.Calls);
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
    public Task Typing_SkipsWhitespaceAndCoalescesBurst() => WpfTest.RunAsync(async () =>
    {
        using var handler = new Handler((_, _) => Task.FromResult(Result(2, "God of War")));
        using var client = new SteamGridDbClient("fixture", handler);
        var vm = new CoverGalleryViewModel(client, new Game(), (_, _) => Task.FromResult(true), true);
        try
        {
            vm.SearchQuery = "   ";
            await Task.Delay(450);
            Assert.Equal(0, handler.Calls);
            vm.SearchQuery = "god";
            vm.SearchQuery = "god o";
            vm.SearchQuery = "god of";
            await WpfTest.WaitUntilAsync(() => vm.Matches.Count == 1 && !vm.IsLoading);
            Assert.Equal(2, handler.Calls);
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
            var first = vm.SearchAsync();
            await WpfTest.WaitUntilAsync(() => vm.Matches.Count == 2);
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

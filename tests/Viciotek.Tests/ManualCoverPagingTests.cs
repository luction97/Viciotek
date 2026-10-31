using System.Net;
using System.Net.Http;
using System.Text.Json;
using Viciotek.Domain;
using Viciotek.Infrastructure.Covers;
using Viciotek.Presentation;
namespace Viciotek.Tests;
public sealed class ManualCoverPagingTests
{
    [Fact]
    public async Task Search_ReturnsEveryProviderMatchBeyondOneHundred()
    {
        using var handler = new Handler(_ => Json(new { success=true, data=Enumerable.Range(1,145).Select(id=>new{id,name="Game "+id}) }));
        using var client=new SteamGridDbClient("fixture",handler);
        Assert.Equal(145,(await client.SearchGamesAsync("game",default)).Count);
    }

    [Fact]
    public Task ManualSearch_VisitsEveryMatchingGameAndPage_WithoutEditionOrImageCaps() => WpfTest.RunAsync(async()=>
    {
        using var handler=new Handler(request=>
        {
            if(request.RequestUri!.AbsolutePath.Contains("/search/"))return Json(new{success=true,data=Enumerable.Range(1,6).Select(id=>new{id,name="Matching Game "+id})});
            Assert.DoesNotContain("dimensions=",request.RequestUri.Query);
            var id=int.Parse(request.RequestUri.AbsolutePath.Split('/').Last());
            var page=Page(request);
            return Json(new{success=true,total=25,limit=24,page,data=Enumerable.Range(page*24,page==0?24:1).Select(i=>new{id=i+1,url=$"https://cdn.example.com/{id}/{i}.png",width=400,height=500})});
        });
        using var client=new SteamGridDbClient("fixture",handler);
        var vm=new CoverGalleryViewModel(client,new Game(),(_,_)=>Task.FromResult(true),true);
        try
        {
            vm.SearchQuery="Matching";await vm.SearchAsync();
            Assert.Equal(6,vm.Matches.Count);
            while(vm.HasMoreResults)await vm.LoadMoreAsync();
            Assert.Equal(150,vm.Candidates.Count);
            Assert.Equal(6,vm.Candidates.Select(c=>c.Game!.Id).Distinct().Count());
            Assert.All(vm.Candidates.GroupBy(c=>c.Game!.Id),group=>Assert.Equal(25,group.Count()));
            Assert.Equal(13,handler.Requests.Count);
            await vm.LoadMoreAsync();Assert.Equal(13,handler.Requests.Count);
        }
        finally {await vm.CloseAsync();}
    });

    [Fact]
    public async Task PageContinuation_UsesRawCountAndMetadataEvenWhenSomeImagesAreFiltered()
    {
        using var handler=new Handler(_=>Json(new{success=true,total=48,limit=24,page=0,data=new[]{new{id=1,url="https://cdn.example.com/landscape.png",width=900,height=600}}}));
        using var client=new SteamGridDbClient("fixture",handler);
        var page=await client.GetGridPageAsync(1,0,default);
        Assert.Empty(page.Grids);Assert.True(page.HasMore);
    }

    [Fact]
    public Task EmptyCompatiblePage_DoesNotClaimThereAreNoCoversWhenMorePagesExist() => WpfTest.RunAsync(async()=>
    {
        using var handler=new Handler(request=>request.RequestUri!.AbsolutePath.Contains("/search/")
            ? Json(new{success=true,data=new[]{new{id=1,name="Game"}}})
            : Json(new{success=true,total=25,limit=24,data=new[]{new{id=1,url="https://cdn.example.com/image.png",width=Page(request)==0?900:600,height=Page(request)==0?600:900}}}));
        using var client=new SteamGridDbClient("fixture",handler);
        var vm=new CoverGalleryViewModel(client,new Game(),(_,_)=>Task.FromResult(true),true);
        try
        {
            vm.SearchQuery="game";await vm.SearchAsync();
            Assert.True(vm.HasMoreResults);Assert.Empty(vm.Candidates);Assert.Null(vm.Status);
            await vm.LoadMoreAsync();Assert.Single(vm.Candidates);Assert.False(vm.HasMoreResults);
        }
        finally{await vm.CloseAsync();}
    });

    [Fact]
    public Task Paging_DiscardsLatePageWhenQueryChanges() => WpfTest.RunAsync(async()=>
    {
        var reply=new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler=new Handler(request=>request.RequestUri!.AbsolutePath.Contains("/search/")
            ? Json(new{success=true,data=new[]{new{id=request.RequestUri.AbsolutePath.EndsWith("new")?2:1,name="Game"}}})
            : Json(new{success=true,total=25,limit=24,data=new[]{new{id=1,url=$"https://cdn.example.com/{request.RequestUri.AbsolutePath.Split('/').Last()}.png",width=600,height=900}}}));
        handler.Intercept=request=>{if(!request.RequestUri!.AbsolutePath.Contains("/search/")&&Page(request)==1){started.TrySetResult();return reply.Task;}return null;};
        using var client=new SteamGridDbClient("fixture",handler);
        var vm=new CoverGalleryViewModel(client,new Game(),(_,_)=>Task.FromResult(true),true);
        try
        {
            vm.SearchQuery="old";await vm.SearchAsync();var pending=vm.LoadMoreAsync();await started.Task;
            vm.SearchQuery="new";var search=vm.SearchAsync();
            reply.TrySetResult(Json(new{success=true,total=25,limit=24,data=new[]{new{id=2,url="https://cdn.example.com/stale.png",width=600,height=900}}}));
            await Task.WhenAll(pending,search);
            Assert.All(vm.Candidates,c=>Assert.Equal(2,c.Game!.Id));
            Assert.DoesNotContain(vm.Candidates,c=>c.Url.EndsWith("stale.png"));
        }
        finally{reply.TrySetResult(Json(new{success=true,data=Array.Empty<object>()}));await vm.CloseAsync();}
    });

    [Fact]
    public Task ThumbnailMemory_IsBoundedWhileAllResultsRemainAccessible() => WpfTest.RunAsync(async()=>
    {
        var bytes=TestHelpers.CreatePng(160,240);
        using var handler=new Handler(request=>request.RequestUri!.Host=="cdn.example.com"
            ? new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)}
            : request.RequestUri.AbsolutePath.Contains("/search/")?Json(new{success=true,data=new[]{new{id=1,name="Game"}}})
            : Json(new{success=true,total=72,limit=24,data=Enumerable.Range(Page(request)*24,24).Select(id=>new{id=id+1,url=$"https://cdn.example.com/{id}.png",thumb=$"https://cdn.example.com/{id}-thumb.png",width=600,height=900})}));
        using var client=new SteamGridDbClient("fixture",handler);
        var vm=new CoverGalleryViewModel(client,new Game(),(_,_)=>Task.FromResult(true),true);
        try
        {
            vm.SearchQuery="game";await vm.SearchAsync();
            while(vm.HasMoreResults)await vm.LoadMoreAsync();
            await WpfTest.WaitUntilAsync(()=>vm.Candidates.All(c=>!c.IsLoading));
            Assert.Equal(72,vm.Candidates.Count);
            Assert.InRange(vm.Candidates.Count(c=>c.Thumbnail!=null),1,48);
            var first=vm.Candidates[0];Assert.Null(first.Thumbnail);
            vm.SetPreviewVisible(first,true);await vm.EnsurePreviewAsync(first);
            Assert.NotNull(first.Thumbnail);Assert.InRange(vm.Candidates.Count(c=>c.Thumbnail!=null),1,48);
        }
        finally{await vm.CloseAsync();}
    });
    private static int Page(HttpRequestMessage request)=>int.Parse(request.RequestUri!.Query.Split('&').Single(p=>p.StartsWith("page=")).Split('=')[1]);
    private static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value))};
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
    {
        public List<Uri> Requests=[];
        public Func<HttpRequestMessage,Task<HttpResponseMessage>?>? Intercept;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Requests.Add(request.RequestUri!);return Intercept?.Invoke(request)??Task.FromResult(respond(request));}
    }
}

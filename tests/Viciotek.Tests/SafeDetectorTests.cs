using System.Text;
using System.Text.Json;
using Viciotek.Application;
using Viciotek.Domain;
using Viciotek.Infrastructure.Detection;
namespace Viciotek.Tests;
public sealed class SafeDetectorTests
{
    [Theory]
    [InlineData(39)][InlineData(40)][InlineData(41)]
    public void Steam_RequiresInstalledManifestAndTypedGameMetadata_AcrossLibraries(int version)
    {
        var root=TestHelpers.CreateTempDirectory();
        try
        {
            var first=Path.Combine(root,"diskA","steamapps");var second=Path.Combine(root,"diskB","steamapps");
            var entries=new[]{(10,"game"),(20,"application"),(30,"tool"),(40,"dlc"),(50,"game")};
            foreach(var (id,type) in entries){var folder=id==50?second:first;var dir=Path.Combine(folder,"common","Product"+id);Directory.CreateDirectory(dir);
                var name=id switch{10=>"Tool Quest",20=>"Mozilla Firefox",30=>"Visual Studio Code",40=>"Game soundtrack",_=>"Another Game"};
                File.WriteAllText(Path.Combine(folder,$"appmanifest_{id}.acf"),$"\"AppState\" {{ \"appid\" \"{id}\" \"name\" \"{name}\" \"installdir\" \"Product{id}\" \"StateFlags\" \"4\" }}");
                File.WriteAllText(Path.Combine(dir,"app.exe"),"fixture never executed");}
            var cache=Path.Combine(root,"appinfo.vdf");WriteAppInfo(cache,version,entries);
            var games=new SteamDetector([first,second],cache).Detect();
            Assert.Equal(2,games.Count);Assert.Contains(games,g=>g.Name=="Tool Quest");Assert.All(games,g=>Assert.True(g.HighConfidence));
            Assert.All(games,g=>Assert.True(LaunchPlanner.CreatePlan(new Game{Name=g.Name,LaunchPath=g.LaunchPath,LaunchKind=g.LaunchKind}).IsValid));
            Assert.Contains(games,g=>g.LaunchPath=="steam://rungameid/50");
            File.Delete(cache);Assert.Empty(new SteamDetector([first,second],cache).Detect());
        }
        finally{Directory.Delete(root,true);}
    }
    [Theory]
    [InlineData("applications",false)][InlineData("tools",false)][InlineData("games",true)]
    public void Epic_RequiresGameCategoryCompleteApplicationAndContainedExecutable(string category,bool accepted)
    {
        var root=TestHelpers.CreateTempDirectory();
        try
        {
            var manifests=Path.Combine(root,"manifests");Directory.CreateDirectory(manifests);var install=Path.Combine(root,"product");Directory.CreateDirectory(install);File.WriteAllText(Path.Combine(install,"app.exe"),"fixture");
            File.WriteAllText(Path.Combine(manifests,"product.item"),JsonSerializer.Serialize(new{DisplayName="Editor or game",InstallLocation=install,LaunchExecutable="app.exe",AppName="id",CatalogNamespace="ns",CatalogItemId="item",AppCategories=new[]{category},bIsIncompleteInstall=false,bIsApplication=true}));
            var games=new EpicDetector(manifests).Detect();Assert.Equal(accepted?1:0,games.Count);
            if(accepted){Assert.Equal(LaunchKind.Uri,games[0].LaunchKind);Assert.StartsWith("com.epicgames.launcher://",games[0].LaunchPath);}
        }
        finally{Directory.Delete(root,true);}
    }
    [Theory]
    [InlineData(true,true,"FileTask",true)][InlineData(false,true,"FileTask",false)][InlineData(true,false,"FileTask",false)][InlineData(true,true,"URLTask",false)]
    public void Gog_RequiresBaseGameMetadataAndPrimaryExecutableTask(bool primary,bool baseGame,string taskType,bool accepted)
    {
        var root=TestHelpers.CreateTempDirectory();
        try
        {
            var dir=Path.Combine(root,"Game");Directory.CreateDirectory(Path.Combine(dir,"bin"));var exe=Path.Combine(dir,"bin","game.exe");File.WriteAllText(exe,"fixture");
            File.WriteAllText(Path.Combine(dir,"goggame-123.info"),JsonSerializer.Serialize(new{name="Game",gameId="123",rootGameId=baseGame?"123":"999",playTasks=new[]{new{isPrimary=primary,type=taskType,path="bin/game.exe",workingDir=".",arguments="--windowed"}}}));
            var games=new GogDetector([root]).Detect();Assert.Equal(accepted?1:0,games.Count);
            if(accepted){var g=games[0];Assert.Equal(exe,g.LaunchPath);Assert.Equal("--windowed",g.Arguments);Assert.Equal(dir,g.WorkingDirectory);Assert.True(g.HighConfidence);}
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void UnreliableGenericDetectors_AreDisabledInsteadOfClassifyingWindowsPrograms()
    {Assert.Empty(new XboxDetector().Detect());Assert.Empty(new RegistryPublisherDetector("Windows",["Microsoft","Google","Mozilla","Adobe"]).Detect());}

    [Theory]
    [InlineData("incomplete")][InlineData("dlc")][InlineData("outside")][InlineData("no-category")]
    public void Epic_RejectsIncompleteDlcEscapingTargetsAndMissingClassification(string problem)
    {
        var root=TestHelpers.CreateTempDirectory();
        try
        {
            var install=Path.Combine(root,"Game");Directory.CreateDirectory(install);
            File.WriteAllText(Path.Combine(install,"game.exe"),"fixture");File.WriteAllText(Path.Combine(root,"outside.exe"),"fixture");
            var manifest=new Dictionary<string,object?>
            {
                ["DisplayName"]="Game",["InstallLocation"]=install,["LaunchExecutable"]=problem=="outside"?"../outside.exe":"game.exe",
                ["AppName"]="game",["CatalogNamespace"]="namespace",["CatalogItemId"]="item",["AppCategories"]=new[]{"games"},
                ["bIsIncompleteInstall"]=problem=="incomplete",["bIsApplication"]=true,["MainGameAppName"]=problem=="dlc"?"parent-game":"game"
            };
            if(problem=="no-category")manifest.Remove("AppCategories");
            File.WriteAllText(Path.Combine(root,"game.item"),JsonSerializer.Serialize(manifest));
            Assert.Empty(new EpicDetector(root).Detect());
        }
        finally{Directory.Delete(root,true);}
    }

    [Theory][InlineData(38)][InlineData(42)]
    public void Steam_UnknownCacheFormatsNeverClassifyProgramsAsGames(int version)
    {
        var root=TestHelpers.CreateTempDirectory();
        try{var file=Path.Combine(root,"appinfo.vdf");WriteAppInfo(file,version,[(1,"game")]);Assert.Empty(SteamAppTypes.ReadGames(file,new HashSet<int>{1}));}
        finally{Directory.Delete(root,true);}
    }

    internal static void WriteAppInfo(string file,int version,IEnumerable<(int Id,string Type)> entries)
    {
        using var stream=File.Create(file);using var writer=new BinaryWriter(stream,Encoding.UTF8,true);
        writer.Write(0x07564400u|(uint)version);writer.Write(1u);if(version==41)writer.Write(0L);
        foreach(var (id,type) in entries)
        {
            using var payload=new MemoryStream();using var kv=new BinaryWriter(payload,Encoding.UTF8,true);
            void Key(string name,int index){if(version==41)kv.Write(index);else{kv.Write(Encoding.UTF8.GetBytes(name));kv.Write((byte)0);}}
            kv.Write((byte)0);Key("appinfo",0);kv.Write((byte)0);Key("common",1);kv.Write((byte)1);Key("type",2);kv.Write(Encoding.UTF8.GetBytes(type));kv.Write((byte)0);kv.Write((byte)8);kv.Write((byte)8);kv.Write((byte)8);kv.Flush();
            writer.Write(id);writer.Write((uint)(payload.Length+(version>=40?60:40)));writer.Write(new byte[version>=40?60:40]);writer.Write(payload.ToArray());
        }
        writer.Write(0);
        if(version==41){var offset=stream.Position;writer.Write(3u);foreach(var key in new[]{"appinfo","common","type"}){writer.Write(Encoding.UTF8.GetBytes(key));writer.Write((byte)0);}writer.Flush();stream.Position=8;writer.Write(offset);}
    }
}

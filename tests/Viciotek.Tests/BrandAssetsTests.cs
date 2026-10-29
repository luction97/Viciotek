using System.Windows.Media.Imaging;

namespace Viciotek.Tests;

public sealed class BrandAssetsTests
{
    private static readonly int[] ExpectedIconSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    [Fact]
    public void OfficialIcon_ContainsShellFramesAndPngSource()
    {
        var bytes = File.ReadAllBytes(RepositoryFile("src/Viciotek/Assets/viciotek.ico"));

        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));
        var count = BitConverter.ToUInt16(bytes, 4);
        Assert.Equal(ExpectedIconSizes.Length, count);

        var sizes = new List<int>(count);
        for (var index = 0; index < count; index++)
        {
            var entry = 6 + (16 * index);
            var width = bytes[entry] == 0 ? 256 : bytes[entry];
            var height = bytes[entry + 1] == 0 ? 256 : bytes[entry + 1];
            var length = BitConverter.ToInt32(bytes, entry + 8);
            var start = BitConverter.ToInt32(bytes, entry + 12);
            sizes.Add(width);

            Assert.Equal(width, height);
            Assert.InRange(start, 0, bytes.Length - 1);
            Assert.InRange(start + length, start + 1, bytes.Length);
            var isPng = bytes[start] == 0x89 && bytes[start + 1] == 0x50 &&
                        bytes[start + 2] == 0x4E && bytes[start + 3] == 0x47;
            if (width <= 64)
            {
                Assert.False(isPng, $"El marco {width}px debe ser DIB para el shell.");
            }
            else
            {
                Assert.True(isPng, $"El marco {width}px debe ser PNG.");
            }
        }

        Assert.Equal(ExpectedIconSizes, sizes);
    }

    [Fact]
    public void OfficialLogo_IsSquareAndHighResolution()
    {
        using var stream = File.OpenRead(RepositoryFile("src/Viciotek/Assets/icono/viciotek.png"));
        var frame = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0];

        Assert.Equal(frame.PixelWidth, frame.PixelHeight);
        Assert.True(frame.PixelWidth >= 256, $"El logo mide {frame.PixelWidth}px.");
    }

    private static string RepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Viciotek.sln")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        var path = Path.Combine(directory!.FullName, relativePath);
        Assert.True(File.Exists(path), $"Falta el recurso de marca: {path}");
        return path;
    }
}

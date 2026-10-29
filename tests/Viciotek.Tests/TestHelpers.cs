using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Viciotek.Tests;

internal static class TestHelpers
{
    public static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "viciotek-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static byte[] CreatePng(
        int width,
        int height,
        Func<int, int, (byte B, byte G, byte R)>? pixel = null)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = pixel?.Invoke(x, y) ?? ((byte)40, (byte)120, (byte)200);
                var offset = (y * stride) + (x * 4);
                pixels[offset] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
                pixels[offset + 3] = 255;
            }
        }

        var source = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    public static (int Width, int Height) ReadImageSize(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }

    public static (byte R, byte G, byte B) ReadPixel(string path, int x, int y)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var stride = frame.PixelWidth * 4;
        var pixels = new byte[stride * frame.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        var offset = (y * stride) + (x * 4);
        return (pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }
}

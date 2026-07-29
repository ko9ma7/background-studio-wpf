using BackgroundStudio.Core;
using BackgroundStudio.Services;
using System.IO;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace BackgroundStudio.Tests;

public sealed class ImageComposerTests
{
    [Fact]
    public void ComposerSupportsTransformMaskOutlineAndRasterFormats()
    {
        RunSta(() =>
        {
            var source = CreateCutout();
            var options = new EditOptions(
                BackgroundMode.Transparent,
                "#FFFFFF",
                18,
                0,
                0,
                0,
                0,
                0.45,
                0.18,
                ForegroundFilter.Comic,
                RenderMode.Outline,
                0.8,
                0.1,
                0,
                true,
                2,
                "#112233",
                1.1,
                1.2,
                1.15,
                0.1,
                10,
                0.9,
                12,
                true,
                false,
                -1,
                CanvasAspect.Square);
            var result = ImageComposer.Compose(source, source, options, null);
            var temp = Path.Combine(Path.GetTempPath(), $"background-studio-{Guid.NewGuid():N}");
            Directory.CreateDirectory(temp);
            try
            {
                foreach (var extension in new[] { ".png", ".jpg", ".bmp", ".tiff" })
                {
                    var path = Path.Combine(temp, $"result{extension}");
                    ImageComposer.Save(result, path);
                    Assert.True(new FileInfo(path).Length > 0);
                }
                var svg = Path.Combine(temp, "outline.svg");
                ImageComposer.SaveSvgOutline(
                    ImageComposer.PrepareForeground(source, options),
                    svg,
                    options.OutlineColor,
                    options.OutlineWidth);
                Assert.Contains("<path", File.ReadAllText(svg));
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        });
    }

    private static BitmapSource CreateCutout()
    {
        const int width = 20;
        const int height = 10;
        var pixels = new byte[width * height * 4];
        for (var y = 2; y < 8; y++)
        {
            for (var x = 2; x < 6; x++)
            {
                var index = (y * width + x) * 4;
                pixels[index] = 20;
                pixels[index + 1] = 60;
                pixels[index + 2] = 240;
                pixels[index + 3] = 255;
            }
        }
        var result = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * 4);
        result.Freeze();
        return result;
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw error;
        }
    }
}

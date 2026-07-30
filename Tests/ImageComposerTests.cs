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
    public void ManualMaskEraseAndRestoreReplayFromAiCutout()
    {
        RunSta(() =>
        {
            var source = CreateCutout();
            var erased = ImageComposer.ApplyMaskStrokes(
                source,
                [
                    new MaskStroke(
                        MaskTool.Erase,
                        0.2,
                        [new MaskPoint(0.2, 0.5)])
                ]);
            Assert.Equal(0, AlphaAt(erased, 4, 5));

            var restored = ImageComposer.ApplyMaskStrokes(
                source,
                [
                    new MaskStroke(
                        MaskTool.Erase,
                        0.2,
                        [new MaskPoint(0.2, 0.5)]),
                    new MaskStroke(
                        MaskTool.Restore,
                        0.1,
                        [new MaskPoint(0.2, 0.5)])
                ]);
            Assert.Equal(255, AlphaAt(restored, 4, 5));
        });
    }

    [Fact]
    public void CompletedQueueJobsStayCompletedWhenNewJobIsAdded()
    {
        var saved = new BatchJob("saved.png", false) { Status = "완료 · 자동 저장됨" };
        var pending = new BatchJob("new.png", false);

        Assert.False(saved.IsRunnable);
        Assert.True(pending.IsRunnable);
    }

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

    private static byte AlphaAt(BitmapSource source, int x, int y)
    {
        var pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
        source.CopyPixels(pixels, source.PixelWidth * 4, 0);
        return pixels[(y * source.PixelWidth + x) * 4 + 3];
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

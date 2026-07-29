using BackgroundStudio.Core;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;

namespace BackgroundStudio.Services;

public sealed class VideoProcessor
{
    private readonly U2NetEngine engine;

    public VideoProcessor(U2NetEngine engine)
    {
        this.engine = engine;
    }

    public static bool IsFfmpegAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                ArgumentList = { "-version" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit(3000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task ProcessAsync(
        string source,
        string output,
        EditOptions options,
        string? backgroundPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (!IsFfmpegAvailable())
        {
            throw new InvalidOperationException("FFmpeg를 설치하고 PATH에 추가해야 합니다.");
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "BackgroundStudio");
        var temp = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
        var inputFrames = Path.Combine(temp, "input");
        var outputFrames = Path.Combine(temp, "output");
        Directory.CreateDirectory(inputFrames);
        Directory.CreateDirectory(outputFrames);
        try
        {
            await RunAsync(
                [
                    "-hide_banner", "-loglevel", "error", "-i", source,
                    "-vf", "scale='min(1280,iw)':'min(1280,ih)':force_original_aspect_ratio=decrease",
                    Path.Combine(inputFrames, "%08d.png")
                ],
                cancellationToken);
            var frames = Directory.GetFiles(inputFrames, "*.png").Order().ToArray();
            var background = backgroundPath is null ? null : ImageComposer.Load(backgroundPath);
            for (var index = 0; index < frames.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var original = ImageComposer.Load(frames[index]);
                var cutout = await Task.Run(
                    () => engine.Remove(original, options.MaskThreshold, options.EdgeSoftness),
                    cancellationToken);
                var result = await Application.Current.Dispatcher.InvokeAsync(
                    () => ImageComposer.Compose(original, cutout, options, background));
                ImageComposer.SavePng(
                    result,
                    Path.Combine(outputFrames, Path.GetFileName(frames[index])));
                progress?.Report((double)(index + 1) / Math.Max(1, frames.Length) * 0.95);
            }

            var transparent = options.Mode == BackgroundMode.Transparent;
            var encode = transparent
                ? new[] { "-c:v", "libvpx-vp9", "-pix_fmt", "yuva420p", "-auto-alt-ref", "0" }
                : new[] { "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "20" };
            var arguments = new List<string>
            {
                "-hide_banner", "-loglevel", "error", "-framerate", "30",
                "-i", Path.Combine(outputFrames, "%08d.png"), "-i", source,
                "-map", "0:v:0", "-map", "1:a?"
            };
            arguments.AddRange(encode);
            arguments.AddRange(
                ["-c:a", transparent ? "libopus" : "aac", "-shortest", "-y", output]);
            await RunAsync(arguments, cancellationToken);
            progress?.Report(1);
        }
        finally
        {
            var resolvedRoot = Path.GetFullPath(tempRoot) + Path.DirectorySeparatorChar;
            var resolvedTemp = Path.GetFullPath(temp);
            if (resolvedTemp.StartsWith(resolvedRoot, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(resolvedTemp))
            {
                Directory.Delete(resolvedTemp, true);
            }
        }
    }

    private static async Task RunAsync(
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("FFmpeg를 시작하지 못했습니다.");
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"FFmpeg 작업에 실패했습니다: {error}");
        }
    }
}

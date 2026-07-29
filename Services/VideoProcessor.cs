using BackgroundStudio.Core;
using System.Diagnostics;
using System.Globalization;
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
        return FfmpegManager.IsAvailable();
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
            throw new InvalidOperationException(
                "동영상 처리 엔진이 없습니다. 상단의 'FFmpeg 준비'를 눌러 자동 설치하세요.");
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "BackgroundStudio");
        var temp = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
        var inputFrames = Path.Combine(temp, "input");
        var outputFrames = Path.Combine(temp, "output");
        Directory.CreateDirectory(inputFrames);
        Directory.CreateDirectory(outputFrames);
        try
        {
            var frameRate = await ProbeFrameRateAsync(source, cancellationToken);
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

            var extension = Path.GetExtension(output).ToLowerInvariant();
            var transparent = (
                options.Mode == BackgroundMode.Transparent
                && options.RenderMode != RenderMode.Mask)
                || options.RenderMode == RenderMode.Outline;
            if (transparent && extension is not ".webm" and not ".mov")
            {
                throw new InvalidOperationException("투명 동영상은 WebM 또는 MOV로 저장하세요.");
            }
            string[] encode;
            string[] audio;
            if (extension == ".webm")
            {
                encode =
                [
                    "-c:v", "libvpx-vp9",
                    "-pix_fmt", transparent ? "yuva420p" : "yuv420p",
                    "-auto-alt-ref", "0",
                    "-b:v", "0",
                    "-crf", "24"
                ];
                audio = ["-c:a", "libopus"];
            }
            else if (extension == ".mov" && transparent)
            {
                encode = ["-c:v", "prores_ks", "-profile:v", "4444", "-pix_fmt", "yuva444p10le"];
                audio = ["-c:a", "pcm_s16le"];
            }
            else if (extension == ".gif")
            {
                encode =
                [
                    "-vf", "split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse",
                    "-loop", "0"
                ];
                audio = ["-an"];
            }
            else
            {
                encode = ["-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "20"];
                audio = ["-c:a", "aac"];
            }
            var arguments = new List<string>
            {
                "-hide_banner", "-loglevel", "error", "-framerate", frameRate,
                "-i", Path.Combine(outputFrames, "%08d.png"), "-i", source,
                "-map", "0:v:0", "-map", "1:a?"
            };
            arguments.AddRange(encode);
            arguments.AddRange(audio);
            arguments.AddRange(["-shortest", "-y", output]);
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

    private static async Task<string> ProbeFrameRateAsync(
        string source,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = FfmpegManager.ResolveFfprobe(),
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "-v", "error",
            "-select_streams", "v:0",
            "-show_entries", "stream=avg_frame_rate",
            "-of", "default=noprint_wrappers=1:nokey=1",
            source
        })
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("FFprobe를 시작하지 못했습니다.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var value = (await outputTask).Trim();
        if (process.ExitCode != 0)
        {
            return "30";
        }
        var parts = value.Split('/');
        if (parts.Length == 2
            && double.TryParse(parts[0], CultureInfo.InvariantCulture, out var numerator)
            && double.TryParse(parts[1], CultureInfo.InvariantCulture, out var denominator)
            && denominator > 0)
        {
            return (numerator / denominator).ToString("0.###", CultureInfo.InvariantCulture);
        }
        return double.TryParse(value, CultureInfo.InvariantCulture, out var rate) && rate > 0
            ? rate.ToString("0.###", CultureInfo.InvariantCulture)
            : "30";
    }

    private static async Task RunAsync(
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = FfmpegManager.ResolveFfmpeg(),
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

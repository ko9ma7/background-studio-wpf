using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace BackgroundStudio.Services;

public sealed class FfmpegManager
{
    private const string Version = "8.1.2";
    private const string ArchiveUrl =
        "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-8.1.2-essentials_build.zip";
    private const string ExpectedSha256 =
        "db580001caa24ac104c8cb856cd113a87b0a443f7bdf47d8c12b1d740584a2ec";
    private readonly HttpClient httpClient = new();

    public static string InstallDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BackgroundStudio",
        "ffmpeg",
        Version);

    public static string FfmpegPath => Path.Combine(InstallDirectory, "ffmpeg.exe");

    public static string FfprobePath => Path.Combine(InstallDirectory, "ffprobe.exe");

    public bool IsReady => File.Exists(FfmpegPath) && File.Exists(FfprobePath);

    public static string ResolveFfmpeg() => File.Exists(FfmpegPath) ? FfmpegPath : "ffmpeg";

    public static string ResolveFfprobe() => File.Exists(FfprobePath) ? FfprobePath : "ffprobe";

    public static bool IsAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = ResolveFfmpeg(),
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

    public async Task EnsureAsync(
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (IsReady)
        {
            progress?.Report(1);
            return;
        }
        var parent = Path.GetDirectoryName(InstallDirectory)!;
        Directory.CreateDirectory(parent);
        var archivePath = Path.Combine(parent, $"ffmpeg-{Version}.zip.download");
        try
        {
            using var response = await httpClient.GetAsync(
                ArchiveUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var output = new FileStream(
                archivePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                true))
            {
                var buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    written += read;
                    if (total > 0)
                    {
                        progress?.Report((double)written / total.Value * 0.9);
                    }
                }
                await output.FlushAsync(cancellationToken);
            }
            await using (var stream = File.OpenRead(archivePath))
            {
                var checksum = Convert.ToHexString(await SHA256.HashDataAsync(
                    stream,
                    cancellationToken));
                if (!checksum.Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("FFmpeg 다운로드 체크섬이 일치하지 않습니다.");
                }
            }
            Directory.CreateDirectory(InstallDirectory);
            using var archive = ZipFile.OpenRead(archivePath);
            foreach (var name in new[] { "ffmpeg.exe", "ffprobe.exe" })
            {
                var entry = archive.Entries.FirstOrDefault(item =>
                    item.FullName.EndsWith($"/bin/{name}", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException($"FFmpeg 압축 파일에 {name}이 없습니다.");
                entry.ExtractToFile(Path.Combine(InstallDirectory, name), true);
            }
            progress?.Report(1);
        }
        finally
        {
            File.Delete(archivePath);
        }
    }
}

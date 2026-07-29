using System.Security.Cryptography;
using System.Net.Http;

namespace BackgroundStudio.Services;

public sealed class ModelManager
{
    private const string ModelUrl =
        "https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx";
    private const string ExpectedMd5 = "8e83ca70e441ab06c318d82300c84806";
    private readonly HttpClient httpClient = new();

    public string ModelPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BackgroundStudio",
        "models",
        "u2netp.onnx");

    public bool IsReady => File.Exists(ModelPath) && ChecksumMatches(ModelPath);

    public async Task EnsureModelAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (IsReady)
        {
            progress?.Report(1);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
        var temporary = ModelPath + ".download";
        using var response = await httpClient.GetAsync(
            ModelUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using (var output = new FileStream(
            temporary,
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
                    progress?.Report((double)written / total.Value);
                }
            }
            await output.FlushAsync(cancellationToken);
        }

        if (!ChecksumMatches(temporary))
        {
            File.Delete(temporary);
            throw new InvalidDataException("모델 체크섬이 일치하지 않습니다.");
        }
        File.Move(temporary, ModelPath, true);
        progress?.Report(1);
    }

    private static bool ChecksumMatches(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(MD5.HashData(stream))
            .Equals(ExpectedMd5, StringComparison.OrdinalIgnoreCase);
    }
}

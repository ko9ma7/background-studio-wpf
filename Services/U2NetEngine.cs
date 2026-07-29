using BackgroundStudio.Core;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BackgroundStudio.Services;

public sealed class U2NetEngine : IDisposable
{
    private const int ModelSize = 320;
    private readonly InferenceSession session;
    private readonly string inputName;

    public U2NetEngine(string modelPath)
    {
        session = new InferenceSession(modelPath);
        inputName = session.InputMetadata.Keys.First();
    }

    public BitmapSource Remove(BitmapSource source, double threshold, double softness)
    {
        var input = CreateInput(source);
        using var results = session.Run([NamedOnnxValue.CreateFromTensor(inputName, input)]);
        var output = results.First().AsTensor<float>();
        var plane = output.ToArray().AsSpan(0, ModelSize * ModelSize);
        var mask = MaskMath.Normalize(plane);
        return ApplyMask(source, mask, threshold, softness);
    }

    private static DenseTensor<float> CreateInput(BitmapSource source)
    {
        var scaled = new TransformedBitmap(
            source,
            new ScaleTransform(
                (double)ModelSize / source.PixelWidth,
                (double)ModelSize / source.PixelHeight));
        var converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        var stride = ModelSize * 4;
        var pixels = new byte[stride * ModelSize];
        converted.CopyPixels(pixels, stride, 0);
        var tensor = new DenseTensor<float>([1, 3, ModelSize, ModelSize]);
        var means = new[] { 0.485f, 0.456f, 0.406f };
        var deviations = new[] { 0.229f, 0.224f, 0.225f };

        for (var y = 0; y < ModelSize; y++)
        {
            for (var x = 0; x < ModelSize; x++)
            {
                var pixel = (y * ModelSize + x) * 4;
                var values = new[]
                {
                    pixels[pixel + 2] / 255f,
                    pixels[pixel + 1] / 255f,
                    pixels[pixel] / 255f
                };
                for (var channel = 0; channel < 3; channel++)
                {
                    tensor[0, channel, y, x] =
                        (values[channel] - means[channel]) / deviations[channel];
                }
            }
        }
        return tensor;
    }

    private static BitmapSource ApplyMask(
        BitmapSource source,
        byte[] mask,
        double threshold,
        double softness)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        for (var y = 0; y < converted.PixelHeight; y++)
        {
            var sourceY = Math.Clamp(
                (int)Math.Round((double)y / Math.Max(1, converted.PixelHeight - 1) * (ModelSize - 1)),
                0,
                ModelSize - 1);
            for (var x = 0; x < converted.PixelWidth; x++)
            {
                var sourceX = Math.Clamp(
                    (int)Math.Round((double)x / Math.Max(1, converted.PixelWidth - 1) * (ModelSize - 1)),
                    0,
                    ModelSize - 1);
                var alpha = MaskMath.Adjust(mask[sourceY * ModelSize + sourceX], threshold, softness);
                pixels[y * stride + x * 4 + 3] = alpha;
            }
        }

        var result = BitmapSource.Create(
            converted.PixelWidth,
            converted.PixelHeight,
            converted.DpiX,
            converted.DpiY,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        result.Freeze();
        return result;
    }

    public void Dispose()
    {
        session.Dispose();
    }
}

namespace BackgroundStudio.Core;

public static class MaskMath
{
    public static byte[] Normalize(ReadOnlySpan<float> values)
    {
        if (values.IsEmpty)
        {
            return [];
        }

        var minimum = float.MaxValue;
        var maximum = float.MinValue;
        foreach (var value in values)
        {
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }

        var range = maximum - minimum;
        if (range <= float.Epsilon)
        {
            return new byte[values.Length];
        }

        var result = new byte[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            result[index] = (byte)Math.Clamp(
                Math.Round((values[index] - minimum) / range * 255),
                0,
                255);
        }
        return result;
    }

    public static byte Adjust(byte alpha, double threshold, double softness)
    {
        var normalized = alpha / 255d;
        var start = Math.Clamp(threshold - softness / 2, 0, 1);
        var end = Math.Clamp(threshold + softness / 2, 0, 1);
        if (end - start <= double.Epsilon)
        {
            return normalized >= threshold ? byte.MaxValue : byte.MinValue;
        }

        var value = Math.Clamp((normalized - start) / (end - start), 0, 1);
        var smooth = value * value * (3 - 2 * value);
        return (byte)Math.Round(smooth * 255);
    }
}

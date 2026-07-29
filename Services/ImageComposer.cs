using BackgroundStudio.Core;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace BackgroundStudio.Services;

public static class ImageComposer
{
    public static BitmapSource Compose(
        BitmapSource original,
        BitmapSource cutout,
        EditOptions options,
        BitmapSource? background)
    {
        var (width, height) = CanvasSize(original.PixelWidth, original.PixelHeight, options.CanvasAspect);
        var foregroundSource = PrepareForeground(cutout, options);
        if (options.RenderMode == RenderMode.Mask)
        {
            return CreateMask(foregroundSource);
        }
        if (options.RenderMode == RenderMode.Outline)
        {
            return CreateOutline(
                foregroundSource,
                options.OutlineWidth,
                ParseColor(options.OutlineColor));
        }
        var canvas = new Canvas
        {
            Width = width,
            Height = height,
            Background = options.Mode == BackgroundMode.Color
                ? ParseBrush(options.Color)
                : Brushes.Transparent
        };

        if (options.Mode == BackgroundMode.Blur)
        {
            canvas.Children.Add(CreateCoverImage(original, width, height, options.BlurRadius));
        }
        else if (options.Mode == BackgroundMode.Image)
        {
            if (background is null)
            {
                throw new InvalidOperationException("배경 이미지가 필요합니다.");
            }
            canvas.Children.Add(CreateCoverImage(background, width, height, 0));
        }

        var foreground = new Image
        {
            Source = foregroundSource,
            Width = original.PixelWidth,
            Height = original.PixelHeight,
            Stretch = Stretch.Fill
        };
        Canvas.SetLeft(foreground, (width - original.PixelWidth) / 2.0);
        Canvas.SetTop(foreground, (height - original.PixelHeight) / 2.0);
        if (options.ShadowBlur > 0 || options.ShadowOffsetX != 0 || options.ShadowOffsetY != 0)
        {
            foreground.Effect = new DropShadowEffect
            {
                BlurRadius = options.ShadowBlur,
                Opacity = options.ShadowOpacity,
                ShadowDepth = Math.Sqrt(
                    options.ShadowOffsetX * options.ShadowOffsetX +
                    options.ShadowOffsetY * options.ShadowOffsetY),
                Direction = Math.Atan2(-options.ShadowOffsetY, options.ShadowOffsetX)
                    * 180
                    / Math.PI
            };
        }
        canvas.Children.Add(foreground);
        canvas.Measure(new Size(width, height));
        canvas.Arrange(new Rect(0, 0, width, height));

        var result = new RenderTargetBitmap(
            width,
            height,
            original.DpiX,
            original.DpiY,
            PixelFormats.Pbgra32);
        result.Render(canvas);
        result.Freeze();
        return result;
    }

    public static BitmapSource PrepareForeground(BitmapSource cutout, EditOptions options)
    {
        var filtered = ApplyAdjustments(
            ApplyFilter(cutout, options.ForegroundFilter),
            options);
        var box = AlphaBounds(filtered);
        if (box.IsEmpty)
        {
            return filtered;
        }
        var centerX = options.AutoCenter ? filtered.PixelWidth / 2.0 : box.X + box.Width / 2;
        var centerY = options.AutoCenter ? filtered.PixelHeight / 2.0 : box.Y + box.Height / 2;
        var translateX = centerX - (box.X + box.Width / 2)
            + options.SubjectOffsetX * filtered.PixelWidth;
        var translateY = centerY - (box.Y + box.Height / 2)
            + options.SubjectOffsetY * filtered.PixelHeight;
        var image = new Image
        {
            Source = filtered,
            Width = filtered.PixelWidth,
            Height = filtered.PixelHeight,
            Stretch = Stretch.Fill,
            RenderTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform(
                        options.SubjectScale * (options.FlipHorizontal ? -1 : 1),
                        options.SubjectScale * (options.FlipVertical ? -1 : 1),
                        box.X + box.Width / 2,
                        box.Y + box.Height / 2),
                    new RotateTransform(
                        options.Rotation,
                        box.X + box.Width / 2,
                        box.Y + box.Height / 2),
                    new TranslateTransform(translateX, translateY)
                }
            }
        };
        var canvas = new Canvas
        {
            Width = filtered.PixelWidth,
            Height = filtered.PixelHeight,
            Background = Brushes.Transparent
        };
        canvas.Children.Add(image);
        return Render(canvas, filtered.PixelWidth, filtered.PixelHeight, filtered.DpiX, filtered.DpiY);
    }

    public static BitmapSource Load(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var source = decoder.Frames[0];
        source.Freeze();
        return source;
    }

    public static void Save(BitmapSource source, string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        BitmapEncoder encoder = extension switch
        {
            ".png" => new PngBitmapEncoder(),
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 92 },
            ".bmp" => new BmpBitmapEncoder(),
            ".tif" or ".tiff" => new TiffBitmapEncoder(),
            _ => throw new ArgumentException("PNG, JPEG, BMP, TIFF 형식으로 저장할 수 있습니다.")
        };
        var frameSource = extension is ".jpg" or ".jpeg" or ".bmp"
            ? FlattenOnWhite(source)
            : source;
        encoder.Frames.Add(BitmapFrame.Create(frameSource));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public static void SavePng(BitmapSource source, string path) => Save(source, path);

    public static void SaveSvgOutline(
        BitmapSource source,
        string path,
        string strokeColor,
        int strokeWidth)
    {
        var converted = ConvertToBgra32(source);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var segments = new Dictionary<(int X, int Y), List<(int X, int Y)>>();
        bool Inside(int x, int y) =>
            x >= 0 && y >= 0 && x < converted.PixelWidth && y < converted.PixelHeight
            && pixels[y * stride + x * 4 + 3] >= 128;
        void Add((int X, int Y) start, (int X, int Y) end)
        {
            if (!segments.TryGetValue(start, out var ends))
            {
                ends = [];
                segments[start] = ends;
            }
            ends.Add(end);
        }
        for (var y = 0; y < converted.PixelHeight; y++)
        {
            for (var x = 0; x < converted.PixelWidth; x++)
            {
                if (!Inside(x, y)) continue;
                if (!Inside(x, y - 1)) Add((x + 1, y), (x, y));
                if (!Inside(x + 1, y)) Add((x + 1, y + 1), (x + 1, y));
                if (!Inside(x, y + 1)) Add((x, y + 1), (x + 1, y + 1));
                if (!Inside(x - 1, y)) Add((x, y), (x, y + 1));
            }
        }
        var pathData = new StringBuilder();
        while (segments.Count > 0)
        {
            var start = segments.Keys.First();
            var current = start;
            pathData.Append(CultureInfo.InvariantCulture, $"M{start.X},{start.Y}");
            while (segments.TryGetValue(current, out var ends))
            {
                var next = ends[^1];
                ends.RemoveAt(ends.Count - 1);
                if (ends.Count == 0) segments.Remove(current);
                pathData.Append(CultureInfo.InvariantCulture, $" L{next.X},{next.Y}");
                current = next;
                if (current == start) break;
            }
            pathData.Append(" Z ");
        }
        var color = ParseColor(strokeColor);
        var normalizedColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        var svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {source.PixelWidth} {source.PixelHeight}">
              <path d="{pathData}" fill="none" stroke="{normalizedColor}" stroke-width="{strokeWidth}" stroke-linejoin="round"/>
            </svg>
            """;
        File.WriteAllText(path, svg, Encoding.UTF8);
    }

    private static BitmapSource ApplyFilter(BitmapSource source, ForegroundFilter filter)
    {
        var converted = ConvertToBgra32(source);
        if (filter == ForegroundFilter.Original)
        {
            return converted;
        }
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var original = (byte[])pixels.Clone();
        for (var y = 0; y < converted.PixelHeight; y++)
        {
            for (var x = 0; x < converted.PixelWidth; x++)
            {
                var index = y * stride + x * 4;
                var blue = original[index];
                var green = original[index + 1];
                var red = original[index + 2];
                switch (filter)
                {
                    case ForegroundFilter.Bright:
                        red = Scale(red, 1.15, 0);
                        green = Scale(green, 1.15, 0);
                        blue = Scale(blue, 1.15, 0);
                        break;
                    case ForegroundFilter.Vivid:
                        (red, green, blue) = Saturate(red, green, blue, 1.4, 1.1);
                        break;
                    case ForegroundFilter.Warm:
                        red = Scale(red, 1.08, 5);
                        green = Scale(green, 1.02, 0);
                        blue = Scale(blue, 0.9, 0);
                        break;
                    case ForegroundFilter.Cool:
                        red = Scale(red, 0.9, 0);
                        green = Scale(green, 1.02, 0);
                        blue = Scale(blue, 1.1, 4);
                        break;
                    case ForegroundFilter.Grayscale:
                        red = green = blue = Luminance(red, green, blue);
                        break;
                    case ForegroundFilter.Comic:
                        red = (byte)(red / 32 * 32);
                        green = (byte)(green / 32 * 32);
                        blue = (byte)(blue / 32 * 32);
                        if (IsStrongEdge(original, stride, converted.PixelWidth, converted.PixelHeight, x, y))
                        {
                            red = green = blue = 24;
                        }
                        break;
                    case ForegroundFilter.HighContrast:
                        red = Scale(red, 1.45, -58);
                        green = Scale(green, 1.45, -58);
                        blue = Scale(blue, 1.45, -58);
                        break;
                    case ForegroundFilter.Posterize:
                        red = (byte)(red / 64 * 64);
                        green = (byte)(green / 64 * 64);
                        blue = (byte)(blue / 64 * 64);
                        break;
                    case ForegroundFilter.Sepia:
                        var sepiaRed = Math.Clamp(0.393 * red + 0.769 * green + 0.189 * blue, 0, 255);
                        var sepiaGreen = Math.Clamp(0.349 * red + 0.686 * green + 0.168 * blue, 0, 255);
                        var sepiaBlue = Math.Clamp(0.272 * red + 0.534 * green + 0.131 * blue, 0, 255);
                        red = (byte)sepiaRed;
                        green = (byte)sepiaGreen;
                        blue = (byte)sepiaBlue;
                        break;
                    case ForegroundFilter.Invert:
                        red = (byte)(255 - red);
                        green = (byte)(255 - green);
                        blue = (byte)(255 - blue);
                        break;
                    case ForegroundFilter.Pencil:
                        var pencil = IsStrongEdge(original, stride, converted.PixelWidth, converted.PixelHeight, x, y)
                            ? (byte)28
                            : (byte)245;
                        red = green = blue = pencil;
                        break;
                }
                pixels[index] = blue;
                pixels[index + 1] = green;
                pixels[index + 2] = red;
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

    private static BitmapSource ApplyAdjustments(BitmapSource source, EditOptions options)
    {
        var converted = ConvertToBgra32(source);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var alpha = new byte[converted.PixelWidth * converted.PixelHeight];
        var hue = options.Hue * Math.PI / 180;
        var cos = Math.Cos(hue);
        var sin = Math.Sin(hue);
        for (var y = 0; y < converted.PixelHeight; y++)
        {
            for (var x = 0; x < converted.PixelWidth; x++)
            {
                var index = y * stride + x * 4;
                var red = pixels[index + 2] / 255.0;
                var green = pixels[index + 1] / 255.0;
                var blue = pixels[index] / 255.0;
                var luminance = red * 0.299 + green * 0.587 + blue * 0.114;
                red = luminance + (red - luminance) * options.Saturation;
                green = luminance + (green - luminance) * options.Saturation;
                blue = luminance + (blue - luminance) * options.Saturation;
                red = (red - 0.5) * options.Contrast + 0.5 + options.Brightness - 1;
                green = (green - 0.5) * options.Contrast + 0.5 + options.Brightness - 1;
                blue = (blue - 0.5) * options.Contrast + 0.5 + options.Brightness - 1;
                red += options.Temperature * 0.12;
                blue -= options.Temperature * 0.12;
                var hueRed = (.213 + cos * .787 - sin * .213) * red
                    + (.715 - cos * .715 - sin * .715) * green
                    + (.072 - cos * .072 + sin * .928) * blue;
                var hueGreen = (.213 - cos * .213 + sin * .143) * red
                    + (.715 + cos * .285 + sin * .140) * green
                    + (.072 - cos * .072 - sin * .283) * blue;
                var hueBlue = (.213 - cos * .213 - sin * .787) * red
                    + (.715 - cos * .715 + sin * .715) * green
                    + (.072 + cos * .928 + sin * .072) * blue;
                pixels[index + 2] = (byte)Math.Clamp(hueRed * 255, 0, 255);
                pixels[index + 1] = (byte)Math.Clamp(hueGreen * 255, 0, 255);
                pixels[index] = (byte)Math.Clamp(hueBlue * 255, 0, 255);
                alpha[y * converted.PixelWidth + x] = (byte)Math.Clamp(
                    pixels[index + 3] * options.ForegroundOpacity,
                    0,
                    255);
            }
        }
        alpha = ExpandAlpha(alpha, converted.PixelWidth, converted.PixelHeight, options.MaskExpansion);
        for (var y = 0; y < converted.PixelHeight; y++)
        {
            for (var x = 0; x < converted.PixelWidth; x++)
            {
                pixels[y * stride + x * 4 + 3] = alpha[y * converted.PixelWidth + x];
            }
        }
        return CreateBitmap(converted, pixels, stride);
    }

    private static byte[] ExpandAlpha(byte[] source, int width, int height, int amount)
    {
        var radius = Math.Clamp(Math.Abs(amount), 0, 12);
        if (radius == 0)
        {
            return source;
        }
        var result = new byte[source.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = amount > 0 ? byte.MinValue : byte.MaxValue;
                for (var dy = -radius; dy <= radius; dy++)
                {
                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        var sampleX = Math.Clamp(x + dx, 0, width - 1);
                        var sampleY = Math.Clamp(y + dy, 0, height - 1);
                        var sample = source[sampleY * width + sampleX];
                        value = amount > 0 ? Math.Max(value, sample) : Math.Min(value, sample);
                    }
                }
                result[y * width + x] = value;
            }
        }
        return result;
    }

    private static (int Width, int Height) CanvasSize(int width, int height, CanvasAspect aspect)
    {
        if (aspect == CanvasAspect.Original)
        {
            return (width, height);
        }
        var ratio = aspect switch
        {
            CanvasAspect.Square => 1.0,
            CanvasAspect.Portrait45 => 4.0 / 5,
            CanvasAspect.Landscape169 => 16.0 / 9,
            _ => (double)width / height
        };
        if ((double)width / height > ratio)
        {
            return ((int)Math.Round(height * ratio), height);
        }
        return (width, (int)Math.Round(width / ratio));
    }

    private static BitmapSource CreateMask(BitmapSource source)
    {
        var converted = ConvertToBgra32(source);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        for (var index = 0; index < pixels.Length; index += 4)
        {
            var value = pixels[index + 3];
            pixels[index] = pixels[index + 1] = pixels[index + 2] = value;
            pixels[index + 3] = 255;
        }
        return CreateBitmap(converted, pixels, stride);
    }

    private static BitmapSource CreateOutline(BitmapSource source, int radius, Color color)
    {
        var converted = ConvertToBgra32(source);
        var stride = converted.PixelWidth * 4;
        var sourcePixels = new byte[stride * converted.PixelHeight];
        var output = new byte[sourcePixels.Length];
        converted.CopyPixels(sourcePixels, stride, 0);
        radius = Math.Clamp(radius, 1, 12);
        for (var y = 0; y < converted.PixelHeight; y++)
        {
            for (var x = 0; x < converted.PixelWidth; x++)
            {
                var inside = sourcePixels[y * stride + x * 4 + 3] >= 128;
                var edge = false;
                for (var dy = -radius; dy <= radius && !edge; dy++)
                {
                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        var sampleX = x + dx;
                        var sampleY = y + dy;
                        var sampleInside = sampleX >= 0 && sampleY >= 0
                            && sampleX < converted.PixelWidth && sampleY < converted.PixelHeight
                            && sourcePixels[sampleY * stride + sampleX * 4 + 3] >= 128;
                        if (sampleInside != inside)
                        {
                            edge = true;
                            break;
                        }
                    }
                }
                if (!edge) continue;
                var index = y * stride + x * 4;
                output[index] = color.B;
                output[index + 1] = color.G;
                output[index + 2] = color.R;
                output[index + 3] = color.A;
            }
        }
        return CreateBitmap(converted, output, stride);
    }

    private static Int32Rect AlphaBounds(BitmapSource source)
    {
        var converted = ConvertToBgra32(source);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var left = converted.PixelWidth;
        var top = converted.PixelHeight;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < converted.PixelHeight; y++)
        {
            for (var x = 0; x < converted.PixelWidth; x++)
            {
                if (pixels[y * stride + x * 4 + 3] <= 8) continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }
        return right < left
            ? Int32Rect.Empty
            : new Int32Rect(left, top, right - left + 1, bottom - top + 1);
    }

    private static BitmapSource FlattenOnWhite(BitmapSource source)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            context.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
        }
        var result = new RenderTargetBitmap(
            source.PixelWidth,
            source.PixelHeight,
            source.DpiX,
            source.DpiY,
            PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }

    private static BitmapSource Render(
        Canvas canvas,
        int width,
        int height,
        double dpiX,
        double dpiY)
    {
        canvas.Measure(new Size(width, height));
        canvas.Arrange(new Rect(0, 0, width, height));
        var result = new RenderTargetBitmap(width, height, dpiX, dpiY, PixelFormats.Pbgra32);
        result.Render(canvas);
        result.Freeze();
        return result;
    }

    private static BitmapSource ConvertToBgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgra32)
        {
            return source;
        }
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static BitmapSource CreateBitmap(BitmapSource source, byte[] pixels, int stride)
    {
        var result = BitmapSource.Create(
            source.PixelWidth,
            source.PixelHeight,
            source.DpiX,
            source.DpiY,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        result.Freeze();
        return result;
    }

    private static byte Scale(byte value, double scale, double offset) =>
        (byte)Math.Clamp(Math.Round(value * scale + offset), 0, 255);

    private static (byte R, byte G, byte B) Saturate(
        byte red,
        byte green,
        byte blue,
        double saturation,
        double contrast)
    {
        var gray = Luminance(red, green, blue);
        byte Adjust(byte value) => Scale(
            (byte)Math.Clamp(Math.Round(gray + (value - gray) * saturation), 0, 255),
            contrast,
            128 * (1 - contrast));
        return (Adjust(red), Adjust(green), Adjust(blue));
    }

    private static byte Luminance(byte red, byte green, byte blue) =>
        (byte)Math.Clamp(Math.Round(red * 0.299 + green * 0.587 + blue * 0.114), 0, 255);

    private static bool IsStrongEdge(
        byte[] pixels,
        int stride,
        int width,
        int height,
        int x,
        int y)
    {
        var index = y * stride + x * 4;
        var current = Luminance(pixels[index + 2], pixels[index + 1], pixels[index]);
        var right = Math.Min(width - 1, x + 1);
        var bottom = Math.Min(height - 1, y + 1);
        var rightIndex = y * stride + right * 4;
        var bottomIndex = bottom * stride + x * 4;
        var delta = Math.Abs(current - Luminance(
            pixels[rightIndex + 2],
            pixels[rightIndex + 1],
            pixels[rightIndex]))
            + Math.Abs(current - Luminance(
                pixels[bottomIndex + 2],
                pixels[bottomIndex + 1],
                pixels[bottomIndex]));
        return delta > 70;
    }

    private static Image CreateCoverImage(
        BitmapSource source,
        double width,
        double height,
        double blurRadius)
    {
        var image = new Image
        {
            Source = source,
            Width = width,
            Height = height,
            Stretch = Stretch.UniformToFill
        };
        if (blurRadius > 0)
        {
            image.Effect = new BlurEffect { Radius = blurRadius };
        }
        return image;
    }

    private static Brush ParseBrush(string value)
    {
        var converter = new BrushConverter();
        try
        {
            return (Brush)converter.ConvertFromString(null, CultureInfo.InvariantCulture, value)!;
        }
        catch (Exception exception) when (
            exception is FormatException or NotSupportedException)
        {
            throw new ArgumentException("올바른 색상값을 입력하세요. 예: #FFFFFF", exception);
        }
    }

    private static Color ParseColor(string value)
    {
        var brush = ParseBrush(value) as SolidColorBrush
            ?? throw new ArgumentException("단색 외곽선 색상을 입력하세요.");
        return brush.Color;
    }
}

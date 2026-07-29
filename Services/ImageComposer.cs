using BackgroundStudio.Core;
using System.Globalization;
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
        var width = original.PixelWidth;
        var height = original.PixelHeight;
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
            Source = cutout,
            Width = width,
            Height = height,
            Stretch = Stretch.Fill
        };
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

    public static void SavePng(BitmapSource source, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
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
}

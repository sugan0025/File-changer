using System;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace Filee.App.Services;

/// <summary>
/// Pre-computes and caches an optimized downsampled (x1/4) Gaussian blurred wallpaper bitmap
/// with micro-grain noise texture to provide real frosted optical depth behind glass cards.
/// Zero per-frame GPU re-rasterization cost.
/// </summary>
public static class BackdropService
{
    private static Bitmap? s_blurred;
    private static bool s_initializing;
    private static readonly object s_lock = new();

    public static Bitmap? Blurred
    {
        get
        {
            if (s_blurred == null && !s_initializing)
            {
                lock (s_lock)
                {
                    if (s_blurred == null && !s_initializing)
                    {
                        s_initializing = true;
                        try
                        {
                            s_blurred = CreateBlurredBackdrop();
                        }
                        catch
                        {
                            // Graceful fallback for test or headless environments
                        }
                    }
                }
            }
            return s_blurred;
        }
    }

    public static Bitmap? CreateBlurredBackdrop(int targetWidth = 480, int targetHeight = 300)
    {
        try
        {
            var uri = new Uri("avares://Filee/Assets/Backgrounds/app_bg.png");
            using var stream = AssetLoader.Open(uri);
            using var origBitmap = SKBitmap.Decode(stream);
            if (origBitmap == null) return null;

            // 1. Downscale x1/4 for massive performance & natural blur foundation
            var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var resized = origBitmap.Resize(info, SKSamplingOptions.Default);
            if (resized == null) return null;

            // 2. Apply Gaussian blur pass (sigma = 18) with Clamp tile mode to prevent transparent borders
            using var surface = SKSurface.Create(info);
            using var paint = new SKPaint();
            using var blurFilter = SKImageFilter.CreateBlur(18, 18, SKShaderTileMode.Clamp);
            paint.ImageFilter = blurFilter;

            surface.Canvas.DrawBitmap(resized, 0, 0, paint);

            // 3. Apply subtle 3% fine noise grain to eliminate gradient banding & give frosty texture
            AddSubtleNoise(surface.Canvas, targetWidth, targetHeight);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            using var ms = new MemoryStream();
            data.SaveTo(ms);
            ms.Position = 0;

            return new Bitmap(ms);
        }
        catch
        {
            return null;
        }
    }

    private static void AddSubtleNoise(SKCanvas canvas, int width, int height)
    {
        var rand = new Random(42); // deterministic seed
        using var noiseBitmap = new SKBitmap(128, 128, SKColorType.Bgra8888, SKAlphaType.Premul);
        for (int y = 0; y < 128; y++)
        {
            for (int x = 0; x < 128; x++)
            {
                byte v = (byte)rand.Next(100, 255);
                byte a = (byte)rand.Next(3, 10); // ~2-4% opacity
                noiseBitmap.SetPixel(x, y, new SKColor(v, v, v, a));
            }
        }

        using var noisePaint = new SKPaint
        {
            BlendMode = SKBlendMode.Overlay
        };

        for (int y = 0; y < height; y += 128)
        {
            for (int x = 0; x < width; x += 128)
            {
                canvas.DrawBitmap(noiseBitmap, x, y, noisePaint);
            }
        }
    }

    public static Rect CalculateSlice(Point offset, Size cardSize, Size windowSize, Size imgSize)
    {
        if (windowSize.Width <= 0 || windowSize.Height <= 0 || imgSize.Width <= 0 || imgSize.Height <= 0)
            return new Rect(0, 0, Math.Max(1, imgSize.Width), Math.Max(1, imgSize.Height));

        // UniformToFill aspect mapping: image scales to fill window maintaining aspect ratio
        double scale = Math.Max(windowSize.Width / imgSize.Width, windowSize.Height / imgSize.Height);
        double renderedW = imgSize.Width * scale;
        double renderedH = imgSize.Height * scale;

        double originX = (windowSize.Width - renderedW) / 2.0;
        double originY = (windowSize.Height - renderedH) / 2.0;

        double imgX = (offset.X - originX) / scale;
        double imgY = (offset.Y - originY) / scale;
        double imgW = cardSize.Width / scale;
        double imgH = cardSize.Height / scale;

        double clampedX = Math.Clamp(imgX, 0, imgSize.Width);
        double clampedY = Math.Clamp(imgY, 0, imgSize.Height);
        double clampedW = Math.Clamp(imgW, 0, imgSize.Width - clampedX);
        double clampedH = Math.Clamp(imgH, 0, imgSize.Height - clampedY);

        return new Rect(clampedX, clampedY, Math.Max(1, clampedW), Math.Max(1, clampedH));
    }

    public static Rect SourceRectFor(Point offset, Size cardSize, Size windowSize, Size imgSize) =>
        CalculateSlice(offset, cardSize, windowSize, imgSize);
}

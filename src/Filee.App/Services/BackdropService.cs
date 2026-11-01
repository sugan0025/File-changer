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

            // 2. Apply Gaussian blur pass (sigma = 18)
            using var surface = SKSurface.Create(info);
            using var paint = new SKPaint();
            using var blurFilter = SKImageFilter.CreateBlur(18, 18);
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

    public static Rect SourceRectFor(Point offset, Size cardSize, Size windowSize, Size imgSize)
    {
        if (windowSize.Width <= 0 || windowSize.Height <= 0)
            return new Rect(0, 0, imgSize.Width, imgSize.Height);

        double x = (offset.X / windowSize.Width) * imgSize.Width;
        double y = (offset.Y / windowSize.Height) * imgSize.Height;
        double w = (cardSize.Width / windowSize.Width) * imgSize.Width;
        double h = (cardSize.Height / windowSize.Height) * imgSize.Height;

        x = Math.Clamp(x, 0, imgSize.Width);
        y = Math.Clamp(y, 0, imgSize.Height);
        w = Math.Clamp(w, 0, imgSize.Width - x);
        h = Math.Clamp(h, 0, imgSize.Height - y);

        return new Rect(x, y, w, h);
    }
}

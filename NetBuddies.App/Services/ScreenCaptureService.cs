using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace NetBuddies.App.Services;

public sealed record ScreenShareSource(string Id, string Name, nint WindowHandle)
{
    public bool IsDesktop => WindowHandle == 0;
}

public static class ScreenCaptureService
{
    public static IReadOnlyList<ScreenShareSource> GetSources()
    {
        var sources = new List<ScreenShareSource>
        {
            new("desktop", "Entire desktop", 0)
        };

        if (!OperatingSystem.IsWindows())
        {
            return sources;
        }

        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || GetWindowTextLength(handle) == 0)
            {
                return true;
            }

            var title = new string('\0', GetWindowTextLength(handle) + 1);
            GetWindowText(handle, title, title.Length);
            title = title.TrimEnd('\0').Trim();
            if (!string.IsNullOrWhiteSpace(title))
            {
                sources.Add(new($"window:{handle}", title, handle));
            }

            return true;
        }, 0);

        return sources
            .GroupBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(40)
            .ToArray();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static byte[] CaptureJpeg(ScreenShareSource source, int targetHeight, int jpegQuality)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Screen sharing capture currently supports Windows clients.");
        }

        using var capture = source.IsDesktop
            ? CaptureDesktop()
            : CaptureWindow(source.WindowHandle);
        using var scaled = ScaleToHeight(capture, targetHeight);
        using var stream = new MemoryStream();
        SaveJpeg(scaled, stream, jpegQuality);
        return stream.ToArray();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static Bitmap CaptureDesktop()
    {
        var left = GetSystemMetrics(76);
        var top = GetSystemMetrics(77);
        var width = Math.Max(GetSystemMetrics(78), 1);
        var height = Math.Max(GetSystemMetrics(79), 1);
        var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(left, top, 0, 0, new Size(width, height));
        return bitmap;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static Bitmap CaptureWindow(nint handle)
    {
        if (handle == 0 || !GetWindowRect(handle, out var rect))
        {
            return CaptureDesktop();
        }

        var width = Math.Max(rect.Right - rect.Left, 1);
        var height = Math.Max(rect.Bottom - rect.Top, 1);
        var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height));
        return bitmap;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static Bitmap ScaleToHeight(Bitmap source, int targetHeight)
    {
        var height = targetHeight <= 720 ? 720 : 1080;
        if (source.Height <= height)
        {
            return new Bitmap(source);
        }

        var width = Math.Max(1, (int)Math.Round(source.Width * (height / (double)source.Height)));
        var scaled = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(scaled);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(source, 0, 0, width, height);
        return scaled;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void SaveJpeg(Bitmap bitmap, Stream stream, int jpegQuality)
    {
        var quality = Math.Clamp(jpegQuality, 45, 92);
        var codec = ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(encoder => encoder.FormatID == ImageFormat.Jpeg.Guid);
        if (codec is null)
        {
            bitmap.Save(stream, ImageFormat.Jpeg);
            return;
        }

        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        bitmap.Save(stream, codec, parameters);
    }

    private delegate bool EnumWindowsProc(nint handle, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint handle, string text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint handle);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint handle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

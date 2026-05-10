using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SkiaSharp;

namespace NetBuddies.App.Services;

public static class GameAssetService
{
    private const string AssetRoot = "Assets/Games";
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, GameImageAsset?> AnimatedCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, GameImageFrameSet?> FrameCache = new(StringComparer.OrdinalIgnoreCase);

    public static Bitmap? Load(string relativePath)
    {
        var normalized = relativePath
            .Replace('\\', '/')
            .TrimStart('/');

        if (Cache.TryGetValue(normalized, out var cached))
        {
            return cached;
        }

        Bitmap? bitmap = null;
        foreach (var candidate in GetAssetCandidates(normalized))
        {
            var loosePath = GetLoosePath(candidate);
            if (File.Exists(loosePath))
            {
                bitmap = new Bitmap(loosePath);
                break;
            }

            var resourceUri = new Uri($"avares://NetBuddies.App/{AssetRoot}/{candidate}");
            if (AssetLoader.Exists(resourceUri))
            {
                using var stream = AssetLoader.Open(resourceUri);
                bitmap = new Bitmap(stream);
                break;
            }
        }

        Cache[normalized] = bitmap;
        return bitmap;
    }

    public static GameImageAsset? LoadAnimated(string relativePath)
    {
        var normalized = relativePath
            .Replace('\\', '/')
            .TrimStart('/');

        if (AnimatedCache.TryGetValue(normalized, out var cached))
        {
            return cached;
        }

        var frameSet = LoadFrameSet(normalized);
        var asset = frameSet?.CreateAsset();
        AnimatedCache[normalized] = asset;
        return asset;
    }

    public static GameImageAsset? LoadAnimatedInstance(string relativePath, TimeSpan initialDelay)
    {
        var normalized = relativePath
            .Replace('\\', '/')
            .TrimStart('/');

        return LoadFrameSet(normalized)?.CreateAsset(initialDelay);
    }

    public static GameImageAsset? LoadAnimatedFromBytes(string name, byte[] bytes)
    {
        return CreateFrameSet(name, bytes)?.CreateAsset();
    }

    public static string ExternalGamesFolder => Path.Combine(AppContext.BaseDirectory, AssetRoot);

    private static string GetLoosePath(string normalized)
    {
        return Path.Combine(
            AppContext.BaseDirectory,
            AssetRoot,
            normalized.Replace('/', Path.DirectorySeparatorChar));
    }

    private static IEnumerable<string> GetAssetCandidates(string normalized)
    {
        var extension = Path.GetExtension(normalized);
        if (!extension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.ChangeExtension(normalized, ".gif").Replace('\\', '/');
        }

        yield return normalized;
    }

    private static GameImageFrameSet? LoadFrameSet(string normalized)
    {
        if (FrameCache.TryGetValue(normalized, out var cached))
        {
            return cached;
        }

        foreach (var candidate in GetAssetCandidates(normalized))
        {
            var loosePath = GetLoosePath(candidate);
            if (File.Exists(loosePath))
            {
                var frameSet = CreateFrameSet(candidate, File.ReadAllBytes(loosePath));
                FrameCache[normalized] = frameSet;
                return frameSet;
            }

            var resourceUri = new Uri($"avares://NetBuddies.App/{AssetRoot}/{candidate}");
            if (!AssetLoader.Exists(resourceUri))
            {
                continue;
            }

            using var stream = AssetLoader.Open(resourceUri);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var embeddedFrameSet = CreateFrameSet(candidate, memory.ToArray());
            FrameCache[normalized] = embeddedFrameSet;
            return embeddedFrameSet;
        }

        FrameCache[normalized] = null;
        return null;
    }

    private static GameImageFrameSet? CreateFrameSet(string path, byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return null;
        }

        if (!IsGif(bytes))
        {
            using var stream = new MemoryStream(bytes);
            return new GameImageFrameSet(path, [new Bitmap(stream)], [TimeSpan.FromMilliseconds(250)]);
        }

        var frames = new List<Bitmap>();
        var delays = new List<TimeSpan>();
        DecodeGifFrames(bytes, frames, delays);
        if (frames.Count == 0)
        {
            using var stream = new MemoryStream(bytes);
            frames.Add(new Bitmap(stream));
            delays.Add(TimeSpan.FromMilliseconds(250));
        }

        return new GameImageFrameSet(path, frames, delays);
    }

    private static bool IsGif(byte[] bytes)
    {
        return bytes.Length >= 6
            && bytes[0] == 'G'
            && bytes[1] == 'I'
            && bytes[2] == 'F';
    }

    private static void DecodeGifFrames(byte[] bytes, List<Bitmap> frames, List<TimeSpan> delays)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.FrameCount <= 1)
        {
            return;
        }

        var imageInfo = new SKImageInfo(
            codec.Info.Width,
            codec.Info.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);

        for (var index = 0; index < codec.FrameCount; index++)
        {
            using var bitmap = new SKBitmap(imageInfo);
            var result = codec.GetPixels(imageInfo, bitmap.GetPixels(), new SKCodecOptions(index));
            if (result is not SKCodecResult.Success and not SKCodecResult.IncompleteInput)
            {
                continue;
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream(encoded.ToArray());
            frames.Add(new Bitmap(stream));

            var duration = codec.FrameInfo[index].Duration;
            delays.Add(TimeSpan.FromMilliseconds(Math.Clamp(duration, 40, 500)));
        }
    }
}

public sealed record GameImageFrameSet(
    string Name,
    IReadOnlyList<Bitmap> Frames,
    IReadOnlyList<TimeSpan> Delays)
{
    public GameImageAsset CreateAsset()
    {
        return new GameImageAsset(Name, Frames, Delays);
    }

    public GameImageAsset CreateAsset(TimeSpan initialDelay)
    {
        return new GameImageAsset(Name, Frames, Delays, initialDelay);
    }
}

public sealed partial class GameImageAsset : ObservableObject
{
    private readonly IReadOnlyList<Bitmap> _frames;
    private readonly IReadOnlyList<TimeSpan> _delays;
    private readonly DispatcherTimer? _timer;
    private bool _isWaitingInitialDelay;
    private int _frameIndex;

    public GameImageAsset(
        string name,
        IReadOnlyList<Bitmap> frames,
        IReadOnlyList<TimeSpan> delays,
        TimeSpan initialDelay = default)
    {
        Name = name;
        _frames = frames;
        _delays = delays;
        CurrentFrame = frames.Count > 0 ? frames[0] : null;

        if (frames.Count <= 1)
        {
            return;
        }

        _timer = new DispatcherTimer
        {
            Interval = initialDelay > TimeSpan.Zero
                ? initialDelay
                : delays.Count > 0 ? delays[0] : TimeSpan.FromMilliseconds(100)
        };
        _isWaitingInitialDelay = initialDelay > TimeSpan.Zero;
        _timer.Tick += (_, _) =>
        {
            if (_isWaitingInitialDelay)
            {
                _isWaitingInitialDelay = false;
                _timer.Interval = _frameIndex < _delays.Count
                    ? _delays[_frameIndex]
                    : TimeSpan.FromMilliseconds(100);
                return;
            }

            _frameIndex = (_frameIndex + 1) % _frames.Count;
            CurrentFrame = _frames[_frameIndex];
            _timer.Interval = _frameIndex < _delays.Count
                ? _delays[_frameIndex]
                : TimeSpan.FromMilliseconds(100);
        };
        _timer.Start();
    }

    public string Name { get; }

    [ObservableProperty]
    private Bitmap? _currentFrame;
}

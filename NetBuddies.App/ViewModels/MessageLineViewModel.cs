using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NAudio.Wave;
using SkiaSharp;

namespace NetBuddies.App.ViewModels;

public sealed partial class MessageLineViewModel : ViewModelBase
{
    private WaveFileReader? _voiceNoteReader;
    private WaveOutEvent? _voiceNoteOutput;
    private readonly List<TimeSpan> _inlineFrameDelays = [];
    private readonly List<Bitmap> _inlineFrames = [];
    private readonly byte[]? _inlineImageBytes;
    private DispatcherTimer? _inlineTimer;
    private int _inlineFrameIndex;

    public MessageLineViewModel(
        string sender,
        string body,
        bool isMine,
        bool isEvent = false,
        Bitmap? avatarImage = null,
        string voiceNotePath = "",
        byte[]? inlineImageBytes = null,
        string inlineFileName = "")
    {
        Sender = sender;
        Body = body;
        IsMine = isMine;
        IsEvent = isEvent;
        AvatarImage = avatarImage;
        VoiceNotePath = voiceNotePath;
        InlineFileName = string.IsNullOrWhiteSpace(inlineFileName) ? body : inlineFileName;
        _inlineImageBytes = inlineImageBytes;
        InlineImage = CreateInlineImage(inlineImageBytes);
        if (inlineImageBytes is not null)
        {
            StartGifAnimation(inlineImageBytes);
        }
    }

    public string Sender { get; }
    public string Body { get; }
    public bool IsMine { get; }
    public bool IsEvent { get; }
    public Bitmap? AvatarImage { get; }
    public string VoiceNotePath { get; }
    public string InlineFileName { get; }
    public bool HasVoiceNote => !string.IsNullOrWhiteSpace(VoiceNotePath);
    public bool HasInlineImage => InlineImage is not null;
    public bool HasInlineDownload => HasInlineImage && _inlineImageBytes is { Length: > 0 };
    public string Stamp { get; } = DateTime.Now.ToString("HH:mm");

    [ObservableProperty]
    private bool _isVoiceNotePlaying;

    [ObservableProperty]
    private Bitmap? _inlineImage;

    [ObservableProperty]
    private string _inlineDownloadStatus = "";

    partial void OnInlineImageChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasInlineImage));
        OnPropertyChanged(nameof(HasInlineDownload));
    }

    [RelayCommand]
    private void DownloadInlineMedia()
    {
        if (_inlineImageBytes is not { Length: > 0 })
        {
            return;
        }

        try
        {
            var downloadsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "NetBuddiesDownloads",
                "Images");
            Directory.CreateDirectory(downloadsPath);

            var fileName = string.IsNullOrWhiteSpace(InlineFileName)
                ? $"netbuddies-image-{DateTime.Now:yyyyMMdd-HHmmss}.png"
                : InlineFileName;
            var safeFileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
            var fullPath = CreateUniquePath(Path.Combine(downloadsPath, safeFileName));
            File.WriteAllBytes(fullPath, _inlineImageBytes);
            InlineDownloadStatus = $"Saved to {fullPath}";
        }
        catch (Exception ex)
        {
            InlineDownloadStatus = $"Could not save: {ex.Message}";
        }
    }

    [RelayCommand]
    private void PlayVoiceNote()
    {
        if (!HasVoiceNote || !File.Exists(VoiceNotePath))
        {
            return;
        }

        StopVoiceNote();

        _voiceNoteReader = new WaveFileReader(VoiceNotePath);
        _voiceNoteOutput = new WaveOutEvent();
        _voiceNoteOutput.Init(_voiceNoteReader);
        _voiceNoteOutput.PlaybackStopped += VoiceNoteOutput_PlaybackStopped;
        _voiceNoteOutput.Play();
        IsVoiceNotePlaying = true;
    }

    [RelayCommand]
    private void StopVoiceNote()
    {
        if (_voiceNoteOutput is not null)
        {
            _voiceNoteOutput.PlaybackStopped -= VoiceNoteOutput_PlaybackStopped;
            _voiceNoteOutput.Stop();
        }

        DisposeVoiceNotePlayer();
    }

    private void VoiceNoteOutput_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        DisposeVoiceNotePlayer();
    }

    private void DisposeVoiceNotePlayer()
    {
        _voiceNoteOutput?.Dispose();
        _voiceNoteOutput = null;
        _voiceNoteReader?.Dispose();
        _voiceNoteReader = null;
        IsVoiceNotePlaying = false;
    }

    private void StartGifAnimation(byte[] bytes)
    {
        if (!IsGif(bytes))
        {
            return;
        }

        try
        {
            DecodeGifFrames(bytes, _inlineFrames, _inlineFrameDelays);
            if (_inlineFrames.Count <= 1)
            {
                return;
            }

            InlineImage = _inlineFrames[0];
            _inlineTimer = new DispatcherTimer
            {
                Interval = _inlineFrameDelays[0]
            };
            _inlineTimer.Tick += (_, _) =>
            {
                _inlineFrameIndex = (_inlineFrameIndex + 1) % _inlineFrames.Count;
                InlineImage = _inlineFrames[_inlineFrameIndex];
                _inlineTimer.Interval = _inlineFrameDelays[_inlineFrameIndex];
            };
            _inlineTimer.Start();
        }
        catch
        {
            _inlineFrames.Clear();
            _inlineFrameDelays.Clear();
        }
    }

    private static Bitmap? CreateInlineImage(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static string CreateUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        return Path.Combine(directory, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}{extension}");
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

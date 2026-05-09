using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetBuddies.App.Services;
using NetBuddies.Core;

namespace NetBuddies.App.ViewModels;

public sealed partial class ScreenShareViewModel : ViewModelBase, IDisposable
{
    private readonly BuddyClient _client;
    private readonly ScreenShareSource? _source;
    private readonly bool _isSender;
    private readonly int _frameRate;
    private readonly int _jpegQuality;
    private readonly CancellationTokenSource _captureTokenSource = new();
    private long _receivedFrameVersion;

    public string SessionId { get; }
    public string BuddyName { get; }

    [ObservableProperty]
    private Bitmap? _currentFrame;

    [ObservableProperty]
    private string _statusText;

    public ScreenShareViewModel(
        BuddyClient client,
        string sessionId,
        string buddyName,
        ScreenShareSource? source,
        int qualityHeight,
        int frameRate,
        int jpegQuality,
        bool isSender)
    {
        _client = client;
        SessionId = sessionId;
        BuddyName = buddyName;
        QualityHeight = qualityHeight <= 720 ? 720 : 1080;
        _frameRate = Math.Clamp(frameRate, 5, 30);
        _jpegQuality = Math.Clamp(jpegQuality, 50, 90);
        _source = source;
        _isSender = isSender;
        StatusText = isSender
            ? $"Sharing {source?.Name ?? "screen"} with {buddyName} at {QualityHeight}p, {_frameRate} fps."
            : $"Watching {buddyName}'s screen share.";

        if (isSender && source is not null)
        {
            _ = RunCaptureLoopAsync(_captureTokenSource.Token);
        }
    }

    public int QualityHeight { get; }

    public void ReceiveFrame(NetBuddiesPacket packet)
    {
        if (string.IsNullOrWhiteSpace(packet.PayloadBase64))
        {
            return;
        }

        var version = Interlocked.Increment(ref _receivedFrameVersion);
        _ = DecodeLatestFrameAsync(packet.PayloadBase64, version);
    }

    public async Task StopAsync(bool notifyBuddy = true)
    {
        await _captureTokenSource.CancelAsync();
        if (notifyBuddy)
        {
            await _client.SendScreenShareEndAsync(BuddyName, SessionId);
        }

        StatusText = "Screen share ended.";
    }

    [RelayCommand]
    private async Task EndShareAsync()
    {
        await StopAsync();
    }

    private async Task RunCaptureLoopAsync(CancellationToken cancellationToken)
    {
        if (_source is null)
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            StatusText = "Screen sharing capture currently supports Windows clients.";
            return;
        }

        var frameDelay = TimeSpan.FromMilliseconds(1000.0 / _frameRate);
        while (!cancellationToken.IsCancellationRequested)
        {
            var startedAt = DateTimeOffset.UtcNow;

            try
            {
#pragma warning disable CA1416
                var frame = await Task.Run(
                    () => ScreenCaptureService.CaptureJpeg(_source, QualityHeight, _jpegQuality),
                    cancellationToken);
#pragma warning restore CA1416

                await _client.SendScreenShareFrameAsync(BuddyName, SessionId, frame);
                _ = DecodeLatestFrameAsync(Convert.ToBase64String(frame), Interlocked.Increment(ref _receivedFrameVersion));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    StatusText = $"Screen share stopped: {ex.Message}");
                return;
            }

            var elapsed = DateTimeOffset.UtcNow - startedAt;
            var remainingDelay = frameDelay - elapsed;
            if (remainingDelay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(remainingDelay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task DecodeLatestFrameAsync(string payloadBase64, long version)
    {
        try
        {
            var frame = await Task.Run(() =>
            {
                var bytes = Convert.FromBase64String(payloadBase64);
                return new Bitmap(new MemoryStream(bytes));
            }, _captureTokenSource.Token);

            if (Interlocked.Read(ref _receivedFrameVersion) != version)
            {
                frame.Dispose();
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                CurrentFrame?.Dispose();
                CurrentFrame = frame;
                StatusText = _isSender
                    ? $"Sharing {_source?.Name ?? "screen"} with {BuddyName} at {QualityHeight}p, {_frameRate} fps."
                    : $"Watching {BuddyName}'s screen share.";
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = "Could not show a screen share frame.");
        }
    }

    public void Dispose()
    {
        _captureTokenSource.Cancel();
        CurrentFrame?.Dispose();
        _captureTokenSource.Dispose();
    }
}

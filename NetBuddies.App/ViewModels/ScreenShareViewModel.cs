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
        try
        {
            var bytes = Convert.FromBase64String(packet.PayloadBase64);
            CurrentFrame = new Bitmap(new MemoryStream(bytes));
            StatusText = $"Watching {BuddyName}'s screen share.";
        }
        catch
        {
            StatusText = "Could not show a screen share frame.";
        }
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

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    CurrentFrame = new Bitmap(new MemoryStream(frame));
                });

                await _client.SendScreenShareFrameAsync(BuddyName, SessionId, frame);
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

    public void Dispose()
    {
        _captureTokenSource.Cancel();
        _captureTokenSource.Dispose();
    }
}

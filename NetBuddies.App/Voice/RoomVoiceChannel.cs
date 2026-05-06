using NAudio.Wave;
using NetBuddies.Core;

namespace NetBuddies.App.Voice;

public sealed class RoomVoiceChannel : IDisposable
{
    private static readonly WaveFormat VoiceFormat = new(16000, 16, 1);
    private readonly Func<byte[], int, Task> _sendAudioAsync;
    private WaveInEvent? _capture;
    private BufferedWaveProvider? _playbackBuffer;
    private WaveOutEvent? _playback;
    private bool _isDisposed;
    public event Action<double>? MicrophoneLevelChanged;

    public RoomVoiceChannel(BuddyClient client, string roomName)
        : this((buffer, byteCount) => client.SendVoiceAsync(roomName, buffer, byteCount))
    {
    }

    public RoomVoiceChannel(Func<byte[], int, Task> sendAudioAsync)
    {
        _sendAudioAsync = sendAudioAsync;
    }

    public static IReadOnlyList<(int DeviceNumber, string Name)> GetMicrophones()
    {
        var devices = new List<(int DeviceNumber, string Name)>();
        for (var index = 0; index < WaveInEvent.DeviceCount; index++)
        {
            var capabilities = WaveInEvent.GetCapabilities(index);
            devices.Add((index, capabilities.ProductName));
        }

        return devices;
    }

    public void Start(int deviceNumber)
    {
        if (_capture is not null)
        {
            return;
        }

        _playbackBuffer = new BufferedWaveProvider(VoiceFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true
        };
        _playback = new WaveOutEvent();
        _playback.Init(_playbackBuffer);
        _playback.Play();

        _capture = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = VoiceFormat,
            BufferMilliseconds = 50
        };
        _capture.DataAvailable += Capture_DataAvailable;
        _capture.StartRecording();
    }

    public void Receive(NetBuddiesPacket packet)
    {
        if (_playbackBuffer is null || string.IsNullOrWhiteSpace(packet.PayloadBase64))
        {
            return;
        }

        try
        {
            var bytes = Convert.FromBase64String(packet.PayloadBase64);
            _playbackBuffer.AddSamples(bytes, 0, bytes.Length);
        }
        catch
        {
        }
    }

    private void Capture_DataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        var buffer = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, buffer, 0, e.BytesRecorded);
        MicrophoneLevelChanged?.Invoke(CalculateLevel(buffer, buffer.Length));
        _ = _sendAudioAsync(buffer, buffer.Length);
    }

    private static double CalculateLevel(byte[] buffer, int byteCount)
    {
        if (byteCount == 0)
        {
            return 0;
        }

        double sumSquares = 0;
        var samples = byteCount / 2;
        for (var index = 0; index + 1 < byteCount; index += 2)
        {
            var sample = BitConverter.ToInt16(buffer, index) / 32768.0;
            sumSquares += sample * sample;
        }

        var rms = Math.Sqrt(sumSquares / Math.Max(samples, 1));
        return Math.Clamp(rms * 250, 0, 100);
    }

    public void Dispose()
    {
        _isDisposed = true;

        if (_capture is not null)
        {
            _capture.DataAvailable -= Capture_DataAvailable;
            _capture.StopRecording();
            _capture.Dispose();
            _capture = null;
        }

        _playback?.Stop();
        _playback?.Dispose();
        _playback = null;
        _playbackBuffer = null;
    }
}

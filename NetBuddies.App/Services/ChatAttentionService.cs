using System.Runtime.InteropServices;
using Avalonia.Controls;
using NAudio.Wave;

namespace NetBuddies.App.Services;

public static class ChatAttentionService
{
    private const uint FlashwStop = 0;
    private const uint FlashwAll = 3;
    private const uint FlashwTimerNoFg = 12;
    private static readonly List<IDisposable> ActiveSounds = [];

    public static void PlayMessageSound()
    {
        PlayTone([
            (880, 80),
            (1175, 120)
        ], volume: 0.22f);
    }

    public static void PlayNudgeSound()
    {
        PlayTone([
            (740, 55),
            (988, 55),
            (740, 55),
            (1318, 110)
        ], volume: 0.28f);
    }

    public static void StartFlashing(Window window)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var info = CreateFlashInfo(handle, FlashwAll | FlashwTimerNoFg);
        _ = FlashWindowEx(ref info);
    }

    public static void StopFlashing(Window window)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var info = CreateFlashInfo(handle, FlashwStop);
        _ = FlashWindowEx(ref info);
    }

    private static FlashWindowInfo CreateFlashInfo(IntPtr handle, uint flags)
    {
        return new FlashWindowInfo
        {
            Size = Convert.ToUInt32(Marshal.SizeOf<FlashWindowInfo>()),
            Hwnd = handle,
            Flags = flags,
            Count = uint.MaxValue,
            Timeout = 0
        };
    }

    private static void PlayTone((int Frequency, int DurationMs)[] notes, float volume)
    {
        var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
        var samples = new List<float>();

        foreach (var note in notes)
        {
            var sampleCount = waveFormat.SampleRate * note.DurationMs / 1000;
            for (var index = 0; index < sampleCount; index++)
            {
                var fade = Math.Min(1.0, Math.Min(index / 220.0, (sampleCount - index) / 220.0));
                var value = Math.Sin(2 * Math.PI * note.Frequency * index / waveFormat.SampleRate);
                samples.Add((float)(value * volume * fade));
            }

            var gapSamples = waveFormat.SampleRate / 80;
            for (var index = 0; index < gapSamples; index++)
            {
                samples.Add(0);
            }
        }

        var provider = new BufferedWaveProvider(waveFormat);
        var bytes = new byte[samples.Count * sizeof(float)];
        Buffer.BlockCopy(samples.ToArray(), 0, bytes, 0, bytes.Length);
        provider.AddSamples(bytes, 0, bytes.Length);

        var output = new WaveOutEvent();
        output.Init(provider);
        output.PlaybackStopped += (_, _) =>
        {
            output.Dispose();
            ActiveSounds.Remove(output);
        };
        ActiveSounds.Add(output);
        output.Play();
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashWindowInfo flashWindowInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public IntPtr Hwnd;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }
}

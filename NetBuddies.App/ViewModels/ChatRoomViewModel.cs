using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetBuddies.App.Voice;
using NetBuddies.Core;

namespace NetBuddies.App.ViewModels;

public partial class ChatRoomViewModel : ViewModelBase, IDisposable
{
    private readonly BuddyClient _client;
    private readonly Func<string, Bitmap?> _profileImageProvider;
    private RoomVoiceChannel? _voiceChannel;

    public ChatRoomViewModel(BuddyClient client, string roomName, Func<string, Bitmap?> profileImageProvider)
    {
        _client = client;
        _profileImageProvider = profileImageProvider;
        RoomName = roomName;
        Messages.Add(CreateMessage("Net Buddies", $"Welcome to {roomName}.", isEvent: true));
        LoadMicrophones();
    }

    public string RoomName { get; }
    public ObservableCollection<RoomMessageLineViewModel> Messages { get; } = [];
    public ObservableCollection<string> Participants { get; } = [];
    public ObservableCollection<string> VoiceParticipants { get; } = [];
    public ObservableCollection<MicrophoneDeviceViewModel> Microphones { get; } = [];

    [ObservableProperty]
    private string _draftMessage = "";

    [ObservableProperty]
    private MicrophoneDeviceViewModel? _selectedMicrophone;

    [ObservableProperty]
    private bool _isVoiceConnected;

    [ObservableProperty]
    private string _voiceStatus = "Voice channel idle";

    [ObservableProperty]
    private double _microphoneLevel;

    [ObservableProperty]
    private bool _echoCancellationEnabled = true;

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        var message = DraftMessage.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        DraftMessage = "";
        await _client.SendRoomAsync(RoomName, "Message", message);
    }

    [RelayCommand]
    private async Task JoinVoiceAsync()
    {
        if (IsVoiceConnected)
        {
            return;
        }

        if (SelectedMicrophone is null)
        {
            VoiceStatus = "Select a microphone first.";
            return;
        }

        try
        {
            _voiceChannel = new RoomVoiceChannel(_client, RoomName)
            {
                EchoCancellationEnabled = EchoCancellationEnabled
            };
            _voiceChannel.MicrophoneLevelChanged += level => Dispatcher.UIThread.Post(() => MicrophoneLevel = level);
            _voiceChannel.Start(SelectedMicrophone.DeviceNumber);
            IsVoiceConnected = true;
            VoiceStatus = $"Voice connected using {SelectedMicrophone.Name}.";
            await _client.SendRoomAsync(RoomName, "VoiceJoin");
        }
        catch (Exception ex)
        {
            _voiceChannel?.Dispose();
            _voiceChannel = null;
            IsVoiceConnected = false;
            VoiceStatus = $"Voice failed: {ex.Message}";
        }
    }

    partial void OnSelectedMicrophoneChanged(MicrophoneDeviceViewModel? value)
    {
        if (!IsVoiceConnected || _voiceChannel is null || value is null)
        {
            return;
        }

        try
        {
            _voiceChannel.ChangeMicrophone(value.DeviceNumber);
            VoiceStatus = $"Voice switched to {value.Name}.";
        }
        catch (Exception ex)
        {
            VoiceStatus = $"Could not change microphone: {ex.Message}";
        }
    }

    partial void OnEchoCancellationEnabledChanged(bool value)
    {
        if (_voiceChannel is not null)
        {
            _voiceChannel.EchoCancellationEnabled = value;
        }
    }

    [RelayCommand]
    private async Task LeaveVoiceAsync()
    {
        await LeaveVoiceInternalAsync();
    }

    public void ReceiveRoomPacket(NetBuddiesPacket packet)
    {
        switch (packet.RoomAction)
        {
            case "Message":
                Messages.Add(CreateMessage(packet.From, packet.Text));
                break;
            case "System":
                Messages.Add(CreateMessage("Net Buddies", packet.Text, isEvent: true));
                break;
            case "Presence":
                Replace(Participants, packet.Users);
                Replace(VoiceParticipants, packet.Text.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                break;
        }
    }

    public void ReceiveVoice(NetBuddiesPacket packet)
    {
        if (IsVoiceConnected)
        {
            _voiceChannel?.Receive(packet);
        }
    }

    public async Task LeaveRoomAsync()
    {
        await LeaveVoiceInternalAsync();
        try
        {
            await _client.SendRoomAsync(RoomName, "Leave");
        }
        catch
        {
        }
    }

    private async Task LeaveVoiceInternalAsync()
    {
        if (!IsVoiceConnected)
        {
            return;
        }

        _voiceChannel?.Dispose();
        _voiceChannel = null;
        MicrophoneLevel = 0;
        IsVoiceConnected = false;
        VoiceStatus = "Voice channel idle";
        try
        {
            await _client.SendRoomAsync(RoomName, "VoiceLeave");
        }
        catch
        {
        }
    }

    private void LoadMicrophones()
    {
        Microphones.Clear();
        foreach (var device in RoomVoiceChannel.GetMicrophones())
        {
            Microphones.Add(new MicrophoneDeviceViewModel(device.DeviceNumber, device.Name));
        }

        SelectedMicrophone = Microphones.FirstOrDefault();
        if (SelectedMicrophone is null)
        {
            VoiceStatus = "No microphone detected.";
        }
    }

    public RoomMessageLineViewModel CreateMessage(string sender, string body, bool isEvent = false)
    {
        var avatar = sender.Equals("Net Buddies", StringComparison.OrdinalIgnoreCase)
            ? null
            : _profileImageProvider(sender);
        return new RoomMessageLineViewModel(sender, body, isEvent, avatar);
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> values)
    {
        collection.Clear();
        foreach (var value in values)
        {
            collection.Add(value);
        }
    }

    public void Dispose()
    {
        _voiceChannel?.Dispose();
        _voiceChannel = null;
    }
}

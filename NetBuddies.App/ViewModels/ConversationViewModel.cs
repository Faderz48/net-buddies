using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NAudio.Wave;
using NetBuddies.App.Services;
using NetBuddies.App.Voice;
using NetBuddies.Core;

namespace NetBuddies.App.ViewModels;

public enum ChatAttentionKind
{
    Message,
    Nudge
}

public partial class ConversationViewModel : ViewModelBase, IDisposable
{
    private readonly BuddyClient _client;
    private readonly Func<string, Bitmap?> _profileImageProvider;
    private readonly Dictionary<string, string> _pendingOutgoingFiles = new(StringComparer.OrdinalIgnoreCase);
    private RoomVoiceChannel? _privateVoiceChannel;
    private WaveInEvent? _voiceNoteCapture;
    private WaveFileWriter? _voiceNoteWriter;
    private MemoryStream? _voiceNoteStream;
    private DateTimeOffset _voiceNoteStartedAt;
    private DateTimeOffset _lastTypingSent = DateTimeOffset.MinValue;
    private long _typingVersion;

    public string BuddyName { get; }
    public ObservableCollection<MessageLineViewModel> Messages { get; } = [];
    public ObservableCollection<MicrophoneDeviceViewModel> Microphones { get; } = [];
    public ObservableCollection<ActivityRequestViewModel> PendingRequests { get; } = [];
    public event Action? NudgeReceived;
    public event Action<ConversationViewModel, NetBuddiesGameType>? GameRequested;
    public event Action<ConversationViewModel, string>? RoomInviteRequested;
    public event Action<ConversationViewModel, NetBuddiesPacket>? GameAccepted;
    public event Action<ConversationViewModel, NetBuddiesPacket>? RoomInviteAccepted;
    public event Action<ConversationViewModel, NetBuddiesPacket>? ScreenShareAccepted;
    public event Action<ConversationViewModel, ScreenShareSource, int, int, int>? ScreenShareInviteRequested;
    public event Action<ChatAttentionKind>? AttentionRequested;
    public event Action<string>? DownloadSaved;

    public ChatAttentionKind? PendingAttentionKind { get; private set; }
    public bool HasPendingAttention => PendingAttentionKind is not null;

    [ObservableProperty]
    private string _draftMessage = "";

    [ObservableProperty]
    private string _typingStatus = "";

    [ObservableProperty]
    private string _roomInviteName = "";

    [ObservableProperty]
    private MicrophoneDeviceViewModel? _selectedMicrophone;

    [ObservableProperty]
    private bool _isPrivateVoiceConnected;

    [ObservableProperty]
    private string _privateVoiceStatus = "Private voice idle";

    [ObservableProperty]
    private double _microphoneLevel;

    [ObservableProperty]
    private bool _isMicrophoneActive;

    [ObservableProperty]
    private Bitmap? _buddyProfileImage;

    [ObservableProperty]
    private bool _isRecordingVoiceNote;

    [ObservableProperty]
    private string _voiceNoteButtonText = "VoiceNote";

    [ObservableProperty]
    private string _voiceNoteStatus = "";

    [ObservableProperty]
    private bool _echoCancellationEnabled = true;

    public ConversationViewModel(BuddyClient client, string buddyName, Func<string, Bitmap?> profileImageProvider)
    {
        _client = client;
        _profileImageProvider = profileImageProvider;
        BuddyName = buddyName;
        BuddyProfileImage = GetAvatar(BuddyName);
        LoadMicrophones();
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        var text = DraftMessage.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Messages.Add(CreateMessage("Me", text, isMine: true));
        DraftMessage = "";
        await _client.SendTypingAsync(BuddyName, isTyping: false);
        await _client.SendChatAsync(BuddyName, text);
    }

    [RelayCommand]
    private async Task SendNudgeAsync()
    {
        Messages.Add(CreateMessage("Net Buddies", $"You nudged {BuddyName}.", isMine: true, isEvent: true));
        await _client.SendNudgeAsync(BuddyName);
    }

    [RelayCommand]
    private async Task ToggleVoiceNoteAsync()
    {
        if (IsRecordingVoiceNote)
        {
            await StopAndSendVoiceNoteAsync();
            return;
        }

        StartVoiceNote();
    }

    public async Task SendFileAsync(string path)
    {
        var transferId = Guid.NewGuid().ToString("N");
        var fileName = Path.GetFileName(path);
        _pendingOutgoingFiles[transferId] = path;
        Messages.Add(CreateMessage("Me", $"File offer sent: {fileName}", isMine: true, isEvent: true));
        await _client.SendFileOfferAsync(BuddyName, transferId, path);
    }

    public async Task SendImageAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        var fileName = Path.GetFileName(path);
        Messages.Add(CreateMessage("Me", fileName, isMine: true, inlineImageBytes: bytes));
        await _client.SendImageAsync(BuddyName, path);
    }

    public async Task SendGifAsync(string title, byte[] gifBytes)
    {
        var label = string.IsNullOrWhiteSpace(title) ? "GIPHY GIF" : title.Trim();
        Messages.Add(CreateMessage("Me", label, isMine: true, inlineImageBytes: gifBytes));
        await _client.SendGifAsync(BuddyName, label, gifBytes);
    }

    [RelayCommand]
    private void StartTicTacToe()
    {
        GameRequested?.Invoke(this, NetBuddiesGameType.TicTacToe);
    }

    [RelayCommand]
    private void StartCheckers()
    {
        GameRequested?.Invoke(this, NetBuddiesGameType.Checkers);
    }

    [RelayCommand]
    private void StartMinesweeperFlags()
    {
        GameRequested?.Invoke(this, NetBuddiesGameType.MinesweeperFlags);
    }

    [RelayCommand]
    private void InviteToRoom()
    {
        var roomName = string.IsNullOrWhiteSpace(RoomInviteName)
            ? $"Room with {BuddyName}"
            : RoomInviteName.Trim();
        RoomInviteName = "";
        RoomInviteRequested?.Invoke(this, roomName);
    }

    [RelayCommand]
    private async Task SendPrivateVoiceInviteAsync()
    {
        if (IsPrivateVoiceConnected)
        {
            PrivateVoiceStatus = "Private voice is already connected.";
            return;
        }

        if (SelectedMicrophone is null)
        {
            PrivateVoiceStatus = "Select a microphone before sending a voice invite.";
            return;
        }

        await _client.SendPrivateVoiceInviteAsync(BuddyName);
        PrivateVoiceStatus = $"Voice invite sent to {BuddyName}.";
        Messages.Add(CreateMessage("Net Buddies", $"Voice chat invite sent to {BuddyName}.", isMine: true, isEvent: true));
    }

    [RelayCommand]
    private async Task LeavePrivateVoiceAsync()
    {
        if (IsPrivateVoiceConnected)
        {
            await _client.SendPrivateVoiceEndAsync(BuddyName);
        }

        StopPrivateVoice();
        Messages.Add(CreateMessage("Net Buddies", $"Private voice ended with {BuddyName}.", isMine: true, isEvent: true));
    }

    private bool StartPrivateVoice()
    {
        if (IsPrivateVoiceConnected)
        {
            return true;
        }

        if (SelectedMicrophone is null)
        {
            PrivateVoiceStatus = "Select a microphone first.";
            return false;
        }

        try
        {
            _privateVoiceChannel = new RoomVoiceChannel((buffer, byteCount) =>
                _client.SendPrivateVoiceAsync(BuddyName, buffer, byteCount))
            {
                EchoCancellationEnabled = EchoCancellationEnabled
            };
            _privateVoiceChannel.MicrophoneLevelChanged += level => Dispatcher.UIThread.Post(() => MicrophoneLevel = level);
            _privateVoiceChannel.Start(SelectedMicrophone.DeviceNumber);
            IsPrivateVoiceConnected = true;
            IsMicrophoneActive = true;
            PrivateVoiceStatus = $"Private voice connected using {SelectedMicrophone.Name}.";
            Messages.Add(CreateMessage("Net Buddies", $"Private voice started with {BuddyName}.", isMine: true, isEvent: true));
            return true;
        }
        catch (Exception ex)
        {
            _privateVoiceChannel?.Dispose();
            _privateVoiceChannel = null;
            IsPrivateVoiceConnected = false;
            PrivateVoiceStatus = $"Private voice failed: {ex.Message}";
            return false;
        }
    }

    public void ReceivePrivateVoice(NetBuddiesPacket packet)
    {
        switch (packet.Text)
        {
            case "PrivateVoiceInvite":
                ReceivePrivateVoiceInvite(packet);
                return;
            case "PrivateVoiceAccept":
                if (StartPrivateVoice())
                {
                    PrivateVoiceStatus = $"Private voice connected with {BuddyName}.";
                }
                return;
            case "PrivateVoiceDecline":
                PrivateVoiceStatus = $"{BuddyName} declined the voice invite.";
                Messages.Add(CreateMessage("Net Buddies", $"{BuddyName} declined the voice invite.", isMine: false, isEvent: true));
                return;
            case "PrivateVoiceEnd":
                StopPrivateVoice();
                Messages.Add(CreateMessage("Net Buddies", $"{BuddyName} ended private voice.", isMine: false, isEvent: true));
                return;
        }

        if (!IsPrivateVoiceConnected)
        {
            return;
        }

        _privateVoiceChannel?.Receive(packet);
    }

    public async Task SendScreenShareInviteAsync(string sessionId, string sourceName, int qualityHeight, int frameRate, int jpegQuality)
    {
        await _client.SendScreenShareInviteAsync(BuddyName, sessionId, sourceName, qualityHeight, frameRate, jpegQuality);
        Messages.Add(CreateMessage("Net Buddies", $"Screen share invite sent to {BuddyName} ({qualityHeight}p, {frameRate} fps).", isMine: true, isEvent: true));
    }

    public void RequestScreenShare(ScreenShareSource source, int qualityHeight, int frameRate, int jpegQuality)
    {
        ScreenShareInviteRequested?.Invoke(this, source, qualityHeight, frameRate, jpegQuality);
    }

    public void ReceiveScreenShare(NetBuddiesPacket packet)
    {
        switch (packet.FileAction)
        {
            case "Invite":
                AddRequest(
                    "Screen share invite",
                    $"{packet.From} wants to share {packet.Text} at {packet.FileName}p.",
                    async () =>
                    {
                        await _client.SendScreenShareResponseAsync(packet.From, packet.TransferId, accepted: true);
                        ScreenShareAccepted?.Invoke(this, packet);
                    },
                    async () =>
                    {
                        await _client.SendScreenShareResponseAsync(packet.From, packet.TransferId, accepted: false);
                        Messages.Add(CreateMessage("Net Buddies", $"Declined screen share from {packet.From}.", isMine: true, isEvent: true));
                    });
                RequestAttention(ChatAttentionKind.Message);
                return;
            case "Accept":
                ScreenShareAccepted?.Invoke(this, packet);
                Messages.Add(CreateMessage("Net Buddies", $"{packet.From} accepted your screen share.", isMine: false, isEvent: true));
                return;
            case "Decline":
                Messages.Add(CreateMessage("Net Buddies", $"{packet.From} declined your screen share.", isMine: false, isEvent: true));
                return;
            case "End":
                Messages.Add(CreateMessage("Net Buddies", $"{packet.From} ended screen share.", isMine: false, isEvent: true));
                return;
        }
    }

    private void ReceivePrivateVoiceInvite(NetBuddiesPacket packet)
    {
        AddRequest(
            "Voice chat invite",
            $"{packet.From} wants to start a private voice chat.",
            async () =>
            {
                if (!StartPrivateVoice())
                {
                    return;
                }

                await _client.SendPrivateVoiceResponseAsync(packet.From, accepted: true);
                PrivateVoiceStatus = $"Private voice connected with {packet.From}.";
            },
            async () =>
            {
                await _client.SendPrivateVoiceResponseAsync(packet.From, accepted: false);
                PrivateVoiceStatus = $"Declined voice invite from {packet.From}.";
                Messages.Add(CreateMessage("Net Buddies", $"Declined voice invite from {packet.From}.", isMine: true, isEvent: true));
            });
    }

    public void ReceiveRoomInvite(NetBuddiesPacket packet)
    {
        AddRequest(
            "Chat room invite",
            $"{packet.From} invited you to {packet.RoomName}.",
            async () =>
            {
                await _client.SendRoomInviteResponseAsync(packet.From, packet.RoomName, accepted: true);
                RoomInviteAccepted?.Invoke(this, packet);
            },
            async () =>
            {
                await _client.SendRoomInviteResponseAsync(packet.From, packet.RoomName, accepted: false);
                Messages.Add(CreateMessage("Net Buddies", $"Declined room invite: {packet.RoomName}", isMine: true, isEvent: true));
            });
    }

    public void ReceiveRoomInviteResponse(NetBuddiesPacket packet)
    {
        Messages.Add(CreateMessage("Net Buddies", packet.Text, isMine: false, isEvent: true));
    }

    public void ReceiveGameInvite(NetBuddiesPacket packet, string gameName)
    {
        AddRequest(
            "Game request",
            $"{packet.From} wants to play {gameName}.",
            () =>
            {
                GameAccepted?.Invoke(this, packet);
                return Task.CompletedTask;
            },
            async () =>
            {
                await _client.SendGameAsync(packet.From, packet.GameId, packet.GameType, "Decline");
                Messages.Add(CreateMessage("Net Buddies", $"Declined {gameName}.", isMine: true, isEvent: true));
            });
    }

    public void ReceiveGameDecline(NetBuddiesPacket packet, string gameName)
    {
        Messages.Add(CreateMessage("Net Buddies", $"{packet.From} declined {gameName}.", isMine: false, isEvent: true));
    }

    public void ReceiveChat(NetBuddiesPacket packet)
    {
        TypingStatus = "";
        Messages.Add(CreateMessage(packet.From, packet.Text, isMine: false));
        RequestAttention(ChatAttentionKind.Message);
    }

    public void ReceiveNudge(NetBuddiesPacket packet)
    {
        Messages.Add(CreateMessage(packet.From, $"{packet.From} sent a nudge!", isMine: false, isEvent: true));
        RequestAttention(ChatAttentionKind.Nudge);
        NudgeReceived?.Invoke();
    }

    public ChatAttentionKind? ConsumePendingAttention()
    {
        var kind = PendingAttentionKind;
        PendingAttentionKind = null;
        OnPropertyChanged(nameof(HasPendingAttention));
        return kind;
    }

    private void RequestAttention(ChatAttentionKind kind)
    {
        PendingAttentionKind = kind;
        OnPropertyChanged(nameof(HasPendingAttention));
        AttentionRequested?.Invoke(kind);
    }

    public void ReceiveTyping(NetBuddiesPacket packet)
    {
        if (packet.Text == "typing")
        {
            TypingStatus = $"{packet.From} is typing...";
            var version = Interlocked.Increment(ref _typingVersion);
            _ = ClearTypingLaterAsync(version);
        }
        else
        {
            TypingStatus = "";
        }
    }

    public string? ReceiveFile(NetBuddiesPacket packet)
    {
        if (packet.FileAction is "ImageData" or "GifData")
        {
            AddInlineMediaMessage(packet);
            RequestAttention(ChatAttentionKind.Message);
            return null;
        }

        if (string.IsNullOrWhiteSpace(packet.FileAction) && !string.IsNullOrWhiteSpace(packet.PayloadBase64))
        {
            return SaveIncomingFile(packet);
        }

        switch (packet.FileAction)
        {
            case "VoiceNote":
                return SaveIncomingVoiceNote(packet);
            case "Offer":
                AddRequest(
                    "File transfer",
                    $"{packet.From} wants to send {packet.FileName} ({packet.FileSize:N0} bytes).",
                    async () =>
                    {
                        await _client.SendFileAcceptAsync(packet.From, packet.TransferId);
                        Messages.Add(CreateMessage("Net Buddies", $"Accepted file: {packet.FileName}", isMine: true, isEvent: true));
                    },
                    async () =>
                    {
                        await _client.SendFileDeclineAsync(packet.From, packet.TransferId, packet.FileName);
                        Messages.Add(CreateMessage("Net Buddies", $"Declined file: {packet.FileName}", isMine: true, isEvent: true));
                    });
                return null;
            case "Accept":
                if (_pendingOutgoingFiles.TryGetValue(packet.TransferId, out var path))
                {
                    _pendingOutgoingFiles.Remove(packet.TransferId);
                    Messages.Add(CreateMessage("Net Buddies", $"{packet.From} accepted {Path.GetFileName(path)}. Sending now.", isMine: true, isEvent: true));
                    _ = _client.SendFileDataAsync(packet.From, packet.TransferId, path);
                }

                return null;
            case "Decline":
                _pendingOutgoingFiles.Remove(packet.TransferId);
                Messages.Add(CreateMessage("Net Buddies", $"{packet.From} declined file: {packet.FileName}", isMine: false, isEvent: true));
                return null;
            case "Data":
                return SaveIncomingFile(packet);
            default:
                return null;
        }
    }

    private string SaveIncomingFile(NetBuddiesPacket packet)
    {
        var safeFileName = string.Join("_", packet.FileName.Split(Path.GetInvalidFileNameChars()));
        var downloadsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "NetBuddiesDownloads");
        Directory.CreateDirectory(downloadsPath);

        var fullPath = Path.Combine(downloadsPath, safeFileName);
        var bytes = Convert.FromBase64String(packet.PayloadBase64);
        File.WriteAllBytes(fullPath, bytes);

        Messages.Add(CreateMessage(
            packet.From,
            $"Received file: {packet.FileName} ({packet.FileSize:N0} bytes)",
            isMine: false,
            isEvent: true));
        DownloadSaved?.Invoke(fullPath);
        return fullPath;
    }

    private void AddInlineMediaMessage(NetBuddiesPacket packet)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(packet.PayloadBase64);
        }
        catch
        {
            Messages.Add(CreateMessage("Net Buddies", $"Could not show media from {packet.From}.", isMine: false, isEvent: true));
            return;
        }

        var label = packet.FileAction == "GifData"
            ? string.IsNullOrWhiteSpace(packet.Text) ? "GIPHY GIF" : packet.Text
            : packet.FileName;
        Messages.Add(CreateMessage(packet.From, label, isMine: false, inlineImageBytes: bytes));
        SaveIncomingInlineMedia(packet, bytes);
    }

    private void SaveIncomingInlineMedia(NetBuddiesPacket packet, byte[] bytes)
    {
        var downloadsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "NetBuddiesDownloads",
            "Images");
        Directory.CreateDirectory(downloadsPath);

        var fallbackName = packet.FileAction == "GifData"
            ? $"giphy-{DateTime.Now:yyyyMMdd-HHmmss}.gif"
            : $"image-{DateTime.Now:yyyyMMdd-HHmmss}.png";
        var fileName = string.IsNullOrWhiteSpace(packet.FileName) ? fallbackName : packet.FileName;
        var safeFileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
        var fullPath = Path.Combine(downloadsPath, safeFileName);
        File.WriteAllBytes(fullPath, bytes);
        DownloadSaved?.Invoke(fullPath);
    }

    private string SaveIncomingVoiceNote(NetBuddiesPacket packet)
    {
        var downloadsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "NetBuddiesDownloads",
            "VoiceNotes");
        Directory.CreateDirectory(downloadsPath);

        var safeFileName = string.Join("_", packet.FileName.Split(Path.GetInvalidFileNameChars()));
        var fullPath = Path.Combine(downloadsPath, safeFileName);
        var bytes = Convert.FromBase64String(packet.PayloadBase64);
        File.WriteAllBytes(fullPath, bytes);

        var seconds = string.IsNullOrWhiteSpace(packet.Text) ? "" : $" ({packet.Text}s)";
        Messages.Add(CreateMessage(
            packet.From,
            $"VoiceNote received{seconds}: {safeFileName}",
            isMine: false,
            isEvent: true,
            voiceNotePath: fullPath));
        DownloadSaved?.Invoke(fullPath);
        return fullPath;
    }

    private void StartVoiceNote()
    {
        if (SelectedMicrophone is null)
        {
            VoiceNoteStatus = "Select a microphone first.";
            return;
        }

        try
        {
            _voiceNoteStream = new MemoryStream();
            _voiceNoteWriter = new WaveFileWriter(_voiceNoteStream, new WaveFormat(16000, 16, 1));
            _voiceNoteCapture = new WaveInEvent
            {
                DeviceNumber = SelectedMicrophone.DeviceNumber,
                WaveFormat = _voiceNoteWriter.WaveFormat,
                BufferMilliseconds = 50
            };
            _voiceNoteCapture.DataAvailable += VoiceNoteCapture_DataAvailable;
            _voiceNoteStartedAt = DateTimeOffset.UtcNow;
            _voiceNoteCapture.StartRecording();
            IsRecordingVoiceNote = true;
            IsMicrophoneActive = true;
            VoiceNoteButtonText = "Send VoiceNote";
            VoiceNoteStatus = $"Recording with {SelectedMicrophone.Name}.";
        }
        catch (Exception ex)
        {
            CleanupVoiceNoteRecorder();
            VoiceNoteStatus = $"VoiceNote failed: {ex.Message}";
        }
    }

    private async Task StopAndSendVoiceNoteAsync()
    {
        var duration = DateTimeOffset.UtcNow - _voiceNoteStartedAt;
        byte[] wavBytes;

        try
        {
            _voiceNoteCapture?.StopRecording();
            _voiceNoteWriter?.Flush();
            _voiceNoteWriter?.Dispose();
            _voiceNoteWriter = null;
            wavBytes = _voiceNoteStream?.ToArray() ?? [];
        }
        finally
        {
            CleanupVoiceNoteRecorder();
        }

        if (wavBytes.Length == 0 || duration < TimeSpan.FromMilliseconds(300))
        {
            VoiceNoteStatus = "VoiceNote was too short.";
            return;
        }

        await _client.SendVoiceNoteAsync(BuddyName, wavBytes, duration);
        Messages.Add(CreateMessage("Me", $"VoiceNote sent ({duration.TotalSeconds:0}s).", isMine: true, isEvent: true));
        VoiceNoteStatus = $"VoiceNote sent ({duration.TotalSeconds:0}s).";
    }

    private void VoiceNoteCapture_DataAvailable(object? sender, WaveInEventArgs e)
    {
        _voiceNoteWriter?.Write(e.Buffer, 0, e.BytesRecorded);
        MicrophoneLevel = CalculateLevel(e.Buffer, e.BytesRecorded);
    }

    private void CleanupVoiceNoteRecorder()
    {
        if (_voiceNoteCapture is not null)
        {
            _voiceNoteCapture.DataAvailable -= VoiceNoteCapture_DataAvailable;
            _voiceNoteCapture.Dispose();
            _voiceNoteCapture = null;
        }

        _voiceNoteWriter?.Dispose();
        _voiceNoteWriter = null;
        _voiceNoteStream?.Dispose();
        _voiceNoteStream = null;
        IsRecordingVoiceNote = false;
        VoiceNoteButtonText = "VoiceNote";
        if (!IsPrivateVoiceConnected)
        {
            IsMicrophoneActive = false;
            MicrophoneLevel = 0;
        }
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

    public void RefreshProfileImages()
    {
        BuddyProfileImage = GetAvatar(BuddyName);
    }

    public MessageLineViewModel CreateMessage(
        string sender,
        string body,
        bool isMine,
        bool isEvent = false,
        string voiceNotePath = "",
        byte[]? inlineImageBytes = null)
    {
        var avatarName = isMine && sender.Equals("Me", StringComparison.OrdinalIgnoreCase)
            ? _client.DisplayName
            : sender;
        return new MessageLineViewModel(sender, body, isMine, isEvent, GetAvatar(avatarName), voiceNotePath, inlineImageBytes);
    }

    private Bitmap? GetAvatar(string sender)
    {
        if (sender.Equals("Net Buddies", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return _profileImageProvider(sender);
    }

    private void AddRequest(string title, string detail, Func<Task> acceptAsync, Func<Task> declineAsync)
    {
        PendingRequests.Insert(0, new ActivityRequestViewModel(title, detail, acceptAsync, declineAsync, request => PendingRequests.Remove(request)));

        while (PendingRequests.Count > 4)
        {
            PendingRequests.RemoveAt(PendingRequests.Count - 1);
        }
    }

    partial void OnDraftMessageChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _ = _client.SendTypingAsync(BuddyName, isTyping: false);
            return;
        }

        if (DateTimeOffset.UtcNow - _lastTypingSent < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _lastTypingSent = DateTimeOffset.UtcNow;
        _ = _client.SendTypingAsync(BuddyName, isTyping: true);
    }

    private async Task ClearTypingLaterAsync(long version)
    {
        await Task.Delay(TimeSpan.FromSeconds(4));
        if (Interlocked.Read(ref _typingVersion) == version)
        {
            TypingStatus = "";
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
            PrivateVoiceStatus = "No microphone detected.";
        }
    }

    partial void OnSelectedMicrophoneChanged(MicrophoneDeviceViewModel? value)
    {
        if (!IsPrivateVoiceConnected || _privateVoiceChannel is null || value is null)
        {
            return;
        }

        try
        {
            _privateVoiceChannel.ChangeMicrophone(value.DeviceNumber);
            PrivateVoiceStatus = $"Private voice switched to {value.Name}.";
        }
        catch (Exception ex)
        {
            PrivateVoiceStatus = $"Could not change microphone: {ex.Message}";
        }
    }

    partial void OnEchoCancellationEnabledChanged(bool value)
    {
        if (_privateVoiceChannel is not null)
        {
            _privateVoiceChannel.EchoCancellationEnabled = value;
        }
    }

    private void StopPrivateVoice()
    {
        _privateVoiceChannel?.Dispose();
        _privateVoiceChannel = null;
        IsPrivateVoiceConnected = false;
        if (!IsRecordingVoiceNote)
        {
            IsMicrophoneActive = false;
            MicrophoneLevel = 0;
        }
        PrivateVoiceStatus = "Private voice idle";
    }

    public void Dispose()
    {
        CleanupVoiceNoteRecorder();
        StopPrivateVoice();
    }
}

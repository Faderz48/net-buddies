using System.Collections.ObjectModel;
using System.Security.Cryptography.X509Certificates;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetBuddies.App.Services;
using NetBuddies.Core;

namespace NetBuddies.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly Dictionary<string, ConversationViewModel> _conversations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GameSessionViewModel> _gameSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ChatRoomViewModel> _roomSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ScreenShareViewModel> _screenShares = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ScreenShareSource> _pendingScreenShareSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _pendingScreenShareQualities = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _pendingScreenShareFrameRates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _pendingScreenShareJpegQualities = new(StringComparer.OrdinalIgnoreCase);
    private BuddyServer? _server;
    private BuddyClient? _client;
    private bool _isLoadingProfile;
    private string _presenceSignature = "";

    public event Action<ConversationViewModel>? OpenConversationRequested;
    public event Action<GameSessionViewModel>? OpenGameRequested;
    public event Action<ChatRoomViewModel>? OpenRoomRequested;
    public event Action<ScreenShareViewModel>? OpenScreenShareRequested;
    public event Action? OpenBuddyListRequested;

    public ObservableCollection<BuddyViewModel> Buddies { get; } = [];
    public ObservableCollection<ChatRoomListItemViewModel> Rooms { get; } = [];
    public ObservableCollection<string> ActivityLog { get; } = [];
    public IReadOnlyList<string> StatusChoices { get; } = ["Online", "Away", "Busy", "Invisible"];

    [ObservableProperty]
    private string _displayName = $"Buddy{Random.Shared.Next(100, 999)}";

    [ObservableProperty]
    private string _hostAddress = "127.0.0.1";

    [ObservableProperty]
    private int _port = 5050;

    [ObservableProperty]
    private BuddyViewModel? _selectedBuddy;

    [ObservableProperty]
    private ChatRoomListItemViewModel? _selectedRoom;

    [ObservableProperty]
    private string _statusText = "Offline";

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _personalMessage = "Stay connected. Always.";

    [ObservableProperty]
    private string _selectedStatus = "Online";

    [ObservableProperty]
    private string _profileImageBase64 = "";

    [ObservableProperty]
    private Bitmap? _profileImage;

    [ObservableProperty]
    private string _onlineHeading = "Online (0)";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isHosting;

    [ObservableProperty]
    private string _newRoomName = "";

    [ObservableProperty]
    private bool _useSecureTls;

    [ObservableProperty]
    private bool _trustSelfSignedCertificate = true;

    [ObservableProperty]
    private string _serverInviteCode = "";

    [ObservableProperty]
    private string _certificatePath = "";

    [ObservableProperty]
    private string _certificatePassword = "";

    [ObservableProperty]
    private string _tlsStatus = "TLS is optional for private tunnels, recommended for direct hosting.";

    private BuddyProfile[] _onlineProfiles = [];

    public MainWindowViewModel()
    {
        LoadProfile();
    }

    [RelayCommand]
    private async Task StartHostingAsync()
    {
        if (_server is not null)
        {
            AddActivity($"Server is already running on port {Port}.");
            return;
        }

        _server = new BuddyServer();
        _server.StatusChanged += message => Dispatcher.UIThread.Post(() => AddActivity(message));
        try
        {
            await _server.StartAsync(Port, CreateServerOptions());
            IsHosting = true;
            StatusText = UseSecureTls
                ? $"Secure server running on port {Port}"
                : $"Server running on port {Port}";
            AddActivity("Server operators can leave this running while buddies join.");
        }
        catch (Exception ex)
        {
            await _server.DisposeAsync();
            _server = null;
            IsHosting = false;
            StatusText = $"Could not start server on port {Port}.";
            AddActivity($"{StatusText} {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task JoinLocalServerAsync()
    {
        HostAddress = "127.0.0.1";
        await JoinAsync();
    }

    [RelayCommand]
    private async Task JoinAsync()
    {
        if (_client is not null)
        {
            await CloseRoomsAsync();
            await _client.DisconnectAsync();
        }

        Buddies.Clear();
        Rooms.Clear();
        SearchText = "";
        _onlineProfiles = [];
        _presenceSignature = "";
        OnlineHeading = "Online (0)";
        _client = new BuddyClient();
        WireClientEvents(_client);
        StatusText = $"Connecting to {HostAddress}:{Port}...";
        try
        {
            SaveProfile();
            await _client.ConnectAsync(
                HostAddress,
                Port,
                DisplayName,
                PersonalMessage,
                SelectedStatus,
                ProfileImageBase64,
                UseSecureTls,
                TrustSelfSignedCertificate,
                ServerInviteCode);
            IsConnected = true;
            StatusText = $"{SelectedStatus} as {DisplayName}";
            AddActivity($"Joined {HostAddress}:{Port}.");
            SaveProfile(includeConnectionSettings: true);
            OpenBuddyListRequested?.Invoke();
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusText = $"Could not connect: {ex.Message}";
            AddActivity(StatusText);
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (_client is not null)
        {
            await CloseRoomsAsync();
            await _client.DisconnectAsync();
            _client = null;
        }

        Buddies.Clear();
        Rooms.Clear();
        _onlineProfiles = [];
        _presenceSignature = "";
        OnlineHeading = "Online (0)";
        IsConnected = false;
        StatusText = IsHosting ? "Hosting, not joined" : "Offline";
        AddActivity("Disconnected from chat.");
    }

    [RelayCommand]
    private async Task StopHostingAsync()
    {
        if (_server is not null)
        {
            await _server.StopAsync();
            await _server.DisposeAsync();
            _server = null;
        }

        IsHosting = false;
        StatusText = IsConnected ? StatusText : "Offline";
    }

    [RelayCommand]
    private void OpenChat(BuddyViewModel? buddy)
    {
        if (buddy is null || _client is null)
        {
            return;
        }

        OpenConversationRequested?.Invoke(GetConversation(buddy.Name));
    }

    [RelayCommand]
    private async Task SendNudgeToAsync(BuddyViewModel? buddy)
    {
        if (buddy is null || _client is null)
        {
            return;
        }

        var conversation = GetConversation(buddy.Name);
        OpenConversationRequested?.Invoke(conversation);
        await _client.SendNudgeAsync(buddy.Name);
        conversation.Messages.Add(conversation.CreateMessage(
            "Net Buddies",
            $"You nudged {buddy.Name}.",
            isMine: true,
            isEvent: true));
    }

    public async Task SendFileToAsync(BuddyViewModel? buddy, string path)
    {
        if (buddy is null || _client is null)
        {
            return;
        }

        var conversation = GetConversation(buddy.Name);
        OpenConversationRequested?.Invoke(conversation);
        await conversation.SendFileAsync(path);
    }

    public Task StartGameAsync(BuddyViewModel? buddy, NetBuddiesGameType gameType)
    {
        if (buddy is null || _client is null)
        {
            return Task.CompletedTask;
        }

        var session = new GameSessionViewModel(
            _client,
            Guid.NewGuid().ToString("N"),
            gameType,
            buddy.Name,
            isHost: true);
        _gameSessions[session.GameId] = session;
        var conversation = GetConversation(buddy.Name);
        conversation.Messages.Add(conversation.CreateMessage(
            "Net Buddies",
            $"{GameDisplayName(gameType)} invite sent. Waiting for {buddy.Name} to accept.",
            isMine: true,
            isEvent: true));
        return session.SendInviteAsync();
    }

    public async Task CreateChatRoomAsync()
    {
        if (_client is null)
        {
            return;
        }

        var roomName = string.IsNullOrWhiteSpace(NewRoomName)
            ? $"{DisplayName}'s Room"
            : NewRoomName.Trim();
        NewRoomName = "";
        var room = GetRoom(roomName);
        OpenRoomRequested?.Invoke(room);
        await _client.SendRoomAsync(roomName, "Create");
    }

    public async Task JoinRoomAsync(ChatRoomListItemViewModel? roomItem)
    {
        if (_client is null || roomItem is null)
        {
            return;
        }

        var room = GetRoom(roomItem.Name);
        OpenRoomRequested?.Invoke(room);
        await _client.SendRoomAsync(room.RoomName, "Join");
    }

    private Task StartGameAsync(string buddyName, NetBuddiesGameType gameType)
    {
        var buddy = _onlineProfiles.FirstOrDefault(profile =>
            profile.Name.Equals(buddyName, StringComparison.OrdinalIgnoreCase));

        buddy ??= new BuddyProfile { Name = buddyName };
        return StartGameAsync(new BuddyViewModel(buddy), gameType);
    }

    private async Task InviteBuddyToRoomAsync(string buddyName, string roomName)
    {
        if (_client is null)
        {
            return;
        }

        var room = GetRoom(roomName);
        OpenRoomRequested?.Invoke(room);
        await _client.SendRoomAsync(roomName, "Create");
        await _client.SendRoomInviteAsync(buddyName, roomName);
        room.Messages.Add(room.CreateMessage("Net Buddies", $"Invited {buddyName} to {roomName}.", isEvent: true));
    }

    private void WireClientEvents(BuddyClient client)
    {
        client.PresenceChanged += profiles =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                DisplayName = client.DisplayName;
                StatusText = $"{SelectedStatus} as {DisplayName}";
                _onlineProfiles = profiles
                    .Where(profile => !profile.Name.Equals(client.DisplayName, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                RefreshBuddyList();
                RefreshConversationImages();
                var presenceSignature = string.Join('\n', _onlineProfiles.Select(profile =>
                    $"{profile.Name}|{profile.Status}|{profile.PersonalMessage}|{profile.ProfileImageBase64.Length}"));
                if (!string.Equals(_presenceSignature, presenceSignature, StringComparison.Ordinal))
                {
                    _presenceSignature = presenceSignature;
                    var buddyNames = string.Join(", ", _onlineProfiles.Select(profile => profile.Name));
                    AddActivity(string.IsNullOrWhiteSpace(buddyNames)
                        ? "No other buddies online."
                        : $"{_onlineProfiles.Length} other buddy/buddies online: {buddyNames}.");
                }
            });
        };

        client.ChatReceived += packet =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var conversation = GetConversation(packet.From);
                conversation.ReceiveChat(packet);
                OpenConversationRequested?.Invoke(conversation);
            });
        };

        client.TypingReceived += packet =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var conversation = GetConversation(packet.From);
                conversation.ReceiveTyping(packet);
            });
        };

        client.NudgeReceived += packet =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var conversation = GetConversation(packet.From);
                conversation.ReceiveNudge(packet);
                OpenConversationRequested?.Invoke(conversation);
            });
        };

        client.FileReceived += packet =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var conversation = GetConversation(packet.From);
                var path = conversation.ReceiveFile(packet);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    AddActivity($"Saved incoming file to {path}.");
                }
                OpenConversationRequested?.Invoke(conversation);
            });
        };

        client.GameReceived += packet =>
        {
            Dispatcher.UIThread.Post(async () =>
            {
                if (_client is null)
                {
                    return;
                }

                var gameType = Enum.TryParse<NetBuddiesGameType>(packet.GameType, out var parsed)
                    ? parsed
                    : NetBuddiesGameType.TicTacToe;

                if (packet.GameAction == "Invite")
                {
                    var conversation = GetConversation(packet.From);
                    conversation.ReceiveGameInvite(packet, GameDisplayName(gameType));
                    OpenConversationRequested?.Invoke(conversation);
                    return;
                }

                if (_gameSessions.TryGetValue(packet.GameId, out var session))
                {
                    if (packet.GameAction == "Accept")
                    {
                        OpenGameRequested?.Invoke(session);
                    }

                    session.HandlePacket(packet);
                    return;
                }

                if (packet.GameAction == "Decline")
                {
                    var conversation = GetConversation(packet.From);
                    conversation.ReceiveGameDecline(packet, GameDisplayName(gameType));
                    OpenConversationRequested?.Invoke(conversation);
                    return;
                }

                if (packet.GameAction != "Move")
                {
                    return;
                }

                if (!_gameSessions.TryGetValue(packet.GameId, out session))
                {
                    session = new GameSessionViewModel(
                        _client,
                        packet.GameId,
                        gameType,
                        packet.From,
                        isHost: false);
                    _gameSessions[session.GameId] = session;
                    OpenGameRequested?.Invoke(session);

                }

                session.HandlePacket(packet);
            });
        };

        client.RoomReceived += packet =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (packet.RoomAction == "Invite")
                {
                    var conversation = GetConversation(packet.From);
                    conversation.ReceiveRoomInvite(packet);
                    OpenConversationRequested?.Invoke(conversation);
                    return;
                }

                if (packet.RoomAction is "InviteAccepted" or "InviteDeclined")
                {
                    var conversation = GetConversation(packet.From);
                    conversation.ReceiveRoomInviteResponse(packet);
                    OpenConversationRequested?.Invoke(conversation);
                    return;
                }

                if (packet.RoomAction == "List")
                {
                    Rooms.Clear();
                    foreach (var room in packet.Rooms)
                    {
                        Rooms.Add(new ChatRoomListItemViewModel(room));
                    }

                    return;
                }

                if (string.IsNullOrWhiteSpace(packet.RoomName))
                {
                    return;
                }

                var session = GetRoom(packet.RoomName);
                if (packet.RoomAction is "Message" or "System" or "Presence")
                {
                    session.ReceiveRoomPacket(packet);
                    OpenRoomRequested?.Invoke(session);
                }
            });
        };

        client.VoiceReceived += packet =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!string.IsNullOrWhiteSpace(packet.RoomName) && _roomSessions.TryGetValue(packet.RoomName, out var room))
                {
                    room.ReceiveVoice(packet);
                    return;
                }

                var isPrivateVoiceControl = packet.Text is "PrivateVoiceInvite" or "PrivateVoiceAccept" or "PrivateVoiceDecline" or "PrivateVoiceEnd";
                if (!string.IsNullOrWhiteSpace(packet.From) && _conversations.TryGetValue(packet.From, out var conversation))
                {
                    conversation.ReceivePrivateVoice(packet);
                    if (isPrivateVoiceControl)
                    {
                        OpenConversationRequested?.Invoke(conversation);
                    }

                    return;
                }

                if (!string.IsNullOrWhiteSpace(packet.From)
                    && isPrivateVoiceControl)
                {
                    conversation = GetConversation(packet.From);
                    conversation.ReceivePrivateVoice(packet);
                    OpenConversationRequested?.Invoke(conversation);
                }
            });
        };

        client.ScreenShareReceived += packet =>
        {
            if (packet.FileAction == "Frame" && _screenShares.TryGetValue(packet.TransferId, out var frameShare))
            {
                frameShare.ReceiveFrame(packet);
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (packet.FileAction == "End")
                {
                    if (_screenShares.Remove(packet.TransferId, out var share))
                    {
                        _ = share.StopAsync(notifyBuddy: false);
                    }
                }

                var conversation = GetConversation(packet.From);
                conversation.ReceiveScreenShare(packet);
                OpenConversationRequested?.Invoke(conversation);
            });
        };

        client.SystemMessageReceived += message =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusText = $"{SelectedStatus} as {client.DisplayName}";
                DisplayName = client.DisplayName;
                AddActivity(message);
            });
        };

        client.Disconnected += () =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_client == client)
                {
                    IsConnected = false;
                    StatusText = IsHosting ? "Hosting, not joined" : "Offline";
                    Buddies.Clear();
                    Rooms.Clear();
                }
            });
        };
    }

    private BuddyServerOptions CreateServerOptions()
    {
        return new BuddyServerOptions
        {
            Certificate = UseSecureTls ? LoadCertificate() : null,
            InviteCode = ServerInviteCode.Trim()
        };
    }

    private X509Certificate2? LoadCertificate()
    {
        if (!UseSecureTls)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(CertificatePath))
        {
            throw new InvalidOperationException("TLS hosting needs a .pfx certificate path.");
        }

        return X509CertificateLoader.LoadPkcs12FromFile(CertificatePath, CertificatePassword);
    }

    private ConversationViewModel GetConversation(string buddyName)
    {
        if (_client is null)
        {
            throw new InvalidOperationException("A chat cannot be opened before joining a server.");
        }

        if (!_conversations.TryGetValue(buddyName, out var conversation))
        {
            conversation = new ConversationViewModel(_client, buddyName, GetProfileImageForSender);
            conversation.GameRequested += Conversation_GameRequested;
            conversation.RoomInviteRequested += Conversation_RoomInviteRequested;
            conversation.GameAccepted += Conversation_GameAccepted;
            conversation.RoomInviteAccepted += Conversation_RoomInviteAccepted;
            conversation.ScreenShareAccepted += Conversation_ScreenShareAccepted;
            conversation.ScreenShareInviteRequested += Conversation_ScreenShareInviteRequested;
            _conversations[buddyName] = conversation;
        }

        return conversation;
    }

    private void Conversation_GameRequested(ConversationViewModel conversation, NetBuddiesGameType gameType)
    {
        _ = StartGameAsync(conversation.BuddyName, gameType);
    }

    private void Conversation_RoomInviteRequested(ConversationViewModel conversation, string roomName)
    {
        _ = InviteBuddyToRoomAsync(conversation.BuddyName, roomName);
    }

    private void Conversation_GameAccepted(ConversationViewModel conversation, NetBuddiesPacket packet)
    {
        if (_client is null)
        {
            return;
        }

        var gameType = Enum.TryParse<NetBuddiesGameType>(packet.GameType, out var parsed)
            ? parsed
            : NetBuddiesGameType.TicTacToe;
        var session = new GameSessionViewModel(
            _client,
            packet.GameId,
            gameType,
            packet.From,
            isHost: false);
        _gameSessions[session.GameId] = session;
        OpenGameRequested?.Invoke(session);
        _ = session.AcceptInviteAsync(packet.Text);
    }

    private void Conversation_RoomInviteAccepted(ConversationViewModel conversation, NetBuddiesPacket packet)
    {
        var room = GetRoom(packet.RoomName);
        OpenRoomRequested?.Invoke(room);
        _ = _client?.SendRoomAsync(packet.RoomName, "Join");
    }

    public async Task SendScreenShareInviteAsync(
        string buddyName,
        ScreenShareSource source,
        int qualityHeight,
        int frameRate,
        int jpegQuality)
    {
        var conversation = GetConversation(buddyName);
        var sessionId = Guid.NewGuid().ToString("N");
        _pendingScreenShareSources[sessionId] = source;
        _pendingScreenShareQualities[sessionId] = qualityHeight;
        _pendingScreenShareFrameRates[sessionId] = frameRate;
        _pendingScreenShareJpegQualities[sessionId] = jpegQuality;
        await conversation.SendScreenShareInviteAsync(sessionId, source.Name, qualityHeight, frameRate, jpegQuality);
    }

    private void Conversation_ScreenShareInviteRequested(
        ConversationViewModel conversation,
        ScreenShareSource source,
        int qualityHeight,
        int frameRate,
        int jpegQuality)
    {
        _ = SendScreenShareInviteAsync(conversation.BuddyName, source, qualityHeight, frameRate, jpegQuality);
    }

    private void Conversation_ScreenShareAccepted(ConversationViewModel conversation, NetBuddiesPacket packet)
    {
        if (_client is null)
        {
            return;
        }

        var quality = int.TryParse(packet.FileName, out var parsed)
            ? parsed
            : _pendingScreenShareQualities.TryGetValue(packet.TransferId, out var pendingQuality)
                ? pendingQuality
                : 720;
        var (frameRate, jpegQuality) = ParseScreenShareTransport(packet.RoomAction);
        if (_pendingScreenShareFrameRates.TryGetValue(packet.TransferId, out var pendingFrameRate))
        {
            frameRate = pendingFrameRate;
        }

        if (_pendingScreenShareJpegQualities.TryGetValue(packet.TransferId, out var pendingJpegQuality))
        {
            jpegQuality = pendingJpegQuality;
        }

        var isSender = packet.FileAction == "Accept";
        var source = isSender && _pendingScreenShareSources.TryGetValue(packet.TransferId, out var pendingSource)
            ? pendingSource
            : null;
        if (isSender)
        {
            _pendingScreenShareSources.Remove(packet.TransferId);
            _pendingScreenShareQualities.Remove(packet.TransferId);
            _pendingScreenShareFrameRates.Remove(packet.TransferId);
            _pendingScreenShareJpegQualities.Remove(packet.TransferId);
        }

        var share = new ScreenShareViewModel(
            _client,
            packet.TransferId,
            conversation.BuddyName,
            source,
            quality,
            frameRate,
            jpegQuality,
            isSender);
        _screenShares[packet.TransferId] = share;
        OpenScreenShareRequested?.Invoke(share);
    }

    private static (int FrameRate, int JpegQuality) ParseScreenShareTransport(string value)
    {
        var parts = value.Split('|');
        if (parts.Length >= 2
            && int.TryParse(parts[0], out var frameRate)
            && int.TryParse(parts[1], out var jpegQuality))
        {
            return (frameRate, jpegQuality);
        }

        return (15, 72);
    }

    private static string GameDisplayName(NetBuddiesGameType gameType)
    {
        return gameType switch
        {
            NetBuddiesGameType.TicTacToe => "Tic Tac Toe",
            NetBuddiesGameType.Checkers => "Checkers",
            NetBuddiesGameType.MinesweeperFlags => "Minesweeper Flags",
            _ => "a game"
        };
    }

    private ChatRoomViewModel GetRoom(string roomName)
    {
        if (_client is null)
        {
            throw new InvalidOperationException("A room cannot be opened before joining a server.");
        }

        if (!_roomSessions.TryGetValue(roomName, out var room))
        {
            room = new ChatRoomViewModel(_client, roomName, GetProfileImageForSender);
            _roomSessions[roomName] = room;
        }

        return room;
    }

    partial void OnSearchTextChanged(string value)
    {
        RefreshBuddyList();
    }

    private void RefreshBuddyList()
    {
        var filter = SearchText.Trim();
        Buddies.Clear();

        foreach (var profile in _onlineProfiles.Where(profile =>
                     string.IsNullOrWhiteSpace(filter)
                     || profile.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                     || profile.PersonalMessage.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            Buddies.Add(new BuddyViewModel(profile));
        }

        OnlineHeading = $"Online ({_onlineProfiles.Length})";
    }

    public async Task SetProfilePictureAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        ProfileImageBase64 = Convert.ToBase64String(bytes);
        ProfileImage = ImageFromBase64(ProfileImageBase64);
        SaveProfile();
        RefreshConversationImages();
        await PublishProfileAsync();
    }

    public void SetCertificatePath(string path)
    {
        CertificatePath = path;
        UseSecureTls = !string.IsNullOrWhiteSpace(path);
        TlsStatus = "TLS certificate selected.";
    }

    public void GenerateTlsCertificate()
    {
        if (string.IsNullOrWhiteSpace(CertificatePassword))
        {
            CertificatePassword = Guid.NewGuid().ToString("N")[..16];
        }

        CertificatePath = TlsCertificateService.GenerateSelfSignedPfx(CertificatePassword);
        UseSecureTls = true;
        TrustSelfSignedCertificate = true;
        TlsStatus = "Generated a self-signed TLS certificate. Clients should use TLS and Trust self-signed.";
        AddActivity($"Generated TLS certificate: {CertificatePath}");
    }

    private void RefreshConversationImages()
    {
        foreach (var conversation in _conversations.Values)
        {
            conversation.RefreshProfileImages();
        }
    }

    private Bitmap? GetProfileImageForSender(string sender)
    {
        if (sender.Equals("Me", StringComparison.OrdinalIgnoreCase)
            || sender.Equals(DisplayName, StringComparison.OrdinalIgnoreCase)
            || (_client?.DisplayName.Length > 0
                && sender.Equals(_client.DisplayName, StringComparison.OrdinalIgnoreCase)))
        {
            return ProfileImage;
        }

        var profile = _onlineProfiles.FirstOrDefault(profile =>
            profile.Name.Equals(sender, StringComparison.OrdinalIgnoreCase));
        return profile is null ? null : ImageFromBase64(profile.ProfileImageBase64);
    }

    partial void OnDisplayNameChanged(string value)
    {
        if (_isLoadingProfile)
        {
            return;
        }

        SaveProfile();
    }

    partial void OnPersonalMessageChanged(string value)
    {
        if (_isLoadingProfile)
        {
            return;
        }

        SaveProfile();
        _ = PublishProfileAsync();
    }

    partial void OnSelectedStatusChanged(string value)
    {
        if (_isLoadingProfile)
        {
            return;
        }

        StatusText = IsConnected ? $"{SelectedStatus} as {DisplayName}" : StatusText;
        SaveProfile();
        _ = PublishProfileAsync();
    }

    private void LoadProfile()
    {
        _isLoadingProfile = true;
        var settings = ProfileSettingsStore.Load();

        if (!string.IsNullOrWhiteSpace(settings.DisplayName))
        {
            DisplayName = settings.DisplayName;
        }

        PersonalMessage = string.IsNullOrWhiteSpace(settings.PersonalMessage)
            ? "Stay connected. Always."
            : settings.PersonalMessage;
        SelectedStatus = string.IsNullOrWhiteSpace(settings.Status)
            ? "Online"
            : settings.Status;
        ProfileImageBase64 = settings.ProfileImageBase64;
        ProfileImage = ImageFromBase64(ProfileImageBase64);
        HostAddress = string.IsNullOrWhiteSpace(settings.LastHostAddress)
            ? HostAddress
            : settings.LastHostAddress;
        Port = settings.LastPort is >= 1 and <= 65535
            ? settings.LastPort
            : Port;
        UseSecureTls = settings.LastUseSecureTls;
        TrustSelfSignedCertificate = settings.LastTrustSelfSignedCertificate;
        ServerInviteCode = settings.LastServerInviteCode;
        _isLoadingProfile = false;
    }

    private void SaveProfile(bool includeConnectionSettings = false)
    {
        if (_isLoadingProfile)
        {
            return;
        }

        var existing = ProfileSettingsStore.Load();
        ProfileSettingsStore.Save(existing with
        {
            DisplayName = DisplayName.Trim(),
            Status = SelectedStatus,
            PersonalMessage = PersonalMessage.Trim(),
            ProfileImageBase64 = ProfileImageBase64,
            AppTheme = AppThemeService.CurrentThemeName,
            LastHostAddress = includeConnectionSettings ? HostAddress.Trim() : existing.LastHostAddress,
            LastPort = includeConnectionSettings ? Port : existing.LastPort,
            LastUseSecureTls = includeConnectionSettings ? UseSecureTls : existing.LastUseSecureTls,
            LastTrustSelfSignedCertificate = includeConnectionSettings
                ? TrustSelfSignedCertificate
                : existing.LastTrustSelfSignedCertificate,
            LastServerInviteCode = includeConnectionSettings ? ServerInviteCode : existing.LastServerInviteCode
        });
    }

    private async Task PublishProfileAsync()
    {
        if (_client is not { IsConnected: true })
        {
            return;
        }

        try
        {
            await _client.SendProfileAsync(PersonalMessage, SelectedStatus, ProfileImageBase64);
        }
        catch (Exception ex)
        {
            AddActivity($"Could not update profile: {ex.Message}");
        }
    }

    private static Bitmap? ImageFromBase64(string imageBase64)
    {
        if (string.IsNullOrWhiteSpace(imageBase64))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(imageBase64);
            return new Bitmap(new MemoryStream(bytes));
        }
        catch
        {
            return null;
        }
    }

    private void AddActivity(string message)
    {
        ActivityLog.Insert(0, $"[{DateTime.Now:HH:mm}] {message}");

        while (ActivityLog.Count > 8)
        {
            ActivityLog.RemoveAt(ActivityLog.Count - 1);
        }
    }

    private async Task CloseRoomsAsync()
    {
        foreach (var room in _roomSessions.Values.ToArray())
        {
            try
            {
                await room.LeaveRoomAsync();
            }
            catch (Exception ex)
            {
                AddActivity($"Could not leave {room.RoomName}: {ex.Message}");
            }

            room.Dispose();
        }

        _roomSessions.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await CloseRoomsAsync();
            await _client.DisposeAsync();
        }

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }
}

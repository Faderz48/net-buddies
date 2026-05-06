using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;

namespace NetBuddies.Core;

public sealed class BuddyServer : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ClientSession> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _roomLock = new();
    private readonly Dictionary<string, ChatRoomState> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<IPAddress, ConnectionWindow> _connectionWindows = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _serverTokenSource;
    private BuddyServerOptions _options = new();

    public event Action<string>? StatusChanged;

    public int Port { get; private set; }
    public bool IsRunning => _listener is not null;

    public Task StartAsync(int port, CancellationToken cancellationToken = default)
    {
        return StartAsync(port, new BuddyServerOptions(), cancellationToken);
    }

    public Task StartAsync(int port, BuddyServerOptions options, CancellationToken cancellationToken = default)
    {
        if (_listener is not null)
        {
            throw new InvalidOperationException("Server is already running.");
        }

        Port = port;
        _options = options;
        _serverTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        StatusChanged?.Invoke(_options.UseTls
            ? $"Secure server listening on port {port}."
            : $"Server listening on port {port}.");
        if (_options.RequiresInviteCode)
        {
            StatusChanged?.Invoke("Invite code required for new clients.");
        }

        _ = AcceptClientsAsync(_serverTokenSource.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_listener is null && _clients.IsEmpty)
        {
            return;
        }

        _serverTokenSource?.Cancel();
        _listener?.Stop();
        _listener = null;

        foreach (var session in _clients.Values)
        {
            session.Dispose();
        }

        _clients.Clear();
        _connectionWindows.Clear();
        lock (_roomLock)
        {
            _rooms.Clear();
        }

        await BroadcastPresenceAsync();
        StatusChanged?.Invoke("Server stopped.");
    }

    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var tcpClient = await _listener.AcceptTcpClientAsync(cancellationToken);
                if (!AllowConnection(tcpClient))
                {
                    tcpClient.Dispose();
                    continue;
                }

                _ = HandleClientAsync(tcpClient, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Server accept error: {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        ClientSession? session = null;

        try
        {
            using var networkStream = tcpClient.GetStream();
            using var transportStream = await CreateTransportStreamAsync(networkStream, cancellationToken);
            using var reader = new StreamReader(transportStream, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(transportStream, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true
            };

            var helloLine = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(helloLine))
            {
                return;
            }

            var hello = NetBuddiesPacket.FromJsonLine(helloLine);
            if (hello.Kind != PacketKind.Hello || string.IsNullOrWhiteSpace(hello.From))
            {
                return;
            }

            if (_options.RequiresInviteCode
                && !string.Equals(hello.InviteCode, _options.InviteCode, StringComparison.Ordinal))
            {
                await writer.WriteLineAsync(new NetBuddiesPacket
                {
                    Kind = PacketKind.System,
                    From = "Net Buddies",
                    Text = "Invalid server invite code."
                }.ToJsonLine());
                StatusChanged?.Invoke($"Rejected {hello.From.Trim()} due to invalid invite code.");
                return;
            }

            var displayName = MakeUniqueName(hello.From.Trim());
            session = new ClientSession(
                displayName,
                hello.Text,
                hello.RoomAction,
                hello.PayloadBase64,
                tcpClient,
                writer);
            _clients[displayName] = session;
            StatusChanged?.Invoke($"{displayName} joined.");
            await session.SendAsync(new NetBuddiesPacket
            {
                Kind = PacketKind.System,
                From = "Net Buddies",
                To = displayName,
                Text = $"Connected as {displayName}."
            });
            await BroadcastPresenceAsync();
            await BroadcastRoomListAsync();

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                var packet = NetBuddiesPacket.FromJsonLine(line);
                await RoutePacketAsync(packet with { From = displayName });
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Client error: {ex.Message}");
        }
        finally
        {
            if (session is not null)
            {
                _clients.TryRemove(session.Name, out _);
                RemoveClientFromRooms(session.Name);
                session.Dispose();
                StatusChanged?.Invoke($"{session.Name} left.");
                await BroadcastPresenceAsync();
                await BroadcastRoomListAsync();
            }
            else
            {
                tcpClient.Dispose();
            }
        }
    }

    private bool AllowConnection(TcpClient tcpClient)
    {
        if (_options.MaxConnectionsPerMinutePerAddress <= 0)
        {
            return true;
        }

        if (tcpClient.Client.RemoteEndPoint is not IPEndPoint remoteEndPoint)
        {
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        var window = _connectionWindows.AddOrUpdate(
            remoteEndPoint.Address,
            _ => new ConnectionWindow(now, 1),
            (_, current) => now - current.StartedAt > TimeSpan.FromMinutes(1)
                ? new ConnectionWindow(now, 1)
                : current with { Count = current.Count + 1 });

        if (window.Count <= _options.MaxConnectionsPerMinutePerAddress)
        {
            return true;
        }

        StatusChanged?.Invoke($"Rate limited connection attempts from {remoteEndPoint.Address}.");
        return false;
    }

    private async Task<Stream> CreateTransportStreamAsync(NetworkStream networkStream, CancellationToken cancellationToken)
    {
        if (!_options.UseTls || _options.Certificate is null)
        {
            return networkStream;
        }

        var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false);
        await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = _options.Certificate,
            ClientCertificateRequired = false,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        }, cancellationToken);
        return sslStream;
    }

    private string MakeUniqueName(string requestedName)
    {
        var baseName = string.IsNullOrWhiteSpace(requestedName) ? "Buddy" : requestedName;
        var candidate = baseName;
        var suffix = 2;

        while (_clients.ContainsKey(candidate))
        {
            candidate = $"{baseName} {suffix}";
            suffix++;
        }

        return candidate;
    }

    private async Task RoutePacketAsync(NetBuddiesPacket packet)
    {
        if (packet.Kind == PacketKind.Profile)
        {
            if (_clients.TryGetValue(packet.From, out var sender))
            {
                sender.UpdateProfile(packet.Text, packet.RoomAction, packet.PayloadBase64);
                await BroadcastPresenceAsync();
            }

            return;
        }

        if (packet.Kind == PacketKind.Room)
        {
            await RouteRoomPacketAsync(packet);
            return;
        }

        if (packet.Kind == PacketKind.Voice)
        {
            await RouteVoicePacketAsync(packet);
            return;
        }

        if (packet.Kind is PacketKind.Chat or PacketKind.Typing or PacketKind.Nudge or PacketKind.FileData or PacketKind.Game or PacketKind.ScreenShare)
        {
            if (_clients.TryGetValue(packet.To, out var recipient))
            {
                await recipient.SendAsync(packet);
            }
            else if (_clients.TryGetValue(packet.From, out var sender))
            {
                await sender.SendAsync(new NetBuddiesPacket
                {
                    Kind = PacketKind.System,
                    From = "Net Buddies",
                    To = packet.From,
                    Text = $"{packet.To} is no longer connected."
                });
            }
        }
    }

    private async Task RouteRoomPacketAsync(NetBuddiesPacket packet)
    {
        if (packet.RoomAction is "Invite" or "InviteAccepted" or "InviteDeclined"
            && !string.IsNullOrWhiteSpace(packet.To))
        {
            if (_clients.TryGetValue(packet.To, out var invited))
            {
                await invited.SendAsync(packet);
            }

            return;
        }

        var roomName = NormalizeRoomName(packet.RoomName);
        if (string.IsNullOrWhiteSpace(roomName))
        {
            return;
        }

        switch (packet.RoomAction)
        {
            case "Create":
            case "Join":
                lock (_roomLock)
                {
                    if (!_rooms.TryGetValue(roomName, out var room))
                    {
                        room = new ChatRoomState(roomName);
                        _rooms[roomName] = room;
                    }

                    room.Members.Add(packet.From);
                }

                await BroadcastRoomListAsync();
                await BroadcastRoomPresenceAsync(roomName);
                await SendRoomSystemAsync(roomName, $"{packet.From} joined {roomName}.");
                break;

            case "Leave":
                lock (_roomLock)
                {
                    if (_rooms.TryGetValue(roomName, out var room))
                    {
                        room.Members.Remove(packet.From);
                        room.VoiceUsers.Remove(packet.From);
                        if (room.Members.Count == 0)
                        {
                            _rooms.Remove(roomName);
                        }
                    }
                }

                await BroadcastRoomListAsync();
                await BroadcastRoomPresenceAsync(roomName);
                await SendRoomSystemAsync(roomName, $"{packet.From} left {roomName}.");
                break;

            case "Message":
                await BroadcastToRoomAsync(roomName, packet);
                break;

            case "VoiceJoin":
                lock (_roomLock)
                {
                    if (_rooms.TryGetValue(roomName, out var room) && room.Members.Contains(packet.From))
                    {
                        room.VoiceUsers.Add(packet.From);
                    }
                }

                await BroadcastRoomListAsync();
                await BroadcastRoomPresenceAsync(roomName);
                await SendRoomSystemAsync(roomName, $"{packet.From} joined voice.");
                break;

            case "VoiceLeave":
                lock (_roomLock)
                {
                    if (_rooms.TryGetValue(roomName, out var room))
                    {
                        room.VoiceUsers.Remove(packet.From);
                    }
                }

                await BroadcastRoomListAsync();
                await BroadcastRoomPresenceAsync(roomName);
                await SendRoomSystemAsync(roomName, $"{packet.From} left voice.");
                break;
        }
    }

    private async Task RouteVoicePacketAsync(NetBuddiesPacket packet)
    {
        if (!string.IsNullOrWhiteSpace(packet.To))
        {
            if (_clients.TryGetValue(packet.To, out var recipient))
            {
                await recipient.SendAsync(packet);
            }

            return;
        }

        var recipients = GetRoomRecipients(packet.RoomName, includeSender: false, voiceOnly: true, sender: packet.From);
        foreach (var recipient in recipients)
        {
            await recipient.SendAsync(packet);
        }
    }

    private async Task BroadcastRoomListAsync()
    {
        var rooms = GetRoomInfos();
        var packet = new NetBuddiesPacket
        {
            Kind = PacketKind.Room,
            From = "Net Buddies",
            RoomAction = "List",
            Rooms = rooms
        };

        foreach (var session in _clients.Values)
        {
            await session.SendAsync(packet);
        }
    }

    private async Task BroadcastRoomPresenceAsync(string roomName)
    {
        ChatRoomState? room;
        lock (_roomLock)
        {
            _rooms.TryGetValue(roomName, out room);
        }

        if (room is null)
        {
            return;
        }

        string[] members;
        string[] voiceUsers;
        lock (_roomLock)
        {
            members = room.Members.Order(StringComparer.OrdinalIgnoreCase).ToArray();
            voiceUsers = room.VoiceUsers.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        var packet = new NetBuddiesPacket
        {
            Kind = PacketKind.Room,
            From = "Net Buddies",
            RoomName = roomName,
            RoomAction = "Presence",
            Users = members,
            Text = string.Join("|", voiceUsers)
        };

        foreach (var recipient in GetRoomRecipients(roomName, includeSender: true))
        {
            await recipient.SendAsync(packet);
        }
    }

    private async Task SendRoomSystemAsync(string roomName, string message)
    {
        var packet = new NetBuddiesPacket
        {
            Kind = PacketKind.Room,
            From = "Net Buddies",
            RoomName = roomName,
            RoomAction = "System",
            Text = message
        };

        await BroadcastToRoomAsync(roomName, packet);
    }

    private async Task BroadcastToRoomAsync(string roomName, NetBuddiesPacket packet)
    {
        foreach (var recipient in GetRoomRecipients(roomName, includeSender: true))
        {
            await recipient.SendAsync(packet);
        }
    }

    private ClientSession[] GetRoomRecipients(
        string roomName,
        bool includeSender,
        bool voiceOnly = false,
        string sender = "")
    {
        string[] names;
        lock (_roomLock)
        {
            if (!_rooms.TryGetValue(roomName, out var room))
            {
                return [];
            }

            names = (voiceOnly ? room.VoiceUsers : room.Members)
                .Where(name => includeSender || !name.Equals(sender, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        return names
            .Select(name => _clients.TryGetValue(name, out var client) ? client : null)
            .OfType<ClientSession>()
            .ToArray();
    }

    private ChatRoomInfo[] GetRoomInfos()
    {
        lock (_roomLock)
        {
            return _rooms.Values
                .Select(room => new ChatRoomInfo
                {
                    Name = room.Name,
                    MemberCount = room.Members.Count,
                    VoiceCount = room.VoiceUsers.Count
                })
                .OrderBy(room => room.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private void RemoveClientFromRooms(string name)
    {
        lock (_roomLock)
        {
            foreach (var roomName in _rooms.Keys.ToArray())
            {
                var room = _rooms[roomName];
                room.Members.Remove(name);
                room.VoiceUsers.Remove(name);
                if (room.Members.Count == 0)
                {
                    _rooms.Remove(roomName);
                }
            }
        }
    }

    private static string NormalizeRoomName(string roomName)
    {
        return roomName.Trim().Length > 40
            ? roomName.Trim()[..40]
            : roomName.Trim();
    }

    private async Task BroadcastPresenceAsync()
    {
        var profiles = _clients.Values
            .Select(client => client.Profile)
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var presence = new NetBuddiesPacket
        {
            Kind = PacketKind.Presence,
            From = "Net Buddies",
            Users = profiles.Select(profile => profile.Name).ToArray(),
            Profiles = profiles
        };

        foreach (var session in _clients.Values)
        {
            await session.SendAsync(presence);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _serverTokenSource?.Dispose();
    }

    private sealed class ClientSession(
        string name,
        string personalMessage,
        string status,
        string profileImageBase64,
        TcpClient client,
        StreamWriter writer) : IDisposable
    {
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private string _personalMessage = personalMessage;
        private string _status = string.IsNullOrWhiteSpace(status) ? "Online" : status;
        private string _profileImageBase64 = profileImageBase64;

        public string Name { get; } = name;
        public BuddyProfile Profile => new()
        {
            Name = Name,
            Status = _status,
            PersonalMessage = _personalMessage,
            ProfileImageBase64 = _profileImageBase64
        };

        public void UpdateProfile(string personalMessage, string status, string profileImageBase64)
        {
            _personalMessage = personalMessage;
            _status = string.IsNullOrWhiteSpace(status) ? "Online" : status;
            _profileImageBase64 = profileImageBase64;
        }

        public async Task SendAsync(NetBuddiesPacket packet)
        {
            await _sendLock.WaitAsync();
            try
            {
                await writer.WriteLineAsync(packet.ToJsonLine());
            }
            catch
            {
                Dispose();
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Dispose()
        {
            client.Dispose();
            _sendLock.Dispose();
        }
    }

    private sealed class ChatRoomState(string name)
    {
        public string Name { get; } = name;
        public HashSet<string> Members { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> VoiceUsers { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record ConnectionWindow(DateTimeOffset StartedAt, int Count);
}

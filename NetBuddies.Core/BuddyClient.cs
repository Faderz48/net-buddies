using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace NetBuddies.Core;

public sealed class BuddyClient : IAsyncDisposable
{
    private const int FileChunkSize = 48 * 1024;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private TcpClient? _client;
    private Stream? _transportStream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _readTokenSource;

    public event Action<IReadOnlyList<BuddyProfile>>? PresenceChanged;
    public event Action<NetBuddiesPacket>? ChatReceived;
    public event Action<NetBuddiesPacket>? TypingReceived;
    public event Action<NetBuddiesPacket>? NudgeReceived;
    public event Action<NetBuddiesPacket>? FileReceived;
    public event Action<NetBuddiesPacket>? GameReceived;
    public event Action<NetBuddiesPacket>? RoomReceived;
    public event Action<NetBuddiesPacket>? VoiceReceived;
    public event Action<NetBuddiesPacket>? ScreenShareReceived;
    public event Action<string>? SystemMessageReceived;
    public event Action? Disconnected;

    public string DisplayName { get; private set; } = "";
    public bool IsConnected => _client?.Connected == true;

    public async Task ConnectAsync(
        string host,
        int port,
        string displayName,
        string personalMessage,
        string status,
        string profileImageBase64,
        bool useTls = false,
        bool allowUntrustedCertificate = false,
        string inviteCode = "",
        CancellationToken cancellationToken = default)
    {
        await DisconnectAsync();

        DisplayName = displayName.Trim();
        _client = new TcpClient();
        await _client.ConnectAsync(host, port, cancellationToken);

        _transportStream = await CreateTransportStreamAsync(
            _client.GetStream(),
            host,
            useTls,
            allowUntrustedCertificate,
            cancellationToken);
        _reader = new StreamReader(_transportStream, Encoding.UTF8, leaveOpen: true);
        _writer = new StreamWriter(_transportStream, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true
        };

        _readTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await SendAsync(new NetBuddiesPacket
        {
            Kind = PacketKind.Hello,
            From = DisplayName,
            Text = personalMessage,
            InviteCode = inviteCode,
            RoomAction = status,
            PayloadBase64 = profileImageBase64
        }, throwOnFailure: true);
        _ = ReadLoopAsync(_readTokenSource.Token);
    }

    private static async Task<Stream> CreateTransportStreamAsync(
        NetworkStream networkStream,
        string host,
        bool useTls,
        bool allowUntrustedCertificate,
        CancellationToken cancellationToken)
    {
        if (!useTls)
        {
            return networkStream;
        }

        var sslStream = new SslStream(
            networkStream,
            leaveInnerStreamOpen: false,
            (_, certificate, chain, errors) => ValidateServerCertificate(
                certificate,
                chain,
                errors,
                allowUntrustedCertificate));
        await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = host,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        }, cancellationToken);
        return sslStream;
    }

    private static bool ValidateServerCertificate(
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors errors,
        bool allowUntrustedCertificate)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        return allowUntrustedCertificate && certificate is not null;
    }

    public Task SendProfileAsync(string personalMessage, string status, string profileImageBase64)
    {
        return SendAsync(new NetBuddiesPacket
        {
            Kind = PacketKind.Profile,
            From = DisplayName,
            Text = personalMessage,
            RoomAction = status,
            PayloadBase64 = profileImageBase64
        });
    }

    public Task SendTypingAsync(string to, bool isTyping)
    {
        return SendAsync(new NetBuddiesPacket
        {
            Kind = PacketKind.Typing,
            From = DisplayName,
            To = to,
            Text = isTyping ? "typing" : ""
        });
    }

    public Task SendChatAsync(string to, string message)
    {
        return SendAsync(new NetBuddiesPacket
        {
            Kind = PacketKind.Chat,
            From = DisplayName,
            To = to,
            Text = message
        });
    }

    public Task SendNudgeAsync(string to)
    {
        return SendAsync(new NetBuddiesPacket
        {
            Kind = PacketKind.Nudge,
            From = DisplayName,
            To = to,
            Text = "Nudge!"
        });
    }

    public async Task SendFileOfferAsync(string to, string transferId, string path, CancellationToken cancellationToken = default)
    {
        var fileInfo = new FileInfo(path);
        await SendAsync(new NetBuddiesPacket
        {
            Kind = PacketKind.FileData,
            From = DisplayName,
            To = to,
            FileAction = "Offer",
            TransferId = transferId,
            FileName = Path.GetFileName(path),
            FileSize = fileInfo.Length
        });
    }

    public Task SendFileAcceptAsync(string to, string transferId)
    {
        return SendAsync(new NetBuddiesPacket
        {
            Kind = PacketKind.FileData,
            From = DisplayName,
            To = to,
            FileAction = "Accept",
            TransferId = transferId
        });
    }

    public Task SendFileDeclineAsync(string to, string transferId, string fileName)
    {
        return SendAsync(new NetBuddiesPacket
        {
            Kind = PacketKind.FileData,
            From = DisplayName,
            To = to,
            FileAction = "Decline",
            TransferId = transferId,
            FileName = fileName
        });
    }

    public async Task SendFileDataAsync(
        string to,
        string transferId,
        string path,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var fileInfo = new FileInfo(path);
        await SendAsync(new NetBuddiesPacket
        {
            Kind = PacketKind.FileData,
            From = DisplayName,
            To = to,
            FileAction = "DataStart",
            TransferId = transferId,
            FileName = fileInfo.Name,
            FileSize = fileInfo.Length
        });

        await using var stream = File.OpenRead(path);
        var buffer = new byte[FileChunkSize];
        int read;
        long sent = 0;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await SendAsync(new NetBuddiesPacket
            {
                Kind = PacketKind.FileData,
                From = DisplayName,
                To = to,
                FileAction = "DataChunk",
                TransferId = transferId,
                FileName = fileInfo.Name,
                FileSize = fileInfo.Length,
                PayloadBase64 = Convert.ToBase64String(buffer, 0, read)
            });
            sent += read;
            progress?.Report(sent);
        }

        await SendAsync(new NetBuddiesPacket
        {
            Kind = PacketKind.FileData,
            From = DisplayName,
            To = to,
            FileAction = "DataEnd",
            TransferId = transferId,
            FileName = fileInfo.Name,
            FileSize = fileInfo.Length
        });
    }

    public async Task SendImageAsync(string to, string path, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        await SendInlineMediaAsync(to, Path.GetFileName(path), bytes, "ImageData");
    }

    public Task SendGifAsync(string to, string title, byte[] gifBytes)
    {
        var safeTitle = string.IsNullOrWhiteSpace(title) ? "Tenor GIF" : title.Trim();
        return SendInlineMediaAsync(to, $"{safeTitle}.gif", gifBytes, "GifData", safeTitle);
    }

    private Task SendInlineMediaAsync(string to, string fileName, byte[] bytes, string action, string text = "")
    {
        return SendAsync(new NetBuddiesPacket
        {
            Kind = PacketKind.FileData,
            From = DisplayName,
            To = to,
            FileAction = action,
            FileName = fileName,
            FileSize = bytes.LongLength,
            Text = text,
            PayloadBase64 = Convert.ToBase64String(bytes)
        });
    }

    public Task SendVoiceNoteAsync(string to, byte[] wavBytes, TimeSpan duration)
    {
        return SendAsync(new NetBuddiesPacket
        {
            Kind = PacketKind.FileData,
            From = DisplayName,
            To = to,
            FileAction = "VoiceNote",
            FileName = $"VoiceNote-{DateTime.Now:yyyyMMdd-HHmmss}.wav",
            FileSize = wavBytes.LongLength,
            Text = duration.TotalSeconds.ToString("0"),
            PayloadBase64 = Convert.ToBase64String(wavBytes)
        });
    }

    public Task SendGameAsync(
        string to,
        string gameId,
        string gameType,
        string gameAction,
        string payload = "")
    {
        return SendAsync(new NetBuddiesPacket
        {
            Kind = PacketKind.Game,
            From = DisplayName,
            To = to,
            GameId = gameId,
            GameType = gameType,
            GameAction = gameAction,
            Text = payload
        });
    }

    public Task SendRoomAsync(string roomName, string action, string text = "")
    {
        return SendAsync(new NetBuddiesPacket
        {
            Kind = PacketKind.Room,
            From = DisplayName,
            RoomName = roomName,
            RoomAction = action,
            Text = text
        });
    }

    public Task SendRoomInviteAsync(string to, string roomName)
    {
        return SendAsync(new NetBuddiesPacket
        {
            Kind = PacketKind.Room,
            From = DisplayName,
            To = to,
            RoomName = roomName,
            RoomAction = "Invite",
            Text = $"{DisplayName} invited you to {roomName}."
        });
    }

    public Task SendRoomInviteResponseAsync(string to, string roomName, bool accepted)
    {
        return SendAsync(new NetBuddiesPacket
        {
            Kind = PacketKind.Room,
            From = DisplayName,
            To = to,
            RoomName = roomName,
            RoomAction = accepted ? "InviteAccepted" : "InviteDeclined",
            Text = accepted
                ? $"{DisplayName} accepted your room invite."
                : $"{DisplayName} declined your room invite."
        });
    }

    public Task SendVoiceAsync(string roomName, byte[] audioBytes, int byteCount)
    {
        var payload = Convert.ToBase64String(audioBytes, 0, byteCount);
        return SendAsync(new NetBuddiesPacket
        {
            Kind = PacketKind.Voice,
            From = DisplayName,
            RoomName = roomName,
            PayloadBase64 = payload
        });
    }

    public Task SendPrivateVoiceAsync(string to, byte[] audioBytes, int byteCount)
    {
        var payload = Convert.ToBase64String(audioBytes, 0, byteCount);
        return SendAsync(new NetBuddiesPacket
        {
            Kind = PacketKind.Voice,
            From = DisplayName,
            To = to,
            PayloadBase64 = payload
        });
    }

    public Task SendPrivateVoiceInviteAsync(string to)
    {
        return SendAsync(new NetBuddiesPacket
        {
            Kind = PacketKind.Voice,
            From = DisplayName,
            To = to,
            Text = "PrivateVoiceInvite"
        });
    }

    public Task SendPrivateVoiceResponseAsync(string to, bool accepted)
    {
        return SendAsync(new NetBuddiesPacket
        {
            Kind = PacketKind.Voice,
            From = DisplayName,
            To = to,
            Text = accepted ? "PrivateVoiceAccept" : "PrivateVoiceDecline"
        });
    }

    public Task SendPrivateVoiceEndAsync(string to)
    {
        return SendAsync(new NetBuddiesPacket
        {
            Kind = PacketKind.Voice,
            From = DisplayName,
            To = to,
            Text = "PrivateVoiceEnd"
        });
    }

    public Task SendScreenShareInviteAsync(string to, string sessionId, string sourceName, int qualityHeight, int frameRate, int jpegQuality)
    {
        return SendScreenShareControlAsync(
            to,
            sessionId,
            "Invite",
            sourceName,
            qualityHeight.ToString(),
            $"{frameRate}|{jpegQuality}");
    }

    public Task SendScreenShareResponseAsync(string to, string sessionId, bool accepted)
    {
        return SendScreenShareControlAsync(to, sessionId, accepted ? "Accept" : "Decline");
    }

    public Task SendScreenShareEndAsync(string to, string sessionId)
    {
        return SendScreenShareControlAsync(to, sessionId, "End");
    }

    public Task SendScreenShareFrameAsync(string to, string sessionId, byte[] jpegBytes)
    {
        return SendAsync(new NetBuddiesPacket
        {
            Kind = PacketKind.ScreenShare,
            From = DisplayName,
            To = to,
            TransferId = sessionId,
            FileAction = "Frame",
            FileName = "screen.jpg",
            FileSize = jpegBytes.LongLength,
            PayloadBase64 = Convert.ToBase64String(jpegBytes)
        });
    }

    private Task SendScreenShareControlAsync(string to, string sessionId, string action, string text = "", string fileName = "", string roomAction = "")
    {
        return SendAsync(new NetBuddiesPacket
        {
            Kind = PacketKind.ScreenShare,
            From = DisplayName,
            To = to,
            TransferId = sessionId,
            FileAction = action,
            Text = text,
            FileName = fileName,
            RoomAction = roomAction
        });
    }

    public async Task DisconnectAsync()
    {
        _readTokenSource?.Cancel();
        _readTokenSource?.Dispose();
        _readTokenSource = null;

        _reader?.Dispose();
        _writer?.Dispose();
        _transportStream?.Dispose();
        _client?.Dispose();

        _transportStream = null;
        _reader = null;
        _writer = null;
        _client = null;
        await Task.CompletedTask;
    }

    private async Task SendAsync(NetBuddiesPacket packet, bool throwOnFailure = false)
    {
        if (_writer is null)
        {
            throw new InvalidOperationException("Not connected to a Net Buddies server.");
        }

        await _sendLock.WaitAsync();
        try
        {
            await _writer.WriteLineAsync(packet.ToJsonLine());
        }
        catch (Exception ex) when (!throwOnFailure && ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
        {
            Disconnected?.Invoke();
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _reader is not null)
            {
                var line = await _reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                var packet = NetBuddiesPacket.FromJsonLine(line);
                HandlePacket(packet);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            Disconnected?.Invoke();
        }
    }

    private void HandlePacket(NetBuddiesPacket packet)
    {
        switch (packet.Kind)
        {
            case PacketKind.Presence:
                PresenceChanged?.Invoke(packet.Profiles);
                break;
            case PacketKind.Chat:
                ChatReceived?.Invoke(packet);
                break;
            case PacketKind.Typing:
                TypingReceived?.Invoke(packet);
                break;
            case PacketKind.Nudge:
                NudgeReceived?.Invoke(packet);
                break;
            case PacketKind.FileData:
                FileReceived?.Invoke(packet);
                break;
            case PacketKind.Game:
                GameReceived?.Invoke(packet);
                break;
            case PacketKind.Room:
                RoomReceived?.Invoke(packet);
                break;
            case PacketKind.Voice:
                VoiceReceived?.Invoke(packet);
                break;
            case PacketKind.ScreenShare:
                ScreenShareReceived?.Invoke(packet);
                break;
            case PacketKind.System:
                if (!string.IsNullOrWhiteSpace(packet.To))
                {
                    DisplayName = packet.To;
                }

                SystemMessageReceived?.Invoke(packet.Text);
                break;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _sendLock.Dispose();
    }
}

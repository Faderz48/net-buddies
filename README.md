<img width="2287" height="1123" alt="net buddie screen" src="https://github.com/user-attachments/assets/ef28f9bf-259d-40f5-80d4-363535f15bbf" />
# Net Buddies

Net Buddies is a C#/.NET Avalonia  MSN-style instant messaging app.

## Current MVP

- One desktop app can host a server and join it.
- Hosting is a separate server-operator action.
- Other copies of the app can join by IP address and port.
- Joining opens an MSN-style buddy list window.
- Connected users appear automatically in the buddy list.
- Usernames, personal messages, and profile pictures are saved locally.
- MSN-style presence statuses are saved and shared: Online, Away, Busy, and Invisible.
- Profile pictures are shared through the server and shown in everyone else's buddy list.
- Double-click a buddy, or select one and press Open Chat, to open a private chat window.
- Right-click a buddy to send a message, send a file, or nudge them.
- Private chats show typing indicators.
- Incoming nudges shake the chat window.
- Private chat windows can send game requests for Tic Tac Toe, Checkers, and Minesweeper Flags.
- Private chat windows can invite buddies to a named chat room.
- Private chat windows support optional one-to-one voice chat with microphone selection and activity meter.
- Private chat actions are grouped in the chat header: `Voice Chat`, `File`, and `Games`.
- Chat header actions are ordered `File`, `Voice Chat`, `Room Invite`, then `Games`.
- File transfers, game requests, and room invites now require the other buddy to accept or deny before anything starts.
- Right-click a buddy and choose `Play Game` to start Tic Tac Toe, Checkers, or Minesweeper Flags.
- Games use visual boards with drawn marks, checkers pieces, kings, flags, and mine-number styling.
- Tic Tac Toe highlights the winning line and uses polished vector X/O marks.
- Minesweeper Flags shows flag scoring, remaining flags, and expands empty safe areas.
- A standalone server app is available for Windows and Linux hosting without opening the full client.
- Private internet hosting can be done through Tailscale or Cloudflare Tunnel so the server is not exposed directly.
- Secure direct hosting supports TLS 1.2/1.3 with a `.pfx` certificate plus an optional invite code.
- Use `Actions > Create Chat Room` to create a shared room everyone on the server can join.
- Chat rooms can be named before creation from the buddy list.
- Room windows support group text chat and an optional voice channel.
- Voice channels include a microphone selector and separate `Join Voice` / `Leave` controls.
- Room voice shows live microphone activity so you can confirm your mic is working.
- Private chat windows support text messages, nudges, and basic file sending.
- Private chat windows can send inline images from the paperclip button without saving them as downloads.
- Private chat windows include a GIPHY GIF picker. Paste a GIPHY API key in the GIF picker once, or set `NETBUDDIES_GIPHY_API_KEY` before launching the client.
- Private chat windows can send invite-based screen share requests. The sender can choose the whole desktop or an application window and 30 fps 720p/1080p presets. Screen capture currently runs on Windows clients; Linux servers relay it normally.
- Incoming files are saved to `Documents/NetBuddiesDownloads`.

Local profile settings are stored in `%APPDATA%/NetBuddies/profile.json`.

## Run

```powershell
dotnet run --project .\NetBuddies.App\NetBuddies.App.csproj
```

Standalone server:

```powershell
dotnet run --project .\NetBuddies.Server\NetBuddies.Server.csproj -- --port 5050
```

Published Windows server:

```powershell
NetBuddies.Server.exe --port 5050
```

Published Linux server:

```bash
./NetBuddies.Server --port 5050
./NetBuddies-Server-x86_64.AppImage --port 5050
```

The standalone server accepts `/stop` in its terminal window for clean shutdown.

## Internet Hosting

Recommended first step: use Tailscale or Cloudflare Tunnel and keep Net Buddies private to people you invite at the network layer. This avoids exposing your home IP and avoids raw port forwarding.

For secure direct hosting, create or supply a `.pfx` certificate, enable `Use TLS`, and set an invite code. The client must enable `Use TLS` and enter the same invite code. For self-signed certificates, keep `Trust self-signed` checked on the client.

The sign-in window can now generate a self-signed TLS certificate for you:

1. Enter or keep the generated certificate password.
2. Click `Generate TLS Certificate`.
3. Click `Start Server`.
4. Clients tick `Use TLS`, keep `Trust self-signed` enabled, and join using the server address/port.

Generate a local self-signed certificate for testing:

```powershell
.\packaging\generate-server-cert.ps1 -DnsName "netbuddies.local" -Password "change-this" -OutputPath ".\publish\netbuddies-server.pfx"
```

Standalone secure server example:

```powershell
NetBuddies.Server.exe --port 5050 --cert .\netbuddies-server.pfx --cert-password "change-this" --invite-code "friends-only"
```

Linux AppImage secure server example:

```bash
./NetBuddies-Server-x86_64.AppImage --port 5050 --cert ./netbuddies-server.pfx --cert-password "change-this" --invite-code "friends-only"
```

To test locally, run two copies of the app:

1. In the first copy, click `Start Server`, then `Join Local`.
2. In the second copy, use `127.0.0.1` and port `5050`, then click `Join Chat`.
3. Each app should see the other buddy online.

For people joining from another computer on the same network, use the host machine's LAN IP address instead of `127.0.0.1`. Internet hosting will usually need port forwarding or a relay server later.

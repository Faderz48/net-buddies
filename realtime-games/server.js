const http = require("http");
const fs = require("fs");
const path = require("path");
const { Server, Room } = require("colyseus");
const { WebSocketServer } = require("ws");

const port = Number.parseInt(process.env.NETBUDDIES_REALTIME_PORT || process.argv[2] || "2567", 10);
const bindAddress = process.env.NETBUDDIES_REALTIME_BIND || process.argv[3] || "0.0.0.0";
const pongPort = Number.parseInt(process.env.NETBUDDIES_PONG_PORT || process.argv[4] || String(port + 1), 10);
const gamesDirectory = process.env.NETBUDDIES_REALTIME_GAMES_DIR || path.join(__dirname, "games");
const detectedGames = loadGameManifests(gamesDirectory);
class NetBuddiesLobbyRoom extends Room {
  onCreate(options) {
    this.maxClients = Number.parseInt(options.maxClients || "16", 10);
    this.setMetadata({
      game: options.game || "Lobby",
      owner: options.owner || "Net Buddies",
      createdAt: new Date().toISOString()
    });
    this.onMessage("ready", (client, value) => {
      this.broadcast("ready", { sessionId: client.sessionId, ready: Boolean(value) });
    });
  }

  onJoin(client, options) {
    this.broadcast("playerJoined", {
      sessionId: client.sessionId,
      name: options.name || "Buddy"
    });
  }

  onLeave(client) {
    this.broadcast("playerLeft", { sessionId: client.sessionId });
  }
}

const httpServer = http.createServer((request, response) => {
  applyCors(response);
  if (request.method === "OPTIONS") {
    response.writeHead(204);
    response.end();
    return;
  }

  const requestUrl = new URL(request.url, `http://${request.headers.host || "127.0.0.1"}`);

  if (requestUrl.pathname === "/health") {
    response.writeHead(200, { "content-type": "application/json" });
    response.end(JSON.stringify({ ok: true, service: "netbuddies-realtime-games" }));
    return;
  }

  if (requestUrl.pathname === "/games") {
    response.writeHead(200, { "content-type": "application/json" });
    response.end(JSON.stringify({ games: detectedGames }));
    return;
  }

  if (requestUrl.pathname.startsWith("/games/")) {
    serveGameAsset(requestUrl, response);
    return;
  }

  if (requestUrl.pathname === "/") {
    response.writeHead(200, { "content-type": "text/plain" });
    response.end("Net Buddies real-time game server is running.\n");
    return;
  }

  response.writeHead(404, { "content-type": "text/plain" });
  response.end("Not found.\n");
});

const gameServer = new Server({ server: httpServer });
gameServer.define("lobby", NetBuddiesLobbyRoom);
const loadedAddonRooms = loadAddonRooms(gameServer, detectedGames);

gameServer.listen(port, bindAddress);
const pongServer = http.createServer((request, response) => {
  applyCors(response);
  if (request.method === "OPTIONS") {
    response.writeHead(204);
    response.end();
    return;
  }

  if (request.url === "/health") {
    response.writeHead(200, { "content-type": "application/json" });
    response.end(JSON.stringify({ ok: true, service: "netbuddies-game-relay", relays: ["buddy-pong", "web-game"] }));
    return;
  }

  if (request.url === "/games") {
    response.writeHead(200, { "content-type": "application/json" });
    response.end(JSON.stringify({ games: detectedGames }));
    return;
  }

  response.writeHead(200, { "content-type": "text/plain" });
  response.end("Net Buddies real-time relay server is running.\n");
});
attachWebGameRelay(pongServer);
pongServer.listen(pongPort, bindAddress);

console.log(`Net Buddies real-time game server listening on ${bindAddress}:${port}`);
console.log(`Health: http://127.0.0.1:${port}/health`);
console.log(`Web Game Relay: ws://127.0.0.1:${pongPort}/netbuddies/webgame/{gameId}/{roomId}`);
console.log(`Detected ${detectedGames.length} modular game${detectedGames.length === 1 ? "" : "s"} in ${gamesDirectory}`);
for (const game of detectedGames) {
  console.log(`Game: ${game.name} [${game.id}] runtime=${game.runtime} room=${game.room || "none"} client=${game.clientKind || "default"}`);
}
console.log(`Loaded ${loadedAddonRooms.length} addon Colyseus room${loadedAddonRooms.length === 1 ? "" : "s"}.`);
for (const room of loadedAddonRooms) {
  console.log(`Addon room: ${room.roomName} from ${room.gameId}`);
}

function loadGameManifests(rootDirectory) {
  if (!fs.existsSync(rootDirectory)) {
    return [];
  }

  return fs.readdirSync(rootDirectory, { withFileTypes: true })
    .filter((entry) => entry.isDirectory() && !entry.name.startsWith("_"))
    .map((entry) => readGameManifest(path.join(rootDirectory, entry.name), entry.name))
    .sort((left, right) => left.name.localeCompare(right.name));
}

function readGameManifest(folderPath, fallbackId) {
  const manifestPath = path.join(folderPath, "game.json");
  if (!fs.existsSync(manifestPath)) {
    return {
      id: fallbackId,
      name: fallbackId,
      runtime: "missing game.json",
      room: "",
      clientKind: "",
      folder: folderPath
    };
  }

  try {
    const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
    return {
      id: String(manifest.id || fallbackId),
      name: String(manifest.name || manifest.id || fallbackId),
      description: String(manifest.description || ""),
      runtime: String(manifest.runtime || "realtime"),
      room: String(manifest.room || ""),
      clientKind: String(manifest.clientKind || ""),
      serverPortOffset: Number.parseInt(manifest.serverPortOffset || "0", 10) || 0,
      clientEntry: String(manifest.clientEntry || "client/index.html"),
      serverEntry: String(manifest.serverEntry || "server/room.js"),
      folder: folderPath
    };
  } catch (error) {
    return {
      id: fallbackId,
      name: fallbackId,
      runtime: `invalid game.json: ${error.message}`,
      room: "",
      clientKind: "",
      folder: folderPath
    };
  }
}

function serveGameAsset(requestUrl, response) {
  const parts = requestUrl.pathname.split("/").filter(Boolean);
  const gameId = decodeURIComponent(parts[1] || "");
  const game = detectedGames.find((item) => item.id.toLowerCase() === gameId.toLowerCase());
  if (!game) {
    response.writeHead(404, { "content-type": "text/plain" });
    response.end("Game not found.\n");
    return;
  }

  const relativeParts = parts.slice(2).map((part) => decodeURIComponent(part));
  const relativePath = relativeParts.length === 0
    ? game.clientEntry || "client/index.html"
    : relativeParts.join(path.sep);
  const root = path.resolve(game.folder);
  const filePath = path.resolve(root, relativePath);
  if (filePath !== root && !filePath.startsWith(root + path.sep)) {
    response.writeHead(403, { "content-type": "text/plain" });
    response.end("Forbidden.\n");
    return;
  }

  fs.stat(filePath, (statError, stat) => {
    if (statError || !stat.isFile()) {
      response.writeHead(404, { "content-type": "text/plain" });
      response.end("Game asset not found.\n");
      return;
    }

    response.writeHead(200, {
      "content-type": contentTypeFor(filePath),
      "cache-control": "no-cache"
    });
    fs.createReadStream(filePath).pipe(response);
  });
}

function contentTypeFor(filePath) {
  switch (path.extname(filePath).toLowerCase()) {
    case ".html":
      return "text/html; charset=utf-8";
    case ".js":
      return "text/javascript; charset=utf-8";
    case ".css":
      return "text/css; charset=utf-8";
    case ".json":
      return "application/json; charset=utf-8";
    case ".png":
      return "image/png";
    case ".gif":
      return "image/gif";
    case ".jpg":
    case ".jpeg":
      return "image/jpeg";
    case ".svg":
      return "image/svg+xml";
    default:
      return "application/octet-stream";
  }
}

function applyCors(response) {
  response.setHeader("access-control-allow-origin", "*");
  response.setHeader("access-control-allow-methods", "GET, POST, OPTIONS");
  response.setHeader("access-control-allow-headers", "Content-Type, Authorization");
}

function loadAddonRooms(server, games) {
  const loaded = [];
  const roomNames = new Set(["lobby"]);
  for (const game of games) {
    const serverEntry = game.serverEntry || "server/room.js";
    const roomPath = path.resolve(game.folder, serverEntry);
    if (!fs.existsSync(roomPath)) {
      continue;
    }

    try {
      delete require.cache[require.resolve(roomPath)];
      const module = require(roomPath);
      const factory = module.createRoom;
      const RoomClass = typeof factory === "function"
        ? factory(createAddonSdk())
        : module.Room || module.default || module;
      const roomName = String(module.roomName || game.room || toSnakeCase(game.id));
      if (typeof RoomClass !== "function") {
        throw new Error("server/room.js must export a Room class.");
      }

      if (roomNames.has(roomName)) {
        throw new Error(`room name already registered: ${roomName}`);
      }

      server.define(roomName, RoomClass).filterBy(["roomKey"]);
      roomNames.add(roomName);
      loaded.push({ gameId: game.id, roomName, path: roomPath });
    } catch (error) {
      console.error(`Could not load addon room for ${game.id}: ${error.message}`);
    }
  }

  return loaded;
}

function toSnakeCase(value) {
  return String(value || "game")
    .replace(/([a-z0-9])([A-Z])/g, "$1_$2")
    .replace(/[^a-zA-Z0-9]+/g, "_")
    .replace(/^_+|_+$/g, "")
    .toLowerCase() || "game";
}

function attachWebGameRelay(server) {
  const sockets = new WebSocketServer({ noServer: true });
  const rooms = new Map();

  server.on("upgrade", (request, socket, head) => {
    const url = new URL(request.url, `http://${request.headers.host || "127.0.0.1"}`);
    if (!url.pathname.startsWith("/netbuddies/webgame/")) {
      return;
    }

    sockets.handleUpgrade(request, socket, head, (websocket) => {
      sockets.emit("connection", websocket, request, url);
    });
  });

  sockets.on("connection", (websocket, request, url) => {
    const parts = url.pathname.split("/").filter(Boolean);
    const gameId = decodeURIComponent(parts[2] || "game");
    const roomId = decodeURIComponent(parts[3] || "default");
    const name = url.searchParams.get("name") || "Buddy";
    const side = url.searchParams.get("side") || "player";
    const key = `${gameId}:${roomId}`;
    let room = rooms.get(key);
    if (!room) {
      room = { gameId, roomId, clients: [] };
      rooms.set(key, room);
    }

    const client = {
      id: Math.random().toString(36).slice(2),
      name,
      side,
      socket: websocket
    };
    room.clients.push(client);
    websocket.send(JSON.stringify({
      type: "hello",
      id: client.id,
      gameId,
      roomId,
      side,
      players: room.clients.map((item) => ({ id: item.id, name: item.name, side: item.side }))
    }));
    broadcastWebGame(room, {
      type: "playerJoined",
      id: client.id,
      name,
      side,
      players: room.clients.map((item) => ({ id: item.id, name: item.name, side: item.side }))
    }, client);

    websocket.on("message", (data) => {
      let payload;
      try {
        payload = JSON.parse(data.toString());
      } catch {
        payload = { type: "message", body: data.toString() };
      }

      broadcastWebGame(room, {
        type: "message",
        from: { id: client.id, name: client.name, side: client.side },
        body: payload
      }, client);
    });

    websocket.on("close", () => {
      room.clients = room.clients.filter((item) => item !== client);
      broadcastWebGame(room, {
        type: "playerLeft",
        id: client.id,
        players: room.clients.map((item) => ({ id: item.id, name: item.name, side: item.side }))
      }, client);
      if (room.clients.length === 0) {
        rooms.delete(key);
      }
    });
  });
}

function broadcastWebGame(room, payload, exceptClient) {
  const text = JSON.stringify(payload);
  for (const client of room.clients) {
    if (client === exceptClient) {
      continue;
    }

    if (client.socket.readyState === client.socket.OPEN) {
      client.socket.send(text);
    }
  }
}

function createAddonSdk() {
  return {
    Room,
    now: () => Date.now(),
    clamp
  };
}

function clamp(value, min, max) {
  if (!Number.isFinite(value)) {
    return min;
  }

  return Math.max(min, Math.min(max, value));
}

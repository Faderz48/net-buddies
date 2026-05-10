const http = require("http");
const { Server, Room } = require("colyseus");
const { WebSocketServer } = require("ws");

const port = Number.parseInt(process.env.NETBUDDIES_REALTIME_PORT || process.argv[2] || "2567", 10);
const bindAddress = process.env.NETBUDDIES_REALTIME_BIND || process.argv[3] || "0.0.0.0";
const pongPort = Number.parseInt(process.env.NETBUDDIES_PONG_PORT || process.argv[4] || String(port + 1), 10);
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

class BuddyPongRoom extends Room {
  onCreate(options) {
    this.maxClients = 2;
    this.setMetadata({ game: "Buddy Pong", owner: options.owner || "Net Buddies" });
    this.players = new Map();
    this.state = {
      phase: "waiting",
      ball: { x: 0.5, y: 0.5, vx: 0.006, vy: 0.004 },
      paddles: {},
      score: {}
    };

    this.onMessage("input", (client, input) => {
      const player = this.players.get(client.sessionId);
      if (!player) {
        return;
      }

      player.move = Math.max(-1, Math.min(1, Number(input.move || 0)));
    });

    this.setSimulationInterval(() => this.tick(), 1000 / 30);
  }

  onJoin(client, options) {
    if (this.players.size >= 2) {
      client.leave();
      return;
    }

    const side = this.players.size === 0 ? "left" : "right";
    this.players.set(client.sessionId, {
      name: options.name || "Buddy",
      side,
      move: 0,
      y: 0.5
    });
    this.state.paddles[client.sessionId] = { side, y: 0.5, name: options.name || "Buddy" };
    this.state.score[client.sessionId] = 0;
    this.broadcastState();

    if (this.players.size === 2) {
      this.state.phase = "playing";
      this.broadcast("start", {});
    }
  }

  onLeave(client) {
    this.players.delete(client.sessionId);
    delete this.state.paddles[client.sessionId];
    delete this.state.score[client.sessionId];
    this.state.phase = "waiting";
    this.broadcastState();
  }

  tick() {
    if (this.state.phase !== "playing") {
      return;
    }

    for (const player of this.players.values()) {
      player.y = Math.max(0.08, Math.min(0.92, player.y + player.move * 0.018));
    }

    for (const [sessionId, player] of this.players.entries()) {
      this.state.paddles[sessionId] = { side: player.side, y: player.y, name: player.name };
    }

    const ball = this.state.ball;
    ball.x += ball.vx;
    ball.y += ball.vy;

    if (ball.y <= 0.03 || ball.y >= 0.97) {
      ball.vy *= -1;
    }

    const left = [...this.players.values()].find((player) => player.side === "left");
    const right = [...this.players.values()].find((player) => player.side === "right");
    if (left && ball.x <= 0.08 && Math.abs(ball.y - left.y) < 0.13) {
      ball.vx = Math.abs(ball.vx) + 0.0003;
    }

    if (right && ball.x >= 0.92 && Math.abs(ball.y - right.y) < 0.13) {
      ball.vx = -Math.abs(ball.vx) - 0.0003;
    }

    if (ball.x < 0 || ball.x > 1) {
      const scorer = [...this.players.entries()].find(([, player]) =>
        ball.x < 0 ? player.side === "right" : player.side === "left");
      if (scorer) {
        this.state.score[scorer[0]] += 1;
      }
      ball.x = 0.5;
      ball.y = 0.5;
      ball.vx = ball.x < 0 ? 0.006 : -0.006;
      ball.vy = Math.random() > 0.5 ? 0.004 : -0.004;
    }

    this.broadcastState();
  }

  broadcastState() {
    this.broadcast("state", this.state);
  }
}

class SnakeBattleRoom extends Room {
  onCreate(options) {
    this.maxClients = 4;
    this.setMetadata({ game: "Snake Battle", owner: options.owner || "Net Buddies" });
    this.players = new Map();
    this.onMessage("input", (client, input) => {
      const player = this.players.get(client.sessionId);
      if (!player) {
        return;
      }

      const direction = String(input.direction || "");
      if (["up", "down", "left", "right"].includes(direction)) {
        player.direction = direction;
      }
    });
    this.setSimulationInterval(() => this.tick(), 1000 / 10);
  }

  onJoin(client, options) {
    this.players.set(client.sessionId, {
      name: options.name || "Buddy",
      direction: "right",
      body: [{ x: Math.floor(Math.random() * 18) + 1, y: Math.floor(Math.random() * 18) + 1 }],
      alive: true,
      score: 0
    });
    this.broadcastState();
  }

  onLeave(client) {
    this.players.delete(client.sessionId);
    this.broadcastState();
  }

  tick() {
    for (const player of this.players.values()) {
      if (!player.alive) {
        continue;
      }

      const head = { ...player.body[0] };
      if (player.direction === "up") head.y -= 1;
      if (player.direction === "down") head.y += 1;
      if (player.direction === "left") head.x -= 1;
      if (player.direction === "right") head.x += 1;

      if (head.x < 0 || head.x >= 20 || head.y < 0 || head.y >= 20) {
        player.alive = false;
        continue;
      }

      player.body.unshift(head);
      player.body = player.body.slice(0, 6);
      player.score += 1;
    }

    this.broadcastState();
  }

  broadcastState() {
    this.broadcast("state", {
      players: Object.fromEntries(this.players.entries())
    });
  }
}

const httpServer = http.createServer((request, response) => {
  if (request.url === "/health") {
    response.writeHead(200, { "content-type": "application/json" });
    response.end(JSON.stringify({ ok: true, service: "netbuddies-realtime-games" }));
    return;
  }

  response.writeHead(200, { "content-type": "text/plain" });
  response.end("Net Buddies real-time game server is running.\n");
});

const gameServer = new Server({ server: httpServer });
gameServer.define("lobby", NetBuddiesLobbyRoom);
gameServer.define("buddy_pong", BuddyPongRoom);
gameServer.define("snake_battle", SnakeBattleRoom);

gameServer.listen(port, bindAddress);
const pongServer = http.createServer((request, response) => {
  if (request.url === "/health") {
    response.writeHead(200, { "content-type": "application/json" });
    response.end(JSON.stringify({ ok: true, service: "netbuddies-buddy-pong" }));
    return;
  }

  response.writeHead(200, { "content-type": "text/plain" });
  response.end("Net Buddies Buddy Pong server is running.\n");
});
attachNetBuddiesPong(pongServer);
pongServer.listen(pongPort, bindAddress);

console.log(`Net Buddies real-time game server listening on ${bindAddress}:${port}`);
console.log(`Health: http://127.0.0.1:${port}/health`);
console.log(`Buddy Pong WebSocket: ws://127.0.0.1:${pongPort}/netbuddies/pong/{roomId}`);

function attachNetBuddiesPong(server) {
  const sockets = new WebSocketServer({ noServer: true });
  const rooms = new Map();

  server.on("upgrade", (request, socket, head) => {
    const url = new URL(request.url, `http://${request.headers.host || "127.0.0.1"}`);
    if (!url.pathname.startsWith("/netbuddies/pong/")) {
      return;
    }

    sockets.handleUpgrade(request, socket, head, (websocket) => {
      sockets.emit("connection", websocket, request, url);
    });
  });

  sockets.on("connection", (websocket, request, url) => {
    const roomId = decodeURIComponent(url.pathname.split("/").pop() || "default");
    const name = url.searchParams.get("name") || "Buddy";
    const requestedSide = url.searchParams.get("side") === "left" ? "left" : "right";
    const room = getPongRoom(rooms, roomId);
    const side = room.clients.some((client) => client.side === requestedSide)
      ? requestedSide === "left" ? "right" : "left"
      : requestedSide;
    const client = {
      id: Math.random().toString(36).slice(2),
      name,
      side,
      socket: websocket,
      move: 0,
      y: 0.5,
      score: 0
    };

    room.clients.push(client);
    websocket.send(JSON.stringify({ type: "hello", side, roomId }));
    broadcastPong(room);

    websocket.on("message", (data) => {
      try {
        const message = JSON.parse(data.toString());
        if (message.type === "input") {
          client.move = Math.max(-1, Math.min(1, Number(message.move || 0)));
        }
      } catch {
      }
    });

    websocket.on("close", () => {
      room.clients = room.clients.filter((item) => item !== client);
      if (room.clients.length === 0) {
        clearInterval(room.timer);
        rooms.delete(roomId);
      } else {
        broadcastPong(room);
      }
    });
  });
}

function getPongRoom(rooms, roomId) {
  let room = rooms.get(roomId);
  if (room) {
    return room;
  }

  room = {
    id: roomId,
    ball: { x: 0.5, y: 0.5, vx: 0.008, vy: 0.005 },
    clients: [],
    timer: null
  };
  room.timer = setInterval(() => tickPong(room), 1000 / 30);
  rooms.set(roomId, room);
  return room;
}

function tickPong(room) {
  const left = room.clients.find((client) => client.side === "left");
  const right = room.clients.find((client) => client.side === "right");
  for (const client of room.clients) {
    client.y = Math.max(0.12, Math.min(0.88, client.y + client.move * 0.026));
  }

  if (left && right) {
    const ball = room.ball;
    ball.x += ball.vx;
    ball.y += ball.vy;

    if (ball.y <= 0.035 || ball.y >= 0.965) {
      ball.vy *= -1;
      ball.y = Math.max(0.035, Math.min(0.965, ball.y));
    }

    if (ball.x <= 0.08 && ball.vx < 0 && Math.abs(ball.y - left.y) <= 0.14) {
      ball.vx = Math.min(0.015, Math.abs(ball.vx) + 0.0006);
      ball.vy += (ball.y - left.y) * 0.045;
    }

    if (ball.x >= 0.92 && ball.vx > 0 && Math.abs(ball.y - right.y) <= 0.14) {
      ball.vx = -Math.min(0.015, Math.abs(ball.vx) + 0.0006);
      ball.vy += (ball.y - right.y) * 0.045;
    }

    if (ball.x < -0.03 || ball.x > 1.03) {
      if (ball.x < -0.03 && right) {
        right.score += 1;
      } else if (ball.x > 1.03 && left) {
        left.score += 1;
      }

      ball.x = 0.5;
      ball.y = 0.5;
      ball.vx = ball.x < -0.03 ? 0.008 : (Math.random() > 0.5 ? 0.008 : -0.008);
      ball.vy = Math.random() > 0.5 ? 0.005 : -0.005;
    }
  }

  broadcastPong(room);
}

function broadcastPong(room) {
  const payload = JSON.stringify({
    type: "state",
    ball: room.ball,
    players: room.clients.map((client) => ({
      name: client.name,
      side: client.side,
      y: client.y,
      score: client.score
    }))
  });

  for (const client of room.clients) {
    if (client.socket.readyState === client.socket.OPEN) {
      client.socket.send(payload);
    }
  }
}

function clamp(value, min, max) {
  if (!Number.isFinite(value)) {
    return min;
  }

  return Math.max(min, Math.min(max, value));
}

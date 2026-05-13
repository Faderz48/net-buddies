exports.roomName = "buddy_driller";

exports.createRoom = ({ Room, clamp }) => class BuddyDrillerRoom extends Room {
  onCreate(options) {
    this.maxClients = 2;
    this.setMetadata({ game: "Buddy Driller", owner: options?.owner || "Net Buddies", roomKey: options?.roomKey || options?.roomId || "" });
    this.width = 13;
    this.totalRows = 260;
    this.viewRows = 18;
    this.phase = "waiting";
    this.reason = "";
    this.winnerSessionId = "";
    this.players = new Map();
    this.rows = Array.from({ length: this.totalRows }, (_, y) => this.createRow(y));
    this.tickCount = 0;

    this.onMessage("input", (client, input) => this.safeHandleInput(client, input));
    this.onMessage("end", (client) => this.killPlayer(client.sessionId, "ended"));
    this.setSimulationInterval(() => this.safeTick(), 1000 / 10);
  }

  safeHandleInput(client, input) {
    try {
      this.handleInput(client, input);
    } catch (error) {
      console.error(`BuddyDriller input error: ${error.message}`);
    }
  }

  onJoin(client, options) {
    if (this.players.size >= 2) {
      client.leave();
      return;
    }

    const slot = this.players.size;
    const x = slot === 0 ? Math.floor(this.width / 2) - 2 : Math.floor(this.width / 2) + 2;
    const player = {
      name: String(options.name || "Buddy"),
      x,
      y: 1,
      air: 100,
      alive: true,
      depth: 0,
      maxDepth: 0,
      color: slot === 0 ? "#ffdf5a" : "#74e2ff",
      direction: "down",
      lastAction: 0,
      reason: ""
    };
    this.players.set(client.sessionId, player);
    client.send("hello", { slot, roomId: this.roomId });

    if (this.players.size === 2) {
      this.phase = "playing";
      this.broadcast("start", {});
    }

    this.broadcastState();
  }

  onLeave(client) {
    if (this.phase === "playing") {
      const remaining = [...this.players.keys()].find((id) => id !== client.sessionId);
      if (remaining) {
        this.endGame("opponent_left", remaining);
      }
    }

    this.players.delete(client.sessionId);
    if (this.phase !== "ended") {
      this.phase = "waiting";
    }
    this.broadcastState();
  }

  createRow(y) {
    if (y < 3) {
      return Array(this.width).fill(0);
    }

    const row = [];
    for (let x = 0; x < this.width; x++) {
      const r = this.randomFor(x, y);
      if (y % 15 === 0 && x === 2 + Math.floor(this.randomFor(y, x) * (this.width - 4))) {
        row.push(9);
      } else if (r > 0.93 && y > 16) {
        row.push(6);
      } else {
        row.push(1 + Math.floor(r * 5) % 5);
      }
    }
    return row;
  }

  randomFor(x, y) {
    const value = Math.sin((x + 1) * 127.1 + (y + 3) * 311.7) * 43758.5453;
    return value - Math.floor(value);
  }

  handleInput(client, input) {
    if (this.phase !== "playing") {
      return;
    }

    const player = this.players.get(client.sessionId);
    if (!player || !player.alive) {
      return;
    }

    const now = Date.now();
    if (now - player.lastAction < 45) {
      return;
    }
    player.lastAction = now;

    const action = String(input.action || "");
    const direction = String(input.direction || "down");
    const delta = this.directionDelta(direction);
    if (!delta) {
      return;
    }
    player.direction = direction;

    const targetX = player.x + delta.dx;
    const targetY = player.y + delta.dy;
    if (!this.isInside(targetX, targetY)) {
      return;
    }

    const cell = this.cellAt(targetX, targetY);
    if (cell === 0 || cell === 9) {
      this.movePlayer(player, targetX, targetY, cell);
      return;
    }

    if (action === "drill" || direction === "down" || direction === "left" || direction === "right" || direction === "up") {
      this.drill(targetX, targetY, cell, player);
    }
  }

  directionDelta(direction) {
    if (direction === "left") return { dx: -1, dy: 0 };
    if (direction === "right") return { dx: 1, dy: 0 };
    if (direction === "up") return { dx: 0, dy: -1 };
    if (direction === "down") return { dx: 0, dy: 1 };
    return null;
  }

  movePlayer(player, x, y, cell) {
    if (this.playerAt(x, y)) {
      return;
    }

    player.x = x;
    player.y = y;
    player.depth = Math.max(0, y - 1);
    player.maxDepth = Math.max(player.maxDepth, player.depth);
    if (cell === 9) {
      player.air = clamp(player.air + 30, 0, 100);
      this.setCell(x, y, 0);
    }
  }

  drill(x, y, cell, player) {
    if (cell === 6) {
      player.air = clamp(player.air - 4, 0, 100);
      this.setCell(x, y, 0);
      return;
    }

    const cluster = this.collectCluster(x, y, cell);
    const targets = cluster.length >= 4 ? cluster : [{ x, y }];
    for (const target of targets) {
      this.setCell(target.x, target.y, 0);
    }
    player.air = clamp(player.air - (cluster.length >= 4 ? 0.6 : 1.2), 0, 100);
  }

  collectCluster(startX, startY, color) {
    const seen = new Set();
    const pending = [{ x: startX, y: startY }];
    const result = [];
    while (pending.length > 0) {
      const item = pending.pop();
      const key = `${item.x},${item.y}`;
      if (seen.has(key) || this.cellAt(item.x, item.y) !== color) {
        continue;
      }

      seen.add(key);
      result.push(item);
      pending.push({ x: item.x - 1, y: item.y });
      pending.push({ x: item.x + 1, y: item.y });
      pending.push({ x: item.x, y: item.y - 1 });
      pending.push({ x: item.x, y: item.y + 1 });
    }
    return result;
  }

  tick() {
    if (this.phase !== "playing") {
      this.broadcastState();
      return;
    }

    this.tickCount += 1;
    for (const [sessionId, player] of this.players.entries()) {
      if (!player.alive) {
        continue;
      }

      player.air = clamp(player.air - 0.075, 0, 100);
      if (player.air <= 0) {
        this.killPlayer(sessionId, "air");
      }
    }

    if (this.tickCount % 3 === 0) {
      this.applyGravity();
    }

    this.checkGameOver();
    this.broadcastState();
  }

  safeTick() {
    try {
      this.tick();
    } catch (error) {
      console.error(`BuddyDriller tick error: ${error.message}`);
      this.endGame("server_error", "");
    }
  }

  applyGravity() {
    for (let y = this.totalRows - 2; y >= 0; y--) {
      for (let x = 0; x < this.width; x++) {
        const cell = this.rows[y][x];
        if (cell === 0 || cell === 9) {
          continue;
        }

        if (this.rows[y + 1][x] !== 0) {
          continue;
        }

        const playerBelow = this.playerAt(x, y + 1);
        if (playerBelow) {
          this.killPlayer(playerBelow.sessionId, "crushed");
        }

        this.rows[y + 1][x] = cell;
        this.rows[y][x] = 0;
      }
    }
  }

  playerAt(x, y) {
    for (const [sessionId, player] of this.players.entries()) {
      if (player.alive && player.x === x && player.y === y) {
        return { sessionId, player };
      }
    }
    return null;
  }

  killPlayer(sessionId, reason) {
    const player = this.players.get(sessionId);
    if (!player || !player.alive) {
      return;
    }

    player.alive = false;
    player.reason = reason;
    player.maxDepth = Math.max(player.maxDepth, player.depth);
  }

  checkGameOver() {
    if (this.phase !== "playing") {
      return;
    }

    const players = [...this.players.entries()];
    if (players.length < 2) {
      return;
    }

    if (players.every(([, player]) => !player.alive || player.y >= this.totalRows - 3)) {
      let winnerSessionId = "";
      let bestDepth = -1;
      for (const [sessionId, player] of players) {
        if (player.maxDepth > bestDepth) {
          bestDepth = player.maxDepth;
          winnerSessionId = sessionId;
        } else if (player.maxDepth === bestDepth) {
          winnerSessionId = "";
        }
      }

      this.endGame("deepest_run", winnerSessionId);
    }
  }

  endGame(reason, winnerSessionId) {
    if (this.phase === "ended") {
      return;
    }

    this.phase = "ended";
    this.reason = reason;
    this.winnerSessionId = winnerSessionId || "";
    this.broadcast("gameOver", {
      reason,
      winnerSessionId: this.winnerSessionId,
      players: Object.fromEntries(this.players.entries())
    });
    this.broadcastState();
  }

  broadcastState() {
    const players = Object.fromEntries([...this.players.entries()].map(([sessionId, player]) => [sessionId, {
      name: player.name,
      x: player.x,
      y: player.y,
      air: Math.round(player.air),
      alive: player.alive,
      depth: player.depth,
      maxDepth: player.maxDepth,
      color: player.color,
      direction: player.direction || "down",
      reason: player.reason
    }]));
    const cameraTop = this.cameraTop();
    const visibleRows = this.rows.slice(cameraTop, cameraTop + this.viewRows).map((row) => row.slice());
    this.broadcast("state", {
      width: this.width,
      totalRows: this.totalRows,
      viewRows: this.viewRows,
      cameraTop,
      rows: visibleRows,
      phase: this.phase,
      reason: this.reason,
      winnerSessionId: this.winnerSessionId,
      players
    });
  }

  cameraTop() {
    let deepest = 0;
    for (const player of this.players.values()) {
      deepest = Math.max(deepest, player.y);
    }

    return clamp(Math.floor(deepest - 6), 0, this.totalRows - this.viewRows);
  }

  cellAt(x, y) {
    return this.isInside(x, y) ? this.rows[y][x] : 6;
  }

  setCell(x, y, value) {
    if (this.isInside(x, y)) {
      this.rows[y][x] = value;
    }
  }

  isInside(x, y) {
    return x >= 0 && x < this.width && y >= 0 && y < this.totalRows;
  }
};

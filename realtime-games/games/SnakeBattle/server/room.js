exports.roomName = "snake_battle";

exports.createRoom = ({ Room }) => class SnakeBattleRoom extends Room {
  onCreate(options) {
    this.maxClients = 4;
    this.setMetadata({ game: "Snake Battle", owner: options?.owner || "Net Buddies", roomKey: options?.roomKey || options?.roomId || "" });
    this.players = new Map();
    this.phase = "waiting";
    this.winnerSessionId = "";
    this.reason = "";
    this.food = this.randomFood();
    this.onMessage("input", (client, input) => {
      const player = this.players.get(client.sessionId);
      if (!player) {
        return;
      }

      const direction = String(input.direction || "");
      if (this.canTurn(player.direction, direction)) {
        player.nextDirection = direction;
      }
    });
    this.setSimulationInterval(() => this.tick(), 1000 / 10);
  }

  onJoin(client, options) {
    const spawn = this.randomOpenCell();
    this.players.set(client.sessionId, {
      name: options.name || "Buddy",
      direction: "right",
      nextDirection: "right",
      body: [spawn],
      alive: true,
      score: 0,
      color: this.pickColor(this.players.size)
    });
    if (this.players.size >= 2) {
      this.phase = "playing";
    }
    client.send("hello", { roomId: this.roomId });
    this.broadcastState();
  }

  onLeave(client) {
    if (this.phase === "playing") {
      const remaining = [...this.players.keys()].find((id) => id !== client.sessionId);
      this.endGame("opponent_left", remaining || "");
    }
    this.players.delete(client.sessionId);
    this.broadcastState();
  }

  tick() {
    if (this.phase !== "playing") {
      return;
    }

    const occupied = new Set();
    for (const player of this.players.values()) {
      for (const part of player.body) {
        occupied.add(`${part.x},${part.y}`);
      }
    }

    for (const player of this.players.values()) {
      if (!player.alive) {
        continue;
      }

      player.direction = player.nextDirection || player.direction;
      const head = { ...player.body[0] };
      if (player.direction === "up") head.y -= 1;
      if (player.direction === "down") head.y += 1;
      if (player.direction === "left") head.x -= 1;
      if (player.direction === "right") head.x += 1;

      if (head.x < 0 || head.x >= 24 || head.y < 0 || head.y >= 18 || occupied.has(`${head.x},${head.y}`)) {
        player.alive = false;
        continue;
      }

      player.body.unshift(head);
      occupied.add(`${head.x},${head.y}`);
      if (head.x === this.food.x && head.y === this.food.y) {
        player.score += 1;
        this.food = this.randomOpenCell();
      } else {
        const tail = player.body.pop();
        occupied.delete(`${tail.x},${tail.y}`);
      }
    }

    this.checkGameOver();
    this.broadcastState();
  }

  canTurn(current, next) {
    if (!["up", "down", "left", "right"].includes(next)) {
      return false;
    }

    return !((current === "up" && next === "down")
      || (current === "down" && next === "up")
      || (current === "left" && next === "right")
      || (current === "right" && next === "left"));
  }

  randomOpenCell() {
    return {
      x: Math.floor(Math.random() * 24),
      y: Math.floor(Math.random() * 18)
    };
  }

  randomFood() {
    return this.randomOpenCell();
  }

  pickColor(index) {
    return ["#90f46d", "#ffcc4f", "#71c7ff", "#ff7aa7"][index % 4];
  }

  broadcastState() {
    this.broadcast("state", {
      food: this.food,
      phase: this.phase,
      winnerSessionId: this.winnerSessionId,
      reason: this.reason,
      players: Object.fromEntries(this.players.entries())
    });
  }

  checkGameOver() {
    const alive = [...this.players.entries()].filter(([, player]) => player.alive);
    if (this.players.size >= 2 && alive.length <= 1) {
      this.endGame("last_snake", alive[0]?.[0] || "");
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
      winnerSessionId: this.winnerSessionId
    });
  }
};

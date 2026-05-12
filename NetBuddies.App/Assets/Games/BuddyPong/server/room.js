exports.roomName = "buddy_pong";

exports.createRoom = ({ Room, clamp }) => class BuddyPongRoom extends Room {
  onCreate(options) {
    this.maxClients = 2;
    this.setMetadata({ game: "Buddy Pong", owner: options?.owner || "Net Buddies", roomKey: options?.roomKey || options?.roomId || "" });
    this.players = new Map();
    this.state = {
      phase: "waiting",
      ball: { x: 0.5, y: 0.5, vx: 0.008, vy: 0.005 },
      paddles: {},
      score: {},
      winnerSessionId: "",
      reason: ""
    };

    this.onMessage("input", (client, input) => {
      const player = this.players.get(client.sessionId);
      if (!player) {
        return;
      }

      player.move = clamp(Number(input.move || 0), -1, 1);
    });
    this.onMessage("end", (client) => this.endGame("ended", client.sessionId));

    this.setSimulationInterval(() => this.tick(), 1000 / 30);
  }

  onJoin(client, options) {
    if (this.players.size >= 2) {
      client.leave();
      return;
    }

    const side = this.players.size === 0 ? "left" : "right";
    const name = String(options.name || "Buddy");
    this.players.set(client.sessionId, {
      name,
      side,
      move: 0,
      y: 0.5
    });
    this.state.paddles[client.sessionId] = { side, y: 0.5, name };
    this.state.score[client.sessionId] = 0;
    client.send("hello", { side, roomId: this.roomId });

    if (this.players.size === 2) {
      this.state.phase = "playing";
      this.broadcast("start", {});
    }

    this.broadcastState();
  }

  onLeave(client) {
    if (this.state.phase === "playing") {
      const remaining = [...this.players.keys()].find((id) => id !== client.sessionId);
      this.endGame("opponent_left", remaining || "");
    }
    this.players.delete(client.sessionId);
    delete this.state.paddles[client.sessionId];
    delete this.state.score[client.sessionId];
    if (this.state.phase !== "ended") {
      this.state.phase = "waiting";
    }
    this.resetBall();
    this.broadcastState();
  }

  tick() {
    if (this.state.phase !== "playing") {
      return;
    }

    for (const player of this.players.values()) {
      player.y = clamp(player.y + player.move * 0.026, 0.12, 0.88);
    }

    for (const [sessionId, player] of this.players.entries()) {
      this.state.paddles[sessionId] = { side: player.side, y: player.y, name: player.name };
    }

    const ball = this.state.ball;
    ball.x += ball.vx;
    ball.y += ball.vy;

    if (ball.y <= 0.035 || ball.y >= 0.965) {
      ball.vy *= -1;
      ball.y = clamp(ball.y, 0.035, 0.965);
    }

    const left = [...this.players.values()].find((player) => player.side === "left");
    const right = [...this.players.values()].find((player) => player.side === "right");
    if (left && ball.x <= 0.08 && ball.vx < 0 && Math.abs(ball.y - left.y) <= 0.14) {
      ball.vx = Math.min(0.015, Math.abs(ball.vx) + 0.0006);
      ball.vy += (ball.y - left.y) * 0.045;
    }

    if (right && ball.x >= 0.92 && ball.vx > 0 && Math.abs(ball.y - right.y) <= 0.14) {
      ball.vx = -Math.min(0.015, Math.abs(ball.vx) + 0.0006);
      ball.vy += (ball.y - right.y) * 0.045;
    }

    if (ball.x < -0.03 || ball.x > 1.03) {
      const scoredSide = ball.x < -0.03 ? "right" : "left";
      const scorer = [...this.players.entries()].find(([, player]) => player.side === scoredSide);
      if (scorer) {
        this.state.score[scorer[0]] += 1;
        if (this.state.score[scorer[0]] >= 5) {
          this.endGame("score_limit", scorer[0]);
          return;
        }
      }

      this.resetBall(scoredSide === "left" ? 1 : -1);
    }

    this.broadcastState();
  }

  resetBall(direction) {
    this.state.ball.x = 0.5;
    this.state.ball.y = 0.5;
    this.state.ball.vx = (direction || (Math.random() > 0.5 ? 1 : -1)) * 0.008;
    this.state.ball.vy = (Math.random() > 0.5 ? 1 : -1) * 0.005;
  }

  broadcastState() {
    this.broadcast("state", this.state);
  }

  endGame(reason, winnerSessionId) {
    if (this.state.phase === "ended") {
      return;
    }

    this.state.phase = "ended";
    this.state.reason = reason;
    this.state.winnerSessionId = winnerSessionId || "";
    this.broadcast("gameOver", {
      reason,
      winnerSessionId: this.state.winnerSessionId,
      score: this.state.score
    });
    this.broadcastState();
  }
};

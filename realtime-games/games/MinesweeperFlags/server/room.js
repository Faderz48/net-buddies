const { Room } = require("colyseus");
const BOARD_SIZE = 16;
const MINES_TOTAL = 48;

class MinesweeperFlagsRoom extends Room {
  onCreate(options) {
    this.maxClients = 2;
    this.setMetadata({ game: "Minesweeper Flags", addon: "MinesweeperFlags", roomKey: options?.roomKey || options?.roomId || "" });
    this.players = new Map();
    this.mineSet = new Set();
    this.revealed = Array(BOARD_SIZE * BOARD_SIZE).fill(false);
    this.flaggedBy = Array(BOARD_SIZE * BOARD_SIZE).fill("");
    this.turnSessionId = "";
    this.phase = "waiting";
    this.winnerSessionId = "";
    this.reason = "";

    while (this.mineSet.size < MINES_TOTAL) {
      this.mineSet.add(Math.floor(Math.random() * BOARD_SIZE * BOARD_SIZE));
    }

    this.onMessage("move", (client, message) => {
      if (message?.type === "click") {
        this.clickCell(client, Number.parseInt(message.index ?? "-1", 10));
      }
    });
  }

  onJoin(client, options) {
    if (this.players.size >= 2) {
      client.leave();
      return;
    }

    const side = this.players.size === 0 ? "red" : "blue";
    this.players.set(client.sessionId, {
      name: options?.name || "Buddy",
      side,
      score: 0
    });

    if (!this.turnSessionId) {
      this.turnSessionId = client.sessionId;
    }

    if (this.players.size === 2) {
      this.phase = "playing";
    }

    client.send("hello", { side, roomId: this.roomId });
    this.broadcastState();
  }

  onLeave(client) {
    if (this.phase !== "ended" && this.players.size > 1) {
      this.phase = "ended";
      this.reason = "opponent_left";
      const remaining = this.clients.find((item) => item.sessionId !== client.sessionId);
      this.winnerSessionId = remaining?.sessionId || "";
      this.broadcast("gameOver", this.buildResult());
    }

    this.players.delete(client.sessionId);
    this.broadcastState();
  }

  clickCell(client, index) {
    if (this.phase !== "playing"
      || client.sessionId !== this.turnSessionId
      || index < 0
      || index >= BOARD_SIZE * BOARD_SIZE
      || this.revealed[index]
      || this.flaggedBy[index]) {
      return;
    }

    if (this.mineSet.has(index)) {
      this.flaggedBy[index] = client.sessionId;
      const player = this.players.get(client.sessionId);
      if (player) {
        player.score += 1;
      }
    } else {
      this.revealed[index] = true;
    }

    this.advanceTurn();
    this.checkGameOver();
    this.broadcastState();
  }

  advanceTurn() {
    const active = [...this.players.keys()];
    if (active.length < 2) {
      return;
    }

    const currentIndex = active.indexOf(this.turnSessionId);
    this.turnSessionId = active[(currentIndex + 1) % active.length] || active[0];
  }

  checkGameOver() {
    if (this.flaggedBy.filter(Boolean).length < MINES_TOTAL) {
      return;
    }

    this.phase = "ended";
    this.reason = "all_mines_found";
    const scores = [...this.players.entries()].sort((left, right) => right[1].score - left[1].score);
    this.winnerSessionId = scores.length > 1 && scores[0][1].score === scores[1][1].score ? "" : scores[0]?.[0] || "";
    this.broadcast("gameOver", this.buildResult());
  }

  adjacent(index) {
    const x = index % BOARD_SIZE;
    const y = Math.floor(index / BOARD_SIZE);
    let count = 0;
    for (let yy = y - 1; yy <= y + 1; yy++) {
      for (let xx = x - 1; xx <= x + 1; xx++) {
        if (xx === x && yy === y) {
          continue;
        }

        if (xx >= 0 && xx < BOARD_SIZE && yy >= 0 && yy < BOARD_SIZE && this.mineSet.has(yy * BOARD_SIZE + xx)) {
          count++;
        }
      }
    }

    return count;
  }

  buildState() {
    return {
      size: BOARD_SIZE,
      minesTotal: MINES_TOTAL,
      phase: this.phase,
      turnSessionId: this.turnSessionId,
      winnerSessionId: this.winnerSessionId,
      reason: this.reason,
      players: Object.fromEntries(this.players.entries()),
      cells: this.revealed.map((open, index) => ({
        open,
        count: open ? this.adjacent(index) : 0,
        flaggedBy: this.flaggedBy[index]
      }))
    };
  }

  buildResult() {
    return {
      phase: this.phase,
      winnerSessionId: this.winnerSessionId,
      reason: this.reason,
      players: Object.fromEntries(this.players.entries())
    };
  }

  broadcastState() {
    this.broadcast("state", this.buildState());
  }
}

module.exports = {
  roomName: "minesweeper_flags",
  Room: MinesweeperFlagsRoom
};

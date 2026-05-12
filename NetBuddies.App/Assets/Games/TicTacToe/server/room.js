const { Room } = require("colyseus");

class TicTacToeRoom extends Room {
  onCreate(options) {
    this.maxClients = 2;
    this.setMetadata({ game: "Tic Tac Toe", addon: "TicTacToe", roomKey: options?.roomKey || options?.roomId || "" });
    this.players = new Map();
    this.board = Array(9).fill("");
    this.turn = "X";
    this.phase = "waiting";
    this.winnerSymbol = "";
    this.reason = "";
    this.onMessage("move", (client, message) => {
      const player = this.players.get(client.sessionId);
      const index = Number.parseInt(message?.index ?? "-1", 10);
      if (this.phase !== "playing" || !player || player.symbol !== this.turn || index < 0 || index >= 9 || this.board[index]) {
        return;
      }

      this.board[index] = player.symbol;
      this.checkGameOver(player.symbol);
      if (this.phase !== "ended") {
        this.turn = this.turn === "X" ? "O" : "X";
      }
      this.broadcastState({ index, symbol: player.symbol });
    });
  }

  onJoin(client, options) {
    const symbol = this.players.size === 0 ? "X" : "O";
    this.players.set(client.sessionId, {
      name: options?.name || "Buddy",
      symbol
    });
    if (this.players.size === 2) {
      this.phase = "playing";
    }
    client.send("hello", {
      symbol,
      board: this.board,
      turn: this.turn,
      phase: this.phase
    });
    this.broadcastState();
  }

  onLeave(client) {
    if (this.phase !== "ended" && this.players.size > 1) {
      const remaining = [...this.players.entries()].find(([id]) => id !== client.sessionId);
      this.phase = "ended";
      this.reason = "opponent_left";
      this.winnerSymbol = remaining?.[1]?.symbol || "";
      this.broadcast("gameOver", this.buildResult());
    }
    this.players.delete(client.sessionId);
    this.broadcastState();
  }

  checkGameOver(symbol) {
    const lines = [[0,1,2],[3,4,5],[6,7,8],[0,3,6],[1,4,7],[2,5,8],[0,4,8],[2,4,6]];
    const won = lines.some(([a, b, c]) => this.board[a] && this.board[a] === this.board[b] && this.board[a] === this.board[c]);
    if (won) {
      this.phase = "ended";
      this.reason = "win";
      this.winnerSymbol = symbol;
      this.broadcast("gameOver", this.buildResult());
    } else if (this.board.every(Boolean)) {
      this.phase = "ended";
      this.reason = "draw";
      this.winnerSymbol = "";
      this.broadcast("gameOver", this.buildResult());
    }
  }

  buildResult() {
    return {
      phase: this.phase,
      reason: this.reason,
      winnerSymbol: this.winnerSymbol,
      board: this.board
    };
  }

  broadcastState(lastMove) {
    this.broadcast("state", {
      board: this.board,
      turn: this.turn,
      phase: this.phase,
      winnerSymbol: this.winnerSymbol,
      reason: this.reason,
      lastMove
    });
  }
}

module.exports = {
  roomName: "web_tic_tac_toe",
  Room: TicTacToeRoom
};

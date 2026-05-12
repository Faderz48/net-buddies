const { Room } = require("colyseus");

class LeapfrogCheckersRoom extends Room {
  onCreate(options) {
    this.maxClients = 2;
    this.setMetadata({ game: "Leapfrog Checkers", addon: "Checkers", roomKey: options?.roomKey || options?.roomId || "" });
    this.players = new Map();
    this.onMessage("move", (client, message) => this.broadcast("move", {
      from: client.sessionId,
      move: message
    }, { except: client }));
    this.onMessage("gameOver", (client, message) => this.broadcast("gameOver", {
      from: client.sessionId,
      ...message
    }));
    this.onMessage("state", (client, message) => this.broadcast("state", {
      from: client.sessionId,
      state: message
    }, { except: client }));
  }

  onJoin(client, options) {
    const side = this.players.size === 0 ? "red" : "black";
    this.players.set(client.sessionId, {
      side,
      name: options?.name || "Buddy"
    });
    client.send("hello", {
      side,
      name: options?.name || "Buddy"
    });
  }

  onLeave(client) {
    if (this.players.size > 1) {
      const remaining = [...this.players.keys()].find((id) => id !== client.sessionId);
      this.broadcast("gameOver", {
        reason: "opponent_left",
        winnerSessionId: remaining || ""
      });
    }

    this.players.delete(client.sessionId);
  }
}

module.exports = {
  roomName: "leapfrog_checkers",
  Room: LeapfrogCheckersRoom
};

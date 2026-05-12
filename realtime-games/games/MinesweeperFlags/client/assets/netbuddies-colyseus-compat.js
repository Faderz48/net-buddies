(function () {
  if (!window.Colyseus || !window.Colyseus.Client) {
    return;
  }

  var prototype = window.Colyseus.Client.prototype;
  if (!prototype.consumeSeatReservation || prototype.__netBuddiesCompat) {
    return;
  }

  var original = prototype.consumeSeatReservation;
  prototype.consumeSeatReservation = function (response) {
    if (response && !response.room && response.name && response.roomId) {
      response = Object.assign({}, response, {
        room: {
          name: response.name,
          roomId: response.roomId,
          processId: response.processId,
          publicAddress: response.publicAddress
        }
      });
    }

    return original.apply(this, [response].concat(Array.prototype.slice.call(arguments, 1)));
  };

  prototype.__netBuddiesCompat = true;
})();

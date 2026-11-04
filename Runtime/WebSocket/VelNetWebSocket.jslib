// Vendored from NativeWebSocket (Apache 2.0); entry points renamed VelNetWebSocket*.

var LibraryVelNetWebSocket = {
	$velNetWebSocketState: {
		/*
		 * Map of instances
		 *
		 * Instance structure:
		 * {
		 * 	url: string,
		 * 	ws: WebSocket
		 * }
		 */
		instances: {},

		/* Last instance ID */
		lastId: 0,

		/* Event listeners */
		onOpen: null,
		onMessage: null,
		onError: null,
		onClose: null,

		/* Debug mode */
		debug: false
	},

	/**
	 * Set onOpen callback
	 *
	 * @param callback Reference to C# static function
	 */
	VelNetWebSocketSetOnOpen: function(callback) {

		velNetWebSocketState.onOpen = callback;

	},

	/**
	 * Set onMessage callback
	 *
	 * @param callback Reference to C# static function
	 */
	VelNetWebSocketSetOnMessage: function(callback) {

		velNetWebSocketState.onMessage = callback;

	},

	/**
	 * Set onError callback
	 *
	 * @param callback Reference to C# static function
	 */
	VelNetWebSocketSetOnError: function(callback) {

		velNetWebSocketState.onError = callback;

	},

	/**
	 * Set onClose callback
	 *
	 * @param callback Reference to C# static function
	 */
	VelNetWebSocketSetOnClose: function(callback) {

		velNetWebSocketState.onClose = callback;

	},

	/**
	 * Allocate new WebSocket instance struct
	 *
	 * @param url Server URL
	 */
	VelNetWebSocketAllocate: function(url) {

		var urlStr = UTF8ToString(url);
		var id = velNetWebSocketState.lastId++;

		velNetWebSocketState.instances[id] = {
		  subprotocols: [],
			url: urlStr,
			ws: null
		};

		return id;

	},

  /**
   * Add subprotocol to instance
   *
   * @param instanceId Instance ID
   * @param subprotocol Subprotocol name to add to instance
   */
  VelNetWebSocketAddSubProtocol: function(instanceId, subprotocol) {

    var subprotocolStr = UTF8ToString(subprotocol);
    velNetWebSocketState.instances[instanceId].subprotocols.push(subprotocolStr);

  },

	/**
	 * Remove reference to WebSocket instance
	 *
	 * If socket is not closed function will close it but onClose event will not be emitted because
	 * this function should be invoked by C# WebSocket destructor.
	 *
	 * @param instanceId Instance ID
	 */
	VelNetWebSocketFree: function(instanceId) {

		var instance = velNetWebSocketState.instances[instanceId];

		if (!instance) return 0;

		// Close if not closed
		if (instance.ws && instance.ws.readyState < 2)
			instance.ws.close();

		// Remove reference
		delete velNetWebSocketState.instances[instanceId];

		return 0;

	},

	/**
	 * Connect WebSocket to the server
	 *
	 * @param instanceId Instance ID
	 */
	VelNetWebSocketConnect: function(instanceId) {

		var instance = velNetWebSocketState.instances[instanceId];
		if (!instance) return -1;

		if (instance.ws !== null)
			return -2;

		instance.ws = new WebSocket(instance.url, instance.subprotocols);

		instance.ws.binaryType = 'arraybuffer';

		instance.ws.onopen = function() {

			if (velNetWebSocketState.debug)
				console.log("[JSLIB WebSocket] Connected.");

			if (velNetWebSocketState.onOpen)
				Module.dynCall_vi(velNetWebSocketState.onOpen, instanceId);

		};

		instance.ws.onmessage = function(ev) {

			if (velNetWebSocketState.debug)
				console.log("[JSLIB WebSocket] Received message:", ev.data);

			if (velNetWebSocketState.onMessage === null)
				return;

			if (ev.data instanceof ArrayBuffer) {

				var dataBuffer = new Uint8Array(ev.data);

				var buffer = _malloc(dataBuffer.length);
				HEAPU8.set(dataBuffer, buffer);

				try {
					Module.dynCall_viii(velNetWebSocketState.onMessage, instanceId, buffer, dataBuffer.length);
				} finally {
					_free(buffer);
				}

      } else {
				var dataBuffer = (new TextEncoder()).encode(ev.data);

				var buffer = _malloc(dataBuffer.length);
				HEAPU8.set(dataBuffer, buffer);

				try {
					Module.dynCall_viii(velNetWebSocketState.onMessage, instanceId, buffer, dataBuffer.length);
				} finally {
					_free(buffer);
				}

      }

		};

		instance.ws.onerror = function(ev) {

			if (velNetWebSocketState.debug)
				console.log("[JSLIB WebSocket] Error occured.");

			if (velNetWebSocketState.onError) {

				var msg = "WebSocket error.";
				var length = lengthBytesUTF8(msg) + 1;
				var buffer = _malloc(length);
				stringToUTF8(msg, buffer, length);

				try {
					Module.dynCall_vii(velNetWebSocketState.onError, instanceId, buffer);
				} finally {
					_free(buffer);
				}

			}

		};

		instance.ws.onclose = function(ev) {

			if (velNetWebSocketState.debug)
				console.log("[JSLIB WebSocket] Closed.");

			if (velNetWebSocketState.onClose)
				Module.dynCall_vii(velNetWebSocketState.onClose, instanceId, ev.code);

			delete instance.ws;

		};

		return 0;

	},

	/**
	 * Close WebSocket connection
	 *
	 * @param instanceId Instance ID
	 * @param code Close status code
	 * @param reasonPtr Pointer to reason string
	 */
	VelNetWebSocketClose: function(instanceId, code, reasonPtr) {

		var instance = velNetWebSocketState.instances[instanceId];
		if (!instance) return -1;

		if (!instance.ws)
			return -3;

		if (instance.ws.readyState === 2)
			return -4;

		if (instance.ws.readyState === 3)
			return -5;

		var reason = ( reasonPtr ? UTF8ToString(reasonPtr) : undefined );

		try {
			instance.ws.close(code, reason);
		} catch(err) {
			return -7;
		}

		return 0;

	},

	/**
	 * Send message over WebSocket
	 *
	 * @param instanceId Instance ID
	 * @param bufferPtr Pointer to the message buffer
	 * @param length Length of the message in the buffer
	 */
	VelNetWebSocketSend: function(instanceId, bufferPtr, length) {

		var instance = velNetWebSocketState.instances[instanceId];
		if (!instance) return -1;

		if (!instance.ws)
			return -3;

		if (instance.ws.readyState !== 1)
			return -6;

		instance.ws.send(HEAPU8.buffer.slice(bufferPtr, bufferPtr + length));

		return 0;

	},

	/**
	 * Send text message over WebSocket
	 *
	 * @param instanceId Instance ID
	 * @param bufferPtr Pointer to the message buffer
	 * @param length Length of the message in the buffer
	 */
	VelNetWebSocketSendText: function(instanceId, message) {

		var instance = velNetWebSocketState.instances[instanceId];
		if (!instance) return -1;

		if (!instance.ws)
			return -3;

		if (instance.ws.readyState !== 1)
			return -6;

		instance.ws.send(UTF8ToString(message));

		return 0;

	},

	/**
	 * Return WebSocket readyState
	 *
	 * @param instanceId Instance ID
	 */
	VelNetWebSocketGetState: function(instanceId) {

		var instance = velNetWebSocketState.instances[instanceId];
		if (!instance) return -1;

		if (instance.ws)
			return instance.ws.readyState;
		else
			return 3;

	}

};

autoAddDeps(LibraryVelNetWebSocket, '$velNetWebSocketState');
mergeInto(LibraryManager.library, LibraryVelNetWebSocket);

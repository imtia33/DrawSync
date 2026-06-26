/**
 * realtime.js - SignalR Realtime Collaboration Client
 * Handles WebSocket connection to the DrawingHub for live drawing sync.
 */

class RealtimeClient {
  constructor() {
    this.connection = null;
    this.drawingId = null;
    this.userName = null;
    this.boardType = null;
    this.organizationId = null;
    this.myColor = null;
    this.isConnected = false;
    this.presence = []; // [{ userId, userName, color }]
    this.remoteCursors = {}; // { userId: { userName, color, x, y, lastSeen } }

    // Throttling for cursor sends (~30fps)
    this._lastCursorSend = 0;
    this._cursorSendInterval = 1000 / 30; // ~33ms

    // Remote cursor cleanup interval
    this._cursorCleanupInterval = null;

    // Event callbacks (set by consumers)
    this.onUserJoined = null; // (user) => {}
    this.onUserLeft = null; // (user) => {}
    this.onElementChanged = null; // (element, action, userId) => {}
    this.onCursorMoved = null; // (userId, userName, color, cursor) => {}
    this.onBoardCleared = null; // (userId) => {}
    this.onToolChanged = null; // (userId, userName, color, tool) => {}
    this.onConnectionStatusChanged = null; // (status) => {}
  }

  /**
   * Connect to the SignalR hub and join the drawing room.
   */
  async connect(drawingId, userName, boardType) {
    this.drawingId = drawingId;
    this.userName = userName;
    this.boardType = boardType;

    // Build SignalR connection
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/drawing")
      .withAutomaticReconnect([0, 1000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    // Register server -> client handlers
    this._registerHandlers();

    // Register connection lifecycle handlers
    this._registerConnectionHandlers();

    try {
      // Start connection
      await this.connection.start();
      // CRITICAL FIX: mark the connection as live so sendElement/sendCursor/etc. stop early-returning.
      // Previously isConnected was only ever set on reconnect, so realtime sync was completely broken.
      this.isConnected = true;
      this._updateConnectionStatus("connected");
      this._logDebug("connected", "SignalR transport started.");

      // Join the drawing room
      const result = await this.connection.invoke(
        "JoinDrawing",
        drawingId,
        userName,
        boardType,
      );

      if (result && result.success) {
        console.log('[DEBUG WebSocket] Joined drawing room:', drawingId, 'with color:', result.color);
        this.myColor = result.color;
        this.presence = result.presence || [];
        this.organizationId = result.organizationId || null;
        this._renderPresence();
        this._showPresenceBar();
        this._logDebug("joined", `Joined drawing ${drawingId} (org=${this.organizationId || "?"}). Presence=${this.presence.length}.`);
      } else {
        this._logDebug("join-failed", "JoinDrawing returned no success payload.", "warn");
      }

      // Start cursor cleanup
      this._cursorCleanupInterval = setInterval(
        () => this._cleanupCursors(),
        5000,
      );
    } catch (err) {
      console.error("[DEBUG WebSocket] SignalR connection failed:", err);

      this.isConnected = false;
      this._logDebug("connect-error", `SignalR connection/join failed: ${err && err.message ? err.message : err}`, "error");

      // Check if it's a "board full" error
      if (err.message && err.message.includes("full")) {
        this._showBoardFullModal();
      } else {
        this._updateConnectionStatus("disconnected");
      }
    }
  }

  /**
   * Disconnect from the hub and leave the drawing room.
   */
  async disconnect() {
    console.log('[DEBUG WebSocket] Disconnecting from DrawingHub');
    if (this._cursorCleanupInterval) {
      clearInterval(this._cursorCleanupInterval);
      this._cursorCleanupInterval = null;
    }

    if (this.connection && this.isConnected) {
      try {
        await this.connection.invoke("LeaveDrawing", this.drawingId);
      } catch (e) {
        // Ignore errors during disconnect
      }
      await this.connection.stop();
    }

    this.isConnected = false;
    this.presence = [];
    this.remoteCursors = {};
  }

  /**
   * Send an element change to the server (add/update/delete).
   */
  async sendElement(element, action) {
    if (!this.isConnected || !this.connection) return;

    try {
      // Serialize the element to a plain object
      const elementData = this._serializeElement(element);
      console.log('[DEBUG WebSocket] Sending element change:', action, elementData);
      await this.connection.invoke(
        "SendElement",
        this.drawingId,
        elementData,
        action,
      );
    } catch (err) {
      console.error("[DEBUG WebSocket] Failed to send element:", err);
    }
  }

  /**
   * Send cursor position to the server (throttled to ~30fps).
   */
  sendCursor(x, y) {
    if (!this.isConnected || !this.connection) return;

    const now = performance.now();
    if (now - this._lastCursorSend < this._cursorSendInterval) return;
    this._lastCursorSend = now;

    console.log('[DEBUG WebSocket] Sending cursor position:', x, y);
    this.connection.invoke("SendCursor", this.drawingId, { x, y }).catch(() => {
      // Silently ignore cursor send failures
    });
  }

  /**
   * Send a board clear event to the server.
   */
  async sendClear() {
    if (!this.isConnected || !this.connection) return;

    try {
      console.log('[DEBUG WebSocket] Sending board clear event');
      await this.connection.invoke("SendClear", this.drawingId);
    } catch (err) {
      console.error("[DEBUG WebSocket] Failed to send clear:", err);
    }
  }

  /**
   * Send tool change to the server.
   */
  async sendToolChange(tool) {
    if (!this.isConnected || !this.connection) return;

    try {
      console.log('[DEBUG WebSocket] Sending tool change event:', tool);
      await this.connection.invoke("SendToolChange", this.drawingId, tool);
    } catch (err) {
      console.error("[DEBUG WebSocket] Failed to send tool change:", err);
    }
  }

  // --- Private Methods ---

  _registerHandlers() {
    // User joined the drawing
    this.connection.on("UserJoined", (user) => {
      this._logDebug("recv-user-joined", `user=${user && user.userName} (${user && user.userId})`);
      // Add to presence if not already there
      if (!this.presence.find((p) => p.userId === user.userId)) {
        this.presence.push(user);
      }
      this._renderPresence();

      if (this.onUserJoined) {
        this.onUserJoined(user);
      }
    });

    // User left the drawing
    this.connection.on("UserLeft", (data) => {
      this._logDebug("recv-user-left", `user=${data && data.userId}`);
      this.presence = this.presence.filter((p) => p.userId !== data.userId);
      delete this.remoteCursors[data.userId];
      this._renderPresence();
      this._removeRemoteCursor(data.userId);

      if (this.onUserLeft) {
        this.onUserLeft(data);
      }
    });

    // Element changed by another user
    this.connection.on("ElementChanged", (data) => {
      this._logDebug("recv-element", `action='${data && data.action}' from user=${data && data.userId}`);
      if (this.onElementChanged) {
        this.onElementChanged(data.element, data.action, data.userId);
      }
    });

    // Cursor moved by another user
    this.connection.on("CursorMoved", (data) => {
      console.log('[DEBUG WebSocket] CursorMoved event received from user:', data.userId, data.cursor);
      this.remoteCursors[data.userId] = {
        userName: data.userName,
        color: data.color,
        x: data.cursor.x,
        y: data.cursor.y,
        lastSeen: Date.now(),
      };

      if (this.onCursorMoved) {
        this.onCursorMoved(data.userId, data.userName, data.color, data.cursor);
      }
    });

    // Board cleared by another user
    this.connection.on("BoardCleared", (data) => {
      console.log('[DEBUG WebSocket] BoardCleared event received from user:', data.userId);
      if (this.onBoardCleared) {
        this.onBoardCleared(data.userId);
      }
    });

    // Tool changed by another user
    this.connection.on("ToolChanged", (data) => {
      console.log('[DEBUG WebSocket] ToolChanged event received:', data);
      if (this.onToolChanged) {
        this.onToolChanged(data.userId, data.userName, data.color, data.tool);
      }
    });

    // Presence update (reassignment of colors, etc.)
    this.connection.on("PresenceUpdate", (presenceList) => {
      console.log('[DEBUG WebSocket] PresenceUpdate received:', presenceList);
      this.presence = presenceList;
      this._renderPresence();
    });
  }

  _registerConnectionHandlers() {
    this.connection.onreconnecting(() => {
      console.log('[DEBUG WebSocket] Reconnecting to DrawingHub...');
      this.isConnected = false;
      this._updateConnectionStatus("reconnecting");
      this._logDebug("reconnecting", "Transport reconnecting…", "warn");
    });

    this.connection.onreconnected(async () => {
      console.log('[DEBUG WebSocket] Reconnected to DrawingHub successfully');
      this.isConnected = true;
      this._updateConnectionStatus("connected");
      this._logDebug("reconnected", "Transport reconnected.");

      // Rejoin the drawing room after reconnect
      try {
        const result = await this.connection.invoke(
          "JoinDrawing",
          this.drawingId,
          this.userName,
          this.boardType,
        );
        if (result && result.success) {
          console.log('[DEBUG WebSocket] Rejoined drawing room:', this.drawingId);
          this.myColor = result.color;
          this.presence = result.presence || [];
          this._renderPresence();
          this._logDebug("rejoined", `Rejoined drawing after reconnect. Presence=${this.presence.length}.`);
        }
      } catch (err) {
        console.error("Failed to rejoin drawing after reconnect:", err);
        this._logDebug("rejoin-failed", `Rejoin failed: ${err && err.message ? err.message : err}`, "error");
      }
    });

    this.connection.onclose(() => {
      console.log('[DEBUG WebSocket] Connection to DrawingHub closed');
      this.isConnected = false;
      this._updateConnectionStatus("disconnected");
      this._logDebug("closed", "Connection closed.", "warn");
    });
  }

  _serializeElement(element) {
    // Convert Element instance to a plain object for JSON serialization
    if (!element) return null;

    const obj = {};
    // Copy all enumerable own properties
    for (const key of Object.keys(element)) {
      const val = element[key];
      // Skip functions and undefined
      if (typeof val === "function" || val === undefined) continue;
      obj[key] = val;
    }
    return obj;
  }

  _updateConnectionStatus(status) {
    const statusEl = document.getElementById("connectionStatus");
    if (!statusEl) return;

    statusEl.style.display = "flex";
    const dot = statusEl.querySelector(".status-dot");
    if (dot) {
      dot.className = "status-dot " + status;
    }

    const titles = {
      connected: "Connected",
      reconnecting: "Reconnecting...",
      disconnected: "Disconnected",
    };
    statusEl.title = titles[status] || status;

    if (this.onConnectionStatusChanged) {
      this.onConnectionStatusChanged(status);
    }
  }

  _showPresenceBar() {
    const bar = document.getElementById("presenceBar");
    if (bar) bar.style.display = "flex";
  }

  _renderPresence() {
    const container = document.getElementById("presenceAvatars");
    const countEl = document.getElementById("presenceCount");
    if (!container) return;

    container.innerHTML = "";
    for (const user of this.presence) {
      const avatar = document.createElement("div");
      avatar.className = "presence-avatar";
      avatar.style.backgroundColor = user.color;
      avatar.setAttribute("data-tooltip", user.userName);
      // Show initials
      const initials = user.userName
        .split(" ")
        .map((w) => w[0])
        .join("")
        .toUpperCase()
        .slice(0, 2);
      avatar.textContent = initials;
      container.appendChild(avatar);
    }

    if (countEl) {
      countEl.textContent = `${this.presence.length}/5`;
    }
  }

  _cleanupCursors() {
    const now = Date.now();
    const staleThreshold = 10000; // 10 seconds
    for (const [userId, cursor] of Object.entries(this.remoteCursors)) {
      if (now - cursor.lastSeen > staleThreshold) {
        delete this.remoteCursors[userId];
        this._removeRemoteCursor(userId);
      }
    }
  }

  _removeRemoteCursor(userId) {
    const el = document.getElementById(`remote-cursor-${userId}`);
    if (el) el.remove();
  }

  _showBoardFullModal() {
    const modal = document.getElementById("boardFullModal");
    if (modal) {
      modal.style.display = "flex";
      const closeBtn = document.getElementById("boardFullCloseBtn");
      if (closeBtn) {
        closeBtn.onclick = () => {
          // Navigate back to whiteboard index
          window.history.back();
        };
      }
    }
  }

  // --- Realtime Debugger (client side) ---
  // Emits structured log lines to the browser console AND to the on-board debug panel (if present)
  // and to the global window.__realtimeDebugEvents array, so we can verify broadcasts are working.
  _logDebug(category, message, level = "info") {
    const entry = {
      t: Date.now(),
      level,
      category,
      message,
      drawingId: this.drawingId,
      org: this.organizationId || null,
      connected: this.isConnected,
      presence: this.presence.length,
    };
    window.__realtimeDebugEvents = window.__realtimeDebugEvents || [];
    window.__realtimeDebugEvents.push(entry);
    if (window.__realtimeDebugEvents.length > 200) window.__realtimeDebugEvents.shift();

    const tag = level === "error" ? "%c[RT-ERROR]" : level === "warn" ? "%c[RT-WARN]" : "%c[RT-INFO]";
    const color = level === "error" ? "color:#ef4444;font-weight:bold" : level === "warn" ? "color:#f59e0b;font-weight:bold" : "color:#10b981;font-weight:bold";
    console.log(tag, color, `${category}: ${message}`);

    // Push into the on-board debug panel if it exists.
    const panel = document.getElementById("rtDebugLog");
    if (panel) {
      const line = document.createElement("div");
      line.className = `rt-debug-line rt-debug-${level}`;
      const time = new Date(entry.t).toLocaleTimeString();
      line.innerHTML = `<span class="rt-debug-time">${time}</span> <span class="rt-debug-cat">${category}</span> <span class="rt-debug-msg"></span>`;
      line.querySelector(".rt-debug-msg").textContent = message;
      panel.prepend(line);
      // Keep the panel bounded.
      while (panel.children.length > 80) panel.removeChild(panel.lastChild);
    }
    // Update the debug panel header stats if present.
    const stats = document.getElementById("rtDebugStats");
    if (stats) {
      stats.textContent = `connected=${this.isConnected} • presence=${this.presence.length} • drawing=${this.drawingId || "-"} • org=${this.organizationId || "-"}`;
    }
  }
}

// Export to global scope
window.RealtimeClient = RealtimeClient;

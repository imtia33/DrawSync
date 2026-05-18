/**
 * storage.js - Local-first IndexedDB storage for Boards and Elements
 */
const DB_NAME = "DrawSyncLocal";
const DB_VERSION = 2;
const STORES = {
  BOARDS: "boards",
  ELEMENTS: "strokes", // Keeping the store name 'strokes' to maintain data but calling it elements
};

const Storage = {
  db: null,

  async init() {
    if (this.db) return this.db;
    return new Promise((resolve, reject) => {
      const request = indexedDB.open(DB_NAME, DB_VERSION);

      request.onupgradeneeded = (e) => {
        const db = e.target.result;
        if (!db.objectStoreNames.contains(STORES.BOARDS)) {
          db.createObjectStore(STORES.BOARDS, { keyPath: "id" });
        }
        if (!db.objectStoreNames.contains(STORES.ELEMENTS)) {
          db.createObjectStore(STORES.ELEMENTS, { keyPath: "id" });
        }
      };

      request.onsuccess = (e) => {
        this.db = e.target.result;
        resolve(this.db);
      };

      request.onerror = (e) => reject(e.target.error);
    });
  },

  // --- Board Operations ---
  async getBoards() {
    await this.init();
    return new Promise((resolve, reject) => {
      const transaction = this.db.transaction(STORES.BOARDS, "readonly");
      const store = transaction.objectStore(STORES.BOARDS);
      const request = store.getAll();
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  },

  async getBoard(id) {
    await this.init();
    return new Promise((resolve, reject) => {
      const transaction = this.db.transaction(STORES.BOARDS, "readonly");
      const store = transaction.objectStore(STORES.BOARDS);
      const request = store.get(id);
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  },

  async createBoard(name, type = "whiteboard") {
    await this.init();
    const id =
      "local_" + Date.now() + "_" + Math.random().toString(36).substr(2, 9);
    const board = {
      id,
      name,
      type,
      backgroundColor: "#f8f9fa",
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    };

    return new Promise((resolve, reject) => {
      const transaction = this.db.transaction(STORES.BOARDS, "readwrite");
      const store = transaction.objectStore(STORES.BOARDS);
      const request = store.add(board);
      request.onsuccess = () => resolve(board);
      request.onerror = () => reject(request.error);
    });
  },

  async updateBoard(board) {
    await this.init();
    board.updatedAt = new Date().toISOString();
    return new Promise((resolve, reject) => {
      const transaction = this.db.transaction(STORES.BOARDS, "readwrite");
      const store = transaction.objectStore(STORES.BOARDS);
      const request = store.put(board);
      request.onsuccess = () => resolve(board);
      request.onerror = () => reject(request.error);
    });
  },

  async deleteBoard(id) {
    await this.init();
    return new Promise((resolve, reject) => {
      const transaction = this.db.transaction(
        [STORES.BOARDS, STORES.ELEMENTS],
        "readwrite",
      );
      transaction.objectStore(STORES.BOARDS).delete(id);
      transaction.objectStore(STORES.ELEMENTS).delete(id);
      transaction.oncomplete = () => resolve();
      transaction.onerror = (e) => reject(e.target.error);
    });
  },

  // --- Element Operations ---
  async getElements(boardId) {
    await this.init();
    return new Promise((resolve, reject) => {
      const transaction = this.db.transaction(STORES.ELEMENTS, "readonly");
      const store = transaction.objectStore(STORES.ELEMENTS);
      const request = store.get(boardId);
      request.onsuccess = () => {
        const data = request.result ? request.result.strokes : null;
        
        // If it's an array (whiteboard strokes), apply migration logic
        if (Array.isArray(data)) {
          const elements = data.map((el) => {
            if (!el.type && el.points) el.type = "pencil";
            return el;
          });
          resolve(elements);
        } else {
          // If it's an object (map features) or null, return as is
          resolve(data || []);
        }
      };
      request.onerror = () => reject(request.error);
    });
  },

  async saveElements(boardId, elements) {
    await this.init();
    return new Promise((resolve, reject) => {
      const transaction = this.db.transaction(
        [STORES.ELEMENTS, STORES.BOARDS],
        "readwrite",
      );

      // Save elements
      transaction
        .objectStore(STORES.ELEMENTS)
        .put({ id: boardId, strokes: elements });

      // Update board updatedAt timestamp
      const boardStore = transaction.objectStore(STORES.BOARDS);
      const boardReq = boardStore.get(boardId);
      boardReq.onsuccess = () => {
        const board = boardReq.result;
        if (board) {
          board.updatedAt = new Date().toISOString();
          boardStore.put(board);
        }
      };

      transaction.oncomplete = () => resolve();
      transaction.onerror = (e) => reject(e.target.error);
    });
  },

  // Aliases for compatibility
  async getStrokes(id) {
    return this.getElements(id);
  },
  async saveStrokes(id, data) {
    return this.saveElements(id, data);
  },
};

window.DrawStorage = Storage;

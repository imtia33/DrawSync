/**
 * whiteboard.js - Refined Professional Workspace Engine
 */

class Element {
  constructor(type, x, y, style) {
    this.id = Math.random().toString(36).substr(2, 9);
    this.type = type;
    this.x = x;
    this.y = y;
    this.width = 0;
    this.height = 0;
    this.strokeColor = style.strokeColor;
    this.fillColor = style.fillColor;
    this.strokeWidth = style.strokeWidth;
    this.strokeStyle = style.strokeStyle;
    this.opacity = style.opacity;
    this.points = []; // [{x, y}]
    this.endX = x;
    this.endY = y;
  }

  getBounds() {
    if (this.type === "pencil") {
      if (this.points.length === 0)
        return { minX: 0, minY: 0, maxX: 0, maxY: 0 };
      const xs = this.points.map((p) => p.x);
      const ys = this.points.map((p) => p.y);
      return {
        minX: Math.min(...xs),
        minY: Math.min(...ys),
        maxX: Math.max(...xs),
        maxY: Math.max(...ys),
      };
    }
    if (this.type === "line" || this.type === "arrow") {
      return {
        minX: Math.min(this.x, this.endX),
        minY: Math.min(this.y, this.endY),
        maxX: Math.max(this.x, this.endX),
        maxY: Math.max(this.y, this.endY),
      };
    }
    return {
      minX: Math.min(this.x, this.x + this.width),
      minY: Math.min(this.y, this.y + this.height),
      maxX: Math.max(this.x, this.x + this.width),
      maxY: Math.max(this.y, this.y + this.height),
    };
  }

  intersects(x, y, radius = 5) {
    const bounds = this.getBounds();
    const padding = radius + this.strokeWidth / 2;

    if (
      x < bounds.minX - padding ||
      x > bounds.maxX + padding ||
      y < bounds.minY - padding ||
      y > bounds.maxY + padding
    )
      return false;

    if (this.type === "pencil") {
      return this.points.some(
        (p) => Math.sqrt((p.x - x) ** 2 + (p.y - y) ** 2) < padding,
      );
    }

    if (this.type === "line" || this.type === "arrow") {
      const L2 = (this.endX - this.x) ** 2 + (this.endY - this.y) ** 2;
      if (L2 === 0)
        return Math.sqrt((this.x - x) ** 2 + (this.y - y) ** 2) < padding;
      let t =
        ((x - this.x) * (this.endX - this.x) +
          (y - this.y) * (this.endY - this.y)) /
        L2;
      t = Math.max(0, Math.min(1, t));
      const projX = this.x + t * (this.endX - this.x);
      const projY = this.y + t * (this.endY - this.y);
      return Math.sqrt((x - projX) ** 2 + (y - projY) ** 2) < padding;
    }

    if (this.fillColor !== "transparent") return true;
    const dx = Math.min(Math.abs(x - bounds.minX), Math.abs(x - bounds.maxX));
    const dy = Math.min(Math.abs(y - bounds.minY), Math.abs(y - bounds.maxY));
    if (this.type === "rectangle" || this.type === "diamond")
      return dx < padding || dy < padding;
    if (this.type === "circle") {
      const rx = Math.abs(this.width / 2);
      const ry = Math.abs(this.height / 2);
      const cx = this.x + this.width / 2;
      const cy = this.y + this.height / 2;
      if (rx === 0 || ry === 0) return false;
      const normX = (x - cx) / rx;
      const normY = (y - cy) / ry;
      const dist = Math.sqrt(normX * normX + normY * normY);
      return Math.abs(dist - 1) < padding / Math.min(rx, ry);
    }
    return false;
  }

  contains(x, y) {
    return this.intersects(x, y, 2);
  }

  draw(ctx) {
    ctx.save();
    ctx.globalAlpha = this.opacity / 100;
    ctx.strokeStyle = this.strokeColor;
    ctx.fillStyle = this.fillColor;
    ctx.lineWidth = this.strokeWidth;
    ctx.lineCap = "round";
    ctx.lineJoin = "round";
    if (this.strokeStyle === "dashed") ctx.setLineDash([10, 10]);
    else if (this.strokeStyle === "dotted") ctx.setLineDash([2, 8]);
    else ctx.setLineDash([]);

    switch (this.type) {
      case "pencil":
        this.drawPencil(ctx);
        break;
      case "rectangle":
        ctx.beginPath();
        ctx.rect(this.x, this.y, this.width, this.height);
        if (this.fillColor !== "transparent") ctx.fill();
        ctx.stroke();
        break;
      case "circle":
        ctx.beginPath();
        ctx.ellipse(
          this.x + this.width / 2,
          this.y + this.height / 2,
          Math.abs(this.width / 2),
          Math.abs(this.height / 2),
          0,
          0,
          Math.PI * 2,
        );
        if (this.fillColor !== "transparent") ctx.fill();
        ctx.stroke();
        break;
      case "diamond":
        ctx.beginPath();
        ctx.moveTo(this.x + this.width / 2, this.y);
        ctx.lineTo(this.x + this.width, this.y + this.height / 2);
        ctx.lineTo(this.x + this.width / 2, this.y + this.height);
        ctx.lineTo(this.x, this.y + this.height / 2);
        ctx.closePath();
        if (this.fillColor !== "transparent") ctx.fill();
        ctx.stroke();
        break;
      case "line":
        ctx.beginPath();
        ctx.moveTo(this.x, this.y);
        ctx.lineTo(this.endX, this.endY);
        ctx.stroke();
        break;
      case "arrow":
        this.drawArrow(ctx);
        break;
    }
    ctx.restore();
  }

  drawPencil(ctx) {
    if (this.points.length < 2) return;
    ctx.beginPath();
    ctx.moveTo(this.points[0].x, this.points[0].y);
    for (let i = 1; i < this.points.length - 2; i++) {
      const xc = (this.points[i].x + this.points[i + 1].x) / 2;
      const yc = (this.points[i].y + this.points[i + 1].y) / 2;
      ctx.quadraticCurveTo(this.points[i].x, this.points[i].y, xc, yc);
    }
    if (this.points.length > 2)
      ctx.quadraticCurveTo(
        this.points[this.points.length - 2].x,
        this.points[this.points.length - 2].y,
        this.points[this.points.length - 1].x,
        this.points[this.points.length - 1].y,
      );
    else ctx.lineTo(this.points[1].x, this.points[1].y);
    ctx.stroke();
  }

  drawArrow(ctx) {
    const headlen = 10 * (this.strokeWidth / 2);
    const angle = Math.atan2(this.endY - this.y, this.endX - this.x);
    ctx.beginPath();
    ctx.moveTo(this.x, this.y);
    ctx.lineTo(this.endX, this.endY);
    ctx.stroke();
    ctx.beginPath();
    ctx.moveTo(this.endX, this.endY);
    ctx.lineTo(
      this.endX - headlen * Math.cos(angle - Math.PI / 6),
      this.endY - headlen * Math.sin(angle - Math.PI / 6),
    );
    ctx.moveTo(this.endX, this.endY);
    ctx.lineTo(
      this.endX - headlen * Math.cos(angle + Math.PI / 6),
      this.endY - headlen * Math.sin(angle + Math.PI / 6),
    );
    ctx.stroke();
  }
}

class Whiteboard {
  constructor(canvasId, boardId) {
    this.canvas = document.getElementById(canvasId);
    this.ctx = this.canvas.getContext("2d");
    this.canvasParent = document.getElementById("canvasParent");
    this.boardId = boardId;
    this.elements = [];
    this.redoStack = [];
    this.selectedElements = [];
    this.camera = { x: 0, y: 0, zoom: 1 };
    this.currentTool = "select";
    this.isDrawing = false;
    this.isPanning = false;
    this.isMoving = false;
    this.isResizing = false;
    this.isSpacePressed = false;
    this.dragStart = { x: 0, y: 0 };
    this.currentElement = null;
    this.style = {
      strokeColor: "#000000",
      fillColor: "transparent",
      strokeWidth: 2,
      strokeStyle: "solid",
      opacity: 100,
    };
    this.backgroundColor = "#f8f9fa";
    this.hasGrid = false;
    this.cpTarget = "stroke";
    this.currentHue = 0;
    this.init();
  }

  async init() {
    this.resize();
    window.addEventListener("resize", () => this.resize());
    const board = await window.DrawStorage.getBoard(this.boardId);
    if (board) {
      document.getElementById("displayBoardName").textContent = board.name;
      this.backgroundColor = board.backgroundColor || "#f8f9fa";
      this.hasGrid = board.hasGrid || false;
      this.applyTheme();
    }
    const saved = await window.DrawStorage.getElements(this.boardId);
    this.elements = saved.map((data) => {
      const el = new Element(data.type, data.x, data.y, data);
      Object.assign(el, data);
      return el;
    });
    this.setupEventListeners();
    this.initColorPicker();
    this.render();
  }

  resize() {
    const rect = this.canvasParent.getBoundingClientRect();
    const padding = 48; // 24px on each side for the "frame"
    this.canvas.width = rect.width - padding;
    this.canvas.height = rect.height - padding;
    this.render();
  }

  setupEventListeners() {
    this.canvas.addEventListener("mousedown", (e) => this.onMouseDown(e));
    this.canvas.addEventListener("mousemove", (e) => this.onMouseMove(e));
    this.canvas.addEventListener("mouseup", () => this.onMouseUp());
    this.canvas.addEventListener("mouseleave", () => this.onMouseUp());
    this.canvas.addEventListener("wheel", (e) => this.onWheel(e), {
      passive: false,
    });

    document
      .querySelectorAll(".tool-btn")
      .forEach((btn) => (btn.onclick = () => this.setTool(btn.dataset.tool)));
    document.querySelectorAll(".bg-item").forEach((item) => {
      item.onclick = () => {
        document
          .querySelectorAll(".bg-item")
          .forEach((x) => x.classList.remove("active"));
        item.classList.add("active");
        this.backgroundColor = item.dataset.bg;
        this.hasGrid = item.dataset.grid === "true";
        this.applyTheme();
        this.saveBoardSettings();
      };
    });

    document.getElementById("strokeWidth").oninput = (e) => {
      const val = parseInt(e.target.value);
      document.getElementById("widthVal").textContent = val + "px";
      this.updateSelectedStyle("strokeWidth", val);
    };
    document.getElementById("opacity").oninput = (e) => {
      const val = parseInt(e.target.value);
      document.getElementById("opacityVal").textContent = val + "%";
      this.updateSelectedStyle("opacity", val);
    };
    document.querySelectorAll("#strokeStyle .style-toggle").forEach((btn) => {
      btn.onclick = () => {
        document
          .querySelectorAll("#strokeStyle .style-toggle")
          .forEach((x) => x.classList.remove("active"));
        btn.classList.add("active");
        this.updateSelectedStyle("strokeStyle", btn.dataset.value);
      };
    });

    document.getElementById("strokeSwatchBtn").onclick = (e) =>
      this.openColorPicker("stroke", e);
    document.getElementById("fillSwatchBtn").onclick = (e) =>
      this.openColorPicker("fill", e);
    document.getElementById("closePicker").onclick = () =>
      (document.getElementById("colorPickerPanel").style.display = "none");

    window.onkeydown = (e) => this.onKeyDown(e);
    window.onkeyup = (e) => {
      if (e.code === "Space") this.isSpacePressed = false;
    };

    document.getElementById("undoBtn").onclick = () => this.undo();
    document.getElementById("redoBtn").onclick = () => this.redo();

    // Export Logic
    const exportBtn = document.getElementById("exportBtn");
    const exportMenu = document.getElementById("exportMenu");
    exportBtn.onclick = (e) => {
      e.stopPropagation();
      exportMenu.style.display =
        exportMenu.style.display === "flex" ? "none" : "flex";
    };
    document.getElementById("exportPng").onclick = () => {
      this.export(false);
      exportMenu.style.display = "none";
    };
    document.getElementById("exportJpg").onclick = () => {
      this.export(true);
      exportMenu.style.display = "none";
    };
    window.onclick = () => {
      exportMenu.style.display = "none";
    };

    document.getElementById("clearBtn").onclick = () => this.showClearModal();
    document.getElementById("confirmClear").onclick = () => this.clear();
    document.getElementById("cancelClear").onclick = () =>
      this.hideClearModal();

    document.getElementById("zoomIn").onclick = () => this.changeZoom(0.2);
    document.getElementById("zoomOut").onclick = () => this.changeZoom(-0.2);
  }

  setTool(tool) {
    this.currentTool = tool;
    this.isDrawing = false;
    this.isPanning = false;
    this.isMoving = false;
    this.isResizing = false;
    this.selectedElements = [];
    this.currentElement = null;
    document
      .querySelectorAll(".tool-btn")
      .forEach((b) => b.classList.toggle("active", b.dataset.tool === tool));
    this.render();
  }

  applyTheme() {
    this.canvas.style.backgroundColor = this.backgroundColor;
    this.canvasParent.classList.toggle("grid-mode", this.hasGrid);

    // Adaptive Zoom Control Theme
    const isDark = this.isColorDark(this.backgroundColor);
    const zoomOverlay = document.querySelector(".zoom-overlay");
    if (zoomOverlay) {
      zoomOverlay.classList.toggle("dark-mode", isDark);
    }
    this.render();
  }

  isColorDark(color) {
    if (color.startsWith("#")) {
      const r = parseInt(color.slice(1, 3), 16);
      const g = parseInt(color.slice(3, 5), 16);
      const b = parseInt(color.slice(5, 7), 16);
      return r * 0.299 + g * 0.587 + b * 0.114 < 128;
    }
    if (color === "#1e293b" || color === "#000000") return true;
    return false;
  }

  async saveBoardSettings() {
    const board = await window.DrawStorage.getBoard(this.boardId);
    if (board) {
      board.backgroundColor = this.backgroundColor;
      board.hasGrid = this.hasGrid;
      await window.DrawStorage.updateBoard(board);
    }
  }

  // --- Precise Color Picker ---
  initColorPicker() {
    const wheel = document.getElementById("hueWheel");
    const sv = document.getElementById("svSquare");
    const wCtx = wheel.getContext("2d");
    const sCtx = sv.getContext("2d");
    wheel.width = 230;
    wheel.height = 230;
    sv.width = 140;
    sv.height = 140;
    for (let i = 0; i < 360; i++) {
      wCtx.beginPath();
      wCtx.moveTo(115, 115);
      wCtx.arc(
        115,
        115,
        110,
        ((i - 1) * Math.PI) / 180,
        ((i + 1) * Math.PI) / 180,
      );
      wCtx.fillStyle = `hsl(${i}, 100%, 50%)`;
      wCtx.fill();
    }
    wCtx.globalCompositeOperation = "destination-out";
    wCtx.beginPath();
    wCtx.arc(115, 115, 85, 0, Math.PI * 2);
    wCtx.fill();
    wCtx.globalCompositeOperation = "source-over";
    const updateSV = (hue) => {
      this.currentHue = hue;
      sCtx.clearRect(0, 0, 140, 140);
      sCtx.fillStyle = `hsl(${hue}, 100%, 50%)`;
      sCtx.fillRect(0, 0, 140, 140);
      let gradW = sCtx.createLinearGradient(0, 0, 140, 0);
      gradW.addColorStop(0, "white");
      gradW.addColorStop(1, "rgba(255,255,255,0)");
      sCtx.fillStyle = gradW;
      sCtx.fillRect(0, 0, 140, 140);
      let gradB = sCtx.createLinearGradient(0, 0, 0, 140);
      gradB.addColorStop(0, "rgba(0,0,0,0)");
      gradB.addColorStop(1, "black");
      sCtx.fillStyle = gradB;
      sCtx.fillRect(0, 0, 140, 140);
    };
    wheel.onmousedown = (e) => {
      const pick = (me) => {
        const rect = wheel.getBoundingClientRect();
        const x = me.clientX - rect.left - 115;
        const y = me.clientY - rect.top - 115;
        const angle = ((Math.atan2(y, x) * 180) / Math.PI + 360) % 360;
        updateSV(angle);
      };
      const onMove = (me) => pick(me);
      const onUp = () => {
        window.removeEventListener("mousemove", onMove);
        window.removeEventListener("mouseup", onUp);
      };
      window.addEventListener("mousemove", onMove);
      window.addEventListener("mouseup", onUp);
      pick(e);
    };
    sv.onmousedown = (e) => {
      const pick = (me) => {
        const rect = sv.getBoundingClientRect();
        const x = Math.max(0, Math.min(139, me.clientX - rect.left));
        const y = Math.max(0, Math.min(139, me.clientY - rect.top));
        document.getElementById("svCursor").style.left = x + 45 + "px";
        document.getElementById("svCursor").style.top = y + 45 + "px";
        const s = (x / 139) * 100;
        const v = (1 - y / 139) * 100;
        this.updateColorPickerUI(this.hsvToHex(this.currentHue, s, v));
      };
      const onMove = (me) => pick(me);
      const onUp = () => {
        window.removeEventListener("mousemove", onMove);
        window.removeEventListener("mouseup", onUp);
      };
      window.addEventListener("mousemove", onMove);
      window.addEventListener("mouseup", onUp);
      pick(e);
    };
    document.getElementById("noFillBtn").onclick = () =>
      this.updateColorPickerUI("transparent");
    document.getElementById("hexInput").onchange = (e) =>
      this.updateColorPickerUI(e.target.value);
  }

  hsvToHex(h, s, v) {
    v /= 100;
    s /= 100;
    let f = (n, k = (n + h / 60) % 6) =>
      v - v * s * Math.max(Math.min(k, 4 - k, 1), 0);
    let rgb = [f(5), f(3), f(1)].map((x) =>
      Math.round(x * 255)
        .toString(16)
        .padStart(2, "0"),
    );
    return `#${rgb.join("")}`;
  }

  updateColorPickerUI(color) {
    document.getElementById("hexInput").value = color;
    document.getElementById("colorPreview").style.backgroundColor = color;
    const key = this.cpTarget === "stroke" ? "strokeColor" : "fillColor";
    this.updateSelectedStyle(key, color);
    document.getElementById(this.cpTarget + "Swatch").style.backgroundColor =
      color;
  }

  openColorPicker(target, e) {
    this.cpTarget = target;
    const panel = document.getElementById("colorPickerPanel");
    panel.style.display = "block";
    panel.style.left = "270px";
    panel.style.top = e.clientY - 100 + "px";
    document.getElementById("pickerTitle").textContent =
      target === "stroke" ? "Stroke Color" : "Fill Color";
  }

  updateSelectedStyle(key, val) {
    this.style[key] = val;
    this.selectedElements.forEach((el) => {
      el[key] = val;
    });
    if (this.selectedElements.length > 0) this.save();
    this.render();
  }

  // --- Input Logic ---
  onMouseDown(e) {
    const { clientX, clientY } = e;
    this.dragStart = { x: clientX, y: clientY };
    const worldPos = this.screenToWorld(clientX, clientY);
    if (this.isSpacePressed || this.currentTool === "pan" || e.button === 1) {
      this.isPanning = true;
      return;
    }
    if (this.currentTool === "eraser") {
      this.isDrawing = true;
      this.eraseAt(worldPos);
      return;
    }
    if (this.currentTool === "select") {
      const handle = this.getResizeHandle(worldPos);
      if (handle) {
        this.isResizing = handle;
        return;
      }
      const clicked = this.elements
        .slice()
        .reverse()
        .find((el) => el.contains(worldPos.x, worldPos.y));
      if (clicked) {
        if (!e.shiftKey && !this.selectedElements.includes(clicked))
          this.selectedElements = [clicked];
        else if (e.shiftKey) {
          if (this.selectedElements.includes(clicked))
            this.selectedElements = this.selectedElements.filter(
              (x) => x !== clicked,
            );
          else this.selectedElements.push(clicked);
        }
        this.isMoving = true;
      } else {
        this.selectedElements = [];
      }
      this.render();
      return;
    }
    this.isDrawing = true;
    const tool = this.currentTool;
    this.currentElement = new Element(tool, worldPos.x, worldPos.y, this.style);
    if (tool === "pencil") this.currentElement.points.push(worldPos);
  }

  onMouseMove(e) {
    const { clientX, clientY } = e;
    const worldPos = this.screenToWorld(clientX, clientY);
    const cursor = document.getElementById("customCursor");
    cursor.style.display = "block";
    cursor.style.left = clientX + "px";
    cursor.style.top = clientY + "px";
    const size =
      this.currentTool === "pencil" || this.currentTool === "eraser"
        ? this.style.strokeWidth * this.camera.zoom
        : 20;
    cursor.style.width = size + "px";
    cursor.style.height = size + "px";

    if (this.isPanning) {
      this.camera.x += clientX - this.dragStart.x;
      this.camera.y += clientY - this.dragStart.y;
      this.dragStart = { x: clientX, y: clientY };
      this.render();
      return;
    }
    if (this.currentTool === "eraser") {
      if (this.isDrawing) {
        this.eraseAt(worldPos);
      }
      return;
    }
    if (this.isResizing && this.selectedElements.length === 1) {
      const el = this.selectedElements[0];
      const dx = (clientX - this.dragStart.x) / this.camera.zoom;
      const dy = (clientY - this.dragStart.y) / this.camera.zoom;
      if (this.isResizing.includes("r")) el.width += dx;
      if (this.isResizing.includes("l")) {
        el.x += dx;
        el.width -= dx;
      }
      if (this.isResizing.includes("b")) el.height += dy;
      if (this.isResizing.includes("t")) {
        el.y += dy;
        el.height -= dy;
      }
      if (el.type === "line" || el.type === "arrow") {
        if (this.isResizing === "tl") {
          el.x += dx;
          el.y += dy;
        }
        if (this.isResizing === "br") {
          el.endX += dx;
          el.endY += dy;
        }
      }
      this.dragStart = { x: clientX, y: clientY };
      this.render();
      return;
    }
    if (this.isMoving) {
      const dx = (clientX - this.dragStart.x) / this.camera.zoom;
      const dy = (clientY - this.dragStart.y) / this.camera.zoom;
      this.selectedElements.forEach((el) => {
        el.x += dx;
        el.y += dy;
        if (el.type === "pencil")
          el.points.forEach((p) => {
            p.x += dx;
            p.y += dy;
          });
        if (el.type === "line" || el.type === "arrow") {
          el.endX += dx;
          el.endY += dy;
        }
      });
      this.dragStart = { x: clientX, y: clientY };
      this.render();
      return;
    }
    if (this.currentTool === "select") {
      const handle = this.getResizeHandle(worldPos);
      if (handle) {
        const cs = {
          tl: "nwse-resize",
          br: "nwse-resize",
          tr: "nesw-resize",
          bl: "nesw-resize",
          tc: "ns-resize",
          bc: "ns-resize",
          lc: "ew-resize",
          rc: "ew-resize",
        };
        this.canvas.style.cursor = cs[handle];
      } else {
        const clicked = this.elements
          .slice()
          .reverse()
          .find((el) => el.contains(worldPos.x, worldPos.y));
        this.canvas.style.cursor = clicked ? "move" : "crosshair";
      }
    }
    if (!this.isDrawing || !this.currentElement) return;
    if (this.currentElement.type === "pencil") {
      this.currentElement.points.push(worldPos);
    } else {
      let dx = worldPos.x - this.currentElement.x;
      let dy = worldPos.y - this.currentElement.y;
      if (e.shiftKey) {
        if (
          this.currentElement.type === "line" ||
          this.currentElement.type === "arrow"
        ) {
          const angle =
            Math.round(Math.atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
          const dist = Math.sqrt(dx * dx + dy * dy);
          dx = dist * Math.cos(angle);
          dy = dist * Math.sin(angle);
        } else {
          const s = Math.max(Math.abs(dx), Math.abs(dy));
          dx = dx < 0 ? -s : s;
          dy = dy < 0 ? -s : s;
        }
      }
      if (
        this.currentElement.type === "line" ||
        this.currentElement.type === "arrow"
      ) {
        this.currentElement.endX = this.currentElement.x + dx;
        this.currentElement.endY = this.currentElement.y + dy;
      } else {
        this.currentElement.width = dx;
        this.currentElement.height = dy;
      }
    }
    this.render();
  }

  onMouseUp() {
    this.isPanning = false;
    if (this.isMoving || this.isResizing) {
      this.isMoving = false;
      this.isResizing = false;
      this.save();
    }
    if (this.isDrawing && this.currentElement) {
      this.elements.push(this.currentElement);
      this.currentElement = null;
      this.save();
      this.render();
    }
    this.isDrawing = false;
    this.canvas.style.cursor = "crosshair";
  }

  eraseAt(worldPos) {
    const radius = (this.style.strokeWidth * 2) / this.camera.zoom;
    let changed = false;
    const newElements = [];

    for (const el of this.elements) {
      if (el.intersects(worldPos.x, worldPos.y, radius)) {
        changed = true;
        if (el.type === "pencil") {
          const segments = [];
          let currentSegment = [];
          for (const p of el.points) {
            const d = Math.sqrt((p.x - worldPos.x) ** 2 + (p.y - worldPos.y) ** 2);
            if (d >= radius) {
              currentSegment.push(p);
            } else {
              if (currentSegment.length > 0) {
                segments.push(currentSegment);
                currentSegment = [];
              }
            }
          }
          if (currentSegment.length > 0) {
            segments.push(currentSegment);
          }

          for (const seg of segments) {
            if (seg.length >= 2) {
              const newEl = new Element("pencil", seg[0].x, seg[0].y, el);
              newEl.points = seg;
              newEl.strokeColor = el.strokeColor;
              newEl.strokeWidth = el.strokeWidth;
              newEl.strokeStyle = el.strokeStyle;
              newEl.opacity = el.opacity;
              newElements.push(newEl);
            }
          }
        }
        // Non-pencil shape elements are erased completely upon intersection
      } else {
        newElements.push(el);
      }
    }

    if (changed) {
      this.elements = newElements;
      this.selectedElements = this.selectedElements.filter((el) =>
        this.elements.includes(el),
      );
      this.render();
      this.save();
    }
  }

  onWheel(e) {
    e.preventDefault();
    const factor = 1.1;
    const zoomChange = e.deltaY < 0 ? factor : 1 / factor;
    const newZoom = this.camera.zoom * zoomChange;
    if (newZoom < 0.1 || newZoom > 20) return;
    const worldPos = this.screenToWorld(e.clientX, e.clientY);
    this.camera.zoom = newZoom;
    this.camera.x =
      e.clientX -
      this.canvas.getBoundingClientRect().left -
      worldPos.x * this.camera.zoom;
    this.camera.y =
      e.clientY -
      this.canvas.getBoundingClientRect().top -
      worldPos.y * this.camera.zoom;
    this.updateZoomUI();
    this.render();
  }

  // --- Helpers ---
  getSelectionBounds(el) {
    const bounds = el.getBounds();
    const padding = 4 / this.camera.zoom;
    return {
      x: bounds.minX - padding,
      y: bounds.minY - padding,
      width: bounds.maxX - bounds.minX + padding * 2,
      height: bounds.maxY - bounds.minY + padding * 2,
    };
  }
  getResizeHandle(worldPos) {
    if (this.selectedElements.length !== 1) return null;
    const s = this.getSelectionBounds(this.selectedElements[0]);
    const hSize = 8 / this.camera.zoom;
    const half = hSize / 2;
    const handles = [
      { id: "tl", x: s.x, y: s.y },
      { id: "tr", x: s.x + s.width, y: s.y },
      { id: "bl", x: s.x, y: s.y + s.height },
      { id: "br", x: s.x + s.width, y: s.y + s.height },
      { id: "tc", x: s.x + s.width / 2, y: s.y },
      { id: "bc", x: s.x + s.width / 2, y: s.y + s.height },
      { id: "lc", x: s.x, y: s.y + s.height / 2 },
      { id: "rc", x: s.x + s.width, y: s.y + s.height / 2 },
    ];
    for (const h of handles) {
      if (
        worldPos.x >= h.x - half &&
        worldPos.x <= h.x + half &&
        worldPos.y >= h.y - half &&
        worldPos.y <= h.y + half
      )
        return h.id;
    }
    return null;
  }

  onKeyDown(e) {
    if (e.code === "Space") this.isSpacePressed = true;
    if (e.key === "Delete" || e.key === "Backspace") {
      if (this.selectedElements.length > 0) {
        this.elements = this.elements.filter(
          (el) => !this.selectedElements.includes(el),
        );
        this.selectedElements = [];
        this.save();
        this.render();
      }
    }
    if ((e.ctrlKey || e.metaKey) && e.key === "z") {
      e.preventDefault();
      this.undo();
    }
    if ((e.ctrlKey || e.metaKey) && e.key === "y") {
      e.preventDefault();
      this.redo();
    }
    const tools = {
      v: "select",
      p: "pencil",
      e: "eraser",
      h: "pan",
      r: "rectangle",
      o: "circle",
      l: "line",
      a: "arrow",
      d: "diamond",
    };
    if (tools[e.key.toLowerCase()]) this.setTool(tools[e.key.toLowerCase()]);
  }

  screenToWorld(x, y) {
    const rect = this.canvas.getBoundingClientRect();
    return {
      x: (x - rect.left - this.camera.x) / this.camera.zoom,
      y: (y - rect.top - this.camera.y) / this.camera.zoom,
    };
  }

  render() {
    this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
    this.ctx.imageSmoothingEnabled = true;
    this.ctx.save();
    this.ctx.translate(this.camera.x, this.camera.y);
    this.ctx.scale(this.camera.zoom, this.camera.zoom);
    this.elements.forEach((el) => {
      el.draw(this.ctx);
      if (this.selectedElements.includes(el)) this.drawSelection(el);
    });
    if (this.currentElement) this.currentElement.draw(this.ctx);
    this.ctx.restore();
  }

  drawSelection(el) {
    const s = this.getSelectionBounds(el);
    const hSize = 8 / this.camera.zoom;
    const half = hSize / 2;
    this.ctx.save();
    this.ctx.strokeStyle = "#4f46e5";
    this.ctx.lineWidth = 1 / this.camera.zoom;
    this.ctx.setLineDash([5, 5]);
    this.ctx.strokeRect(s.x, s.y, s.width, s.height);
    if (this.selectedElements.length === 1) {
      this.ctx.setLineDash([]);
      this.ctx.fillStyle = "white";
      const handles = [
        { x: s.x, y: s.y },
        { x: s.x + s.width, y: s.y },
        { x: s.x, y: s.y + s.height },
        { x: s.x + s.width, y: s.y + s.height },
        { x: s.x + s.width / 2, y: s.y },
        { x: s.x + s.width / 2, y: s.y + s.height },
        { x: s.x, y: s.y + s.height / 2 },
        { x: s.x + s.width, y: s.y + s.height / 2 },
      ];
      handles.forEach((h) => {
        this.ctx.beginPath();
        this.ctx.rect(h.x - half, h.y - half, hSize, hSize);
        this.ctx.fill();
        this.ctx.stroke();
      });
    }
    this.ctx.restore();
  }

  showClearModal() {
    document.getElementById("clearConfirmModal").style.display = "flex";
  }
  hideClearModal() {
    document.getElementById("clearConfirmModal").style.display = "none";
  }

  async save() {
    await window.DrawStorage.saveElements(this.boardId, this.elements);
    document.getElementById("saveStatus").textContent = "Saving...";
    setTimeout(
      () => (document.getElementById("saveStatus").textContent = "Saved"),
      500,
    );
  }
  undo() {
    if (this.elements.length > 0) {
      this.redoStack.push(this.elements.pop());
      this.render();
      this.save();
    }
  }
  redo() {
    if (this.redoStack.length > 0) {
      this.elements.push(this.redoStack.pop());
      this.render();
      this.save();
    }
  }
  clear() {
    this.elements = [];
    this.redoStack = [];
    this.render();
    this.save();
    this.hideClearModal();
  }
  changeZoom(delta) {
    const zoomChange = 1 + delta;
    const centerX = this.canvas.width / 2;
    const centerY = this.canvas.height / 2;
    const worldPos = this.screenToWorld(
      centerX + this.canvas.getBoundingClientRect().left,
      centerY + this.canvas.getBoundingClientRect().top,
    );
    this.camera.zoom *= zoomChange;
    this.camera.x = centerX - worldPos.x * this.camera.zoom;
    this.camera.y = centerY - worldPos.y * this.camera.zoom;
    this.updateZoomUI();
    this.render();
  }
  updateZoomUI() {
    document.getElementById("zoomLevel").textContent =
      Math.round(this.camera.zoom * 100) + "%";
  }

  export(includeBackground = false) {
    const tempCanvas = document.createElement("canvas");
    tempCanvas.width = this.canvas.width;
    tempCanvas.height = this.canvas.height;
    const tCtx = tempCanvas.getContext("2d");

    if (includeBackground) {
      tCtx.fillStyle = this.backgroundColor;
      tCtx.fillRect(0, 0, tempCanvas.width, tempCanvas.height);
      // If grid is active, we could also draw the grid here if desired
    }

    tCtx.drawImage(this.canvas, 0, 0);

    const link = document.createElement("a");
    const format = includeBackground ? "image/jpeg" : "image/png";
    const ext = includeBackground ? "jpg" : "png";
    link.download = `whiteboard.${ext}`;
    link.href = tempCanvas.toDataURL(format, 0.9);
    link.click();
  }
}

document.addEventListener("DOMContentLoaded", () => {
  if (window.boardId)
    window.whiteboard = new Whiteboard("whiteboardCanvas", window.boardId);
});

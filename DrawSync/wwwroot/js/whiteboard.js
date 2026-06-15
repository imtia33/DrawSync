/**
 * whiteboard.js - Refined Professional Workspace Engine
 */

class Element {
  constructor(type, x, y, style) {
    this.id = Math.random().toString(36).substr(2, 9);
    this.type = type;
    this.x = x;
    this.y = y;
    this.width = style.width || 0;
    this.height = style.height || 0;
    this.strokeColor = style.strokeColor;
    this.fillColor = style.fillColor;
    this.strokeWidth = style.strokeWidth;
    this.strokeStyle = style.strokeStyle;
    this.opacity = style.opacity;
    this.points = []; // [{x, y}]
    this.endX = x;
    this.endY = y;

    // Neon Specific Styles
    this.glowIntensity =
      style.glowIntensity !== undefined ? style.glowIntensity : 15;

    // Text Specific Styles
    this.text = style.text || "";
    this.fontFamily = style.fontFamily || "sans-serif";
    this.fontSize = style.fontSize || 24;
    this.bold = style.bold || false;
    this.italic = style.italic || false;
    this.underline = style.underline || false;
  }

  getBounds() {
    if (this.type === "pencil" || this.type === "neon") {
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

    if (this.type === "text") {
      return true;
    }

    if (this.type === "pencil" || this.type === "neon") {
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

  draw(ctx, isDarkBg = false) {
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
      case "neon":
        this.drawNeon(ctx, isDarkBg);
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
        this.drawLine(ctx);
        break;
      case "arrow":
        this.drawArrow(ctx);
        break;
      case "text":
        this.drawText(ctx);
        break;
    }
    ctx.restore();
  }

  drawPath(ctx) {
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

  drawPencil(ctx) {
    const hasFill = this.fillColor && this.fillColor !== "transparent";
    const hasStroke = this.strokeColor && this.strokeColor !== "transparent";
    if (hasFill) {
      // Pass 1: Draw the border/outline in strokeColor (only if strokeColor is set and not transparent)
      if (hasStroke) {
        ctx.save();
        ctx.strokeStyle = this.strokeColor;
        ctx.lineWidth = this.strokeWidth + 2.5; // Slightly thicker border
        this.drawPath(ctx);
        ctx.restore();
      }

      // Pass 2: Draw the main center line in fillColor
      ctx.save();
      ctx.strokeStyle = this.fillColor;
      ctx.lineWidth = this.strokeWidth;
      this.drawPath(ctx);
      ctx.restore();
    } else {
      // Normal single stroke in strokeColor
      this.drawPath(ctx);
    }
  }

  drawNeon(ctx, isDarkBg) {
    if (this.points.length < 2) return;

    ctx.save();
    ctx.lineCap = "round";
    ctx.lineJoin = "round";
    ctx.setLineDash([]); // Neon strokes are always solid

    const hasFill = this.fillColor && this.fillColor !== "transparent";
    const hasStroke = this.strokeColor && this.strokeColor !== "transparent";
    const mainColor = hasFill ? this.fillColor : this.strokeColor;
    const colors = this.getNeonColors(mainColor, isDarkBg);
    const blurAmount = this.glowIntensity || 15;

    // Pass 1: Outer soft deep glow (using mainColor)
    ctx.shadowColor = colors.glow;
    ctx.shadowBlur = blurAmount * 1.5;
    ctx.strokeStyle = colors.glow;
    ctx.lineWidth = this.strokeWidth + 12;
    ctx.globalAlpha = (this.opacity / 100) * 0.2;
    this.drawPath(ctx);

    // Pass 2: Main vibrant glow (using mainColor)
    ctx.shadowColor = colors.glow;
    ctx.shadowBlur = blurAmount;
    ctx.strokeStyle = colors.glow;
    ctx.lineWidth = this.strokeWidth + 4;
    ctx.globalAlpha = (this.opacity / 100) * 0.5;
    this.drawPath(ctx);

    // Pass 3: Solid border stroke (if fillColor and strokeColor are set, drawn underneath the main line and core)
    if (hasFill && hasStroke) {
      ctx.shadowColor = "transparent";
      ctx.shadowBlur = 0;
      ctx.strokeStyle = this.strokeColor;
      ctx.lineWidth = this.strokeWidth + 2.5; // Slightly thicker border outline
      ctx.globalAlpha = this.opacity / 100;
      this.drawPath(ctx);
    }

    // Pass 4: Solid main tube (if fillColor is set, drawn on top of the border and underneath the core)
    if (hasFill) {
      ctx.shadowColor = "transparent";
      ctx.shadowBlur = 0;
      ctx.strokeStyle = this.fillColor;
      ctx.lineWidth = this.strokeWidth;
      ctx.globalAlpha = this.opacity / 100;
      this.drawPath(ctx);
    }

    // Pass 5: Bright, crisp center tube core (using core color)
    ctx.shadowColor = colors.glow;
    ctx.shadowBlur = blurAmount * 0.35;
    ctx.strokeStyle = colors.core;
    // Core sits beautifully inside the border/main line
    ctx.lineWidth = hasFill
      ? Math.max(1.0, this.strokeWidth * 0.4)
      : Math.max(1.5, this.strokeWidth * 0.4);
    ctx.globalAlpha = this.opacity / 100;
    this.drawPath(ctx);

    ctx.restore();
  }

  getNeonColors(hex, isDarkBg) {
    if (!hex || hex === "transparent") {
      return { core: "transparent", glow: "transparent" };
    }

    let r = 0,
      g = 0,
      b = 0;
    if (hex.startsWith("#")) {
      if (hex.length === 4) {
        r = parseInt(hex[1] + hex[1], 16);
        g = parseInt(hex[2] + hex[2], 16);
        b = parseInt(hex[3] + hex[3], 16);
      } else if (hex.length === 7) {
        r = parseInt(hex.slice(1, 3), 16);
        g = parseInt(hex.slice(3, 5), 16);
        b = parseInt(hex.slice(5, 7), 16);
      }
    } else if (hex.startsWith("rgb")) {
      const parts = hex.match(/\d+/g);
      if (parts) {
        r = parseInt(parts[0]);
        g = parseInt(parts[1]);
        b = parseInt(parts[2]);
      }
    } else {
      return { core: isDarkBg ? "#ffffff" : hex, glow: hex };
    }

    let coreR, coreG, coreB;
    if (isDarkBg) {
      // Dark background: Core is bright pastel mixed with white (85% white, 15% color)
      coreR = Math.round(r * 0.15 + 255 * 0.85);
      coreG = Math.round(g * 0.15 + 255 * 0.85);
      coreB = Math.round(b * 0.15 + 255 * 0.85);
    } else {
      // Light background: Core is richer color mixed with white (60% white, 40% color)
      coreR = Math.round(r * 0.4 + 255 * 0.6);
      coreG = Math.round(g * 0.4 + 255 * 0.6);
      coreB = Math.round(b * 0.4 + 255 * 0.6);
    }

    const coreHex =
      "#" +
      [coreR, coreG, coreB]
        .map((x) => {
          const clamped = Math.max(0, Math.min(255, x));
          return clamped.toString(16).padStart(2, "0");
        })
        .join("");

    return { core: coreHex, glow: hex };
  }

  drawLine(ctx) {
    const hasFill = this.fillColor && this.fillColor !== "transparent";
    const hasStroke = this.strokeColor && this.strokeColor !== "transparent";
    if (hasFill) {
      // Pass 1: Draw the border outline in strokeColor
      if (hasStroke) {
        ctx.save();
        ctx.strokeStyle = this.strokeColor;
        ctx.lineWidth = this.strokeWidth + 2.5; // Slightly thicker border
        ctx.beginPath();
        ctx.moveTo(this.x, this.y);
        ctx.lineTo(this.endX, this.endY);
        ctx.stroke();
        ctx.restore();
      }

      // Pass 2: Draw the main center line in fillColor
      ctx.save();
      ctx.strokeStyle = this.fillColor;
      ctx.lineWidth = this.strokeWidth;
      ctx.beginPath();
      ctx.moveTo(this.x, this.y);
      ctx.lineTo(this.endX, this.endY);
      ctx.stroke();
      ctx.restore();
    } else {
      // Normal single stroke line in strokeColor
      ctx.beginPath();
      ctx.moveTo(this.x, this.y);
      ctx.lineTo(this.endX, this.endY);
      ctx.stroke();
    }
  }

  drawArrow(ctx) {
    const headlen = 10 * (this.strokeWidth / 2);
    const angle = Math.atan2(this.endY - this.y, this.endX - this.x);
    const hasFill = this.fillColor && this.fillColor !== "transparent";
    const hasStroke = this.strokeColor && this.strokeColor !== "transparent";

    if (hasFill) {
      // Pass 1: Draw the border outline for BOTH shaft and head in strokeColor
      if (hasStroke) {
        ctx.save();
        ctx.strokeStyle = this.strokeColor;
        ctx.lineWidth = this.strokeWidth + 2.5;

        // Draw shaft border
        ctx.beginPath();
        ctx.moveTo(this.x, this.y);
        ctx.lineTo(this.endX, this.endY);
        ctx.stroke();

        // Draw arrowhead border
        ctx.beginPath();
        ctx.moveTo(this.endX, this.endY);
        const p1X = this.endX - headlen * Math.cos(angle - Math.PI / 6);
        const p1Y = this.endY - headlen * Math.sin(angle - Math.PI / 6);
        const p2X = this.endX - headlen * Math.cos(angle + Math.PI / 6);
        const p2Y = this.endY - headlen * Math.sin(angle + Math.PI / 6);
        ctx.lineTo(p1X, p1Y);
        ctx.lineTo(p2X, p2Y);
        ctx.closePath();
        ctx.stroke();

        ctx.restore();
      }

      // Pass 2: Draw the main center shaft and fill/outline arrowhead in fillColor
      ctx.save();
      ctx.strokeStyle = this.fillColor;
      ctx.fillStyle = this.fillColor;
      ctx.lineWidth = this.strokeWidth;

      // Draw main shaft
      ctx.beginPath();
      ctx.moveTo(this.x, this.y);
      ctx.lineTo(this.endX, this.endY);
      ctx.stroke();

      // Draw and fill arrowhead
      ctx.beginPath();
      ctx.moveTo(this.endX, this.endY);
      const p1X = this.endX - headlen * Math.cos(angle - Math.PI / 6);
      const p1Y = this.endY - headlen * Math.sin(angle - Math.PI / 6);
      const p2X = this.endX - headlen * Math.cos(angle + Math.PI / 6);
      const p2Y = this.endY - headlen * Math.sin(angle + Math.PI / 6);
      ctx.lineTo(p1X, p1Y);
      ctx.lineTo(p2X, p2Y);
      ctx.closePath();
      ctx.fill();
      ctx.stroke();

      ctx.restore();
    } else {
      // Normal single stroke arrow (no fill)
      // Draw main shaft
      ctx.beginPath();
      ctx.moveTo(this.x, this.y);
      ctx.lineTo(this.endX, this.endY);
      ctx.stroke();

      // Draw arrowhead
      ctx.beginPath();
      ctx.moveTo(this.endX, this.endY);
      const p1X = this.endX - headlen * Math.cos(angle - Math.PI / 6);
      const p1Y = this.endY - headlen * Math.sin(angle - Math.PI / 6);
      const p2X = this.endX - headlen * Math.cos(angle + Math.PI / 6);
      const p2Y = this.endY - headlen * Math.sin(angle + Math.PI / 6);
      ctx.lineTo(p1X, p1Y);
      ctx.lineTo(p2X, p2Y);
      ctx.closePath();

      ctx.fillStyle = this.strokeColor;
      ctx.fill();
      ctx.stroke();
    }
  }

  drawText(ctx) {
    if (!this.text) return;
    ctx.save();
    ctx.textBaseline = "top";

    let fontStyle = "";
    if (this.italic) fontStyle += "italic ";
    if (this.bold) fontStyle += "bold ";
    ctx.font = `${fontStyle}${this.fontSize}px ${this.fontFamily}`;

    const lines = this.text.split("\n");
    const lineHeight = this.fontSize * 1.2;

    const isTransparentFill =
      !this.fillColor || this.fillColor === "transparent";
    ctx.fillStyle = isTransparentFill ? this.strokeColor : this.fillColor;

    for (let i = 0; i < lines.length; i++) {
      const line = lines[i];
      const lineY = this.y + i * lineHeight;

      ctx.fillText(line, this.x, lineY);

      if (
        !isTransparentFill &&
        this.strokeColor &&
        this.strokeColor !== "transparent" &&
        this.strokeWidth > 0
      ) {
        ctx.strokeText(line, this.x, lineY);
      }

      if (this.underline) {
        const textWidth = ctx.measureText(line).width;
        ctx.beginPath();
        const underlineY = lineY + this.fontSize * 0.95;
        ctx.moveTo(this.x, underlineY);
        ctx.lineTo(this.x + textWidth, underlineY);
        ctx.lineWidth = Math.max(1, this.fontSize / 15);
        ctx.strokeStyle = isTransparentFill ? this.strokeColor : this.fillColor;
        ctx.stroke();
      }
    }
    ctx.restore();
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
      fontFamily: "sans-serif",
      fontSize: 24,
      bold: false,
      italic: false,
      underline: false,
      glowIntensity: 15,
    };
    this.isTyping = false;
    this.backgroundColor = "#f8f9fa";
    this.hasGrid = false;
    this.cpTarget = "stroke";
    this.currentHue = 0;
    this.realtime = null;
    this.remoteCursors = {}; // { userId: { userName, color, targetX, targetY, displayX, displayY, tool, lastSeen } }
    this._cursorAnimFrame = null;
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
    this.setupRealtime();
    this._startCursorAnimLoop();
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
    document.getElementById("glowIntensity").oninput = (e) => {
      const val = parseInt(e.target.value);
      document.getElementById("glowVal").textContent = val + "px";
      this.updateSelectedStyle("glowIntensity", val);
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

    document.getElementById("savePicker").onclick = () => {
      const key = this.cpTarget === "stroke" ? "strokeColor" : "fillColor";
      this.updateSelectedStyle(key, this.stagedColor);

      const swatch = document.getElementById(this.cpTarget + "Swatch");
      swatch.style.backgroundColor = this.stagedColor;
      swatch.classList.toggle(
        "is-transparent",
        this.stagedColor === "transparent",
      );

      document.getElementById("colorPickerPanel").style.display = "none";
    };

    document.getElementById("closePicker").onclick = () => {
      document.getElementById("colorPickerPanel").style.display = "none";
    };

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

    document.getElementById("fontFamily").onchange = (e) => {
      this.updateSelectedTextStyle("fontFamily", e.target.value);
    };
    document.getElementById("fontSize").oninput = (e) => {
      const val = parseInt(e.target.value);
      document.getElementById("fontSizeVal").textContent = val + "px";
      this.updateSelectedTextStyle("fontSize", val);
    };
    document.getElementById("btnBold").onclick = () => {
      const btn = document.getElementById("btnBold");
      const active = !btn.classList.contains("active");
      btn.classList.toggle("active", active);
      this.updateSelectedTextStyle("bold", active);
    };
    document.getElementById("btnItalic").onclick = () => {
      const btn = document.getElementById("btnItalic");
      const active = !btn.classList.contains("active");
      btn.classList.toggle("active", active);
      this.updateSelectedTextStyle("italic", active);
    };
    document.getElementById("btnUnderline").onclick = () => {
      const btn = document.getElementById("btnUnderline");
      const active = !btn.classList.contains("active");
      btn.classList.toggle("active", active);
      this.updateSelectedTextStyle("underline", active);
    };
    this.canvas.addEventListener("dblclick", (e) => {
      if (this.currentTool !== "select") return;
      const worldPos = this.screenToWorld(e.clientX, e.clientY);
      const clicked = this.elements
        .slice()
        .reverse()
        .find(
          (el) => el.type === "text" && el.contains(worldPos.x, worldPos.y),
        );
      if (clicked) {
        const rect = this.canvas.getBoundingClientRect();
        const screenX =
          rect.left + clicked.x * this.camera.zoom + this.camera.x;
        const screenY = rect.top + clicked.y * this.camera.zoom + this.camera.y;
        e.preventDefault();
        this.startTyping(clicked, screenX, screenY, clicked);
      }
    });
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
    this.updateTextSettingsVisibility();
    this.updateNeonSettingsVisibility();
    this.render();

    // Broadcast tool change to peers
    if (this.realtime) {
      this.realtime.sendToolChange(tool);
    }
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
        document.getElementById("svCursor").style.display = "block";
        updateSV(angle);
        const cursorLeft =
          parseFloat(document.getElementById("svCursor").style.left) - 45 || 0;
        const cursorTop =
          parseFloat(document.getElementById("svCursor").style.top) - 45 || 0;
        const s = (cursorLeft / 139) * 100;
        const v = (1 - cursorTop / 139) * 100;
        this.updateColorPickerUI(this.hsvToHex(angle, s, v));
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
    document.getElementById("noFillBtn").onclick = () => {
      this.updateColorPickerUI("transparent");
      document.getElementById("svCursor").style.display = "none";
    };
    document.getElementById("hexInput").onchange = (e) => {
      let color = e.target.value;
      if (color !== "transparent" && !color.startsWith("#")) {
        color = "#" + color;
      }
      this.updateColorPickerUI(color);
      this.initCursorPositionsForColor(color);
    };
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

  hexToHsv(hex) {
    if (!hex || hex === "transparent" || !hex.startsWith("#")) {
      return { h: 0, s: 0, v: 0 };
    }
    let r = 0,
      g = 0,
      b = 0;
    if (hex.length === 4) {
      r = parseInt(hex[1] + hex[1], 16) / 255;
      g = parseInt(hex[2] + hex[2], 16) / 255;
      b = parseInt(hex[3] + hex[3], 16) / 255;
    } else if (hex.length === 7) {
      r = parseInt(hex.slice(1, 3), 16) / 255;
      g = parseInt(hex.slice(3, 5), 16) / 255;
      b = parseInt(hex.slice(5, 7), 16) / 255;
    }
    const max = Math.max(r, g, b),
      min = Math.min(r, g, b);
    let h,
      s,
      v = max;
    const d = max - min;
    s = max === 0 ? 0 : d / max;
    if (max === min) {
      h = 0;
    } else {
      switch (max) {
        case r:
          h = (g - b) / d + (g < b ? 6 : 0);
          break;
        case g:
          h = (b - r) / d + 2;
          break;
        case b:
          h = (r - g) / d + 4;
          break;
      }
      h /= 6;
    }
    return { h: h * 360, s: s * 100, v: v * 100 };
  }

  initCursorPositionsForColor(color) {
    if (color === "transparent") {
      document.getElementById("svCursor").style.display = "none";
      return;
    }
    document.getElementById("svCursor").style.display = "block";
    const hsv = this.hexToHsv(color);
    this.currentHue = hsv.h;

    const sv = document.getElementById("svSquare");
    const sCtx = sv.getContext("2d");
    sCtx.clearRect(0, 0, 140, 140);
    sCtx.fillStyle = `hsl(${hsv.h}, 100%, 50%)`;
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

    const x = (hsv.s / 100) * 139;
    const y = (1 - hsv.v / 100) * 139;
    document.getElementById("svCursor").style.left = x + 45 + "px";
    document.getElementById("svCursor").style.top = y + 45 + "px";
  }

  updateColorPickerUI(color) {
    this.stagedColor = color;
    document.getElementById("hexInput").value = color;
    const preview = document.getElementById("colorPreview");
    if (color === "transparent") {
      preview.style.backgroundColor = "";
      preview.classList.add("is-transparent");
    } else {
      preview.style.backgroundColor = color;
      preview.classList.remove("is-transparent");
    }
  }

  openColorPicker(target, e) {
    this.cpTarget = target;
    const panel = document.getElementById("colorPickerPanel");
    panel.style.display = "block";
    panel.style.left = "270px";
    // Clamp the top position so it never goes off-screen or collides awkwardly with the top border
    panel.style.top = Math.max(70, e.clientY - 100) + "px";
    document.getElementById("pickerTitle").textContent =
      target === "stroke" ? "Stroke Color" : "Fill Color";

    this.originalColor =
      target === "stroke" ? this.style.strokeColor : this.style.fillColor;
    this.stagedColor = this.originalColor;

    document.getElementById("hexInput").value = this.originalColor;
    const preview = document.getElementById("colorPreview");
    if (this.originalColor === "transparent") {
      preview.style.backgroundColor = "";
      preview.classList.add("is-transparent");
    } else {
      preview.style.backgroundColor = this.originalColor;
      preview.classList.remove("is-transparent");
    }

    this.initCursorPositionsForColor(this.originalColor);
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
    if (this.currentTool === "text") {
      if (this.isTyping) return;
      e.preventDefault();
      this.startTyping(worldPos, clientX, clientY);
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
        if (clicked.type === "text") {
          this.syncTextSettingsUI(clicked);
        }
        if (clicked.type === "neon") {
          this.syncNeonSettingsUI(clicked);
        }
        this.isMoving = true;
      } else {
        this.selectedElements = [];
      }
      this.updateTextSettingsVisibility();
      this.updateNeonSettingsVisibility();
      this.render();
      return;
    }
    this.isDrawing = true;
    const tool = this.currentTool;
    this.currentElement = new Element(tool, worldPos.x, worldPos.y, this.style);
    if (tool === "pencil" || tool === "neon")
      this.currentElement.points.push(worldPos);
  }

  onMouseMove(e) {
    const { clientX, clientY } = e;
    const worldPos = this.screenToWorld(clientX, clientY);

    // Send cursor position to peers (throttled in realtime.js)
    if (this.realtime) {
      this.realtime.sendCursor(worldPos.x, worldPos.y);
    }

    const cursor = document.getElementById("customCursor");
    cursor.style.display = "block";
    cursor.style.left = clientX + "px";
    cursor.style.top = clientY + "px";
    const size =
      this.currentTool === "pencil" ||
      this.currentTool === "neon" ||
      this.currentTool === "eraser"
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
        if (el.type === "pencil" || el.type === "neon")
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
    if (
      this.currentElement.type === "pencil" ||
      this.currentElement.type === "neon"
    ) {
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
      // Broadcast updated elements to peers
      if (this.realtime) {
        this.selectedElements.forEach((el) => {
          this.realtime.sendElement(el, "update");
        });
      }
    }
    if (this.isDrawing && this.currentElement) {
      this.elements.push(this.currentElement);
      // Broadcast new element to peers
      if (this.realtime) {
        this.realtime.sendElement(this.currentElement, "add");
      }
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
        if (el.type === "pencil" || el.type === "neon") {
          const segments = [];
          let currentSegment = [];
          for (const p of el.points) {
            const d = Math.sqrt(
              (p.x - worldPos.x) ** 2 + (p.y - worldPos.y) ** 2,
            );
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
              const newEl = new Element(el.type, seg[0].x, seg[0].y, el);
              newEl.points = seg;
              newEl.strokeColor = el.strokeColor;
              newEl.strokeWidth = el.strokeWidth;
              newEl.strokeStyle = el.strokeStyle;
              newEl.opacity = el.opacity;
              newEl.glowIntensity = el.glowIntensity; // preserve glowIntensity
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
      const oldElements = this.elements;
      this.elements = newElements;
      this.selectedElements = this.selectedElements.filter((el) =>
        this.elements.includes(el),
      );
      this.render();
      this.save();

      // Broadcast erase changes to peers
      if (this.realtime) {
        // Send removed elements as deletes (were in old but not in new)
        for (const el of oldElements) {
          if (!newElements.includes(el)) {
            this.realtime.sendElement(el, "delete");
          }
        }
        // Send new segments from split strokes as adds (new Element instances)
        for (const el of newElements) {
          if (!oldElements.includes(el)) {
            this.realtime.sendElement(el, "add");
          }
        }
      }
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
    if (this.isTyping) return;
    if (e.code === "Space") this.isSpacePressed = true;
    if (e.key === "Delete" || e.key === "Backspace") {
      if (this.selectedElements.length > 0) {
        // Broadcast deleted elements to peers
        if (this.realtime) {
          this.selectedElements.forEach((el) => {
            this.realtime.sendElement(el, "delete");
          });
        }
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
      n: "neon",
      e: "eraser",
      h: "pan",
      r: "rectangle",
      o: "circle",
      l: "line",
      a: "arrow",
      d: "diamond",
      t: "text",
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

    const isDark = this.isColorDark(this.backgroundColor);

    // Draw non-text shape elements first
    this.elements.forEach((el) => {
      if (el.type !== "text") {
        el.draw(this.ctx, isDark);
        if (this.selectedElements.includes(el)) this.drawSelection(el);
      }
    });

    // Draw text elements on top so text overlays shapes
    this.elements.forEach((el) => {
      if (el.type === "text") {
        el.draw(this.ctx, isDark);
        if (this.selectedElements.includes(el)) this.drawSelection(el);
      }
    });

    if (this.currentElement) this.currentElement.draw(this.ctx, isDark);

    // Draw remote cursors on top
    this._renderRemoteCursors();

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
    // Broadcast clear to peers
    if (this.realtime) {
      this.realtime.sendClear();
    }
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

  updateTextSettingsVisibility() {
    const textSettings = document.getElementById("textSettingsSection");
    if (textSettings) {
      const hasTextSelected = this.selectedElements.some(
        (el) => el.type === "text",
      );
      textSettings.style.display =
        this.currentTool === "text" || hasTextSelected ? "block" : "none";
    }
  }

  syncTextSettingsUI(el) {
    if (el.type !== "text") return;
    document.getElementById("fontFamily").value = el.fontFamily;
    document.getElementById("fontSize").value = el.fontSize;
    document.getElementById("fontSizeVal").textContent = el.fontSize + "px";
    document.getElementById("btnBold").classList.toggle("active", el.bold);
    document.getElementById("btnItalic").classList.toggle("active", el.italic);
    document
      .getElementById("btnUnderline")
      .classList.toggle("active", el.underline);
  }

  updateNeonSettingsVisibility() {
    const neonSettings = document.querySelectorAll(".neon-only-setting");
    const hasNeonSelected = this.selectedElements.some(
      (el) => el.type === "neon",
    );
    const isNeonActive = this.currentTool === "neon" || hasNeonSelected;

    neonSettings.forEach((el) => {
      el.style.display = isNeonActive ? "block" : "none";
    });

    if (hasNeonSelected) {
      const selectedNeon = this.selectedElements.find(
        (el) => el.type === "neon",
      );
      if (selectedNeon) {
        this.syncNeonSettingsUI(selectedNeon);
      }
    }
  }

  syncNeonSettingsUI(el) {
    if (el.type !== "neon") return;
    const intensity = el.glowIntensity !== undefined ? el.glowIntensity : 15;
    document.getElementById("glowIntensity").value = intensity;
    document.getElementById("glowVal").textContent = intensity + "px";
  }

  updateSelectedTextStyle(key, val) {
    this.style[key] = val;
    this.selectedElements.forEach((el) => {
      if (el.type === "text") {
        el[key] = val;
        this.updateTextSize(el);
      }
    });
    if (this.selectedElements.length > 0) this.save();
    this.render();
  }

  updateTextSize(el) {
    this.ctx.save();
    let fontStyle = "";
    if (el.italic) fontStyle += "italic ";
    if (el.bold) fontStyle += "bold ";
    this.ctx.font = `${fontStyle}${el.fontSize}px ${el.fontFamily}`;

    const lines = el.text.split("\n");
    let maxWidth = 0;
    for (const line of lines) {
      const w = this.ctx.measureText(line).width;
      if (w > maxWidth) maxWidth = w;
    }
    el.width = maxWidth;
    el.height = lines.length * el.fontSize * 1.2;
    this.ctx.restore();
  }

  startTyping(posOrEl, screenX, screenY, existingElement = null) {
    this.isTyping = true;

    const container = document.createElement("div");
    container.className = "canvas-text-container";
    container.style.left = screenX + "px";
    container.style.top = screenY + "px";

    const dragHandle = document.createElement("div");
    dragHandle.className = "text-drag-handle";
    dragHandle.innerHTML = '<i class="bi bi-arrows-move"></i>';
    dragHandle.title = "Drag to move text box";
    dragHandle.tabIndex = -1;

    const textarea = document.createElement("textarea");
    textarea.className = "canvas-text-input";

    const source = existingElement || this.style;
    textarea.style.fontFamily = source.fontFamily || "sans-serif";
    textarea.style.fontSize = (source.fontSize || 24) * this.camera.zoom + "px";
    textarea.style.fontWeight = source.bold ? "bold" : "normal";
    textarea.style.fontStyle = source.italic ? "italic" : "normal";
    textarea.style.textDecoration = source.underline ? "underline" : "none";

    const textColor =
      source.fillColor !== "transparent"
        ? source.fillColor
        : source.strokeColor !== "transparent"
          ? source.strokeColor
          : "#000000";
    textarea.style.color = textColor;

    textarea.value = existingElement ? existingElement.text : "";

    container.appendChild(dragHandle);
    container.appendChild(textarea);
    document.body.appendChild(container);
    textarea.focus();

    let isDraggingText = false;
    let dragStartX = 0;
    let dragStartY = 0;

    dragHandle.onmousedown = (de) => {
      de.preventDefault();
      de.stopPropagation();
      isDraggingText = true;
      dragStartX = de.clientX;
      dragStartY = de.clientY;

      const onMouseMoveDrag = (me) => {
        if (!isDraggingText) return;
        const dx = me.clientX - dragStartX;
        const dy = me.clientY - dragStartY;

        const currentLeft = parseFloat(container.style.left) || screenX;
        const currentTop = parseFloat(container.style.top) || screenY;
        container.style.left = currentLeft + dx + "px";
        container.style.top = currentTop + dy + "px";

        posOrEl.x += dx / this.camera.zoom;
        posOrEl.y += dy / this.camera.zoom;

        dragStartX = me.clientX;
        dragStartY = me.clientY;
      };

      const onMouseUpDrag = () => {
        isDraggingText = false;
        window.removeEventListener("mousemove", onMouseMoveDrag);
        window.removeEventListener("mouseup", onMouseUpDrag);
        textarea.focus();
      };

      window.addEventListener("mousemove", onMouseMoveDrag);
      window.addEventListener("mouseup", onMouseUpDrag);
    };

    const adjustTextarea = () => {
      textarea.style.width = "auto";
      textarea.style.height = "auto";
      textarea.style.width = Math.max(150, textarea.scrollWidth + 10) + "px";
      textarea.style.height = Math.max(35, textarea.scrollHeight) + "px";
    };
    textarea.oninput = adjustTextarea;
    adjustTextarea();

    let committed = false;
    const commit = () => {
      if (committed) return;
      committed = true;
      const text = textarea.value.trim();

      if (text) {
        if (existingElement) {
          existingElement.text = text;
          this.updateTextSize(existingElement);
        } else {
          const newEl = new Element("text", posOrEl.x, posOrEl.y, this.style);
          newEl.text = text;
          newEl.fontFamily = this.style.fontFamily;
          newEl.fontSize = this.style.fontSize;
          newEl.bold = this.style.bold;
          newEl.italic = this.style.italic;
          newEl.underline = this.style.underline;

          this.updateTextSize(newEl);
          this.elements.push(newEl);
        }
        this.save();
        this.render();
      } else if (existingElement) {
        this.elements = this.elements.filter((el) => el !== existingElement);
        this.save();
        this.render();
      }

      document.body.removeChild(container);
      this.isTyping = false;
      this.isDrawing = false;
      this.render();
    };

    textarea.onblur = (be) => {
      if (isDraggingText) return;
      if (be.relatedTarget === dragHandle) return;
      commit();
    };

    textarea.onkeydown = (e) => {
      if (e.key === "Escape") {
        committed = true;
        document.body.removeChild(container);
        this.isTyping = false;
        this.isDrawing = false;
        this.render();
      }
      if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
        commit();
      }
    };
  }

  // --- Realtime Collaboration Methods ---

  /**
   * Set up SignalR realtime callbacks if available.
   */
  setupRealtime() {
    if (!window.realtimeClient) return;

    this.realtime = window.realtimeClient;

    // Handle incoming element changes from peers
    this.realtime.onElementChanged = (elementData, action, userId) => {
      this._applyRemoteElementChange(elementData, action);
    };

    // Handle incoming cursor movements from peers (set target for lerp)
    this.realtime.onCursorMoved = (userId, userName, color, cursor) => {
      const existing = this.remoteCursors[userId];
      if (existing) {
        // Update target position (lerp will smooth it out)
        existing.targetX = cursor.x;
        existing.targetY = cursor.y;
        existing.lastSeen = Date.now();
      } else {
        // New cursor - start at target position (no jump from 0,0)
        this.remoteCursors[userId] = {
          userName,
          color,
          targetX: cursor.x,
          targetY: cursor.y,
          displayX: cursor.x,
          displayY: cursor.y,
          tool: "select",
          lastSeen: Date.now(),
        };
      }
    };

    // Handle board clear from peers
    this.realtime.onBoardCleared = (userId) => {
      this.elements = [];
      this.redoStack = [];
      this.render();
      this.save();
    };

    // Handle user left - remove their cursor
    this.realtime.onUserLeft = (data) => {
      delete this.remoteCursors[data.userId];
    };

    // Handle tool change from peers
    this.realtime.onToolChanged = (userId, userName, color, tool) => {
      if (this.remoteCursors[userId]) {
        this.remoteCursors[userId].tool = tool;
      }
    };
  }

  /**
   * Apply a remote element change from a peer.
   */
  _applyRemoteElementChange(elementData, action) {
    if (!elementData) return;

    switch (action) {
      case "add": {
        const el = this._reconstructElement(elementData);
        // Avoid duplicates by checking element ID
        if (!this.elements.find((e) => e.id === el.id)) {
          this.elements.push(el);
        }
        break;
      }
      case "update": {
        const existing = this.elements.find((e) => e.id === elementData.id);
        if (existing) {
          Object.assign(existing, elementData);
          // Reconstruct points array if needed
          if (elementData.points) {
            existing.points = elementData.points;
          }
        } else {
          // Element not found locally, add it
          const el = this._reconstructElement(elementData);
          this.elements.push(el);
        }
        break;
      }
      case "delete": {
        this.elements = this.elements.filter((e) => e.id !== elementData.id);
        this.selectedElements = this.selectedElements.filter(
          (e) => e.id !== elementData.id,
        );
        break;
      }
    }

    this.save();
    this.render();
  }

  /**
   * Reconstruct an Element instance from a plain object received via SignalR.
   */
  _reconstructElement(data) {
    const el = new Element(
      data.type || "pencil",
      data.x || 0,
      data.y || 0,
      data,
    );
    // Copy all properties from the data
    Object.assign(el, data);
    // Ensure points is an array
    if (data.points && Array.isArray(data.points)) {
      el.points = data.points;
    }
    return el;
  }

  /**
   * Render remote user cursors on the canvas with smooth lerp animation.
   */
  _renderRemoteCursors() {
    const now = Date.now();
    const staleThreshold = 10000; // 10 seconds

    // Tool icon map (Bootstrap icon unicode characters)
    const toolIcons = {
      select: "\uF2E2", // cursor-fill
      pencil: "\uF4CB", // pencil-fill
      neon: "\uF4A0", // magic (sparkle)
      eraser: "\uF339", // eraser-fill
      text: "\uF5C0", // type
      pan: "\uF3D1", // hand-index-thumb
      line: "\uF534", // slash-lg
      arrow: "\uF138", // arrow-up-right
      rectangle: "\uF53E", // square
      circle: "\uF28A", // circle
      diamond: "\uF304", // diamond
    };

    for (const [userId, cursor] of Object.entries(this.remoteCursors)) {
      // Skip stale cursors
      if (now - cursor.lastSeen > staleThreshold) continue;

      const x = cursor.displayX;
      const y = cursor.displayY;
      const color = cursor.color || "#3b82f6";
      const name = cursor.userName || "User";
      const tool = cursor.tool || "select";

      // Draw cursor arrow
      this.ctx.save();
      this.ctx.translate(x, y);

      // Cursor pointer shape
      this.ctx.beginPath();
      this.ctx.moveTo(0, 0);
      this.ctx.lineTo(0, 14);
      this.ctx.lineTo(4, 11);
      this.ctx.lineTo(7, 17);
      this.ctx.lineTo(9, 16);
      this.ctx.lineTo(6, 10);
      this.ctx.lineTo(10, 9);
      this.ctx.closePath();

      // Fill with user color
      this.ctx.fillStyle = color;
      this.ctx.fill();
      this.ctx.strokeStyle = "white";
      this.ctx.lineWidth = 1.5 / this.camera.zoom;
      this.ctx.stroke();

      // Draw name + tool label
      const label = name.split(" ")[0]; // First name only
      const fontSize = 11 / this.camera.zoom;
      this.ctx.font = `600 ${fontSize}px Inter, sans-serif`;
      const textWidth = this.ctx.measureText(label).width;
      const labelX = 12 / this.camera.zoom;
      const labelY = 16 / this.camera.zoom;
      const padding = 4 / this.camera.zoom;
      const borderRadius = 3 / this.camera.zoom;

      // Name badge background
      this.ctx.fillStyle = color;
      this.ctx.beginPath();
      this.ctx.roundRect(
        labelX - padding,
        labelY - fontSize - padding,
        textWidth + padding * 2,
        fontSize + padding * 2,
        borderRadius,
      );
      this.ctx.fill();

      // Name badge text
      this.ctx.fillStyle = "white";
      this.ctx.fillText(label, labelX, labelY);

      // Tool badge (below the name badge)
      const toolLabel = tool.charAt(0).toUpperCase() + tool.slice(1);
      const toolFontSize = 9 / this.camera.zoom;
      this.ctx.font = `500 ${toolFontSize}px Inter, sans-serif`;
      const toolTextWidth = this.ctx.measureText(toolLabel).width;
      const toolBadgeY = labelY + fontSize + padding * 2;
      const iconSize = (toolFontSize + 2) / this.camera.zoom;
      const toolBadgeWidth = toolTextWidth + iconSize + padding * 3;

      // Tool badge background (semi-transparent)
      this.ctx.fillStyle = color + "CC"; // ~80% opacity
      this.ctx.beginPath();
      this.ctx.roundRect(
        labelX - padding,
        toolBadgeY - toolFontSize - padding,
        toolBadgeWidth,
        toolFontSize + padding * 2,
        borderRadius,
      );
      this.ctx.fill();

      // Tool icon (draw as bootstrap icon font glyph)
      const iconChar = toolIcons[tool] || toolIcons["select"];
      this.ctx.font = `400 ${iconSize}px "bootstrap-icons"`;
      this.ctx.fillStyle = "rgba(255,255,255,0.9)";
      this.ctx.fillText(iconChar, labelX, toolBadgeY);

      // Tool name text
      this.ctx.font = `500 ${toolFontSize}px Inter, sans-serif`;
      this.ctx.fillStyle = "rgba(255,255,255,0.9)";
      this.ctx.fillText(toolLabel, labelX + iconSize + padding, toolBadgeY);

      this.ctx.restore();
    }
  }

  /**
   * Start the cursor animation loop that lerps display positions toward targets.
   */
  _startCursorAnimLoop() {
    const lerpFactor = 0.25; // Smooth but responsive
    const snapThreshold = 0.5; // Snap when very close

    const animate = () => {
      let needsRender = false;

      for (const cursor of Object.values(this.remoteCursors)) {
        const dx = cursor.targetX - cursor.displayX;
        const dy = cursor.targetY - cursor.displayY;
        const dist = Math.sqrt(dx * dx + dy * dy);

        if (dist > snapThreshold) {
          cursor.displayX += dx * lerpFactor;
          cursor.displayY += dy * lerpFactor;
          needsRender = true;
        } else if (
          cursor.displayX !== cursor.targetX ||
          cursor.displayY !== cursor.targetY
        ) {
          // Snap to exact position
          cursor.displayX = cursor.targetX;
          cursor.displayY = cursor.targetY;
          needsRender = true;
        }
      }

      if (needsRender) {
        this.render();
      }

      this._cursorAnimFrame = requestAnimationFrame(animate);
    };

    this._cursorAnimFrame = requestAnimationFrame(animate);
  }
}

document.addEventListener("DOMContentLoaded", () => {
  if (window.boardId)
    window.whiteboard = new Whiteboard("whiteboardCanvas", window.boardId);
});

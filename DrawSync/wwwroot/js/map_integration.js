/**
 * map_integration.js - Leaflet GIS Map Integration for DrawSync
 */

class MapBoard {
    constructor(containerId, boardId) {
        this.containerId = containerId;
        this.boardId = boardId;
        this.map = null;
        this.currentTool = 'pan';
        
        // Data storage
        this.features = {
            markers: [],
            lines: [],
            arrows: [],
            polygons: []
        };

        // Undo stack
        this.history = [];

        // Active drawing state
        this.activeDrawing = {
            points: [],
            tempLayer: null
        };

        this.init();
    }

    async init() {
        const board = await window.DrawStorage.getBoard(this.boardId);
        if (board) {
            document.getElementById("displayBoardName").textContent = board.name;
        }

        // Initialize Leaflet Map
        this.map = L.map(this.containerId).setView([23.8103, 90.4125], 13);

        // Add OSM Tiles
        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            maxZoom: 19,
            attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
        }).addTo(this.map);

        this.setupMapControls();
        await this.loadMapData();
        
        window.addEventListener('resize', () => this.map.invalidateSize());

        // Link Undo button in Board.cshtml
        const undoBtn = document.getElementById("undoBtn");
        if (undoBtn) {
            undoBtn.classList.remove('whiteboard-only'); // Enable for map
            undoBtn.onclick = () => this.undo();
        }
    }

    setupMapControls() {
        // Tool buttons
        document.querySelectorAll('.map-tool-btn').forEach(btn => {
            btn.onclick = () => {
                document.querySelectorAll('.map-tool-btn').forEach(b => b.classList.remove('active'));
                btn.classList.add('active');
                this.setTool(btn.dataset.mapTool);
            };
        });

        this.map.on('click', (e) => this.handleMapClick(e));
        this.map.on('mousemove', (e) => this.handleMouseMove(e));
        
        window.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') this.cancelDrawing();
            if ((e.ctrlKey || e.metaKey) && e.key === 'z') {
                e.preventDefault();
                this.undo();
            }
        });
    }

    setTool(tool) {
        this.cancelDrawing();
        this.currentTool = tool;
        if (tool === 'pan') {
            this.map.dragging.enable();
            document.getElementById(this.containerId).style.cursor = '';
        } else {
            this.map.dragging.disable();
            document.getElementById(this.containerId).style.cursor = 'crosshair';
        }
    }

    handleMapClick(e) {
        const latlng = e.latlng;

        if (this.currentTool === 'marker') {
            const name = prompt("Enter marker name:");
            if (name) {
                this.addMarker(latlng.lat, latlng.lng, name);
                this.saveMapData();
            }
        } 
        else if (this.currentTool === 'line' || this.currentTool === 'arrow') {
            if (this.activeDrawing.points.length === 0) {
                this.activeDrawing.points.push([latlng.lat, latlng.lng]);
            } else {
                const start = this.activeDrawing.points[0];
                const end = [latlng.lat, latlng.lng];
                if (this.currentTool === 'line') this.addLine(start, end);
                else this.addArrow(start, end);
                
                this.cancelDrawing(); // Resets points but keeps tool active
                this.saveMapData();
            }
        }
        else if (this.currentTool === 'polygon') {
            const lastPoint = this.activeDrawing.points[this.activeDrawing.points.length - 1];
            
            if (lastPoint && this.map.distance(latlng, L.latLng(lastPoint)) < 15) {
                if (this.activeDrawing.points.length > 2) {
                    this.addPolygon(this.activeDrawing.points);
                    this.saveMapData();
                }
                this.cancelDrawing();
            } else {
                this.activeDrawing.points.push([latlng.lat, latlng.lng]);
                this.updatePreview();
            }
        }
    }

    handleMouseMove(e) {
        if (this.activeDrawing.points.length > 0) {
            this.updatePreview(e.latlng);
        }
    }

    updatePreview(currentLatLng) {
        if (this.activeDrawing.tempLayer) {
            this.map.removeLayer(this.activeDrawing.tempLayer);
        }

        const pts = [...this.activeDrawing.points];
        if (currentLatLng) pts.push([currentLatLng.lat, currentLatLng.lng]);

        if (pts.length < 2) return;

        if (this.currentTool === 'line' || this.currentTool === 'arrow') {
            this.activeDrawing.tempLayer = L.polyline(pts, { color: '#3b82f6', dashArray: '5, 10', weight: 3 }).addTo(this.map);
        } else if (this.currentTool === 'polygon') {
            this.activeDrawing.tempLayer = L.polygon(pts, { color: '#3b82f6', dashArray: '5, 10', fillOpacity: 0.2 }).addTo(this.map);
        }
    }

    cancelDrawing() {
        if (this.activeDrawing.tempLayer) {
            this.map.removeLayer(this.activeDrawing.tempLayer);
        }
        this.activeDrawing.points = [];
        this.activeDrawing.tempLayer = null;
    }

    addToHistory(type, data, layer) {
        this.history.push({ type, data, layer });
        this.features[type].push(data);
    }

    undo() {
        if (this.history.length === 0) return;
        const last = this.history.pop();
        
        // Remove from map
        if (Array.isArray(last.layer)) {
            last.layer.forEach(l => this.map.removeLayer(l));
        } else {
            this.map.removeLayer(last.layer);
        }

        // Remove from data structure
        const list = this.features[last.type];
        const index = list.indexOf(last.data);
        if (index > -1) list.splice(index, 1);

        this.saveMapData();
    }

    addMarker(lat, lng, title) {
        const marker = L.marker([lat, lng]).addTo(this.map);
        marker.bindPopup(`<b>${title}</b>`);
        const data = { lat, lng, title };
        this.addToHistory('markers', data, marker);
    }

    addLine(start, end) {
        const line = L.polyline([start, end], { color: '#2563eb', weight: 4 }).addTo(this.map);
        const data = { start, end };
        this.addToHistory('lines', data, line);
    }

    addArrow(start, end) {
        const color = '#dc2626';
        const line = L.polyline([start, end], { color: color, weight: 4 }).addTo(this.map);
        
        // Improved Arrowhead math
        const headLengthPx = 20;
        const zoom = this.map.getZoom();
        
        // Calculate coordinate scale based on zoom
        // Roughly: meters per pixel = 156543.03392 * Math.cos(lat * Math.PI / 180) / Math.pow(2, zoom)
        const latScale = 1 / Math.pow(2, zoom - 1); // Approximation for lat/lng degree scale
        const headLen = headLengthPx * latScale * 0.0001; 

        const angle = Math.atan2(end[0] - start[0], end[1] - start[1]);
        const arrowAngle = Math.PI / 6;

        const h1 = [
            end[0] - headLen * Math.sin(angle - arrowAngle),
            end[1] - headLen * Math.cos(angle - arrowAngle)
        ];
        const h2 = [
            end[0] - headLen * Math.sin(angle + arrowAngle),
            end[1] - headLen * Math.cos(angle + arrowAngle)
        ];

        // Wait, my sin/cos logic is still a bit weird because of lat/lng being [y, x]
        // Let's use a simpler vector approach
        const dy = end[0] - start[0];
        const dx = end[1] - start[1];
        const len = Math.sqrt(dx*dx + dy*dy);
        const udx = dx / len;
        const udy = dy / len;
        
        const headSize = headLen * 1500; // Adjusted for degree scale
        
        const p1 = [
            end[0] - headSize * (udy * Math.cos(arrowAngle) - udx * Math.sin(arrowAngle)),
            end[1] - headSize * (udx * Math.cos(arrowAngle) + udy * Math.sin(arrowAngle))
        ];
        const p2 = [
            end[0] - headSize * (udy * Math.cos(-arrowAngle) - udx * Math.sin(-arrowAngle)),
            end[1] - headSize * (udx * Math.cos(-arrowAngle) + udy * Math.sin(-arrowAngle))
        ];

        const head1 = L.polyline([end, p1], { color: color, weight: 4 }).addTo(this.map);
        const head2 = L.polyline([end, p2], { color: color, weight: 4 }).addTo(this.map);
        
        const data = { start, end };
        this.addToHistory('arrows', data, [line, head1, head2]);
    }

    addPolygon(points) {
        const poly = L.polygon(points, { color: '#16a34a', fillColor: '#22c55e', fillOpacity: 0.3, weight: 3 }).addTo(this.map);
        const data = { points };
        this.addToHistory('polygons', data, poly);
    }

    async loadMapData() {
        const data = await window.DrawStorage.getElements(this.boardId);
        
        // Handle both object format (new) and ensure it's not a whiteboard array
        if (data && !Array.isArray(data)) {
            this.features = { 
                markers: data.markers || [], 
                lines: data.lines || [], 
                arrows: data.arrows || [], 
                polygons: data.polygons || [] 
            };
            this.history = []; // History isn't persisted for now, but features are
            
            this.features.markers.forEach(m => this.renderMarker(m));
            this.features.lines.forEach(l => this.renderLine(l));
            this.features.arrows.forEach(a => this.renderArrow(a));
            this.features.polygons.forEach(p => this.renderPolygon(p));
        }
    }

    // Helper methods that only draw, without adding to features/history (used during load)
    renderMarker(m) {
        const marker = L.marker([m.lat, m.lng]).addTo(this.map);
        marker.bindPopup(`<b>${m.title}</b>`);
        return marker;
    }
    renderLine(l) {
        return L.polyline([l.start, l.end], { color: '#2563eb', weight: 4 }).addTo(this.map);
    }
    renderArrow(a) {
        const color = '#dc2626';
        const line = L.polyline([a.start, a.end], { color: color, weight: 4 }).addTo(this.map);
        
        const zoom = this.map.getZoom();
        const latScale = 1 / Math.pow(2, zoom - 1);
        const headLen = 20 * latScale * 0.0001; 
        const arrowAngle = Math.PI / 6;

        const dy = a.end[0] - a.start[0];
        const dx = a.end[1] - a.start[1];
        const len = Math.sqrt(dx*dx + dy*dy);
        const udx = dx / len;
        const udy = dy / len;
        const headSize = headLen * 1500; 
        
        const p1 = [
            a.end[0] - headSize * (udy * Math.cos(arrowAngle) - udx * Math.sin(arrowAngle)),
            a.end[1] - headSize * (udx * Math.cos(arrowAngle) + udy * Math.sin(arrowAngle))
        ];
        const p2 = [
            a.end[0] - headSize * (udy * Math.cos(-arrowAngle) - udx * Math.sin(-arrowAngle)),
            a.end[1] - headSize * (udx * Math.cos(-arrowAngle) + udy * Math.sin(-arrowAngle))
        ];

        const h1 = L.polyline([a.end, p1], { color: color, weight: 4 }).addTo(this.map);
        const h2 = L.polyline([a.end, p2], { color: color, weight: 4 }).addTo(this.map);
        return [line, h1, h2];
    }
    renderPolygon(p) {
        return L.polygon(p.points, { color: '#16a34a', fillColor: '#22c55e', fillOpacity: 0.3, weight: 3 }).addTo(this.map);
    }

    async saveMapData() {
        await window.DrawStorage.saveElements(this.boardId, this.features);
        document.getElementById("saveStatus").textContent = "Saving...";
        setTimeout(() => document.getElementById("saveStatus").textContent = "Saved", 500);
    }
}

window.initMapBoard = (containerId, boardId) => {
    window.mapBoard = new MapBoard(containerId, boardId);
};

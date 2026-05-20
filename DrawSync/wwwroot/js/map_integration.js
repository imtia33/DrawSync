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
            polygons: [],
            paths: [] // For measurement ruler
        };

        // Undo stack
        this.history = [];

        // Active drawing state
        this.activeDrawing = {
            points: [],
            tempLayer: null,
            tempLabel: null // Dynamic measurement tooltip
        };

        // Edit Mode state
        this.editMode = {
            active: false,
            vertexMarkers: [] // Draggable handles
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
            undoBtn.classList.remove('whiteboard-only');
            undoBtn.onclick = () => this.undo();
        }
    }

    setupMapControls() {
        document.querySelectorAll('.map-tool-btn').forEach(btn => {
            btn.onclick = () => {
                document.querySelectorAll('.map-tool-btn').forEach(b => b.classList.remove('active'));
                btn.classList.add('active');
                this.setTool(btn.dataset.mapTool);
            };
        });

        this.map.on('click', (e) => this.handleMapClick(e));
        this.map.on('mousemove', (e) => this.handleMouseMove(e));
        // Right-click to finish paths or polygons
        this.map.on('contextmenu', (e) => {
            if (this.currentTool === 'polygon' && this.activeDrawing.points.length > 2) {
                this.addPolygon(this.activeDrawing.points);
                this.saveMapData();
                this.cancelDrawing();
            } else if (this.currentTool === 'path' && this.activeDrawing.points.length > 1) {
                this.addPath(this.activeDrawing.points);
                this.saveMapData();
                this.cancelDrawing();
            }
        });
        
        window.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') {
                this.cancelDrawing();
                const locModal = document.getElementById("locationModal");
                if (locModal) {
                    locModal.style.display = "none";
                    this.pendingMarkerLatLng = null;
                }
            }
            if ((e.ctrlKey || e.metaKey) && e.key === 'z') {
                e.preventDefault();
                this.undo();
            }
        });

        // Setup Custom Location Modal
        const locModal = document.getElementById("locationModal");
        const locInput = document.getElementById("locationNameInput");
        const locConfirm = document.getElementById("confirmLocation");
        const locCancel = document.getElementById("cancelLocation");

        if (locConfirm && locCancel) {
            locConfirm.onclick = () => {
                const name = locInput.value.trim();
                if (name && this.pendingMarkerLatLng) {
                    this.addMarker(this.pendingMarkerLatLng.lat, this.pendingMarkerLatLng.lng, name);
                    this.saveMapData();
                }
                if (locModal) locModal.style.display = "none";
                this.pendingMarkerLatLng = null;
            };

            locCancel.onclick = () => {
                if (locModal) locModal.style.display = "none";
                this.pendingMarkerLatLng = null;
            };

            locInput.onkeydown = (e) => {
                if (e.key === 'Enter') { e.preventDefault(); locConfirm.click(); }
                if (e.key === 'Escape') { e.preventDefault(); locCancel.click(); }
            };
        }
    }

    setTool(tool) {
        this.cancelDrawing();
        
        // Toggle Edit Mode
        if (tool === 'edit') {
            this.enableEditMode();
        } else if (this.currentTool === 'edit') {
            this.disableEditMode();
        }

        this.currentTool = tool;
        if (tool === 'pan' || tool === 'edit') {
            this.map.dragging.enable();
            document.getElementById(this.containerId).style.cursor = tool === 'edit' ? 'pointer' : '';
        } else {
            this.map.dragging.disable();
            document.getElementById(this.containerId).style.cursor = 'crosshair';
        }
    }

    handleMapClick(e) {
        if (this.currentTool === 'edit' || this.currentTool === 'pan') return;

        const latlng = e.latlng;

        if (this.currentTool === 'marker') {
            this.pendingMarkerLatLng = latlng;
            const modal = document.getElementById("locationModal");
            const input = document.getElementById("locationNameInput");
            if (modal && input) {
                input.value = "";
                modal.style.display = "flex";
                setTimeout(() => input.focus(), 50);
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
                
                this.cancelDrawing();
                this.saveMapData();
            }
        }
        else if (this.currentTool === 'polygon' || this.currentTool === 'path') {
            const lastPoint = this.activeDrawing.points[this.activeDrawing.points.length - 1];
            
            // If clicking near the last point, finish drawing (double click logic)
            if (lastPoint && this.map.distance(latlng, L.latLng(lastPoint)) < 15) {
                if (this.currentTool === 'polygon' && this.activeDrawing.points.length > 2) {
                    this.addPolygon(this.activeDrawing.points);
                    this.saveMapData();
                } else if (this.currentTool === 'path' && this.activeDrawing.points.length > 1) {
                    this.addPath(this.activeDrawing.points);
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

        let labelText = "";
        let labelPos = currentLatLng || L.latLng(pts[pts.length - 1]);

        if (this.currentTool === 'line' || this.currentTool === 'arrow' || this.currentTool === 'path') {
            this.activeDrawing.tempLayer = L.polyline(pts, { color: '#3b82f6', dashArray: '5, 10', weight: 3 }).addTo(this.map);
            const dist = window.MapGeometry.calculatePathLength(pts);
            labelText = window.MapGeometry.formatDistance(dist);
            
            if (this.currentTool === 'line' || this.currentTool === 'arrow') {
                labelPos = window.MapGeometry.getLineMidpoint(pts[0], pts[1]);
            } else {
                // For paths, show at cursor
                labelPos = currentLatLng || L.latLng(pts[pts.length - 1]);
            }
        } else if (this.currentTool === 'polygon') {
            this.activeDrawing.tempLayer = L.polygon(pts, { color: '#3b82f6', dashArray: '5, 10', fillOpacity: 0.2 }).addTo(this.map);
            if (pts.length > 2) {
                const area = window.MapGeometry.calculatePolygonArea(pts);
                labelText = window.MapGeometry.formatArea(area);
                labelPos = window.MapGeometry.getPolygonCenter(pts);
            }
        }

        if (labelText) {
            this.activeDrawing.tempLabel = window.MapLabels.updateDynamicTooltip(
                this.map, 
                this.activeDrawing.tempLabel, 
                labelPos, 
                labelText
            );
        }
    }

    cancelDrawing() {
        if (this.activeDrawing.tempLayer) {
            this.map.removeLayer(this.activeDrawing.tempLayer);
        }
        if (this.activeDrawing.tempLabel) {
            window.MapLabels.removeLabel(this.map, this.activeDrawing.tempLabel);
        }
        this.activeDrawing.points = [];
        this.activeDrawing.tempLayer = null;
        this.activeDrawing.tempLabel = null;
    }

    addToHistory(type, data, layers) {
        // Find existing to replace (for edit mode)
        const existingIdx = this.features[type].findIndex(d => d.id === data.id);
        if (existingIdx > -1) {
            this.features[type][existingIdx] = data;
        } else {
            this.features[type].push(data);
        }
        
        // Push full state to history for undo (simplified deep copy)
        this.history.push({
            type,
            data: JSON.parse(JSON.stringify(data)),
            layers: Array.isArray(layers) ? layers : [layers]
        });
    }

    undo() {
        if (this.history.length === 0) return;
        const last = this.history.pop();
        
        // Remove layers from map
        last.layers.forEach(l => this.map.removeLayer(l));

        // Remove from data structure
        const list = this.features[last.type];
        const index = list.findIndex(d => d.id === last.data.id);
        if (index > -1) {
            list.splice(index, 1);
        }

        // If edit mode active, refresh handlers
        if (this.currentTool === 'edit') {
            this.disableEditMode();
            this.enableEditMode();
        }

        this.saveMapData();
    }

    generateId() {
        return Math.random().toString(36).substring(2, 9);
    }

    addMarker(lat, lng, title, id = this.generateId()) {
        const marker = L.marker([lat, lng]).addTo(this.map);
        marker.bindPopup(`<b>${title}</b>`);
        const data = { id, lat, lng, title };
        
        // Attach raw data reference
        marker.drawData = { type: 'markers', data };

        this.addToHistory('markers', data, marker);
        return { marker, data };
    }

    addLine(start, end, id = this.generateId()) {
        const line = L.polyline([start, end], { color: '#2563eb', weight: 4 }).addTo(this.map);
        
        const dist = window.MapGeometry.calculateDistance(start, end);
        const mid = window.MapGeometry.getLineMidpoint(start, end);
        const label = window.MapLabels.createLabel(this.map, mid, window.MapGeometry.formatDistance(dist));

        const data = { id, start, end };
        line.drawData = { type: 'lines', data, label };

        this.addToHistory('lines', data, [line, label]);
        return { line, label, data };
    }

    addArrow(start, end, id = this.generateId()) {
        const color = '#dc2626';
        const line = L.polyline([start, end], { color: color, weight: 4 }).addTo(this.map);
        
        const zoom = this.map.getZoom();
        const latScale = 1 / Math.pow(2, zoom - 1);
        const headLen = 20 * latScale * 0.0001; 
        const arrowAngle = Math.PI / 6;

        const dy = end[0] - start[0];
        const dx = end[1] - start[1];
        const len = Math.sqrt(dx*dx + dy*dy);
        const udx = dx / len;
        const udy = dy / len;
        const headSize = headLen * 1500; 
        
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
        
        const dist = window.MapGeometry.calculateDistance(start, end);
        const mid = window.MapGeometry.getLineMidpoint(start, end);
        const label = window.MapLabels.createLabel(this.map, mid, window.MapGeometry.formatDistance(dist), 'arrow-label');

        const data = { id, start, end };
        
        // Attach logic to master line
        line.drawData = { type: 'arrows', data, subLayers: [head1, head2, label], label };

        this.addToHistory('arrows', data, [line, head1, head2, label]);
        return { line, data };
    }

    addPolygon(points, id = this.generateId()) {
        const poly = L.polygon(points, { color: '#16a34a', fillColor: '#22c55e', fillOpacity: 0.3, weight: 3 }).addTo(this.map);
        
        const area = window.MapGeometry.calculatePolygonArea(points);
        const center = window.MapGeometry.getPolygonCenter(points);
        const label = window.MapLabels.createLabel(this.map, center, window.MapGeometry.formatArea(area), 'polygon-label');

        const data = { id, points };
        poly.drawData = { type: 'polygons', data, label };

        this.addToHistory('polygons', data, [poly, label]);
        return { poly, label, data };
    }

    addPath(points, id = this.generateId()) {
        const path = L.polyline(points, { color: '#f59e0b', dashArray: '5, 10', weight: 4 }).addTo(this.map);
        
        const dist = window.MapGeometry.calculatePathLength(points);
        const endPoint = points[points.length - 1];
        const label = window.MapLabels.createLabel(this.map, endPoint, window.MapGeometry.formatDistance(dist), 'path-label');

        const data = { id, points };
        path.drawData = { type: 'paths', data, label };

        this.addToHistory('paths', data, [path, label]);
        return { path, label, data };
    }

    /* --- Edit Mode Implementation --- */
    
    enableEditMode() {
        this.editMode.active = true;
        this.editMode.vertexMarkers = [];
        this.map.eachLayer(layer => {
            if (layer.drawData) {
                this.bindEditHandles(layer);
            }
        });
    }

    disableEditMode() {
        this.editMode.active = false;
        // Clean up vertex handles
        this.editMode.vertexMarkers.forEach(m => this.map.removeLayer(m));
        this.editMode.vertexMarkers = [];
        
        // Remove draggable from markers
        this.map.eachLayer(layer => {
            if (layer instanceof L.Marker && layer.drawData && layer.drawData.type === 'markers') {
                layer.dragging.disable();
            }
        });
    }

    bindEditHandles(layer) {
        const type = layer.drawData.type;
        const data = layer.drawData.data;

        if (type === 'markers') {
            layer.dragging.enable();
            layer.on('dragend', (e) => {
                const pos = e.target.getLatLng();
                data.lat = pos.lat;
                data.lng = pos.lng;
                this.saveMapData();
            });
        } 
        else if (type === 'lines' || type === 'arrows') {
            this.createVertexMarker(layer, 0, data.start, data, type);
            this.createVertexMarker(layer, 1, data.end, data, type);
        }
        else if (type === 'polygons' || type === 'paths') {
            data.points.forEach((pt, index) => {
                this.createVertexMarker(layer, index, pt, data, type);
            });
        }
    }

    createVertexMarker(parentLayer, index, latlng, data, type) {
        const icon = L.divIcon({
            className: 'vertex-edit-handle',
            iconSize: [12, 12]
        });
        const marker = L.marker(latlng, { icon: icon, draggable: true, zIndexOffset: 2000 }).addTo(this.map);
        this.editMode.vertexMarkers.push(marker);

        marker.on('drag', (e) => {
            const pos = e.latlng;
            const newPt = [pos.lat, pos.lng];

            // Update data
            if (type === 'lines' || type === 'arrows') {
                if (index === 0) data.start = newPt;
                else data.end = newPt;
                parentLayer.setLatLngs([data.start, data.end]);
                
                // Recalculate label
                const dist = window.MapGeometry.calculateDistance(data.start, data.end);
                const mid = window.MapGeometry.getLineMidpoint(data.start, data.end);
                window.MapLabels.updateLabel(parentLayer.drawData.label, mid, window.MapGeometry.formatDistance(dist));

                // If arrow, redraw heads
                if (type === 'arrows') {
                    // Removing old heads and recreating arrow is complex during drag.
                    // For performance, we can just let it be a line during drag and recreate on dragend,
                    // or re-math it here. We'll rebuild the arrow heads smoothly.
                    this.updateArrowHeads(parentLayer, data.start, data.end);
                }

            } else if (type === 'polygons' || type === 'paths') {
                data.points[index] = newPt;
                parentLayer.setLatLngs(data.points);
                
                if (type === 'polygons') {
                    const area = window.MapGeometry.calculatePolygonArea(data.points);
                    const center = window.MapGeometry.getPolygonCenter(data.points);
                    window.MapLabels.updateLabel(parentLayer.drawData.label, center, window.MapGeometry.formatArea(area));
                } else {
                    const dist = window.MapGeometry.calculatePathLength(data.points);
                    const endPt = data.points[data.points.length - 1];
                    window.MapLabels.updateLabel(parentLayer.drawData.label, endPt, window.MapGeometry.formatDistance(dist));
                }
            }
        });

        marker.on('dragend', () => {
            this.saveMapData();
        });
    }

    updateArrowHeads(masterLine, start, end) {
        const subLayers = masterLine.drawData.subLayers; // [head1, head2, label]
        const head1 = subLayers[0], head2 = subLayers[1];
        
        const zoom = this.map.getZoom();
        const latScale = 1 / Math.pow(2, zoom - 1);
        const headLen = 20 * latScale * 0.0001; 
        const arrowAngle = Math.PI / 6;

        const dy = end[0] - start[0];
        const dx = end[1] - start[1];
        const len = Math.sqrt(dx*dx + dy*dy);
        if(len === 0) return;
        const udx = dx / len;
        const udy = dy / len;
        const headSize = headLen * 1500; 
        
        const p1 = [
            end[0] - headSize * (udy * Math.cos(arrowAngle) - udx * Math.sin(arrowAngle)),
            end[1] - headSize * (udx * Math.cos(arrowAngle) + udy * Math.sin(arrowAngle))
        ];
        const p2 = [
            end[0] - headSize * (udy * Math.cos(-arrowAngle) - udx * Math.sin(-arrowAngle)),
            end[1] - headSize * (udx * Math.cos(-arrowAngle) + udy * Math.sin(-arrowAngle))
        ];

        head1.setLatLngs([end, p1]);
        head2.setLatLngs([end, p2]);
    }

    /* --- Load / Save --- */

    async loadMapData() {
        const data = await window.DrawStorage.getElements(this.boardId);
        
        if (data && !Array.isArray(data)) {
            this.features = { 
                markers: data.markers || [], 
                lines: data.lines || [], 
                arrows: data.arrows || [], 
                polygons: data.polygons || [],
                paths: data.paths || []
            };
            this.history = [];
            
            // Reconstruct everything purely visual (don't addToHistory to avoid duplicates)
            this.features.markers.forEach(m => this.renderFeature('markers', m));
            this.features.lines.forEach(l => this.renderFeature('lines', l));
            this.features.arrows.forEach(a => this.renderFeature('arrows', a));
            this.features.polygons.forEach(p => this.renderFeature('polygons', p));
            this.features.paths.forEach(p => this.renderFeature('paths', p));
        }
    }

    renderFeature(type, data) {
        // Ensure IDs exist for older saves
        if(!data.id) data.id = this.generateId();

        if(type === 'markers') {
            const marker = L.marker([data.lat, data.lng]).addTo(this.map);
            marker.bindPopup(`<b>${data.title}</b>`);
            marker.drawData = { type, data };
        } 
        else if (type === 'lines') {
            const line = L.polyline([data.start, data.end], { color: '#2563eb', weight: 4 }).addTo(this.map);
            const dist = window.MapGeometry.calculateDistance(data.start, data.end);
            const mid = window.MapGeometry.getLineMidpoint(data.start, data.end);
            const label = window.MapLabels.createLabel(this.map, mid, window.MapGeometry.formatDistance(dist));
            line.drawData = { type, data, label };
        }
        else if (type === 'arrows') {
            const line = L.polyline([data.start, data.end], { color: '#dc2626', weight: 4 }).addTo(this.map);
            // Simulate creation for arrow heads
            const head1 = L.polyline([[0,0],[0,0]], { color: '#dc2626', weight: 4 }).addTo(this.map);
            const head2 = L.polyline([[0,0],[0,0]], { color: '#dc2626', weight: 4 }).addTo(this.map);
            const dist = window.MapGeometry.calculateDistance(data.start, data.end);
            const mid = window.MapGeometry.getLineMidpoint(data.start, data.end);
            const label = window.MapLabels.createLabel(this.map, mid, window.MapGeometry.formatDistance(dist), 'arrow-label');
            line.drawData = { type, data, subLayers: [head1, head2, label], label };
            this.updateArrowHeads(line, data.start, data.end);
        }
        else if (type === 'polygons') {
            const poly = L.polygon(data.points, { color: '#16a34a', fillColor: '#22c55e', fillOpacity: 0.3, weight: 3 }).addTo(this.map);
            const area = window.MapGeometry.calculatePolygonArea(data.points);
            const center = window.MapGeometry.getPolygonCenter(data.points);
            const label = window.MapLabels.createLabel(this.map, center, window.MapGeometry.formatArea(area), 'polygon-label');
            poly.drawData = { type, data, label };
        }
        else if (type === 'paths') {
            const path = L.polyline(data.points, { color: '#f59e0b', dashArray: '5, 10', weight: 4 }).addTo(this.map);
            const dist = window.MapGeometry.calculatePathLength(data.points);
            const label = window.MapLabels.createLabel(this.map, data.points[data.points.length-1], window.MapGeometry.formatDistance(dist), 'path-label');
            path.drawData = { type, data, label };
        }
    }

    async saveMapData() {
        await window.DrawStorage.saveElements(this.boardId, this.features);
        const status = document.getElementById("saveStatus");
        if(status) {
            status.textContent = "Saving...";
            setTimeout(() => status.textContent = "Saved", 500);
        }
    }
}

window.initMapBoard = (containerId, boardId) => {
    window.mapBoard = new MapBoard(containerId, boardId);
};

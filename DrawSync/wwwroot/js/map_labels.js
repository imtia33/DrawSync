/**
 * map_labels.js - Rendering engines for geospatial measurement labels
 */

const MapLabels = {
    /**
     * Creates a permanent measurement label at the given coordinate.
     */
    createLabel: function (map, latlng, text, extraClass = '') {
        const icon = L.divIcon({
            className: `map-measurement-label ${extraClass}`,
            html: `<span>${text}</span>`,
            iconSize: null, // Let CSS determine size
            iconAnchor: [0, 0] // Centered via CSS transform
        });
        
        return L.marker(latlng, {
            icon: icon,
            interactive: false,
            keyboard: false,
            zIndexOffset: 1000 // Ensure it's above shapes
        }).addTo(map);
    },

    /**
     * Updates an existing label's text and position.
     */
    updateLabel: function (labelMarker, latlng, text) {
        if (!labelMarker) return;
        labelMarker.setLatLng(latlng);
        
        // Update text efficiently
        const span = labelMarker.getElement()?.querySelector('span');
        if (span) {
            span.textContent = text;
        }
    },

    /**
     * Draws or updates a live dynamic tooltip (follows the cursor during drawing).
     */
    updateDynamicTooltip: function (map, tooltipMarker, latlng, text) {
        if (!tooltipMarker) {
            return this.createLabel(map, latlng, text, 'dynamic-tooltip');
        }
        this.updateLabel(tooltipMarker, latlng, text);
        return tooltipMarker;
    },
    
    /**
     * Removes a label from the map.
     */
    removeLabel: function(map, labelMarker) {
        if (labelMarker && map.hasLayer(labelMarker)) {
            map.removeLayer(labelMarker);
        }
    }
};

window.MapLabels = MapLabels;

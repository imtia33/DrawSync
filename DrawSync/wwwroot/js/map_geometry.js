/**
 * map_geometry.js - Geospatial calculations for DrawSync
 */

const MapGeometry = {
    EARTH_RADIUS: 6378137, // meters

    /**
     * Calculates the geodesic distance between two lat/lng points in meters.
     * Uses Leaflet's built-in CRS Earth distance.
     */
    calculateDistance: function (latlng1, latlng2) {
        return L.CRS.Earth.distance(L.latLng(latlng1), L.latLng(latlng2));
    },

    /**
     * Calculates the total distance of a polyline/path in meters.
     */
    calculatePathLength: function (points) {
        let total = 0;
        for (let i = 0; i < points.length - 1; i++) {
            total += this.calculateDistance(points[i], points[i + 1]);
        }
        return total;
    },

    /**
     * Calculates the geodesic area of a polygon in square meters.
     * Uses spherical geometry approximation.
     */
    calculatePolygonArea: function (points) {
        if (points.length < 3) return 0;
        
        let area = 0;
        const pts = points.map(p => L.latLng(p));
        
        // Ensure the polygon is closed for calculation
        if (!pts[0].equals(pts[pts.length - 1])) {
            pts.push(pts[0]);
        }

        for (let i = 0; i < pts.length - 1; i++) {
            const p1 = pts[i];
            const p2 = pts[i + 1];
            
            // Convert to radians
            const lng1 = p1.lng * Math.PI / 180;
            const lng2 = p2.lng * Math.PI / 180;
            const lat1 = p1.lat * Math.PI / 180;
            const lat2 = p2.lat * Math.PI / 180;

            area += (lng2 - lng1) * (Math.sin(lat1) + Math.sin(lat2));
        }

        area = (area * this.EARTH_RADIUS * this.EARTH_RADIUS) / 2;
        return Math.abs(area);
    },

    /**
     * Formats distance into a human-readable string (m or km)
     */
    formatDistance: function (meters) {
        if (meters < 1000) {
            return `${Math.round(meters)} m`;
        } else {
            return `${(meters / 1000).toFixed(2)} km`;
        }
    },

    /**
     * Formats area into a human-readable string (m², km², or hectares)
     */
    formatArea: function (sqMeters) {
        if (sqMeters < 10000) {
            return `${Math.round(sqMeters)} m²`;
        } else if (sqMeters < 1000000) {
            // Hectares (1 ha = 10,000 m²)
            return `${(sqMeters / 10000).toFixed(2)} ha`;
        } else {
            // Square kilometers (1 km² = 1,000,000 m²)
            return `${(sqMeters / 1000000).toFixed(2)} km²`;
        }
    },

    /**
     * Finds the geographic center of a polygon for placing labels.
     */
    getPolygonCenter: function (points) {
        const pts = points.map(p => L.latLng(p));
        let latSum = 0, lngSum = 0;
        pts.forEach(p => {
            latSum += p.lat;
            lngSum += p.lng;
        });
        return [latSum / pts.length, lngSum / pts.length];
    },

    /**
     * Finds the midpoint of a line segment for placing labels.
     */
    getLineMidpoint: function (p1, p2) {
        const lat1 = p1[0] || p1.lat, lng1 = p1[1] || p1.lng;
        const lat2 = p2[0] || p2.lat, lng2 = p2[1] || p2.lng;
        return [(lat1 + lat2) / 2, (lng1 + lng2) / 2];
    }
};

window.MapGeometry = MapGeometry;

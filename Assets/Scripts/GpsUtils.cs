using UnityEngine;

/// <summary>
/// Geodetic maths for the race route.
///
/// ENU = East / North / Up in metres, on a tangent plane touching the earth at the
/// route origin. Mapped to Unity axes as x = East, y = Up, z = North.
///
/// Important: ENU vectors are *geo-frame* coordinates, not Unity world coordinates.
/// Converting between the two is GeoAnchor's job. Never Instantiate straight into
/// world space with an ENU vector — Unity's world origin is wherever the phone
/// happened to be when AR tracking started, and its +Z is whatever direction it
/// happened to be facing. Parent geo content to GeoAnchor.Root instead.
/// </summary>
public static class GpsUtils
{
    private const double EarthRadiusM = 6371000.0;

    /// <summary>ENU offset in metres from the route origin, including altitude.</summary>
    public static Vector3 GpsToEnu(double lat, double lng, double alt,
                                   double originLat, double originLng, double originAlt)
    {
        double latRad = originLat * System.Math.PI / 180.0;

        // Metres per degree at this latitude, WGS-84 series expansion. The flat
        // 111320 constant is off by a few tenths of a percent, which matters here:
        // GeoAnchor fits rotation + translation with scale locked to 1, so a scale
        // error in the projection can't be absorbed by the fit and instead shows up
        // as tens of metres of position error at the far end of a long route.
        double mPerDegLat = 111132.92
                          - 559.82 * System.Math.Cos(2.0 * latRad)
                          + 1.175 * System.Math.Cos(4.0 * latRad);

        double mPerDegLng = 111412.84 * System.Math.Cos(latRad)
                          - 93.5 * System.Math.Cos(3.0 * latRad);

        float east = (float)((lng - originLng) * mPerDegLng);
        float north = (float)((lat - originLat) * mPerDegLat);
        float up = (float)(alt - originAlt);

        return new Vector3(east, up, north);
    }

    /// <summary>ENU offset with no vertical component, for routes with no surveyed altitudes.</summary>
    public static Vector3 GpsToEnu(double lat, double lng, double originLat, double originLng)
    {
        return GpsToEnu(lat, lng, 0d, originLat, originLng, 0d);
    }

    /// <summary>Great-circle ground distance in metres between two coordinates.</summary>
    public static float HaversineDistance(double lat1, double lng1, double lat2, double lng2)
    {
        double lat1Rad = lat1 * Mathf.Deg2Rad;
        double lat2Rad = lat2 * Mathf.Deg2Rad;
        double deltaLat = (lat2 - lat1) * Mathf.Deg2Rad;
        double deltaLng = (lng2 - lng1) * Mathf.Deg2Rad;

        double a = System.Math.Sin(deltaLat / 2) * System.Math.Sin(deltaLat / 2) +
                   System.Math.Cos(lat1Rad) * System.Math.Cos(lat2Rad) *
                   System.Math.Sin(deltaLng / 2) * System.Math.Sin(deltaLng / 2);

        double c = 2 * System.Math.Atan2(System.Math.Sqrt(a), System.Math.Sqrt(1 - a));

        return (float)(EarthRadiusM * c);
    }

    /// <summary>Compass bearing in degrees (0-360, 0 = true north) from point 1 to point 2.</summary>
    public static double Bearing(double lat1, double lng1, double lat2, double lng2)
    {
        double lat1Rad = lat1 * Mathf.Deg2Rad;
        double lat2Rad = lat2 * Mathf.Deg2Rad;
        double deltaLngRad = (lng2 - lng1) * Mathf.Deg2Rad;

        double y = System.Math.Sin(deltaLngRad) * System.Math.Cos(lat2Rad);
        double x = System.Math.Cos(lat1Rad) * System.Math.Sin(lat2Rad) -
                   System.Math.Sin(lat1Rad) * System.Math.Cos(lat2Rad) * System.Math.Cos(deltaLngRad);

        double bearingDeg = System.Math.Atan2(y, x) * Mathf.Rad2Deg;

        return (bearingDeg + 360.0) % 360.0;
    }
}

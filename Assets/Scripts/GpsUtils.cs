using UnityEngine;

public static class GpsUtils
{
    private const double metersPerDegLat = 111320.0;

    // Flat-earth tangent-plane ENU approximation.
    // Same origin must be passed in by every caller — never hardcode origin separately per script.
    public static Vector3 GpsToWorld(double lat, double lng, double originLat, double originLng)
    {
        double dLat = lat - originLat;
        double dLng = lng - originLng;

        float z = (float)(dLat * metersPerDegLat);
        float x = (float)(dLng * metersPerDegLat * System.Math.Cos(originLat * System.Math.PI / 180.0));

        return new Vector3(x, 0f, z);
    }
}
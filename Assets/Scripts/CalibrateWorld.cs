using UnityEngine;
using Unity.XR.CoreUtils;

/// <summary>
/// Owns world rotation calibration. Given a start and finish GPS coordinate,
/// rotates the XR Origin so the route direction lines up consistently
/// in world space, regardless of which way the player was facing when AR
/// tracking started. Fixed bearing approach (Option C) — no live compass,
/// no user action required.
/// </summary>
public class CalibrateWorld : MonoBehaviour
{
    public static CalibrateWorld Instance;

    [Header("AR References")]
    [SerializeField] private XROrigin xrOrigin;

    [Header("Debug")]
    [SerializeField] private bool logCalibration = true;

    public bool IsCalibrated { get; private set; } = false;

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// Rotates the XR Origin based on the bearing between two GPS
    /// coordinates (typically start line -> finish line).
    /// </summary>
    public void AlignWorldToRoute(double startLat, double startLng, double finishLat, double finishLng)
    {
        if (xrOrigin == null)
        {
            Debug.LogWarning("[CalibrateWorld] XROrigin not assigned — can't rotate world!");
            return;
        }

        double bearing = CalculateBearing(startLat, startLng, finishLat, finishLng);

        // Try positive bearing instead of negative — diagnostic test
        float rotationY = (float)bearing;

        xrOrigin.transform.rotation = Quaternion.Euler(0f, rotationY, 0f);
        IsCalibrated = true;

        if (logCalibration)
        {
            Debug.Log($"[CalibrateWorld] World aligned. Bearing start->finish = {bearing:F2}°, applied rotation Y = {rotationY:F2}°");
        }
    }

    /// <summary>
    /// Resets calibration state so AlignWorldToRoute can be triggered again
    /// (e.g. on game restart).
    /// </summary>
    public void ResetCalibration()
    {
        IsCalibrated = false;
    }

    /// <summary>
    /// Compass bearing (degrees, 0-360, 0 = North) from point 1 to point 2.
    /// </summary>
    private double CalculateBearing(double lat1, double lng1, double lat2, double lng2)
    {
        double lat1Rad = lat1 * Mathf.Deg2Rad;
        double lat2Rad = lat2 * Mathf.Deg2Rad;
        double deltaLngRad = (lng2 - lng1) * Mathf.Deg2Rad;

        double y = System.Math.Sin(deltaLngRad) * System.Math.Cos(lat2Rad);
        double x = System.Math.Cos(lat1Rad) * System.Math.Sin(lat2Rad) -
                   System.Math.Sin(lat1Rad) * System.Math.Cos(lat2Rad) * System.Math.Cos(deltaLngRad);

        double bearingRad = System.Math.Atan2(y, x);
        double bearingDeg = bearingRad * Mathf.Rad2Deg;

        // Normalize to 0-360
        return (bearingDeg + 360.0) % 360.0;
    }
}
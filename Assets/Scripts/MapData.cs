using UnityEngine;

[System.Serializable]
public class MapData
{
    public double originLat;
    public double originLng;

    /// <summary>
    /// Altitude of the start gate in metres above sea level. Survey this once and
    /// store it in Firebase — do NOT try to read it from the device at runtime.
    /// Phone GPS altitude is routinely 20m+ out, which on a slope puts checkpoints
    /// deep underground or floating in the sky.
    /// </summary>
    public double originAlt;

    public double finishLat;
    public double finishLng;
    public double finishAlt;

    public CheckpointData[] checkpoints;

    /// <summary>
    /// True if the route has real surveyed altitudes. JsonUtility fills missing
    /// fields with 0, so a route uploaded before altitudes existed reads as all
    /// zeroes — in that case we flatten everything to the start height rather than
    /// pretending sea level is the ground.
    /// </summary>
    public bool HasSurveyedAltitudes => originAlt != 0d || finishAlt != 0d;
}

[System.Serializable]
public class CheckpointData
{
    public double lat;
    public double lng;
    public double alt;
}

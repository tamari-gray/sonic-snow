using UnityEngine;

[System.Serializable]
public class MapData
{
    public double originLat;
    public double originLng;
    public double finishLat;
    public double finishLng;
    public CheckpointData[] checkpoints;
}

[System.Serializable]
public class CheckpointData
{
    public double lat;
    public double lng;
}

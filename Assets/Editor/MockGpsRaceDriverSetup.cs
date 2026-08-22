using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Drops MockGpsRaceDriver into the scene and turns it on. Same menu-command pattern as every
/// other *Setup.cs in this project (RGBCameraCaptureSetup, CalibrationScreenSetup, ...) — nothing
/// here is hand-placed, and re-running is idempotent.
///
/// This exists for on-device end-to-end testing on the XREAL Beam Pro, which has no GNSS (see
/// xreal-port memory) — without a mock fix the real race flow can never clear calibration's GPS
/// conditions at all. Run SetUpBatch to enable, run TearDownBatch to disable before a build meant
/// for hardware with real GPS (a phone).
/// </summary>
public static class MockGpsRaceDriverSetup
{
    private const string RootName = "MockGpsRaceDriver";
    private const string ScenePath = "Assets/Scenes/Game.unity";

    public static void SetUpBatch()
    {
        EditorSceneManager.OpenScene(ScenePath);
        SetUp();
        bool saved = EditorSceneManager.SaveOpenScenes();
        Debug.Log("[MockGpsRaceDriverSetup] Applied to " + ScenePath + ". Saved: " + saved);
    }

    public static void TearDownBatch()
    {
        EditorSceneManager.OpenScene(ScenePath);
        TearDown();
        bool saved = EditorSceneManager.SaveOpenScenes();
        Debug.Log("[MockGpsRaceDriverSetup] Disabled in " + ScenePath + ". Saved: " + saved);
    }

    [MenuItem("Sonic Snow/XREAL/Enable Mock GPS Race Driver (Beam Pro)")]
    public static void SetUp()
    {
        GameObject root = GameObject.Find(RootName);
        if (root == null) root = new GameObject(RootName);

        MockGpsRaceDriver driver = root.GetComponent<MockGpsRaceDriver>();
        if (driver == null) driver = root.AddComponent<MockGpsRaceDriver>();

        SerializedObject serialized = new SerializedObject(driver);
        serialized.FindProperty("driveWithMockGps").boolValue = true;
        serialized.FindProperty("secondsBetweenSteps").floatValue = 4f;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(root);

        Debug.Log("[MockGpsRaceDriverSetup] MockGpsRaceDriver enabled — this build will fake GPS " +
                  "fixes for calibration, checkpoints and the finish line. Do not ship this to a " +
                  "device with real GPS without disabling it first.");
    }

    [MenuItem("Sonic Snow/XREAL/Disable Mock GPS Race Driver")]
    public static void TearDown()
    {
        GameObject root = GameObject.Find(RootName);
        if (root == null) return;

        MockGpsRaceDriver driver = root.GetComponent<MockGpsRaceDriver>();
        if (driver == null) return;

        SerializedObject serialized = new SerializedObject(driver);
        serialized.FindProperty("driveWithMockGps").boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(root);

        Debug.Log("[MockGpsRaceDriverSetup] MockGpsRaceDriver disabled.");
    }
}

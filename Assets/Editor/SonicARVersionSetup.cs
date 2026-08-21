using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using Debug = UnityEngine.Debug;

/// <summary>
/// Builds the "test" branch's five bisection scenes for the XREAL RGB-capture stutter
/// investigation (see the xreal-first-person-capture memory). xreal-hello-world -- no AR
/// Foundation/ARCore/XR Simulation, Built-in RP originally, a near-empty scene -- records Blend-mode
/// RGB capture cleanly; Sonic Snow's Game.unity captures a genuinely new frame roughly once every
/// 2-6 seconds instead of ~30fps, and URP was already ruled out (clearing it made no difference).
/// Rather than keep subtracting from the broken project, this adds Sonic Snow's differences back
/// onto a HelloMR-derived baseline one layer at a time:
///
///   V1 - bare XR rig (from HelloMR's sample category, same XREALXRLoader setup Game.unity itself
///        uses) + the checkpoint/finish-line prefabs dropped in as static geometry. No gameplay
///        code, no UI, no AR Foundation session usage beyond what the XREAL loader itself needs.
///   V2 - + CalibrationScreen (world-space TMP canvas, the five-condition gate, ARSession.state
///        tracking check -- this is where AR Foundation actually gets exercised) + RaceTimer HUD +
///        CountdownTimer.
///   V3 - + GPS/AR alignment: LocationHandler, GeoAnchor (Kabsch fit against ARSession pose),
///        MapDataFetcher/MapData (route config fetch). The full geo-alignment subsystem running its
///        per-frame Update loop, even though the Beam Pro has no GNSS to actually feed it.
///   V4 - + full gameplay: GameLogic, CheckpointDomeSpawner, FinishLinePillar, particle/retro
///        effects, scoring UI, username-skip flow. Everything Game.unity has except the dormant
///        FirstPersonStreammingCast comparison path and a few peripheral diagnostic scripts.
///   V5 - Game.unity itself, unmodified -- the current, full app.
///
/// V1-V4 all drive capture with AutoRecordAndQuit (fixed settle -> start -> 10s -> stop -> quit)
/// instead of the production CalibrationScreen/GameLogic trigger, so every version's recording
/// window is identical and the only thing that changes between builds is what's added to the scene.
/// V5 uses the real flow already wired into Game.unity (recordingStopAtRaceSeconds caps it at the
/// same 10s).
/// </summary>
public static class SonicARVersionSetup
{
    private const string GamePath = "Assets/Scenes/Game.unity";
    private const string V1Path = "Assets/Scenes/SonicAR_V1.unity";
    private const string V2Path = "Assets/Scenes/SonicAR_V2.unity";
    private const string V3Path = "Assets/Scenes/SonicAR_V3.unity";
    private const string V4Path = "Assets/Scenes/SonicAR_V4.unity";

    private const string CheckpointPrefabPath = "Assets/Prefabs/CheckpointDomeRoot.prefab";
    private const string FinishPrefabPath = "Assets/Prefabs/FinishLine.prefab";

    // Root objects every version keeps when built from a Game.unity copy -- the XR
    // rig/infrastructure, not any Sonic Snow gameplay/UI content.
    private static readonly string[] RigRootNames =
    {
        "AR Session", "XR Origin (AR Rig)", "Main Camera", "EventSystem"
    };

    [MenuItem("Sonic Snow/XREAL Test Versions/Build V1 (HelloMR baseline)")]
    public static void BuildV1()
    {
        CopySceneFile(GamePath, V1Path);
        EditorSceneManager.OpenScene(V1Path);

        StripToRigOnly();
        AddStaticCheckpointAndFinish();
        RGBCameraCaptureSetup.SetUp();
        AddAutoRecordAndQuit();

        Save(V1Path);
        Debug.Log("[SonicARVersionSetup] V1 built: bare XR rig + static checkpoint/finish + RGB capture.");
    }

    /// <summary>
    /// Ablation test, added after V1 (with a real ARSession component -- see AR Session in
    /// RigRootNames) reproduced the exact same ~0-1 renders/s stutter as full Sonic Snow despite
    /// having none of CalibrationScreen/GPS/GameLogic. Game.unity's "AR Session" GameObject carries
    /// real UnityEngine.XR.ARFoundation.ARSession + ARInputManager components (confirmed by script
    /// GUID against the AR Foundation package source); xreal-hello-world's HelloMR.unity has no such
    /// object at all, despite having the same AR Foundation package installed -- so package presence
    /// alone isn't the differentiator, an ACTIVE ARSession component is the leading suspect. This
    /// builds V1 again with that one GameObject removed and nothing else changed, to test directly
    /// whether ARSession is what's contending with XREAL's own RGB-camera acquire path.
    /// </summary>
    [MenuItem("Sonic Snow/XREAL Test Versions/Build V1-NoARSession (ablation)")]
    public static void BuildV1NoARSession()
    {
        const string path = "Assets/Scenes/SonicAR_V1_NoARSession.unity";
        CopySceneFile(V1Path, path);
        Scene scene = EditorSceneManager.OpenScene(path);

        // NOT DestroyImmediate: verified after the fact that DestroyImmediate right after OpenScene in
        // batch mode does not reliably persist through SaveOpenScenes -- the saved file still had the
        // GameObject and its components intact despite "removed" logging and Saved: True, silently
        // invalidating this ablation the first time it ran. SetActive(false) is a plain serialized
        // field write, which does persist correctly, and MarkSceneDirty + a verify-read-back below
        // guards against a repeat of the same silent-failure class.
        GameObject arSession = GameObject.Find("AR Session");
        if (arSession != null)
        {
            arSession.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);
        }
        else
        {
            Debug.LogWarning("[SonicARVersionSetup] No 'AR Session' GameObject found to remove.");
        }

        bool saved = EditorSceneManager.SaveOpenScenes();
        Debug.Log("[SonicARVersionSetup] V1-NoARSession built (AR Session GameObject disabled). Saved: " + saved);

        VerifyGameObjectInactive(path, "AR Session");
    }

    public static void BuildV1NoARSessionAndApkBatch()
    {
        BuildV1NoARSession();
        BuildApk("Assets/Scenes/SonicAR_V1_NoARSession.unity", "Builds/Android/SonicAR_V1_NoARSession.apk");
    }

    /// <summary>
    /// Second ablation. V1-NoARSession still stuttered at ~0-1 renders/s -- removing the separate
    /// "AR Session" GameObject wasn't enough, because Main Camera itself (kept in every version, see
    /// RigRootNames) carries its OWN AR Foundation camera-frame consumers, found by resolving each of
    /// its MonoBehaviour script GUIDs against the AR Foundation package source:
    /// ARCameraManager (subscribes to frameReceived for the camera texture/CPU image),
    /// ARCameraBackground (renders the camera feed as background -- needs a fresh frame every draw),
    /// and AROcclusionManager (pulls environment-depth/human-segmentation images from the camera
    /// subsystem every frame -- the heaviest and least justified of the three on optical-see-through
    /// glasses that don't do video passthrough). This builds V1 again with all three removed from
    /// Main Camera and nothing else changed.
    /// </summary>
    [MenuItem("Sonic Snow/XREAL Test Versions/Build V1-NoARCamera (ablation 2)")]
    public static void BuildV1NoARCamera()
    {
        const string path = "Assets/Scenes/SonicAR_V1_NoARCamera.unity";
        CopySceneFile(V1Path, path);
        Scene scene = EditorSceneManager.OpenScene(path);

        GameObject mainCameraGo = Camera.main != null ? Camera.main.gameObject : GameObject.Find("Main Camera");
        if (mainCameraGo == null)
        {
            Debug.LogError("[SonicARVersionSetup] No Main Camera found -- cannot run this ablation.");
            return;
        }

        // .enabled = false, NOT DestroyImmediate: verified after the fact that DestroyImmediate right
        // after OpenScene in batch mode does not reliably persist through SaveOpenScenes (confirmed by
        // reading the saved SonicAR_V1_NoARCamera.unity back -- all three components were still
        // present and m_Enabled: 1 despite "removed=3" logging and Saved: True). A plain field write
        // does persist; VerifyComponentDisabled below reads the saved file back to confirm before this
        // scene is trusted for a build.
        int disabled = 0;
        disabled += DisableComponent<AROcclusionManager>(mainCameraGo);
        disabled += DisableComponent<ARCameraBackground>(mainCameraGo);
        disabled += DisableComponent<ARCameraManager>(mainCameraGo);
        EditorSceneManager.MarkSceneDirty(scene);

        bool saved = EditorSceneManager.SaveOpenScenes();
        Debug.Log("[SonicARVersionSetup] V1-NoARCamera built (" + disabled + " AR Foundation camera " +
                  "component(s) disabled on Main Camera: AROcclusionManager, ARCameraBackground, " +
                  "ARCameraManager). Saved: " + saved);

        VerifyComponentDisabled(path, "b15f82cc229284894964d2d30806969d", "AROcclusionManager");
        VerifyComponentDisabled(path, "816b289ef451e094f9ae174fb4cf8db0", "ARCameraBackground");
        VerifyComponentDisabled(path, "4966719baa26e4b0e8231a24d9bd491a", "ARCameraManager");
    }

    public static void BuildV1NoARCameraAndApkBatch()
    {
        BuildV1NoARCamera();
        BuildApk("Assets/Scenes/SonicAR_V1_NoARCamera.unity", "Builds/Android/SonicAR_V1_NoARCamera.apk");
    }

    /// <summary>
    /// Third ablation attempt: disabling AROcclusionManager + ARCameraBackground + ARCameraManager
    /// together (ablation 2) hung the whole app on launch instead of giving a clean answer -- no
    /// recording, no crash, just silence, killed by Android's watchdog after ~90s. This isolates just
    /// ARCameraManager, the one directly implicated by V5's own logcat (its
    /// SubsystemLifecycleManager.OnEnable() is what calls Unity.XR.XREAL.XREALCameraProvider.Start(),
    /// which creates the native RGB camera resource RGBCameraCapture's recording path also depends
    /// on) -- leaving ARCameraBackground and AROcclusionManager alone to avoid whatever combination
    /// caused the hang.
    /// </summary>
    [MenuItem("Sonic Snow/XREAL Test Versions/Build V1-NoARCameraManagerOnly (ablation 2b)")]
    public static void BuildV1NoARCameraManagerOnly()
    {
        const string path = "Assets/Scenes/SonicAR_V1_NoARCameraManagerOnly.unity";
        CopySceneFile(V1Path, path);
        Scene scene = EditorSceneManager.OpenScene(path);

        GameObject mainCameraGo = Camera.main != null ? Camera.main.gameObject : GameObject.Find("Main Camera");
        if (mainCameraGo == null)
        {
            Debug.LogError("[SonicARVersionSetup] No Main Camera found -- cannot run this ablation.");
            return;
        }

        int disabled = DisableComponent<ARCameraManager>(mainCameraGo);
        EditorSceneManager.MarkSceneDirty(scene);

        bool saved = EditorSceneManager.SaveOpenScenes();
        Debug.Log("[SonicARVersionSetup] V1-NoARCameraManagerOnly built (" + disabled + " -- just " +
                  "ARCameraManager disabled on Main Camera). Saved: " + saved);

        VerifyComponentDisabled(path, "4966719baa26e4b0e8231a24d9bd491a", "ARCameraManager");
    }

    public static void BuildV1NoARCameraManagerOnlyAndApkBatch()
    {
        BuildV1NoARCameraManagerOnly();
        BuildApk("Assets/Scenes/SonicAR_V1_NoARCameraManagerOnly.unity", "Builds/Android/SonicAR_V1_NoARCameraManagerOnly.apk");
    }

    /// <summary>Rebuilds both scene-based ablations (corrected to use enabled/active toggling with
    /// verified persistence, after DestroyImmediate silently failed to survive SaveOpenScenes in batch
    /// mode) in one Unity process.</summary>
    public static void BuildAblations1And2Batch()
    {
        BuildV1NoARSessionAndApkBatch();
        BuildV1NoARCameraAndApkBatch();
    }

    /// <summary>
    /// Third ablation, a Player Settings test rather than a scene test. Both prior ablations
    /// (removing AR Session, then removing ARCameraManager/ARCameraBackground/AROcclusionManager)
    /// left V1 just as broken (~0-1 renders/s), so a component in the SCENE isn't the differentiator
    /// after all -- and a full ProjectSettings.asset diff against xreal-hello-world found the project
    /// is otherwise close, with `gcIncremental: 0` (off) here vs `1` (on) there as the one plausible
    /// runtime-behaviour difference (min SDK, display-buffer bit depth, Swappy, scripting define
    /// symbols were also checked and are unrelated to camera/threading). Reuses V1.unity completely
    /// unchanged; only PlayerSettings.gcIncremental flips for this one build.
    /// </summary>
    public static void BuildV1IncrementalGCAndApkBatch()
    {
        bool before = PlayerSettings.gcIncremental;
        PlayerSettings.gcIncremental = true;
        AssetDatabase.SaveAssets();
        Debug.Log("[SonicARVersionSetup] gcIncremental: " + before + " -> " + PlayerSettings.gcIncremental);

        BuildApk(V1Path, "Builds/Android/SonicAR_V1_IncrementalGC.apk");

        PlayerSettings.gcIncremental = before;
        AssetDatabase.SaveAssets();
        Debug.Log("[SonicARVersionSetup] gcIncremental reverted to " + PlayerSettings.gcIncremental);
    }

    /// <summary>
    /// V5: Game.unity, completely unmodified -- the current, full app. Builds only, no scene edits
    /// (unlike XREALSonicSnowBuild.BuildAndRun, this does not install/launch -- the PC-side
    /// orchestration script drives that, same as every other version, for consistent timing/logcat
    /// capture control).
    /// </summary>
    [MenuItem("Sonic Snow/XREAL Test Versions/Build V5 (Game.unity, unmodified)")]
    public static void BuildV5Apk() => BuildApk(GamePath, "Builds/Android/SonicAR_V5.apk");

    private static int DisableComponent<T>(GameObject go) where T : Behaviour
    {
        T component = go.GetComponent<T>();
        if (component == null) return 0;
        component.enabled = false;
        return 1;
    }

    /// <summary>Reads path back off disk and fails loudly if the named GameObject's m_IsActive isn't
    /// 0 -- guards against a repeat of the DestroyImmediate-doesn't-persist silent failure.</summary>
    private static void VerifyGameObjectInactive(string path, string objectName)
    {
        string text = File.ReadAllText(path);
        int nameIdx = text.IndexOf("m_Name: " + objectName, System.StringComparison.Ordinal);
        if (nameIdx < 0)
        {
            Debug.LogError("[SonicARVersionSetup] VERIFY FAILED: '" + objectName + "' not found at all in " + path);
            return;
        }

        int activeIdx = text.IndexOf("m_IsActive: ", nameIdx, System.StringComparison.Ordinal);
        bool isInactive = activeIdx >= 0 && text.Substring(activeIdx, 13) == "m_IsActive: 0";

        if (isInactive)
            Debug.Log("[SonicARVersionSetup] VERIFIED: '" + objectName + "' is m_IsActive: 0 in the saved file.");
        else
            Debug.LogError("[SonicARVersionSetup] VERIFY FAILED: '" + objectName + "' is NOT inactive in " +
                            path + " -- this ablation did not actually take effect. Do not trust this build.");
    }

    /// <summary>Same as VerifyGameObjectInactive but for a component identified by script GUID (a
    /// GameObject can carry several MonoBehaviours, so name-based lookup isn't unique enough).</summary>
    private static void VerifyComponentDisabled(string path, string scriptGuid, string componentLabel)
    {
        string text = File.ReadAllText(path);
        string needle = "guid: " + scriptGuid;
        int guidIdx = text.IndexOf(needle, System.StringComparison.Ordinal);
        if (guidIdx < 0)
        {
            Debug.LogError("[SonicARVersionSetup] VERIFY FAILED: " + componentLabel + " (guid " + scriptGuid +
                            ") not found at all in " + path);
            return;
        }

        // m_Enabled is written BEFORE m_Script in Unity's MonoBehaviour YAML block, so look backwards.
        int enabledIdx = text.LastIndexOf("m_Enabled: ", guidIdx, System.StringComparison.Ordinal);
        bool isDisabled = enabledIdx >= 0 && text.Substring(enabledIdx, 12) == "m_Enabled: 0";

        if (isDisabled)
            Debug.Log("[SonicARVersionSetup] VERIFIED: " + componentLabel + " is m_Enabled: 0 in the saved file.");
        else
            Debug.LogError("[SonicARVersionSetup] VERIFY FAILED: " + componentLabel + " is NOT disabled in " +
                            path + " -- this ablation did not actually take effect. Do not trust this build.");
    }

    /// <summary>Batch-mode entry point -- see XREALHelloWorldSetup/RGBCameraCaptureSetup for the pattern.</summary>
    public static void BuildV1Batch()
    {
        BuildV1();
    }

    [MenuItem("Sonic Snow/XREAL Test Versions/Build V1 APK")]
    public static void BuildV1Apk() => BuildApk(V1Path, "Builds/Android/SonicAR_V1.apk");

    /// <summary>Batch-mode entry point: assembles the V1 scene AND builds its APK in one Unity
    /// process, so the ~20-25 minute shader-variant warm-up (see xreal-unity-sdk-integration memory)
    /// is only paid once per session rather than once per version.</summary>
    public static void BuildV1AndApkBatch()
    {
        BuildV1();
        BuildApk(V1Path, "Builds/Android/SonicAR_V1.apk");
    }

    /// <summary>
    /// Builds an APK from a single scene, matching XREALSonicSnowBuild's player-settings-agnostic
    /// approach (this project's Player Settings -- IL2CPP, ARM64, OpenGLES3, URP -- are already
    /// correct for every version, since they're project-level, not per-scene). Does NOT install or
    /// launch; the PC-side orchestration script drives adb separately so it has full control over
    /// timing/polling/logcat capture around each run.
    /// </summary>
    private static void BuildApk(string scenePath, string apkPath)
    {
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { scenePath },
            locationPathName = apkPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None
        });

        BuildSummary summary = report.summary;
        Debug.Log("[SonicARVersionSetup] Build " + scenePath + " -> " + apkPath + ": result=" +
                  summary.result + ", errors=" + summary.totalErrors + ", warnings=" +
                  summary.totalWarnings + ", size=" + summary.totalSize + " bytes");

        if (summary.result != BuildResult.Succeeded)
        {
            foreach (BuildStep step in report.steps)
                foreach (BuildStepMessage msg in step.messages)
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        Debug.LogError("[" + step.name + "] " + msg.content);
        }
    }

    [MenuItem("Sonic Snow/XREAL Test Versions/Build V2 (+ CalibrationScreen)")]
    public static void BuildV2()
    {
        CopySceneFile(V1Path, V2Path);
        EditorSceneManager.OpenScene(V2Path);

        AddBareCanvas();
        CalibrationScreenSetup.SetUp();
        RGBCameraCaptureSetup.SetUp();
        AddAutoRecordAndQuit();

        Save(V2Path);
        Debug.Log("[SonicARVersionSetup] V2 built: V1 + CalibrationScreen (world-space canvas, " +
                  "ARSession.state tracking check, GPS check).");
    }

    public static void BuildV2Apk() => BuildApk(V2Path, "Builds/Android/SonicAR_V2.apk");

    public static void BuildV2AndApkBatch()
    {
        BuildV2();
        BuildApk(V2Path, "Builds/Android/SonicAR_V2.apk");
    }

    [MenuItem("Sonic Snow/XREAL Test Versions/Build V3 (+ GPS/AR alignment)")]
    public static void BuildV3()
    {
        CopySceneFile(V2Path, V3Path);
        EditorSceneManager.OpenScene(V3Path);

        AddComponentByName<LocationHandler>("LocationHandler");
        AddComponentByName<GeoAnchor>("GeoAnchor");
        RGBCameraCaptureSetup.SetUp();
        AddAutoRecordAndQuit();

        Save(V3Path);
        Debug.Log("[SonicARVersionSetup] V3 built: V2 + LocationHandler + GeoAnchor (GPS polling + " +
                  "per-frame AR-pose alignment fit).");
    }

    public static void BuildV3Apk() => BuildApk(V3Path, "Builds/Android/SonicAR_V3.apk");

    public static void BuildV3AndApkBatch()
    {
        BuildV3();
        BuildApk(V3Path, "Builds/Android/SonicAR_V3.apk");
    }

    [MenuItem("Sonic Snow/XREAL Test Versions/Build V4 (+ full gameplay)")]
    public static void BuildV4()
    {
        CopySceneFile(V3Path, V4Path);
        EditorSceneManager.OpenScene(V4Path);

        AddComponentByName<MapDataFetcher>("MapDataFetcher");
        AddComponentByName<GameLogic>("GameLogic");
        AddSpawnerWithPrefab<CheckpointDomeSpawner>("CheckpointDomeSpawner", "checkpointDomePrefab", CheckpointPrefabPath);
        AddSpawnerWithPrefab<FinishLinePillar>("FinishLinePillar", "pillarPrefab", FinishPrefabPath);
        RGBCameraCaptureSetup.SetUp();
        AddAutoRecordAndQuit();

        Save(V4Path);
        Debug.Log("[SonicARVersionSetup] V4 built: V3 + GameLogic + MapDataFetcher + checkpoint/finish " +
                  "spawners -- full gameplay Update loops running, minus HUD/scoring UI and the dormant " +
                  "FirstPersonStreammingCast comparison path (those are V5-only, i.e. Game.unity itself).");
    }

    public static void BuildV4Apk() => BuildApk(V4Path, "Builds/Android/SonicAR_V4.apk");

    public static void BuildV4AndApkBatch()
    {
        BuildV4();
        BuildApk(V4Path, "Builds/Android/SonicAR_V4.apk");
    }

    /// <summary>
    /// Batch-mode entry point that builds V2, V3 and V4 (scene + APK) in one Unity process, so the
    /// shader-variant warm-up (see xreal-unity-sdk-integration memory) is paid once rather than
    /// three times. V5 is Game.unity itself -- no build method needed, just point
    /// XREALSonicSnowBuild.BuildAndRun (or its own APK output) at the unmodified scene.
    /// </summary>
    public static void BuildV2ThroughV4Batch()
    {
        BuildV2();
        BuildApk(V2Path, "Builds/Android/SonicAR_V2.apk");
        BuildV3();
        BuildApk(V3Path, "Builds/Android/SonicAR_V3.apk");
        BuildV4();
        BuildApk(V4Path, "Builds/Android/SonicAR_V4.apk");
    }

    private static void AddBareCanvas()
    {
        GameObject go = GameObject.Find("Canvas");
        if (go != null && go.GetComponent<Canvas>() != null) return;

        go = go != null ? go : new GameObject("Canvas");

        Canvas canvas = go.GetComponent<Canvas>();
        if (canvas == null) canvas = go.AddComponent<Canvas>();
        if (go.GetComponent<UnityEngine.UI.CanvasScaler>() == null) go.AddComponent<UnityEngine.UI.CanvasScaler>();
        if (go.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null) go.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        Camera mainCamera = Camera.main;

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = mainCamera;

        if (mainCamera != null)
        {
            go.transform.SetParent(mainCamera.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 2f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * 0.00111f;
        }

        RectTransform rect = go.GetComponent<RectTransform>();
        if (rect != null) rect.sizeDelta = new Vector2(1080f, 1080f);

        Debug.Log("[SonicARVersionSetup] Added bare world-space Canvas at (0,0,2) scale 0.00111, " +
                  "same placement XREALCanvasConversion uses.");
    }

    /// <summary>Adds a MonoBehaviour of type T on a GameObject named "name" (creating both if
    /// missing), matching the AddAutoRecordAndQuit pattern -- idempotent, no field wiring, relying on
    /// the component's own field defaults (this test cares whether the subsystem's Update loop is
    /// present and running, not exact production threshold tuning).</summary>
    private static T AddComponentByName<T>(string name) where T : Component
    {
        GameObject go = GameObject.Find(name);
        if (go == null) go = new GameObject(name);

        T component = go.GetComponent<T>();
        if (component == null) component = go.AddComponent<T>();
        return component;
    }

    /// <summary>Same as AddComponentByName, but also wires a private prefab field via
    /// SerializedObject -- CheckpointDomeSpawner/FinishLinePillar both need their prefab reference set
    /// or SpawnDomes()/SpawnPillar() has nothing to instantiate.</summary>
    private static void AddSpawnerWithPrefab<T>(string name, string prefabFieldName, string prefabPath) where T : Component
    {
        T component = AddComponentByName<T>(name);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError("[SonicARVersionSetup] Could not load prefab at " + prefabPath + " for " + name);
            return;
        }

        SerializedObject so = new SerializedObject(component);
        SerializedProperty prop = so.FindProperty(prefabFieldName);
        if (prop == null)
        {
            Debug.LogError("[SonicARVersionSetup] " + name + " has no field '" + prefabFieldName + "'.");
            return;
        }

        prop.objectReferenceValue = prefab;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void StripToRigOnly()
    {
        Scene scene = SceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();

        int destroyed = 0;
        foreach (GameObject root in roots)
        {
            if (RigRootNames.Contains(root.name)) continue;
            Object.DestroyImmediate(root);
            destroyed++;
        }

        Debug.Log("[SonicARVersionSetup] Stripped " + destroyed + " root object(s), kept: " +
                  string.Join(", ", RigRootNames));
    }

    private static void AddStaticCheckpointAndFinish()
    {
        GameObject checkpointPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CheckpointPrefabPath);
        GameObject finishPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FinishPrefabPath);

        if (checkpointPrefab == null || finishPrefab == null)
        {
            Debug.LogError("[SonicARVersionSetup] Could not load checkpoint/finish prefabs at " +
                            CheckpointPrefabPath + " / " + FinishPrefabPath);
            return;
        }

        GameObject checkpoint = (GameObject)PrefabUtility.InstantiatePrefab(checkpointPrefab);
        checkpoint.name = "StaticCheckpoint";
        checkpoint.transform.position = new Vector3(0f, 0f, 4f);

        GameObject finish = (GameObject)PrefabUtility.InstantiatePrefab(finishPrefab);
        finish.name = "StaticFinishLine";
        finish.transform.position = new Vector3(0f, 0f, 10f);

        Debug.Log("[SonicARVersionSetup] Added static checkpoint (z=4) and finish line (z=10).");
    }

    private static void AddAutoRecordAndQuit()
    {
        GameObject go = GameObject.Find("AutoRecordAndQuit");
        if (go == null) go = new GameObject("AutoRecordAndQuit");
        if (go.GetComponent<AutoRecordAndQuit>() == null) go.AddComponent<AutoRecordAndQuit>();
    }

    private static void CopySceneFile(string sourcePath, string destPath)
    {
        // Deliberately NOT copying Game.unity's own .meta -- that would give two scenes the same
        // GUID, which breaks EditorBuildSettings scene references. If this is the first run,
        // there's no destination .meta yet and Unity mints a fresh one on Refresh(); if a stale one
        // from a previous run of this script is already on disk, leave it alone so the GUID (and
        // any build-settings entry) stays stable across re-runs.
        File.Copy(sourcePath, destPath, true);
        AssetDatabase.Refresh();
    }

    private static void Save(string path)
    {
        bool saved = EditorSceneManager.SaveOpenScenes();
        Debug.Log("[SonicARVersionSetup] Saved " + path + ": " + saved);
    }
}

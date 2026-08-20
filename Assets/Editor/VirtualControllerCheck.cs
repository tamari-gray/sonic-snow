using UnityEditor;
using UnityEngine;
using Unity.XR.XREAL;

/// <summary>
/// Assigns XREALSettings.VirtualController through the AssetDatabase rather than by hand-editing
/// YAML. The hand-written reference used fileID 100100000 -- the legacy prefab wrapper object --
/// which resolves to a nameless non-GameObject and is useless at runtime. Letting Unity serialize
/// the reference gets the root GameObject's real fileID.
/// Temporary; delete once the controller is working.
/// </summary>
public static class VirtualControllerCheck
{
    const string SettingsPath = "Assets/XR/Settings/XREALSettings.asset";
    const string PrefabPath = "Packages/com.xreal.xr/Runtime/Prefabs/XREALVirtualController.prefab";

    public static void Run()
    {
        var settings = AssetDatabase.LoadAssetAtPath<XREALSettings>(SettingsPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (settings == null || prefab == null)
        {
            Debug.LogError($"[VCCheck] settings={(settings == null ? "NULL" : "ok")} prefab={(prefab == null ? "NULL" : "ok")}");
            EditorApplication.Exit(1);
            return;
        }

        settings.VirtualController = prefab;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        var reloaded = AssetDatabase.LoadAssetAtPath<XREALSettings>(SettingsPath);
        var vc = reloaded.VirtualController;
        Debug.Log($"[VCCheck] assigned -> VirtualController={(vc == null ? "NULL" : vc.name)}");

        EditorApplication.Exit(0);
    }
}

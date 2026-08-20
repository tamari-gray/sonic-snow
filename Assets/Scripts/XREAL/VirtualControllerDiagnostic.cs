using System.Linq;
using System.Text;
using UnityEngine;
using Unity.XR.XREAL;

/// <summary>
/// TEMPORARY diagnostic for the blank Beam Pro screen / missing spatial mouse.
///
/// Three separate misconfigurations were found and fixed (null VirtualController prefab, missing
/// UNITY_URP define, cleared preloadedAssets) and the phone display stayed black after every one,
/// so rather than guess a fourth this reports the actual runtime state: whether XREALSettings
/// loaded at all, whether the prefab path or the bare-singleton fallback was taken, and what the
/// resulting canvases are actually configured to render to.
///
/// Delete once the controller renders — it exists to answer one question, not to ship.
/// </summary>
public static class VirtualControllerDiagnostic
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Report()
    {
        // A frame after load, so XREALSettings.OnLoad (BeforeSceneLoad) and the controller's
        // own Start have both run — reading any earlier would report a half-built state.
        GameObject host = new GameObject(nameof(VirtualControllerDiagnostic));
        Object.DontDestroyOnLoad(host);
        host.AddComponent<Runner>();
    }

    private class Runner : MonoBehaviour
    {
        private int frames;

        private void Update()
        {
            if (++frames < 5) return;

            var sb = new StringBuilder("[VCDiag] ");

            XREALSettings settings = XREALSettings.GetSettings();
            sb.Append($"settings={(settings == null ? "NULL" : "loaded")} ");
            if (settings != null)
            {
                sb.Append($"prefab={(settings.VirtualController == null ? "NULL" : settings.VirtualController.name)} ");
                sb.Append($"inputSource={settings.InitialInputSource} ");
            }

            var controller = XREALVirtualController.Singleton;
            sb.Append($"singleton={(controller == null ? "NULL" : controller.gameObject.name)} ");

            if (controller != null)
            {
                var canvases = controller.GetComponentsInChildren<Canvas>(true);
                sb.Append($"canvases={canvases.Length} ");
                foreach (var c in canvases)
                {
                    sb.Append($"[{c.name} mode={c.renderMode} display={c.targetDisplay} " +
                              $"cam={(c.worldCamera == null ? "none" : c.worldCamera.name)} " +
                              $"active={c.gameObject.activeInHierarchy} enabled={c.enabled}] ");
                }

                var cams = controller.GetComponentsInChildren<Camera>(true);
                sb.Append($"cams={cams.Length} ");
                foreach (var cam in cams)
                    sb.Append($"[{cam.name} display={cam.targetDisplay} active={cam.gameObject.activeInHierarchy} enabled={cam.enabled} depth={cam.depth}] ");
            }

            sb.Append($"| allCanvasesInScene={Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Length} ");
            sb.Append($"displays={Display.displays.Length}");

            Debug.Log(sb.ToString());
            Destroy(gameObject);
        }
    }
}

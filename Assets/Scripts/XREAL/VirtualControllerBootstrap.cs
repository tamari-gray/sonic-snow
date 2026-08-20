using System.Collections;
using UnityEngine;
using Unity.XR.XREAL;

/// <summary>
/// Creates the XREAL virtual controller when the SDK's own attempt has silently done nothing.
///
/// XREALSettings.OnLoad runs at BeforeSceneLoad and only builds the controller when
/// XREALUtility.IsLoaderActive() is already true. XR Management initialises the loader at
/// BeforeSceneLoad as well, and the order between the two is not defined — when XR Management
/// loses the race the gate reads false, neither the prefab nor the fallback branch runs, and no
/// controller is ever created. On device that showed up as a blank phone screen with no
/// "[XREALVirtualController] Start" line anywhere in logcat.
///
/// AfterSceneLoad is late enough that the loader is up, so this retries the prefab path the SDK
/// skipped. It stays out of the way when the SDK did succeed.
///
/// Note the SDK's XREALVirtualController.CreateSingleton() fallback is not a substitute: it does
/// `new GameObject(name, typeof(T))` and so produces a controller with no canvas, no buttons and
/// no URP camera. Only instantiating the prefab yields something that draws.
/// </summary>
public static class VirtualControllerBootstrap
{
#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>How long to keep waiting for the XR loader before giving up.</summary>
    private const float LoaderTimeout = 10f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        GameObject host = new GameObject(nameof(VirtualControllerBootstrap));
        Object.DontDestroyOnLoad(host);
        host.AddComponent<Runner>();
    }

    private class Runner : MonoBehaviour
    {
        private IEnumerator Start()
        {
            float deadline = Time.realtimeSinceStartup + LoaderTimeout;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (XREALVirtualController.Singleton != null)
                {
                    Debug.Log("[VCBoot] Controller already exists; the SDK won the race, nothing to do.");
                    Destroy(gameObject);
                    yield break;
                }

                if (XREALUtility.IsLoaderActive())
                    break;

                yield return null;
            }

            if (!XREALUtility.IsLoaderActive())
            {
                Debug.LogWarning($"[VCBoot] XR loader still inactive after {LoaderTimeout}s; not creating a controller.");
                Destroy(gameObject);
                yield break;
            }

            XREALSettings settings = XREALSettings.GetSettings();
            GameObject prefab = settings == null ? null : settings.VirtualController;

            if (prefab == null)
            {
                Debug.LogWarning("[VCBoot] XREALSettings has no VirtualController prefab; cannot create a controller " +
                                 "that draws. Check the reference in Assets/XR/Settings/XREALSettings.asset.");
                Destroy(gameObject);
                yield break;
            }

            Instantiate(prefab);
            Debug.Log($"[VCBoot] Instantiated '{prefab.name}' — the SDK's BeforeSceneLoad gate had skipped it.");
            Destroy(gameObject);
        }
    }
#endif
}

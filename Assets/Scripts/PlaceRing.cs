using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using EnhancedTouch = UnityEngine.InputSystem.EnhancedTouch;

public class PlaceRing : MonoBehaviour
{

    public static PlaceRing Instance;

    [SerializeField]
    private GameObject ringPrefab;

    private ARRaycastManager arRaycastManager;
    private ARPlaneManager arPlaneManager;
    private List<ARRaycastHit> hitList = new List<ARRaycastHit>();
    private ARAnchorManager arAnchorManager;
    private List<ARAnchor> spawnedAnchors = new List<ARAnchor>();

    private bool canPlaceRings = true;

    private void Awake()
    {
        Instance = this;
    }

    void Start()
    {

        arAnchorManager = GetComponent<ARAnchorManager>();
        arRaycastManager = GetComponent<ARRaycastManager>();
        arPlaneManager = GetComponent<ARPlaneManager>();

        // Safety check — warn in console if plane prefab is missing
        if (arPlaneManager.planePrefab == null)
        {
            Debug.LogWarning("ARPlaneManager has no Plane Prefab assigned! " +
                             "Planes will be detected but not visible. " +
                             "Assign a plane prefab in the Inspector.");
        }
    }


    private void OnEnable()
    {
        EnhancedTouch.TouchSimulation.Enable();
        EnhancedTouch.EnhancedTouchSupport.Enable();
        EnhancedTouch.Touch.onFingerDown += FingerDown;
    }

    private void OnDisable()
    {
        EnhancedTouch.TouchSimulation.Disable();
        EnhancedTouch.EnhancedTouchSupport.Disable();
        EnhancedTouch.Touch.onFingerDown -= FingerDown;
    }

    private void FingerDown(EnhancedTouch.Finger finger)
    {
        if (!canPlaceRings) return;

        if (finger.index != 0) return;

        if (arRaycastManager.Raycast(finger.currentTouch.screenPosition, hitList, TrackableType.PlaneWithinPolygon))
        {
            Pose hitPose = hitList[0].pose;

            ARPlane plane = arPlaneManager.GetPlane(hitList[0].trackableId);
            if (plane == null) return;

            ARAnchor anchor = arAnchorManager.AttachAnchor(plane, hitPose);

            if (anchor == null) return;

            spawnedAnchors.Add(anchor);

            Quaternion uprightRotation = Quaternion.Euler(
                0f,
                hitPose.rotation.eulerAngles.y,
                0f
            );

            // Offset position 1m above the plane
            Vector3 spawnPosition = hitPose.position + Vector3.up * 0.7f;

            GameObject ring = Instantiate(ringPrefab, spawnPosition, uprightRotation);
            ring.transform.SetParent(anchor.transform, true);

        }
    }

    public void ClearAllRings()
    {
        if (spawnedAnchors.Count == 0)
        {
            Debug.Log("No rings to clear");
            return;
        }

        foreach (ARAnchor anchor in spawnedAnchors)
        {
            if (anchor != null)
            {
                Destroy(anchor.gameObject);
            }
        }

        spawnedAnchors.Clear();

        Debug.Log("All AR rings cleared");
    }

    public void EnablePlacement()
    {
        canPlaceRings = true;

        if (arPlaneManager == null)
            arPlaneManager = GetComponent<ARPlaneManager>();

        if (arPlaneManager != null)
        {
            arPlaneManager.enabled = true;

            foreach (var plane in arPlaneManager.trackables)
            {
                plane.gameObject.SetActive(true);
            }

            Debug.Log("Placement enabled + planes visible");
        }
        else
        {
            Debug.LogWarning("ARPlaneManager null in EnablePlacement!");
        }
    }

    public void DisablePlacement()
    {
        canPlaceRings = false;

        if (arPlaneManager == null)
            arPlaneManager = GetComponent<ARPlaneManager>();

        if (arPlaneManager != null)
        {
            arPlaneManager.enabled = false;

            foreach (var plane in arPlaneManager.trackables)
            {
                plane.gameObject.SetActive(false); // hide instead of destroy
            }

            Debug.Log("Placement disabled + planes hidden");
        }
        else
        {
            Debug.LogWarning("ARPlaneManager null in DisablePlacement!");
        }
    }
} 
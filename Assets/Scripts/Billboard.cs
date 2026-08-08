using UnityEngine;

/// <summary>
/// Turns this object to face the rider every frame.
///
/// Put it on the text of a geo-placed marker. Without it the text keeps whatever
/// orientation it inherited at spawn, which reads correctly only while you are riding
/// straight at the marker — come at a checkpoint from the side and you are looking at
/// the text edge-on.
///
/// Note this sets world rotation outright, so it overrides whatever the object inherits
/// from its parent. The spawners still yaw the marker roots toward the start line via
/// <see cref="GpsUtils.ReadableFromOrigin"/>, which now only affects non-symmetric props
/// — the beam is a cylinder and the dome a sphere, so their own yaw is invisible.
/// </summary>
[DisallowMultipleComponent]
public class Billboard : MonoBehaviour
{
    [Tooltip("Stay upright and only spin about the world Y axis, so the text reads like a " +
             "sign planted in the snow. Turn this off to align flat to the screen instead, " +
             "which stays square when you look up at the top of the beam but tilts with the phone.")]
    [SerializeField] private bool uprightOnly = true;

    [Tooltip("Camera to face. Defaults to Camera.main, which is the AR camera.")]
    [SerializeField] private Camera target;

    // LateUpdate, not Update: AR Foundation drives the camera pose during Update, so
    // reading it any earlier bills the text against last frame's head position and it
    // visibly lags when you turn.
    private void LateUpdate()
    {
        if (target == null) target = Camera.main;
        if (target == null) return;

        Transform view = target.transform;

        // Text reads correctly when its own forward points the same way the viewer is
        // looking, not back at them — see GpsUtils.ReadableFromOrigin for the long version.
        if (!uprightOnly)
        {
            transform.rotation = view.rotation;
            return;
        }

        Vector3 awayFromRider = transform.position - view.position;
        awayFromRider.y = 0f;

        // Degenerate only if the rider is standing exactly inside the marker, where any
        // facing is arbitrary anyway. Keep the previous one rather than snapping.
        if (awayFromRider.sqrMagnitude < 1e-6f) return;

        transform.rotation = Quaternion.LookRotation(awayFromRider, Vector3.up);
    }
}

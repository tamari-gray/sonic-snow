using UnityEngine;

public class RingSpin : MonoBehaviour
{
    // Degrees per second — classic Sonic uses ~180
    [SerializeField] float spinSpeed = 180f;

    void Update()
    {
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f);
    }
}

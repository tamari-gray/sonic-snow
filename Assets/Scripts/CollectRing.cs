using UnityEngine;
using UnityEngine.XR.ARFoundation;

public class CollectRing : MonoBehaviour
{
    [SerializeField]
    private float collectRadius = 2f;

    [SerializeField]
    private GameObject collectParticlePrefab;

    private bool canCollectRing = false;

    public static CollectRing Instance;

    private void Awake()
    {
        Instance = this;

        Application.logMessageReceived += HandleLog;
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= HandleLog;
    }

    void HandleLog(string logString, string stackTrace, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error)
        {
            string path = Application.persistentDataPath + "/crash_log.txt";
            System.IO.File.AppendAllText(path,
                $"\n[{System.DateTime.Now}] {logString}\n{stackTrace}\n"
            );
        }
    }


    void Update()
    {
        if (!canCollectRing) return;

        GameObject[] rings = GameObject.FindGameObjectsWithTag("Ring");

        foreach (GameObject ring in rings)
        {
            if (ring == null) continue; // guard against mid-destroy rings

            float distance = Vector3.Distance(transform.position, ring.transform.position);

            if (distance < collectRadius)
            {
                Debug.Log("Touched ring!");

                if (collectParticlePrefab != null)
                {
                    GameObject particles = Instantiate(collectParticlePrefab, ring.transform.position, ring.transform.rotation);
                    Destroy(particles, 2f);
                }

                ScoreManager.instance.AddPoint();

                Destroy(ring);
            }
        }
    }

    public void EnableCollecting()
    {
        canCollectRing = true;

        Debug.Log("Ring Collection enabled ");
    }

    public void DisableCollecting()
    {
        canCollectRing = false;

        Debug.Log("Ring Collection disabled");
    }
}

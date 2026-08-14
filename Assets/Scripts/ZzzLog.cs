// Source - https://stackoverflow.com/a/67704821
// Posted by xjcl, modified by community. See post 'Timeline' for change history
// Retrieved 2026-06-28, License - CC BY-SA 4.0
//
// Ported off OnGUI for the XREAL build: IMGUI doesn't render through the XR pipeline, so
// the log now writes into a TMP text on a world-space canvas instead. See XREALCanvasConversion
// for how the game's other canvases moved to world space.

using UnityEngine;
using System.Collections;
using TMPro;

public class ZzzLog : MonoBehaviour
{
    [SerializeField] private TMP_Text display;

    uint qsize = 15;  // number of messages to keep
    Queue myLogQueue = new Queue();

    void Start()
    {
        Debug.Log("Started up logging.");
    }

    void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
    }

    void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }

    void HandleLog(string logString, string stackTrace, LogType type)
    {
        myLogQueue.Enqueue("[" + type + "] : " + logString);
        if (type == LogType.Exception)
            myLogQueue.Enqueue(stackTrace);
        while (myLogQueue.Count > qsize)
            myLogQueue.Dequeue();

        WriteCrashLog(logString, stackTrace, type);

        if (display != null) display.text = string.Join("\n", myLogQueue.ToArray());
    }

    // On-device crash log — the on-screen queue only holds the last few lines,
    // so errors also get appended to a file we can pull off the phone later.
    void WriteCrashLog(string logString, string stackTrace, LogType type)
    {
        if (type != LogType.Exception && type != LogType.Error) return;

        string path = Application.persistentDataPath + "/crash_log.txt";
        System.IO.File.AppendAllText(path,
            $"\n[{System.DateTime.Now}] {logString}\n{stackTrace}\n"
        );
    }
}

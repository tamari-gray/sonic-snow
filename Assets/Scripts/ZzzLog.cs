// Source - https://stackoverflow.com/a/67704821
// Posted by xjcl, modified by community. See post 'Timeline' for change history
// Retrieved 2026-06-28, License - CC BY-SA 4.0

using UnityEngine;
using System.Collections;

public class ZzzLog : MonoBehaviour
{
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

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(Screen.width - 400, 0, 400, Screen.height));
        GUILayout.Label("\n" + string.Join("\n", myLogQueue.ToArray()));
        GUILayout.EndArea();
    }
}

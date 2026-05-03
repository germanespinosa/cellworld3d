// CellworldGameBridge.cs
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum CellworldBridgeState
{
    Unknown = 0,
    Connected = 1,
    Ready = 2,
    Paused = 3,
    Running = 4,
    Stopped = 5
}

public class CellworldGameBridge : MonoBehaviour
{
    // ===================== SINGLETON / PERSISTENCE =====================

    public static CellworldGameBridge Instance { get; private set; }

    [Header("Persistence")]
    public bool persistAcrossScenes = true;

    [Header("Predator binding across scenes")]
    public bool autoFindPredatorOnSceneLoad = true;
    public string predatorName = "Predator"; // used only if autoFindPredatorOnSceneLoad = true

    private bool initialized = false;
    private bool quitting = false;

    // ===================== ORIGINAL FIELDS =====================

    [Header("Launch Python on start")]
    public bool launchPythonOnStart = true;
    public string pythonExe = "";                    // fallback if env var not set
    public string pythonScriptPath = "Server\\Server.py";             // absolute or project-relative
    public string pythonWorkingDirectory = "";       // optional
    public bool killPythonOnQuit = true;

    [Header("Networking")]
    public int listenPort = 5005;                    // Python -> Unity
    public string pythonIP = "127.0.0.1";
    public int pythonCommandPort = 5006;             // Unity -> Python

    [Header("Predator Mapping (optional if auto-find/register is used)")]
    public Transform predator;
    public float positionScale = 1f;
    public float yHeight = 0f;

    [Header("Logging")]
    public bool logBridgeState = true;
    public bool logEverySend = true;
    public bool forwardPythonStdout = true;
    public bool forwardPythonStderr = true;

    [Header("Sampling")]
    public float SampleInterval { get; set; } = 1f / 60f;

    private UdpClient receiver;
    private UdpClient sender;
    private Thread recvThread;
    private volatile bool running;

    private readonly object dataLock = new object();
    private float lastX, lastY, lastRotDeg;
    private volatile bool hasData = false;
    private bool firstPacketLogged = false;
    private float lastPreySendTime = -1f;

    private System.Diagnostics.Process pythonProc;
    private Thread pythonStdoutThread;
    private Thread pythonStderrThread;
    private bool cleanedUp = false;
    public CellworldBridgeState CurrentState { get; private set; } = CellworldBridgeState.Unknown;
    public string WorldName { get; private set; } = string.Empty;
    public int PuffCount { get; private set; } = 0;
    public int TrialCount { get; private set; } = 0;
    public event Action PuffEventReceived;

    // ===================== UNITY LIFECYCLE =====================

    void Awake()
    {
        // Enforce singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (persistAcrossScenes)
            DontDestroyOnLoad(gameObject);

        UnityMainThreadDispatcher.EnsureExists();

        if (autoFindPredatorOnSceneLoad)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
    }

    void Start()
    {
        // Start() will be called once for this persistent object, but guard anyway.
        if (initialized) return;
        initialized = true;

        if (logBridgeState)
        {
            Debug.Log("[BRIDGE] Start()");
            Debug.Log($"[BRIDGE] Listening UDP: 0.0.0.0:{listenPort}");
            Debug.Log($"[BRIDGE] Command UDP -> {pythonIP}:{pythonCommandPort}");
        }

        // Create sender/receiver once
        sender = new UdpClient();

        receiver = new UdpClient(listenPort);
        receiver.Client.ReceiveTimeout = 1000;

        running = true;
        recvThread = new Thread(ReceiveLoop) { IsBackground = true };
        recvThread.Start();

        // Bind predator for initial scene if requested
        if (autoFindPredatorOnSceneLoad)
            TryBindPredatorInActiveScene();

        if (launchPythonOnStart)
            LaunchPython();
    }

    void Update()
    {
        // Predator can change per scene; if not bound yet, just keep receiving data.
        if (!hasData || predator == null) return;

        float x, y, rotDeg;
        lock (dataLock)
        {
            x = lastX;
            y = lastY;
            rotDeg = lastRotDeg;
        }

        predator.position = new Vector3(x * positionScale, yHeight, y * positionScale);
        predator.rotation = Quaternion.Euler(0f, -90-rotDeg, 0f); // rot is degrees; invert direction
    }

    void OnApplicationQuit()
    {
        quitting = true;
        SendStop();
        Cleanup();
    }

    void OnDestroy()
    {
        // If we are the singleton and we aren't quitting, do NOT cleanup on scene changes.
        // With DontDestroyOnLoad, OnDestroy should only happen on quit or if you manually destroy it.
        if (Instance == this)
        {
            if (!quitting)
                SceneManager.sceneLoaded -= OnSceneLoaded;

            if (quitting)
            {
                SendStop();
                Cleanup();
            }

            Instance = null;
        }
    }

    // ===================== SCENE LOAD HANDLING =====================

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!autoFindPredatorOnSceneLoad) return;
        TryBindPredatorInActiveScene();
    }

    private void TryBindPredatorInActiveScene()
    {
        if (string.IsNullOrWhiteSpace(predatorName)) return;

        var go = GameObject.Find(predatorName);
        if (go != null)
        {
            predator = go.transform;

            if (logBridgeState)
                Debug.Log($"[BRIDGE] Bound predator '{predatorName}' in scene '{SceneManager.GetActiveScene().name}'");
        }
        else
        {
            if (logBridgeState)
                Debug.Log($"[BRIDGE] Predator '{predatorName}' not found in scene '{SceneManager.GetActiveScene().name}' (ok if scene doesn't have one)");
        }
    }

    // Call this from any scene if you prefer explicit binding instead of auto-find.
    public void RegisterPredator(Transform predatorTransform)
    {
        predator = predatorTransform;
        if (logBridgeState)
            Debug.Log($"[BRIDGE] Predator registered explicitly: {(predator ? predator.name : "null")}");
    }

    // ===================== SEND COMMANDS =====================

    public void SendReset() => SendCommand("r");
    public void SendPause() => SendCommand("p");
    public void SendUnpause() => SendCommand("u");
    public void SendStop() => SendCommand("s");
    public void SendCleanUp() => SendCommand("k");
    public void SendBegin() => SendCommand("b");

    public void SendInit(string world, bool render, float time_step, string protocol, string patient)
    {
        WorldName = world;
        string json = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{{\"world_name\":\"{0}\",\"render\":{1},\"time_step\":{2:0.####},\"protocol\":\"{3}\",\"patient\":\"{4}\"}}",
            EscapeJsonString(world ?? string.Empty),
            render ? "true" : "false",
            Round4(time_step),
            EscapeJsonString(protocol ?? string.Empty),
            EscapeJsonString(patient ?? string.Empty)
        );
        SendCommand("i" + json);
    }

    // Sends prey data as: d[prey_x, prey_y, prey_direction]
    public void SendPrey(float x, float y, float directionDeg)
    {
        float now = Time.realtimeSinceStartup;
        if (SampleInterval > 0f && lastPreySendTime >= 0f && (now - lastPreySendTime) < SampleInterval)
            return;

        lastPreySendTime = now;

        float xr = Round4(x);
        float yr = Round4(y);
        float dr = Round4(directionDeg);

        string json = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "[{0:0.####},{1:0.####},{2:0.####}]",
            xr, yr, dr
        );
        SendCommand("d" + json);
    }

    private static float Round4(float value)
    {
        return (float)Math.Round(value, 4, MidpointRounding.AwayFromZero);
    }

    private static string EscapeJsonString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }

    private void SendCommand(string cmd)
    {
        try
        {
            if (sender == null) return;

            //if (logEverySend && !string.IsNullOrEmpty(cmd))
            //    Debug.Log($"[BRIDGE] ->PY '{cmd[0]}'");

            byte[] data = Encoding.UTF8.GetBytes(cmd);
            sender.Send(data, data.Length, pythonIP, pythonCommandPort);
        }
        catch (Exception e)
        {
            Debug.LogError($"[BRIDGE] SEND ERROR: {e.Message}");
        }
    }

    // ===================== RECEIVE LOOP =====================

    private void ReceiveLoop()
    {
        IPEndPoint ep = new IPEndPoint(IPAddress.Any, listenPort);

        while (running)
        {
            try
            {
                byte[] bytes = receiver.Receive(ref ep);
                string msg = Encoding.UTF8.GetString(bytes);

                if (string.IsNullOrEmpty(msg))
                    continue;

                char cmd = msg[0];
                string payload = msg.Length > 1 ? msg.Substring(1) : "";

                // Predator data: d[ x, y, rotDeg ]
                if (cmd == 'd')
                {
                    float[] data = JsonHelper.FromJson<float>(payload);

                    if (data != null && data.Length >= 3)
                    {
                        lock (dataLock)
                        {
                            lastX = data[0];
                            lastY = data[1];
                            lastRotDeg = data[2];
                            hasData = true;
                        }

                        if (logBridgeState && !firstPacketLogged)
                        {
                            firstPacketLogged = true;
                            UnityMainThreadDispatcher.Enqueue(() =>
                                Debug.Log($"[BRIDGE] First predator packet received from {ep.Address}:{ep.Port}")
                            );
                        }
                    }
                }
                // Puff event: 'p'
                else if (cmd == 'p')
                {
                    PuffCount++;
                    UnityMainThreadDispatcher.Enqueue(() =>
                    {
                        PuffEventReceived?.Invoke();
                        if (logBridgeState)
                            Debug.Log("[BRIDGE] PUFF event received");
                    });
                }
                // Trial Finished event: 'f'
                else if (cmd == 'f')
                {
                    TrialCount++;
                    if (logBridgeState)
                        UnityMainThreadDispatcher.Enqueue(() => Debug.Log("[BRIDGE] TRIAL FINISHED event received"));
                }
                // State change event: 's' (e.g. reset/pause/stop)
                else if (cmd == 's')
                {
                    if (payload == "1")
                        CurrentState = CellworldBridgeState.Connected;
                    else if (payload == "2")
                        CurrentState = CellworldBridgeState.Ready;
                    else if (payload == "3")
                        CurrentState = CellworldBridgeState.Paused;
                    else if (payload == "4")
                        CurrentState = CellworldBridgeState.Running;
                    else if (payload == "5")
                        CurrentState = CellworldBridgeState.Stopped;
                if (logBridgeState)
                        UnityMainThreadDispatcher.Enqueue(() => Debug.Log($"[BRIDGE] STATE CHANGE event received: {CurrentState}"));
                }
                else
                {
                    if (logBridgeState)
                        UnityMainThreadDispatcher.Enqueue(() => Debug.Log($"[BRIDGE] Unknown msg '{cmd}' payload='{payload}'"));
                }
            }
            catch (SocketException) { /* timeout */ }
            catch (Exception e)
            {
                if (logBridgeState)
                    UnityMainThreadDispatcher.Enqueue(() => Debug.LogError($"[BRIDGE] RECEIVE ERROR: {e.Message}"));
            }
        }
    }

    // ===================== PYTHON LAUNCH + OUTPUT =====================

    private string ResolvePythonExe()
    {
        string env =
            Environment.GetEnvironmentVariable("CELLWORLD_PYTHON", EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable("CELLWORLD_PYTHON", EnvironmentVariableTarget.Machine)
            ?? Environment.GetEnvironmentVariable("CELLWORLD_PYTHON");

        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return env;

        if (!string.IsNullOrWhiteSpace(pythonExe) && File.Exists(pythonExe))
            return pythonExe;

        return "python";
    }

    private static string ResolveEnvironmentVariable(string variableName)
    {
        if (string.IsNullOrWhiteSpace(variableName))
            return null;

        return Environment.GetEnvironmentVariable(variableName)
            ?? Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.Machine);
    }

    private void LaunchPython()
    {
        if (string.IsNullOrWhiteSpace(pythonScriptPath))
        {
            Debug.LogError("[BRIDGE] pythonScriptPath is empty. Not launching Python.");
            return;
        }

        string scriptFullPath = pythonScriptPath;
        if (!Path.IsPathRooted(scriptFullPath))
            scriptFullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", pythonScriptPath));

        if (!File.Exists(scriptFullPath))
        {
            Debug.LogError($"[BRIDGE] Python script not found: {scriptFullPath}");
            return;
        }

        string workDir = pythonWorkingDirectory;
        if (string.IsNullOrWhiteSpace(workDir))
            workDir = Path.GetDirectoryName(scriptFullPath);

        try
        {
            string py = ResolvePythonExe();
            if (logBridgeState)
                Debug.Log($"[BRIDGE] Launching Python: {py} -u \"{scriptFullPath}\"");

            pythonProc = new System.Diagnostics.Process();
            pythonProc.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = py,

                // IMPORTANT: -u makes Python unbuffered so prints show up immediately
                Arguments = $"-u \"{scriptFullPath}\"",

                WorkingDirectory = workDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            // Also enforce unbuffered mode via env var
            pythonProc.StartInfo.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";

            // Explicitly pass the experiment dir to child Python. Unity may not inherit user-level vars.
            string experimentDir = ResolveEnvironmentVariable("CELLWORLD_EXPERIMENT_DIR");
            if (!string.IsNullOrWhiteSpace(experimentDir))
            {
                pythonProc.StartInfo.EnvironmentVariables["CELLWORLD_EXPERIMENT_DIR"] = experimentDir;
                if (logBridgeState)
                    Debug.Log($"[BRIDGE] CELLWORLD_EXPERIMENT_DIR={experimentDir}");
            }
            else if (logBridgeState)
            {
                Debug.LogWarning("[BRIDGE] CELLWORLD_EXPERIMENT_DIR is not set in process/user/machine environment.");
            }

            pythonProc.Start();

            if (forwardPythonStdout)
            {
                pythonStdoutThread = new Thread(() =>
                    DrainPythonTextStream(
                        pythonProc.StandardOutput,
                        line => Debug.Log("[PY] " + line)))
                {
                    IsBackground = true,
                    Name = "PythonStdoutForwarder"
                };
                pythonStdoutThread.Start();
            }

            if (forwardPythonStderr)
            {
                pythonStderrThread = new Thread(() =>
                    DrainPythonTextStream(
                        pythonProc.StandardError,
                        line => Debug.LogError("[PY-ERR] " + line)))
                {
                    IsBackground = true,
                    Name = "PythonStderrForwarder"
                };
                pythonStderrThread.Start();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BRIDGE] Failed to launch Python: {ex.Message}");
        }
    }

    private static void DrainPythonTextStream(TextReader reader, Action<string> logAction)
    {
        if (reader == null || logAction == null)
            return;

        char[] buffer = new char[256];
        StringBuilder pending = new StringBuilder();

        try
        {
            while (true)
            {
                int charsRead = reader.Read(buffer, 0, buffer.Length);
                if (charsRead <= 0)
                    break;

                pending.Append(buffer, 0, charsRead);
                FlushCompleteLines(pending, logAction);
            }

            FlushRemainingText(pending, logAction);
        }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
        catch (Exception ex)
        {
            UnityMainThreadDispatcher.Enqueue(() =>
                Debug.LogError("[BRIDGE] Python stream forwarder failed: " + ex.Message));
        }
    }

    private static void FlushCompleteLines(StringBuilder pending, Action<string> logAction)
    {
        int newlineIndex;
        while ((newlineIndex = FindNewlineIndex(pending)) >= 0)
        {
            string line = pending.ToString(0, newlineIndex).TrimEnd('\r');
            pending.Remove(0, newlineIndex + 1);

            UnityMainThreadDispatcher.Enqueue(() => logAction(line));
        }
    }

    private static void FlushRemainingText(StringBuilder pending, Action<string> logAction)
    {
        if (pending.Length == 0)
            return;

        string line = pending.ToString().TrimEnd('\r');
        pending.Clear();
        UnityMainThreadDispatcher.Enqueue(() => logAction(line));
    }

    private static int FindNewlineIndex(StringBuilder text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                return i;
        }

        return -1;
    }

    // ===================== CLEANUP =====================

    private void Cleanup()
    {
        if (cleanedUp) return;
        cleanedUp = true;

        if (logBridgeState)
            Debug.Log("[BRIDGE] Cleanup()");

        running = false;

        try { receiver?.Close(); } catch { }
        try { sender?.Close(); } catch { }

        try { recvThread?.Join(200); } catch { }

        if (killPythonOnQuit)
        {
            try
            {
                if (pythonProc != null && !pythonProc.HasExited)
                    pythonProc.Kill();
            }
            catch { }
        }

        try { pythonStdoutThread?.Join(200); } catch { }
        try { pythonStderrThread?.Join(200); } catch { }
    }

}

// ===================== JSON ARRAY HELPER =====================
// Parse "[1,2,3]" by wrapping into {"array":[...]} for JsonUtility
public static class JsonHelper
{
    public static T[] FromJson<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        string wrapped = "{ \"array\": " + json + "}";
        Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(wrapped);
        return wrapper.array;
    }

    [Serializable]
    private class Wrapper<T>
    {
        public T[] array;
    }
}

// ===================== MAIN THREAD DISPATCHER =====================
// Process output callbacks are not on Unity main thread.
public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static readonly System.Collections.Generic.Queue<Action> queue =
        new System.Collections.Generic.Queue<Action>();

    private static UnityMainThreadDispatcher instance;

    public static void EnsureExists()
    {
        if (instance != null) return;

        var go = new GameObject("UnityMainThreadDispatcher");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<UnityMainThreadDispatcher>();
    }

    public static void Enqueue(Action action)
    {
        if (action == null) return;
        EnsureExists();
        lock (queue) { queue.Enqueue(action); }
    }

    void Update()
    {
        lock (queue)
        {
            while (queue.Count > 0)
            {
                try { queue.Dequeue().Invoke(); }
                catch (Exception e) { Debug.LogError("[DISPATCHER] " + e.StackTrace); }
            }
        }
    }
}

using UnityEngine;
using UnityEditor;
using UnityEditor.Callbacks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime;
using System.Text;
using System.Xml;

[InitializeOnLoad]
public class UDuet {
    static UDuet(){
        instance.Start();
    }
    

    public UDuet(){
        projectPath = Path.GetDirectoryName(Application.dataPath);
        if (!projectPath.EndsWith("/")){
            projectPath += "/";
        }
        EditorApplication.update += Update;
    }

    public string projectPath;
    public Logger log = new Logger(Logger.QUET);
    private string buffer = "";
    private Timer pingTimer = new Timer(5f);
    private Timer recvTimer = new Timer(0.2f);
    private Timer reconnectTimer = new Timer(16f);

    private static UDuet _instance;
    public static UDuet instance { 
        get {
            if (_instance != null) return _instance;
            _instance = new UDuet();
            return _instance;
        }
    }

    private bool _slaveChecked = false;
    private bool _isSlave = false;
    public bool isSlave { 
        get {
            if (_slaveChecked) return _isSlave;
            _slaveChecked = true;
            _isSlave = File.Exists(Path.Combine(projectPath, ".uduet.slave"));
            return _isSlave;

        }
    }

    private Socket _sock;
    private Socket sock {
        get {
            if (_sock != null) return _sock;
            string socketfile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "uduet/socketfile");
            int port = Convert.ToInt32(File.ReadAllText(socketfile));
            _sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _sock.Blocking = false;
            _sock.Connect("localhost", port);
            log.Info("Connecting to Watchdog...");
            return _sock;
        }
    }

    [OnOpenAsset]
    public static bool OpenAsset(int instanceId, int line){
        if (!instance.isSlave) return false;
        string assetPath = AssetDatabase.GetAssetPath(instanceId);
        assetPath = Convert.ToBase64String(Encoding.ASCII.GetBytes(assetPath));
        instance.Command("PASS_OPEN", assetPath, line);

        return true;
    }
    
    [MenuItem("File/Create Project Slave")]
    public static void CreateSlave(){
        if (!instance.isSlave){
            instance.Command("CREATE_SLAVE");
        }
    }

    [MenuItem("File/Create Project Slave", true)]
    public static bool CanCreateSlave(){
        return !instance.isSlave;
    }

    [MenuItem("File/Close Project Slave %#w")]
    public static void CloseSlave(){
        if (instance.isSlave){
            instance.Command("CLOSE_SLAVE", instance.projectPath);
        }
    }

    [MenuItem("File/Close Project Slave %#w", true)]
    public static bool CanCloseSlave(){
        return instance.isSlave;
    }

    public void Start(){
        RunWatchdog();
        recvTimer.Enable();
        pingTimer.Enable();
        if (isSlave){
            Command("REGISTER_SLAVE", instance.projectPath);
        } else {
            Command("REGISTER_MASTER", instance.projectPath);
        }
    }

    public void RunWatchdog(){
        string watchdogDirPath = Path.Combine(projectPath, "Assets/Plugins/UDuet/Editor/Bin");
        string monoLibPath = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        string monoPath = Path.Combine(monoLibPath, "../../../bin/mono");
        string serviceArgs = Path.Combine(watchdogDirPath, "watchdog.exe") + " start";
        log.Debug("Starting Watchdog");
        log.Debug("Mono path: " + monoPath);
        log.Debug("Command: " + serviceArgs);


        ProcessStartInfo startInfo = new ProcessStartInfo(monoPath, serviceArgs);
        startInfo.UseShellExecute = false;
        startInfo.EnvironmentVariables["MONO_PATH"] = monoLibPath;
        startInfo.EnvironmentVariables["_"] = monoPath;
        startInfo.WorkingDirectory = watchdogDirPath;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        Process proc = Process.Start(startInfo);
        proc.WaitForExit();
        log.Info("Watchdog started");
    }

    public void Update(){
        if (pingTimer.Tick()){
            Command("PING");
        }
        if (reconnectTimer.Tick()){
            log.Info("Reconnecting to watchdog");
            reconnectTimer.Disable();
            _sock = null;
            Start();
        }
        if (recvTimer.Tick()){
            byte[] buff = new byte[128];
            try {
                int received = sock.Receive(buff);
                if (received > 0){
                    buffer += Encoding.ASCII.GetString(buff, 0, received);
                    int idx;
                    while ((idx = buffer.IndexOf("\r\n")) >= 0){
                        string message = buffer.Substring(0, idx);
                        buffer = buffer.Substring(idx + 2);
                        HandleMessage(message);
                    }
                }
                
            } catch (SocketException e){
                if (e.SocketErrorCode == SocketError.ConnectionReset){
                    OnDisconnec();
                    return;
                }
                if (e.SocketErrorCode != SocketError.WouldBlock){
                    throw e;
                }
            }
        }
        
    }

    private void HandleMessage(string message){
        if (message != "PONG"){
            log.Debug("S> " + message);
        }
        string[] parts = message.Split(' ');
        if (parts[0] == "PASS_OPEN"){
            string assetPath = Encoding.ASCII.GetString(Convert.FromBase64String(parts[1]));
            int line = Convert.ToInt32(parts[2]);
            log.Info("Opening asset at " + assetPath + ":" + line);
            int instanceId = AssetDatabase.LoadMainAssetAtPath(assetPath).GetInstanceID();
            AssetDatabase.OpenAsset(instanceId, line);
        }
    }

    public void OnDisconnec(){
        log.Info("Disconnected from Watchdog");
        recvTimer.Disable();
        pingTimer.Disable();
        reconnectTimer.Enable();

    }

    public void Command(string cmd, params object[] args){
        foreach (object arg in args) cmd += " " + arg.ToString();
        sock.Send(Encoding.ASCII.GetBytes(cmd + "\r\n"));
        if (cmd != "PING"){
            log.Debug("C> " + cmd);
        }
    }


}


public class Logger {
    public const int DEBUG = 0;
    public const int INFO = 1;
    public const int WARN = 2;
    public const int ERROR = 3;
    public const int QUET = 4;

    public int level = DEBUG;

    public Logger(int level=DEBUG){
        this.level = level;
    }

    public void Log(int level, string message){
        if (level >= this.level){
            UnityEngine.Debug.Log(message);
        }
    }

    public void Debug(string message){ 
        Log(DEBUG, message);
    }

    public void Info(string message){
        Log(INFO, message);
    }

    public void Warn(string message){
        Log(WARN, message);
    }

    public void Error(string message){
        Log(ERROR, message);
    }
}

public class Timer {
    private float period = 1;
    private float nextTick;

    public Timer(float period=1){
        this.period = period;
        this.nextTick = float.MaxValue;
    }
    
    public bool Tick(){
        if (Time.realtimeSinceStartup > nextTick){
            nextTick += period;
            return true;
        } else {
            return false;
        }
    }

    public void Disable(){
        nextTick = float.MaxValue;
    }

    public void Enable(){
        nextTick = Time.realtimeSinceStartup;
    }
}

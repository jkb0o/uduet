using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;

using SuperSocket.Common;
using SuperSocket.SocketBase;
using SuperSocket.SocketBase.Config;
using SuperSocket.SocketBase.Protocol;
using SuperSocket.SocketEngine;


namespace UDuet {

public class Watchdog {

    public class Location {
        public static string Home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "uduet");
        public static string Slaves = Path.Combine(Home, "slave");
        public static string Socket = Path.Combine(Home, "socketfile");
        public static string Log = Path.Combine(Home, "watchdog.log");
        public static string Pid = Path.Combine(Home, "pidfile");
    }

    static void Main(string[] args){
        HandleEmbeddedResources();
        if (args.Length == 0){
            ShowUsage();
            return;
        }
        string action = args[args.Length-1];
        Watchdog app = Watchdog.instance;
        switch (action){
            case "job":
                app.Start();
                break;
            case "start":
                app.Daemonize();
                break;
            case "stop":
                app.Stop();
                break;
            case "status":
                app.ShowStatus();
                break;
            default:
                ShowUsage();
                break;
        }
    }
    
    public ManualResetEvent stopEvent = new ManualResetEvent(false);
    private static Watchdog _instance;
    public static Watchdog instance {
        get {
            if (_instance != null) return _instance;
            _instance = new Watchdog();
            return _instance;
        }
    }

    public static void ShowUsage(){
        Console.WriteLine("Usage:");
        Console.WriteLine("    mono watchdog.exe start|stop|status|job");
    }

    /*
     * Jeffrey Richter's recipe for embedding dlls into single exe as resources
     * http://blogs.msdn.com/b/microsoft_press/archive/2010/02/03/jeffrey-richter-excerpt-2-from-clr-via-c-third-edition.aspx
     */
    public static void HandleEmbeddedResources(){
        AppDomain.CurrentDomain.AssemblyResolve += (sender, bargs) =>
        {
            String dllName = new AssemblyName(bargs.Name).Name + ".dll";
            var assem = Assembly.GetExecutingAssembly();
            String resourceName = assem.GetManifestResourceNames().FirstOrDefault(rn => rn.EndsWith(dllName));
            if (resourceName == null) return null; // Not found, maybe another handler will find it
            using (var stream = assem.GetManifestResourceStream(resourceName))
            {
                Byte[] assemblyData = new Byte[stream.Length];
                stream.Read(assemblyData, 0, assemblyData.Length);
                return Assembly.Load(assemblyData);
            }
        };
    }

    public void Daemonize(){
        Pidfile pidfile = new Pidfile();
        if (pidfile.IsValid()){
            Console.WriteLine("Process already started");
            return;
        }
        string mono = Environment.GetEnvironmentVariable("_");
        string app = Environment.GetCommandLineArgs()[0];

        ProcessStartInfo startInfo = new ProcessStartInfo(mono, app + " " + "job");
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        Process.Start(startInfo);
        Thread.Sleep(500);
    }

    public void Stop(){
        Pidfile pidfile = new Pidfile();
        if (pidfile.IsValid() && File.Exists(Location.Socket)){
            int port = Convert.ToInt32(File.ReadAllText(Location.Socket));
            Socket sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            sock.NoDelay = true;
            sock.Blocking = true;
            sock.Connect("localhost", port);
            sock.Send(Encoding.ASCII.GetBytes("STOP\r\n"));
            byte[] buff = new byte[64]; 
            sock.Receive(buff);
            Thread.Sleep(500);
            sock.Close();
            pidfile.Delete();
            Console.WriteLine("Watchdog stopped");
        } else {
            Console.WriteLine("No watchdog is currently running");
        }
    }

    public void ShowStatus(){
        Pidfile pidfile = new Pidfile();
        if (pidfile.IsValid()){
            Console.WriteLine("Watchdog is up and running");
        } else {
            Console.WriteLine("Watchdog is down");
        }
    }

    public void Start(){
        Logger.InitLogger();

        int port = FindAvaiblePort();
        File.WriteAllText(Location.Socket, port.ToString());
        WatchdogServer server = new WatchdogServer();
        if (!server.Setup(port)){
            Logger.log.Error("Failed to setup server.");
            return;
        }

        //Try to start the server
        if (!server.Start())
        {
            Logger.log.Error("Failed to start server.");
            return;
        }

        Logger.log.Info("Watchdog started");
        Pidfile pidfile = new Pidfile(Process.GetCurrentProcess().Id);
        TimeSpan timeout = TimeSpan.FromSeconds(3);
        stopEvent.Reset();

        try {
            do { pidfile.Save(); } while (!stopEvent.WaitOne(timeout));
            Thread.Sleep(100);
            //server.Stop();
            Logger.log.Debug("Stopping watchdog...");
            foreach (Project proj in ProjectManager.instance.GetProjects()){
                proj.Clear();
            }
            if (Directory.Exists(Location.Slaves)){
                Directory.Delete(Location.Slaves, true);
            }
            Logger.log.Info("Watchdog stopped");
        } catch (Exception e){
            Logger.log.Error("Error while stopping watchdog", e);
        }
    }

    private int FindAvaiblePort(){
        Socket sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        sock.Bind(new IPEndPoint(IPAddress.Loopback, 0)); 
        int port = ((IPEndPoint)sock.LocalEndPoint).Port;
        sock.Close();
        return port;
    }
}

public class ProjectSession: AppSession<ProjectSession> {
    public Project project;

    protected override void OnSessionStarted(){
        UDuet.Logger.log.Debug("Session started");
        this.Send("HELLO");
    }

    protected override void HandleUnknownRequest(StringRequestInfo requestInfo){
        UDuet.Logger.log.Warn("Unknown request: " + requestInfo.Key + " " + requestInfo.Body);
        this.Send("BAD_REQUEST");
    }

    protected override void HandleException(Exception e){
        UDuet.Logger.log.Error("Error while processing request " + CurrentCommand, e);
    }

    protected override void OnSessionClosed(CloseReason reason){
        //add you logics which will be executed after the session is closed
        UDuet.Logger.log.Debug("Session closed " + reason);
        base.OnSessionClosed(reason);
    }

    public override void Send(string message){
        base.Send(message + "\r\n");
    }
}

}

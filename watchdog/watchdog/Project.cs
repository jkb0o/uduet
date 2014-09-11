using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Threading;


namespace UDuet {


public class ProjectManager {
    
    private Dictionary<string, Project> projects;

    private static ProjectManager _instance;
    public static ProjectManager instance {
        get {
            if (_instance != null) return _instance;
            _instance = new ProjectManager();
            return _instance;
        }
    }

    public ProjectManager(){
        projects = new Dictionary<string, Project>();
    }

    public Project RegisterProject<TProject>(string root) where TProject: Project, new() {
        if (!root.EndsWith(Path.DirectorySeparatorChar.ToString())){
            root += Path.DirectorySeparatorChar.ToString();
        }
        if (projects.ContainsKey(root)) return projects[root];
        Project proj = new TProject();
        proj.Register(root);
        projects[root] = proj;
        return proj;
    }

    public Project GetProject(string root){
        return projects[root];
    }

    public Project[] GetProjects(){
        Project[] result = new Project[projects.Count];
        projects.Values.CopyTo(result, 0);
        return result;
    }

    public bool Clear(string root){
        if (projects.ContainsKey(root)){
            projects.Remove(root);
            return true;
        } else {
            return false;
        }
    }
}

public abstract class Project {
    public string root = "";
    public ProjectSession session;
    private DateTime cleanAt = DateTime.Now;
    private Thread clearTask;

    public virtual void Register(string root){
        this.root = root;
    }

    public void Ping(){
        cleanAt = DateTime.Now + TimeSpan.FromSeconds(10);
    }

    public void ClearTask(){
        while(true){
            if (DateTime.Now > cleanAt){
                Clear();
                break;
            }
            Thread.Sleep(5000);
        }
    }
    
    public void Associate(ProjectSession session){
        if (this.session != null) this.session.Close();
        this.session = session;

        Ping();
        if (clearTask == null){
            clearTask = new Thread(new ThreadStart(ClearTask));
            clearTask.Start();
        }
        
    }
    
    public IEnumerable<string> ListFiles(string path){
        foreach (string file in Directory.GetFiles(path)){
            yield return file;
        }
        foreach (string dir in Directory.GetDirectories(path)){
            foreach (string subfile in ListFiles(dir)){
                yield return subfile;
            }
        }
    }
    public IEnumerable<string> ListDirectories(string path){
        foreach (string dir in Directory.GetDirectories(path)){
            yield return dir;
            foreach (string subdir in ListDirectories(dir)){
                yield return subdir;
            }
        }
    }

    public virtual void Clear(){
        Logger.log.Debug("Project.Clear()");
        clearTask.Abort();
        ProjectManager.instance.Clear(root);
    }
    public abstract MasterProject GetMaster();

}

public class SlaveProject: Project {
    public MasterProject master;

    private int unityPid;
    private string unityApp = "/Applications/Unity4.5/Unity4.5.app/Contents/MacOS/Unity";

    public override MasterProject GetMaster(){
        return master;
    }

    public void Run(){
        string args = "-projectPath " + root;
        ProcessStartInfo procInfo = new ProcessStartInfo(unityApp, args);
        procInfo.UseShellExecute = false;
        Process process = Process.Start(procInfo);
        unityPid = process.Id;
    }

    public override void Clear(){
        Logger.log.Debug("SlaveProject.Clear()");
        master = null;
        Logger.log.Info("Deleting slave at " + root);
        if (unityPid != 0){
            try {
                Process.GetProcessById(unityPid).Kill();
                unityPid = 0;
            } 
            catch (ArgumentException){}
            catch (InvalidOperationException){}
        }
        if (Directory.Exists(root)) Directory.Delete(root, true);
        base.Clear();
    }
}

public class MasterProject: Project {

    List<SlaveProject> slaves;
    Watcher watcher;


    public MasterProject(){
        slaves = new List<SlaveProject>();
    }

    public override MasterProject GetMaster(){
        return this;
    }

    public override void Register(string root){
        base.Register(root);
        watcher = new Watcher(root);
        watcher.OnChanges += OnFileChanged;
        watcher.OnError += (s, args) => Logger.log.Error("Error in while watching filesystem", args.error);


        string dontsync = Path.Combine(root, ".dontsync");
        if (File.Exists(dontsync)){
            foreach (string pattern in File.ReadAllLines(dontsync)){
                watcher.AddFilter(pattern);
            }
        } else {
            watcher.AddFilter("*.meta");
            watcher.AddFilter("Library/*");
            watcher.AddFilter("Assembly-*");
            watcher.AddFilter("*.sw?");
            watcher.AddFilter("*.sln");
            watcher.AddFilter("Temp/*");
        }
        
        watcher.BeginWatch();
    }

    public void OnFileChanged(object sender, FSEventArgs args){
        if (args.path.EndsWith(Path.DirectorySeparatorChar.ToString())){
            SyncDirectory(args.path);
        } else {
            SyncFile(args.path);
        }
    }

    public void SyncFile(string path){
        string fullPath = Path.Combine(root, path);
        bool exists = File.Exists(fullPath);
        foreach (SlaveProject slave in slaves){
            string targetPath = Path.Combine(slave.root, path);
            string dirPath = Path.GetDirectoryName(targetPath);
            if (exists){
                Logger.log.Debug("Sync proj [" + root + "] => +"  + path);
                Directory.CreateDirectory(dirPath);
                File.Copy(fullPath, targetPath, true);
            } else if (File.Exists(targetPath)){
                Logger.log.Debug("Sync proj [" + root + "] => -"  + path);
                File.Delete(targetPath);
            } else {
                Logger.log.Warn("Sync proj [" + root + "] => *" + path);
            }
        }
    }

    public void SyncDirectory(string path){
        string fullPath = Path.Combine(root, path);
        bool exists = Directory.Exists(fullPath);
        foreach (SlaveProject slave in slaves){
            string targetPath = Path.Combine(slave.root, path);
            if (exists){
                Logger.log.Debug("Sync proj [" + root + "] => +"  + path);
                Directory.CreateDirectory(targetPath);
            } else if (Directory.Exists(targetPath)){
                Logger.log.Debug("Sync proj [" + root + "] => -"  + path);
                Directory.Delete(targetPath, true);
            } else {
                Logger.log.Warn("Sync proj [" + root + "] => *" + path);
            }
        }
    }

    public Project CreateSlave(){
        string slave = Path.Combine(Watchdog.Location.Slaves, Path.GetRandomFileName());
        Directory.CreateDirectory(slave);

        SlaveProject proj = ProjectManager.instance.RegisterProject<SlaveProject>(slave) as SlaveProject;
        proj.master = this;

        foreach (string file in watcher.Content(false)){
            string relPath = file.Substring(root.Length);
            Logger.log.Debug("Create slave [" + root + "] "  + relPath);
            string targetPath = Path.Combine(slave, relPath);
            if (file.EndsWith(Path.DirectorySeparatorChar.ToString())){
                Directory.CreateDirectory(targetPath);
            } else {
                string dirPath = Path.GetDirectoryName(targetPath);
                Directory.CreateDirectory(dirPath);
                File.Copy(file, targetPath, true);
            }
        }

        File.Create(Path.Combine(slave, ".uduet.slave"));

        slaves.Add(proj);
        proj.Run();
        return proj;
    }


    public override void Clear(){
        Logger.log.Debug("MasterProject.Clear()");
        watcher.StopWatch();
        foreach (SlaveProject proj in slaves){
            proj.Clear();
        }
        slaves.Clear();
        base.Clear();
    }
}
}

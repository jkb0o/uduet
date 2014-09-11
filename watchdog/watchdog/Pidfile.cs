using System;
using System.IO;
using System.Diagnostics;

namespace UDuet {

class Pidfile {

    private bool _loaded = false;
    private int _pid = 0;
    public int pid {
        get {
            if (_loaded) return _pid;
            Load();
            return _pid;
        }
        set {
            _pid = value;
        }
    }


    public Pidfile(int pid=2){
        _pid = pid;
    }

    public void Load(){
        if (_loaded) return;
        _loaded = true;
        _pid = Convert.ToInt32(File.ReadAllText(Watchdog.Location.Pid));
    }

    public void Save(){
        _loaded = true;
        Directory.CreateDirectory(Path.GetDirectoryName(Watchdog.Location.Pid));
        File.WriteAllText(Watchdog.Location.Pid, _pid.ToString());
    }

    public void Delete(){
        if (File.Exists(Watchdog.Location.Pid)){
            File.Delete(Watchdog.Location.Pid);
        }
    }

    public bool IsValid(){
        if (!File.Exists(Watchdog.Location.Pid)) return false;
        if ((DateTime.Now - File.GetCreationTime(Watchdog.Location.Pid)).Milliseconds > 5000) return false;
        try {
            Process.GetProcessById(pid);
        } catch (ArgumentException){
            return false;
        }
        return true;
    }
}
}

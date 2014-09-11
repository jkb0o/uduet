using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Text.RegularExpressions;

namespace UDuet {


    public class FSEventArgs: EventArgs {
        public FSEventArgs(string path){
            this.path = path;
        }
        public string path;
    }

    public class FSErrorEventArgs: EventArgs {
        public FSErrorEventArgs(Exception e){
            this.error = e;
        }
        public Exception error;
    }
    


    public class Watcher {

        public string root;
        public int checkInterval = 1000;
        public event EventHandler<FSEventArgs> OnChanges;
        public event EventHandler<FSErrorEventArgs> OnError;

        List<Regex> filters;
        HashSet<string> filteredContent;
        Dictionary<string, DateTime> content;
        Thread watchThread;

        public Watcher(string root=""){
            this.root = root;
            filters = new List<Regex>();
            filteredContent = new HashSet<string>();
            content = new Dictionary<string, DateTime>();
        }

        ~Watcher(){
            StopWatch();
        }

        public void BeginWatch(){
            if (watchThread != null) return;
            foreach (string path in Content(root)){
                if (path.EndsWith(Path.DirectorySeparatorChar.ToString())){
                    content[path] = Directory.GetLastWriteTime(path);
                } else {
                    content[path] = File.GetLastWriteTime(path);
                }
                Logger.log.Debug("Watching " + path);
            }
            watchThread = new Thread(new ThreadStart(Watch));
            watchThread.Start();
        }

        public void StopWatch(){
            if (watchThread == null) return;
            watchThread.Abort();
            watchThread.Join();
            watchThread = null;
        }

        public bool Filter(string relPath){
            if (relPath.StartsWith(root)){
                relPath = relPath.Substring(root.Length);
            }
            if (filteredContent.Contains(relPath)) return false;
            foreach (Regex re in filters){
                if (re.IsMatch(relPath)){
                    filteredContent.Add(relPath);
                    return false;
                }
            }
            return true;
        }

        public IEnumerable<string> Content(bool filter=false){
            foreach (string path in Content(root, filter)){
                yield return path;
            }
        }

        private IEnumerable<string> Content(string path, bool filter=true){
            if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
                path += Path.DirectorySeparatorChar.ToString();

            string relPath = path.Substring(root.Length);
            if (!filter || Filter(relPath)){
                yield return path;

                foreach (string file in Directory.GetFiles(path)){
                    string relFile = file.Substring(root.Length);
                    if (filter && !Filter(relFile)) continue;
                    yield return file;
                }

                foreach (string dir in Directory.GetDirectories(path)){
                    foreach (string subfile in Content(dir, filter)){
                        yield return subfile;
                    }
                }
            }
        }

        public void AddFilter(string pattern){
            pattern = pattern.Trim();
            if (pattern.StartsWith("#")) return;
            if (pattern == "") return;
            filters.Add(new Regex(
                "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.Singleline
            ));
        }

        public void Watch(){
            while (true){
                Thread.Sleep(checkInterval);
                try {
                    Dictionary<string, DateTime> newContent = new Dictionary<string, DateTime>();
                    List<FSEventArgs> changed = new List<FSEventArgs>();
                    foreach (string path in Content(root)){
                        string relPath = path.Substring(root.Length);
                        DateTime modified;
                        if (path.EndsWith(Path.DirectorySeparatorChar.ToString())){
                            modified = Directory.GetLastWriteTime(path);
                        } else {
                            modified = File.GetLastWriteTime(path);
                        }
                        newContent[path] = modified;
                        if (content.ContainsKey(path)){
                            if (content[path] != modified) changed.Add(new FSEventArgs(relPath)); //changed
                            content.Remove(path); 
                        } else {
                            changed.Add(new FSEventArgs(relPath)); //created
                        }
                    }
                    foreach(string path in content.Keys){
                        string relPath = path.Substring(root.Length);
                        changed.Add(new FSEventArgs(relPath)); //deleted
                    }
                    content = newContent;

                    EventHandler<FSEventArgs> handler = OnChanges;
                    foreach( FSEventArgs args in changed){
                        handler(this, args);
                    }
                } catch (Exception e){
                    EventHandler<FSErrorEventArgs> handler = OnError;
                    handler(this, new FSErrorEventArgs(e));
                    throw e;
                }
            }
        }

    }
}

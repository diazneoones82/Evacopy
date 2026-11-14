// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 diazneoones82
using System;
using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Web.Script.Serialization;

public class JobSpec {
    public string Id, Destination, CopyDestination;
    public string[] Sources;
    public bool DeleteSource, IncludeSourceFolder, VerifyCrc;
    public int Threads=32;
    public string Priority="High", Operation="Copy", CopyMode="Standard";
}
public class JobState {
    public string Id, Title, Phase="Queued", Detail="", Error="";
    public double Percent, Speed;
    public int WorkerPid, CopyPid;
    public bool Finished, Successful;
    public string TransferMethod="Robocopy";public long ProgressMetadataProbes;
    public long Sequence, CrcBytesRead;
}
public class Request { public string Command; public JobSpec Job; public string Id; }
public class Reply { public string Error; public JobState[] Jobs; public string Id; public int ServicePid, ProtocolVersion; }
public class WorkerIdentity { public int Pid; public long Started; public string Executable; }
static class Store {
    public static readonly string Root=Environment.GetEnvironmentVariable("EVACOPY_PROFILE")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Evacopy","Jobs");
    public static readonly string Pipe="Evacopy.Service.v2."+System.Security.Principal.WindowsIdentity.GetCurrent().User.Value.Replace('-','_')+"."+ProfileId();
    static string ProfileId(){using(var h=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Root.ToUpperInvariant()))).Replace("-","").Substring(0,12);}
    public static string Exe { get {return System.Reflection.Assembly.GetExecutingAssembly().Location;} }
    public static string Json(object value){return new JavaScriptSerializer().Serialize(value);}
    public static T Read<T>(string path){using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(file))return new JavaScriptSerializer().Deserialize<T>(reader.ReadToEnd());}
    public static T Parse<T>(string value){return new JavaScriptSerializer().Deserialize<T>(value);}
    public static string PathFor(string id,string extension){Guid.ParseExact(id,"N");return Path.Combine(Root,id+extension);}
    public static void Write(string path,object value){Directory.CreateDirectory(Root);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temp,Json(value));try{if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
    public static Reply Send(Request request){using(var pipe=new NamedPipeClientStream(".",Pipe,PipeDirection.InOut,PipeOptions.Asynchronous)){pipe.Connect(2500);using(var writer=new StreamWriter(pipe,new System.Text.UTF8Encoding(false),4096,true){AutoFlush=true})using(var reader=new StreamReader(pipe,System.Text.Encoding.UTF8,true,4096,true)){writer.WriteLine(Json(request));var read=reader.ReadLineAsync();if(!read.Wait(request.Command=="exit"?20000:5000))throw new TimeoutException("Evacopy service response timed out.");var line=read.Result;if(line==null)throw new IOException("Service disconnected.");return Parse<Reply>(line);}}}
    public static void EnsureService(){
        Reply current=null;try{current=Send(new Request{Command="ping"});}catch(IOException){}catch(TimeoutException){}
        if(current!=null){if(current.ProtocolVersion>=8)return;var shutdown=Send(new Request{Command="shutdown"});if(shutdown.Error!=null)throw new IOException("Finish the running jobs in the older Evacopy version before starting this update.");Thread.Sleep(200);}
        Process.Start(new ProcessStartInfo(Exe,"--service"){UseShellExecute=false,CreateNoWindow=true});for(int i=0;i<30;i++){try{if(Send(new Request{Command="ping"}).ProtocolVersion>=8)return;}catch(IOException){}catch(TimeoutException){}Thread.Sleep(100);}throw new IOException("Evacopy service did not start.");
    }
    static bool IsEvacopy(Process process,string executable){string actual=process.MainModule.FileName;return executable!=null?String.Equals(actual,executable,StringComparison.OrdinalIgnoreCase):String.Equals(Path.GetFileName(actual),Path.GetFileName(Exe),StringComparison.OrdinalIgnoreCase)||String.Equals(Path.GetFileName(actual),"Evacopy.exe",StringComparison.OrdinalIgnoreCase);}
    internal static Process RecordedProcess(string path){
        try{var identity=Read<WorkerIdentity>(path);var process=Process.GetProcessById(identity.Pid);if(!process.HasExited&&process.StartTime.ToUniversalTime().Ticks==identity.Started&&IsEvacopy(process,identity.Executable))return process;process.Dispose();}
        catch(ArgumentException){}catch(InvalidOperationException){}catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
        return null;
    }
    public static void StopAll(){
        Process service=null;
        try{var ping=Send(new Request{Command="ping"});service=Process.GetProcessById(ping.ServicePid);if(!IsEvacopy(service,null)){service.Dispose();service=null;}}
        catch(IOException){}catch(TimeoutException){}catch(ArgumentException){}
        try{
            try{var reply=Send(new Request{Command="exit"});if(reply.Error==null){if(service==null||service.WaitForExit(5000))return;}}
            catch(IOException){}catch(TimeoutException){}
            // Recover workers even when the service is unavailable or from an older release.
            var workers=new List<Process>();
            try{
                if(Directory.Exists(Root))foreach(var path in Directory.GetFiles(Root,"*.process")){string id=Path.GetFileNameWithoutExtension(path);Guid parsed;if(!Guid.TryParseExact(id,"N",out parsed))continue;var worker=RecordedProcess(path);if(worker==null)continue;workers.Add(worker);try{File.WriteAllText(PathFor(id,".cancel"),"");}catch(IOException){}}
                var deadline=DateTime.UtcNow.AddSeconds(5);while(workers.Any(p=>!p.HasExited)&&DateTime.UtcNow<deadline)Thread.Sleep(100);
                foreach(var worker in workers)if(!worker.HasExited){worker.Kill();if(!worker.WaitForExit(5000))throw new IOException("A copy worker could not be stopped.");}
            }finally{foreach(var worker in workers)worker.Dispose();}
            if(service==null)service=RecordedProcess(Path.Combine(Root,"service.process"));
            if(service!=null&&!service.HasExited){service.Kill();if(!service.WaitForExit(5000))throw new IOException("The background service could not be stopped.");}
        }finally{if(service!=null)service.Dispose();}
    }
}
static class JobPaths {
    public static string FolderDestination(string source,string destination,bool includeFolder){
        string target=Path.GetFullPath(destination);if(!includeFolder)return target;
        string name=new DirectoryInfo(Path.GetFullPath(source)).Name;
        if(String.IsNullOrEmpty(name)||String.Equals(App.Full(source),App.Full(Path.GetPathRoot(source)),StringComparison.OrdinalIgnoreCase))throw new IOException("A drive root has no source folder name. Disable 'Keep source folder name' to copy its contents.");
        if(String.Equals(new DirectoryInfo(target).Name,name,StringComparison.OrdinalIgnoreCase))return target;
        return Path.Combine(target,name);
    }
    public static string Target(JobSpec spec){return spec.CopyDestination??spec.Destination;}
}
static class Kernel {
    static Dictionary<string,Process> workers=new Dictionary<string,Process>();
    static Dictionary<string,JobSpec> specifications=new Dictionary<string,JobSpec>();
    static bool stopping;
    public static int Run(){bool owned;using(var mutex=new Mutex(true,@"Local\"+Store.Pipe,out owned)){if(!owned)return 0;Directory.CreateDirectory(Store.Root);Store.Write(Path.Combine(Store.Root,"service.process"),new WorkerIdentity{Pid=Process.GetCurrentProcess().Id,Started=Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,Executable=Store.Exe});
        // A broker restart never silently resumes an interrupted move.
        foreach(var path in Directory.GetFiles(Store.Root,"*.state")){try{var s=Store.Read<JobState>(path);if(!s.Finished){
            try{var identity=Store.Read<WorkerIdentity>(Store.PathFor(s.Id,".process"));var running=Process.GetProcessById(identity.Pid);if(!running.HasExited&&running.StartTime.ToUniversalTime().Ticks==identity.Started&&String.Equals(running.MainModule.FileName,Store.Exe,StringComparison.OrdinalIgnoreCase)){workers.Add(s.Id,running);specifications.Add(s.Id,Store.Read<JobSpec>(Store.PathFor(s.Id,".job")));continue;}}catch{}
            s.Finished=true;s.Phase="Interrupted";s.Error="Service restarted; inspect destination before retrying.";Store.Write(path,s);}}catch{}}
        while(!stopping){var security=new PipeSecurity();security.SetAccessRuleProtection(true,false);security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));
            using(var pipe=new NamedPipeServerStream(Store.Pipe,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,security)){
                var pending=pipe.BeginWaitForConnection(null,null);if(!pending.AsyncWaitHandle.WaitOne(TimeSpan.FromMinutes(10))){if(workers.Values.All(p=>p.HasExited))break;pending.AsyncWaitHandle.WaitOne();}pipe.EndWaitForConnection(pending);
                using(var reader=new StreamReader(pipe,System.Text.Encoding.UTF8,true,4096,true))using(var writer=new StreamWriter(pipe,new System.Text.UTF8Encoding(false),4096,true){AutoFlush=true}){try{var read=reader.ReadLineAsync();if(!read.Wait(3000))continue;writer.WriteLine(Store.Json(Handle(Store.Parse<Request>(read.Result))));}catch(Exception ex){try{writer.WriteLine(Store.Json(new Reply{Error=ex.Message}));}catch{}}}
            }
        }foreach(var p in workers.Values)p.Dispose();File.Delete(Path.Combine(Store.Root,"service.process"));mutex.ReleaseMutex();}return 0;
    }
    static bool Overlap(string a,string b){return App.Inside(a,b)||App.Inside(b,a);}
    static IEnumerable<string> SourceRoots(JobSpec spec){return spec.Sources;}
    static void Validate(JobSpec spec){
        if(spec==null||spec.Sources==null||spec.Sources.Length==0)throw new IOException("Choose a source.");
        if(spec.Operation!="Copy"&&spec.Operation!="Move")throw new IOException("Choose Copy or Move.");if(spec.Operation=="Move")spec.DeleteSource=true;if(spec.DeleteSource)spec.Operation="Move";
        spec.Destination=Path.GetFullPath(spec.Destination);App.SafePath(spec.Destination);
        if(spec.Threads<1||spec.Threads>128)throw new IOException("Threads must be between 1 and 128.");
        if(spec.CopyMode!="Standard"&&spec.CopyMode!="Large files")throw new IOException("Choose Standard or Large files I/O.");
        if(!new[]{"Normal","AboveNormal","High"}.Contains(spec.Priority))throw new IOException("Choose a supported priority.");
        spec.Sources=spec.Sources.Select(Path.GetFullPath).ToArray();
        bool folder=spec.Sources.Length==1&&Directory.Exists(spec.Sources[0]);
        if(spec.IncludeSourceFolder&&!folder)throw new IOException("Keep source folder name requires a single source folder.");
        spec.CopyDestination=folder?JobPaths.FolderDestination(spec.Sources[0],spec.Destination,spec.IncludeSourceFolder):spec.Destination;
        App.SafePath(spec.CopyDestination);
        foreach(var s in spec.Sources){bool directory=Directory.Exists(s);if(!directory&&!File.Exists(s))throw new IOException("Source not found: "+s);if(directory?Overlap(s,spec.CopyDestination):String.Equals(App.Full(s),App.Full(Path.Combine(spec.CopyDestination,Path.GetFileName(s))),StringComparison.OrdinalIgnoreCase))throw new IOException("Source and destination must not overlap.");}
        foreach(var pair in specifications){Process running;if(!workers.TryGetValue(pair.Key,out running)||running.HasExited)continue;var other=pair.Value;
            if(Overlap(JobPaths.Target(spec),JobPaths.Target(other))||SourceRoots(other).Any(s=>Overlap(s,JobPaths.Target(spec)))||SourceRoots(spec).Any(s=>Overlap(s,JobPaths.Target(other)))||((spec.DeleteSource||other.DeleteSource)&&SourceRoots(spec).Any(s=>SourceRoots(other).Any(o=>Overlap(s,o)))))throw new IOException("This job overlaps an active copy or move. Choose separate folders or wait.");
        }
    }
    static JobState[] States(){var result=new List<JobState>();foreach(var path in Directory.GetFiles(Store.Root,"*.state")){try{var state=Store.Read<JobState>(path);Process p;if(!state.Finished&&workers.TryGetValue(state.Id,out p)&&p.HasExited){state.Finished=true;state.Phase="Failed";state.Error="Worker exited unexpectedly ("+p.ExitCode+"). Destination may be incomplete.";Store.Write(path,state);}result.Add(state);}catch(IOException){}}return result.OrderByDescending(s=>s.Id).ToArray();}
    static void StopWorkers(){
        foreach(var pair in workers)if(!pair.Value.HasExited)try{File.WriteAllText(Store.PathFor(pair.Key,".cancel"),"");}catch(IOException){}
        var deadline=DateTime.UtcNow.AddSeconds(5);while(workers.Values.Any(p=>!p.HasExited)&&DateTime.UtcNow<deadline)Thread.Sleep(100);
        foreach(var pair in workers){if(pair.Value.HasExited)continue;pair.Value.Kill();if(!pair.Value.WaitForExit(5000))throw new IOException("Could not stop worker "+pair.Key);var state=Store.Read<JobState>(Store.PathFor(pair.Key,".state"));state.Finished=true;state.Successful=false;state.CopyPid=0;state.Phase="Cancelled";state.Error="Evacopy exited. The worker was terminated after cancellation timed out; partial destination files may remain.";Store.Write(Store.PathFor(pair.Key,".state"),state);}
    }
    static Reply Handle(Request request){
        switch(request.Command){
        case "ping": return new Reply{ServicePid=Process.GetCurrentProcess().Id,ProtocolVersion=8};
        case "list": return new Reply{Jobs=States()};
        case "submit":
            Validate(request.Job);var spec=request.Job;spec.Id=Guid.NewGuid().ToString("N");Store.Write(Store.PathFor(spec.Id,".job"),spec);
            Store.Write(Store.PathFor(spec.Id,".state"),new JobState{Id=spec.Id,Title=spec.Operation+" · "+Path.GetFileName(spec.Sources[0].TrimEnd('\\'))});
            var p=Process.Start(new ProcessStartInfo(Store.Exe,"--worker "+spec.Id){UseShellExecute=false,CreateNoWindow=true});workers.Add(spec.Id,p);specifications.Add(spec.Id,spec);Store.Write(Store.PathFor(spec.Id,".process"),new WorkerIdentity{Pid=p.Id,Started=p.StartTime.ToUniversalTime().Ticks,Executable=Store.Exe});return new Reply{Id=spec.Id};
        case "cancel": if(workers.ContainsKey(request.Id)&&!workers[request.Id].HasExited)File.WriteAllText(Store.PathFor(request.Id,".cancel"),"");return new Reply();
        case "exit": StopWorkers();stopping=true;return new Reply{ServicePid=Process.GetCurrentProcess().Id};
        case "shutdown": if(workers.Values.Any(worker=>!worker.HasExited))throw new IOException("Jobs still running.");stopping=true;return new Reply();
        default: throw new IOException("Unknown service command.");
        }
    }
}

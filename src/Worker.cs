// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 diazneoones82
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

static class Worker {
    static JobSpec spec; static JobState state;
    static CancellationTokenSource cancel=new CancellationTokenSource();
    static Stopwatch timer=new Stopwatch();
    static List<Entry> entries=new List<Entry>();
    static bool verifyCrc;static long total, verified; static DateTime lastSave=DateTime.MinValue;
    static void Save(bool force=false){if(!force&&(DateTime.UtcNow-lastSave).TotalMilliseconds<250)return;state.Sequence++;Store.Write(Store.PathFor(spec.Id,".state"),state);lastSave=DateTime.UtcNow;}
    static void Check(){if(File.Exists(Store.PathFor(spec.Id,".cancel")))cancel.Cancel();cancel.Token.ThrowIfCancellationRequested();}
    static void Phase(string text){Check();state.Phase=text;Save(true);}
    static uint Crc(string file){using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))return Crc(stream);}
    static uint Crc(Stream stream){uint crc=0xffffffff;byte[] buffer=new byte[1024*1024];int read;while((read=stream.Read(buffer,0,buffer.Length))>0){Check();state.CrcBytesRead+=read;for(int i=0;i<read;i++)crc=App.Table[(crc^buffer[i])&255]^(crc>>8);}return crc^0xffffffff;}
    public static int Run(string id){
        spec=Store.Read<JobSpec>(Store.PathFor(id,".job"));state=Store.Read<JobState>(Store.PathFor(id,".state"));state.WorkerPid=Process.GetCurrentProcess().Id;string destination=JobPaths.Target(spec);verifyCrc=spec.VerifyCrc||spec.DeleteSource;
        try{
            try{Process.GetCurrentProcess().PriorityClass=(ProcessPriorityClass)Enum.Parse(typeof(ProcessPriorityClass),spec.Priority);}catch{state.Detail="Windows denied requested worker priority.";}
            Phase("Scanning");var directories=new List<string>();bool folder=spec.Sources.Length==1&&Directory.Exists(spec.Sources[0]);
            if(folder)App.Enumerate(spec.Sources[0],App.Full(spec.Sources[0]),destination,entries,directories);
            else foreach(var file in spec.Sources){Check();App.SafePath(Path.GetDirectoryName(file));if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked files are not supported.");entries.Add(new Entry{Source=file,Target=Path.Combine(destination,Path.GetFileName(file)),Size=new FileInfo(file).Length});}
            if(entries.Select(e=>e.Target).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=entries.Count)throw new IOException("Duplicate destination filenames.");
            total=entries.Sum(e=>e.Size);Phase(verifyCrc?"Recording source CRC-32":"Preparing fast copy");
            for(int i=0;i<entries.Count;i++){var e=entries[i];Check();if(verifyCrc)e.Crc=Crc(e.Source);App.SafePath(Path.GetDirectoryName(e.Target));if(File.Exists(e.Target)&&(File.GetAttributes(e.Target)&FileAttributes.ReparsePoint)!=0)throw new IOException("Destination is a linked file.");var prior=new FileInfo(e.Target);if(prior.Exists){e.OriginalTargetTicks=prior.LastWriteTimeUtc.Ticks;e.OriginalTargetSize=prior.Length;}state.Percent=100.0*(i+1)/Math.Max(1,entries.Count);Save();}
            App.SafePath(destination);Directory.CreateDirectory(destination);timer.Start();state.Percent=0;Phase("Copying (estimated)");
            if(folder)Copy(spec.Sources[0],destination,"/E");else CopyFiles(destination);
            state.CopyPid=0;state.Percent=0;Phase(verifyCrc?"Verifying CRC-32":"Checking file sizes");
            for(int i=0;i<entries.Count;i++){var e=entries[i];Check();if(new FileInfo(e.Target).Length!=e.Size||(verifyCrc&&(new FileInfo(e.Source).Length!=e.Size||Crc(e.Source)!=e.Crc||Crc(e.Target)!=e.Crc)))throw new IOException("Copy verification failed: "+e.Source+". Source deletion has not started.");verified+=e.Size;state.Percent=total==0?100.0*(i+1)/entries.Count:100.0*verified/total;state.Speed=verified/1048576.0/Math.Max(.1,timer.Elapsed.TotalSeconds);state.Detail=Path.GetFileName(e.Source);Save();}
            if(spec.DeleteSource){Phase("Deleting verified source");foreach(var e in entries){Check();App.SafePath(Path.GetDirectoryName(e.Source));App.SafePath(Path.GetDirectoryName(e.Target));using(var source=new FileStream(e.Source,FileMode.Open,FileAccess.Read,FileShare.Delete))using(var target=new FileStream(e.Target,FileMode.Open,FileAccess.Read,FileShare.Read)){if(source.Length!=e.Size||Crc(source)!=e.Crc||target.Length!=e.Size||Crc(target)!=e.Crc)throw new IOException("File changed before deletion: "+e.Source);File.Delete(e.Source);}}
                if(folder)App.RemoveEmptyFolders(spec.Sources[0],destination,directories,cancel.Token,delegate(string message){Check();state.Detail=message;Save();});
            }
            state.Percent=100;state.Successful=true;state.Phase=verifyCrc?"Completed · CRC verified":"Completed · CRC off";state.Detail=entries.Count+(verifyCrc?" files CRC verified":" files copied; size checks passed (CRC off)");return 0;
        }catch(OperationCanceledException){state.Phase="Cancelled";state.Error="Partial destination files may remain. Completed source deletions cannot be undone.";return 2;}
        catch(Exception ex){state.Phase="Failed";state.Error=ex.Message;return 1;}
        finally{state.CopyPid=0;state.Finished=true;Save(true);cancel.Dispose();}
    }
    static void CopyFiles(string destination){
        foreach(var group in entries.GroupBy(e=>Path.GetDirectoryName(e.Source),StringComparer.OrdinalIgnoreCase)){
            var names=new List<string>();int length=0;
            foreach(var file in group){string name=App.Quote(Path.GetFileName(file.Source));if(length+name.Length>20000&&names.Count>0){Copy(group.Key,destination,String.Join(" ",names));names.Clear();length=0;}names.Add(name);length+=name.Length+1;}
            if(names.Count>0)Copy(group.Key,destination,String.Join(" ",names));
        }
    }
    static void Copy(string from,string to,string selection){
        Check();string log=Store.PathFor(spec.Id,".robocopy.log");
        var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"robocopy.exe"),App.Quote(from)+" "+App.Quote(to)+" "+selection+" /MT:"+spec.Threads+" /R:1 /W:1 /XJ /COPY:DAT /DCOPY:DAT /BYTES /NP "+(verifyCrc?"/IS /IT ":"")+"/UNILOG+:"+App.Quote(log)){UseShellExecute=false,CreateNoWindow=true};
        using(var process=Process.Start(info))using(var guard=new ProcessGuard(process)){
            state.CopyPid=process.Id;Save(true);
            try{process.PriorityClass=(ProcessPriorityClass)Enum.Parse(typeof(ProcessPriorityClass),spec.Priority);}catch{state.Detail="Windows denied requested copy priority.";}
            try{while(!process.WaitForExit(350)){Check();long bytes=0;foreach(var e in entries){try{var file=new FileInfo(e.Target);if(file.Exists&&(file.Length!=e.OriginalTargetSize||file.LastWriteTimeUtc.Ticks!=e.OriginalTargetTicks))bytes+=Math.Min(file.Length,e.Size);}catch(IOException){}}state.Percent=total==0?0:Math.Min(99,100.0*bytes/total);state.Speed=bytes/1048576.0/Math.Max(.1,timer.Elapsed.TotalSeconds);Save();}Check();if(process.ExitCode>=8)throw new IOException("Robocopy exit code "+process.ExitCode+"; see "+log);}
            finally{if(!process.HasExited){try{process.Kill();process.WaitForExit();}catch{}}state.CopyPid=0;}
        }
    }
}

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
    static CopyProgress monitor;static readonly byte[] crcBuffer=new byte[1024*1024];static long lastCancelProbe;static bool verifyCrc;static long total, verified; static DateTime lastSave=DateTime.MinValue;
    static void Save(bool force=false){if(!force&&(DateTime.UtcNow-lastSave).TotalMilliseconds<250)return;state.Sequence++;Store.Write(Store.PathFor(spec.Id,".state"),state);lastSave=DateTime.UtcNow;}
    static void Check(bool force=false){long now=Stopwatch.GetTimestamp();if(force||now-lastCancelProbe>=Stopwatch.Frequency/10){lastCancelProbe=now;if(File.Exists(Store.PathFor(spec.Id,".cancel")))cancel.Cancel();}cancel.Token.ThrowIfCancellationRequested();}
    static void Phase(string text){Check();state.Phase=text;Save(true);}
    static uint Crc(string file){using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))return Crc(stream);}
    static uint Crc(Stream stream){uint crc=0xffffffff;byte[] buffer=crcBuffer;int read;while((read=stream.Read(buffer,0,buffer.Length))>0){Check();state.CrcBytesRead+=read;crc=FastCrc32.Update(crc,buffer,0,read);}return crc^0xffffffff;}
    public static int Run(string id){
        spec=Store.Read<JobSpec>(Store.PathFor(id,".job"));state=Store.Read<JobState>(Store.PathFor(id,".state"));state.WorkerPid=Process.GetCurrentProcess().Id;string destination=JobPaths.Target(spec);spec.DeleteSource=spec.DeleteSource||spec.Operation=="Move";verifyCrc=spec.VerifyCrc;
        try{
            try{Process.GetCurrentProcess().PriorityClass=(ProcessPriorityClass)Enum.Parse(typeof(ProcessPriorityClass),spec.Priority);}catch{state.Detail="Windows denied requested worker priority.";}
            Phase("Scanning");var directories=new List<string>();bool folder=spec.Sources.Length==1&&Directory.Exists(spec.Sources[0]);
            if(folder)App.Enumerate(spec.Sources[0],App.Full(spec.Sources[0]),destination,entries,directories,()=>Check());
            else foreach(var file in spec.Sources){Check();App.SafePath(Path.GetDirectoryName(file));if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked files are not supported.");entries.Add(new Entry{Source=file,Target=Path.Combine(destination,Path.GetFileName(file)),Size=new FileInfo(file).Length});}
            if(entries.Select(e=>e.Target).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=entries.Count)throw new IOException("Duplicate destination filenames.");
            total=entries.Sum(e=>e.Size);Phase(verifyCrc?"Recording source CRC-32":"Preparing fast copy");
            var checkedDirectories=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for(int i=0;i<entries.Count;i++){var e=entries[i];Check();e.SourceModifiedTicks=File.GetLastWriteTimeUtc(e.Source).Ticks;if(verifyCrc)e.Crc=Crc(e.Source);if(checkedDirectories.Add(Path.GetDirectoryName(e.Target)))App.SafePath(Path.GetDirectoryName(e.Target));if(File.Exists(e.Target)&&(File.GetAttributes(e.Target)&FileAttributes.ReparsePoint)!=0)throw new IOException("Destination is a linked file.");var prior=new FileInfo(e.Target);if(prior.Exists){e.OriginalTargetTicks=prior.LastWriteTimeUtc.Ticks;e.OriginalTargetSize=prior.Length;}state.Percent=100.0*(i+1)/Math.Max(1,entries.Count);Save();}
            if(FastMove.TryRename(spec,destination,()=>Check(true))){state.TransferMethod="Same-volume rename";state.Percent=100;state.Successful=true;state.Phase="Completed · moved (CRC off)";state.Detail=entries.Count+" files moved by same-volume rename; no file data copied";return 0;}
            monitor=new CopyProgress(entries);App.SafePath(destination);Directory.CreateDirectory(destination);timer.Start();state.Percent=0;Phase("Copying (estimated)");
            if(folder)Copy(spec.Sources[0],destination,"/E");else CopyFiles(destination);
            state.CopyPid=0;state.Percent=0;Phase(verifyCrc?"Verifying CRC-32":"Checking file sizes");
            for(int i=0;i<entries.Count;i++){var e=entries[i];Check();if(new FileInfo(e.Target).Length!=e.Size||(spec.DeleteSource&&(new FileInfo(e.Source).Length!=e.Size||File.GetLastWriteTimeUtc(e.Source).Ticks!=e.SourceModifiedTicks))||(verifyCrc&&(new FileInfo(e.Source).Length!=e.Size||Crc(e.Source)!=e.Crc||Crc(e.Target)!=e.Crc)))throw new IOException("Copy verification failed: "+e.Source+". Source deletion has not started.");verified+=e.Size;state.Percent=total==0?100.0*(i+1)/entries.Count:100.0*verified/total;state.Speed=verified/1048576.0/Math.Max(.1,timer.Elapsed.TotalSeconds);state.Detail=Path.GetFileName(e.Source);Save();}
            if(spec.DeleteSource){Phase(verifyCrc?"Deleting CRC-verified source":"Moving source (CRC off)");foreach(var e in entries){Check(true);App.SafePath(Path.GetDirectoryName(e.Source));App.SafePath(Path.GetDirectoryName(e.Target));using(var source=new FileStream(e.Source,FileMode.Open,FileAccess.Read,FileShare.Delete))using(var target=new FileStream(e.Target,FileMode.Open,FileAccess.Read,FileShare.Read)){if(source.Length!=e.Size||target.Length!=e.Size||File.GetLastWriteTimeUtc(e.Source).Ticks!=e.SourceModifiedTicks||(verifyCrc&&(Crc(source)!=e.Crc||Crc(target)!=e.Crc)))throw new IOException("File changed before deletion: "+e.Source);File.Delete(e.Source);}}
                if(folder)App.RemoveEmptyFolders(spec.Sources[0],destination,directories,cancel.Token,delegate(string message){Check();state.Detail=message;Save();});
            }
            state.Percent=100;state.Successful=true;state.Phase=spec.DeleteSource?(verifyCrc?"Completed · moved (CRC verified)":"Completed · moved (CRC off)"):verifyCrc?"Completed · CRC verified":"Completed · CRC off";state.Detail=entries.Count+(verifyCrc?" files CRC verified":" files copied; size checks passed (CRC off)");return 0;
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
        var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"robocopy.exe"),App.Quote(from)+" "+App.Quote(to)+" "+selection+" /MT:"+spec.Threads+" /R:1 /W:1 /XJ /COPY:DAT /DCOPY:DAT /BYTES /NP "+(spec.CopyMode=="Large files"?"/J ":"")+(verifyCrc?"/IS /IT ":"")+"/UNILOG+:"+App.Quote(log)){UseShellExecute=false,CreateNoWindow=true};
        using(var process=Process.Start(info))using(var guard=new ProcessGuard(process)){
            state.CopyPid=process.Id;Save(true);
            try{process.PriorityClass=(ProcessPriorityClass)Enum.Parse(typeof(ProcessPriorityClass),spec.Priority);}catch{state.Detail="Windows denied requested copy priority.";}
            try{while(!process.WaitForExit(350)){Check();var sample=monitor.Update();long bytes=sample.EstimatedBytes;state.ProgressMetadataProbes+=sample.SamplesTaken;state.Percent=total==0?0:Math.Min(99,100.0*bytes/total);state.Speed=bytes/1048576.0/Math.Max(.1,timer.Elapsed.TotalSeconds);Save();}Check();if(process.ExitCode>=8)throw new IOException("Robocopy exit code "+process.ExitCode+"; see "+log);}
            finally{if(!process.HasExited){try{process.Kill();process.WaitForExit();}catch{}}state.CopyPid=0;}
        }
    }
}

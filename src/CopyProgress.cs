// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 diazneoones82
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

internal struct CopyProgressFileState {
    public readonly bool Exists;
    public readonly long Length, LastWriteUtcTicks;
    public CopyProgressFileState(bool exists,long length,long lastWriteUtcTicks){Exists=exists;Length=length;LastWriteUtcTicks=lastWriteUtcTicks;}
}

internal struct CopyProgressSnapshot {
    public readonly long EstimatedBytes;
    public readonly int SamplesTaken;
    public readonly double ElapsedMilliseconds;
    public readonly bool CompletedSweep, TimeBudgetReached;
    public CopyProgressSnapshot(long bytes,int samples,double milliseconds,bool sweep,bool budgetReached){EstimatedBytes=bytes;SamplesTaken=samples;ElapsedMilliseconds=milliseconds;CompletedSweep=sweep;TimeBudgetReached=budgetReached;}
}

// Worker-thread only. Metadata is sampled incrementally, never from the UI.
// The time budget is cooperative: one blocking filesystem call cannot be preempted.
internal sealed class CopyProgress {
    internal const int MaximumSamplesPerUpdate=128;
    internal const int TimeBudgetMilliseconds=20;
    readonly Entry[] entries;
    readonly long[] estimates;
    readonly Func<Entry,CopyProgressFileState> inspect;
    readonly long totalBytes;
    int next;
    long estimatedBytes;
    bool completed;

    public CopyProgress(IList<Entry> files,Func<Entry,CopyProgressFileState> inspectFile=null){
        if(files==null)throw new ArgumentNullException("files");
        entries=new Entry[files.Count];files.CopyTo(entries,0);estimates=new long[entries.Length];
        inspect=inspectFile??InspectFile;
        foreach(var entry in entries){if(entry==null)throw new ArgumentException("A progress entry is null.","files");totalBytes=checked(totalBytes+Math.Max(0,entry.Size));}
    }

    static CopyProgressFileState InspectFile(Entry entry){
        // One FileInfo metadata refresh; Length and LastWriteTimeUtc use its cache.
        var file=new FileInfo(entry.Target);
        return file.Exists?new CopyProgressFileState(true,file.Length,file.LastWriteTimeUtc.Ticks):new CopyProgressFileState(false,0,0);
    }

    public CopyProgressSnapshot Update(){
        if(completed||entries.Length==0)return new CopyProgressSnapshot(estimatedBytes,0,0,false,false);
        var watch=Stopwatch.StartNew();int samples=0;bool swept=false;
        int limit=Math.Min(MaximumSamplesPerUpdate,entries.Length);
        long budgetTicks=Math.Max(1,(long)(Stopwatch.Frequency*(TimeBudgetMilliseconds/1000.0)));
        while(samples<limit&&watch.ElapsedTicks<budgetTicks){
            int index=next;Entry entry=entries[index];
            try{
                var file=inspect(entry);
                bool changed=file.Exists&&(file.Length!=entry.OriginalTargetSize||file.LastWriteUtcTicks!=entry.OriginalTargetTicks);
                long bytes=changed?Math.Min(Math.Max(0,entry.Size),Math.Max(0,file.Length)):0;
                estimatedBytes=checked(estimatedBytes-estimates[index]+bytes);estimates[index]=bytes;
            }catch(IOException){
                // A transient metadata failure is not evidence that prior bytes vanished.
            }catch(UnauthorizedAccessException){}
            samples++;next++;
            if(next==entries.Length){next=0;swept=true;}
        }
        double elapsed=watch.Elapsed.TotalMilliseconds;
        return new CopyProgressSnapshot(estimatedBytes,samples,elapsed,swept,watch.ElapsedTicks>=budgetTicks);
    }

    // Call only after copy completion and the caller's final verification succeeds.
    // This performs no filesystem calls and does not turn estimates into integrity checks.
    public CopyProgressSnapshot Complete(){completed=true;estimatedBytes=totalBytes;return new CopyProgressSnapshot(totalBytes,0,0,true,false);}
}

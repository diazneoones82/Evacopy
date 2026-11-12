// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 diazneoones82
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
class Entry { public string Source, Target; public long Size, OriginalTargetTicks=-1, OriginalTargetSize=-1; public uint Crc; }
partial class App {
    internal static string Full(string s){return Path.GetFullPath(s).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;}
    internal static bool Inside(string a,string b){return Full(a).StartsWith(Full(b),StringComparison.OrdinalIgnoreCase);}
    internal static void SafePath(string path) { for(var p=new DirectoryInfo(path);p!=null;p=p.Parent) if(p.Exists && (p.Attributes & FileAttributes.ReparsePoint)!=0) throw new IOException("Links and junctions are not supported: "+p.FullName); }
    internal static void Enumerate(string dir, string root, string dest,List<Entry> list,List<string> directories){SafePath(dir);directories.Add(dir);foreach(var f in Directory.GetFiles(dir)){if((File.GetAttributes(f)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked files are not supported.");list.Add(new Entry{Source=f,Target=Path.Combine(dest,f.Substring(root.Length)),Size=new FileInfo(f).Length});}foreach(var d in Directory.GetDirectories(dir))Enumerate(d,root,dest,list,directories);}
    internal static void RemoveEmptyFolders(string root,string dest,IEnumerable<string> directories,CancellationToken token,Action<string> report){
        foreach(string dir in directories.OrderByDescending(d=>Full(d).Length)){
            token.ThrowIfCancellationRequested();
            if(!Inside(dir,root) || String.Equals(Full(dir),Full(Path.GetPathRoot(dir)),StringComparison.OrdinalIgnoreCase))continue;
            if(!Directory.Exists(dir))continue;
            SafePath(dir);
            string target=Path.Combine(dest,Full(dir).Substring(Full(root).Length));
            if(!Directory.Exists(target)){report("Retained folder; destination folder missing: "+dir);continue;}
            SafePath(target);
            if(Directory.EnumerateFileSystemEntries(dir).Any()){report("Retained non-empty folder: "+dir);continue;}
            try{Directory.Delete(dir,false);report("Removed empty folder: "+dir);}
            catch(IOException){report("Retained folder; it is in use or no longer empty: "+dir);}
            catch(UnauthorizedAccessException){report("Retained folder; access denied: "+dir);}
        }
    }
    internal static string Quote(string s){return "\""+s.TrimEnd('\\')+"\"";}
    internal static readonly uint[] Table=MakeTable();
    internal static uint[] MakeTable(){var t=new uint[256];for(uint i=0;i<256;i++){uint c=i;for(int k=0;k<8;k++)c=(c&1)!=0?0xEDB88320U^(c>>1):c>>1;t[i]=c;}return t;}
    internal static uint Crc(string path,CancellationToken token){using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))return CrcStream(f,token);}
    internal static uint CrcStream(Stream f,CancellationToken token){uint crc=0xFFFFFFFF;var b=new byte[1024*1024];int n;while((n=f.Read(b,0,b.Length))>0){token.ThrowIfCancellationRequested();for(int i=0;i<n;i++)crc=Table[(crc^b[i])&255]^(crc>>8);}return crc^0xFFFFFFFF;}
}

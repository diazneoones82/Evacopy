// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 diazneoones82
using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;

static class FastMove {
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool GetVolumePathName(string path,StringBuilder volume,int length);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool GetVolumeNameForVolumeMountPoint(string root,StringBuilder name,int length);
    static string Volume(string path){var root=new StringBuilder(32768);var name=new StringBuilder(128);return GetVolumePathName(path,root,root.Capacity)&&GetVolumeNameForVolumeMountPoint(root.ToString(),name,name.Capacity)?name.ToString():null;}
    internal static bool SameVolume(string source,string target){string parent=target;while(!Directory.Exists(parent)){parent=Path.GetDirectoryName(parent);if(String.IsNullOrEmpty(parent))return false;}string a=Volume(source),b=Volume(parent);return a!=null&&String.Equals(a,b,StringComparison.OrdinalIgnoreCase);}
    // Only an absent target is eligible. Existing-target merges/overwrites use Robocopy.
    internal static bool TryRename(JobSpec spec,string destination,Action check){
        if(!spec.DeleteSource||spec.VerifyCrc||spec.Sources.Length!=1)return false;
        string source=spec.Sources[0];bool folder=Directory.Exists(source);
        string target=folder?destination:Path.Combine(destination,Path.GetFileName(source));
        if(File.Exists(target)||Directory.Exists(target)||!SameVolume(source,target))return false;
        App.SafePath(folder?source:Path.GetDirectoryName(source));App.SafePath(Path.GetDirectoryName(target));check();
        try{Directory.CreateDirectory(Path.GetDirectoryName(target));if(folder)Directory.Move(source,target);else File.Move(source,target);return true;}
        catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
    }
}

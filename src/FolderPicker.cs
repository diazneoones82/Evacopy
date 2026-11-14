// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 diazneoones82
using System;
using System.IO;
using System.Runtime.InteropServices;

static class PickerPaths {
    public static string ExistingDirectory(string path){
        if(!String.IsNullOrWhiteSpace(path))try{
            string current=Path.GetFullPath(path);
            if(File.Exists(current))current=Path.GetDirectoryName(current);
            while(!String.IsNullOrEmpty(current)){
                if(Directory.Exists(current))return current;
                current=Path.GetDirectoryName(current);
            }
        }catch(ArgumentException){}catch(NotSupportedException){}catch(PathTooLongException){}
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }
}

// Native Explorer folder picker with an explicit starting location.
static class FolderPicker {
    [ComImport,Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")] class FileOpenDialog {}
    [ComImport,Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ShellItem {
        void BindToHandler(IntPtr context,ref Guid handler,ref Guid iid,out IntPtr result);
        void GetParent(out ShellItem parent);
        void GetDisplayName(uint type,out IntPtr name);
        void GetAttributes(uint mask,out uint attributes);
        void Compare(ShellItem other,uint hint,out int order);
    }
    [ComImport,Guid("D57C7288-D4AD-4768-BE02-9D969532D960"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface OpenDialog {
        [PreserveSig] int Show(IntPtr owner);
        void SetFileTypes(uint count,IntPtr types);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events,out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(ShellItem folder);
        void SetFolder(ShellItem folder);
        void GetFolder(out ShellItem folder);
        void GetCurrentSelection(out ShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName(out IntPtr name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetResult(out ShellItem result);
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=false)]
    static extern void SHCreateItemFromParsingName(string path,IntPtr context,ref Guid iid,out ShellItem item);
    internal static string CheckInitialFolder(string initialPath){
        OpenDialog dialog=(OpenDialog)new FileOpenDialog();ShellItem folder=null,result=null;
        try{Guid iid=typeof(ShellItem).GUID;SHCreateItemFromParsingName(PickerPaths.ExistingDirectory(initialPath),IntPtr.Zero,ref iid,out folder);dialog.SetFolder(folder);dialog.GetFolder(out result);IntPtr text;result.GetDisplayName(0x80058000,out text);try{return Marshal.PtrToStringUni(text);}finally{Marshal.FreeCoTaskMem(text);}}
        finally{if(result!=null)Marshal.ReleaseComObject(result);if(folder!=null)Marshal.ReleaseComObject(folder);Marshal.ReleaseComObject(dialog);}
    }
    public static string Choose(IntPtr owner,string initialPath,string title){
        OpenDialog dialog=(OpenDialog)new FileOpenDialog();ShellItem folder=null,result=null;
        try{
            dialog.SetOptions(0x20|0x40|0x8|0x800);dialog.SetTitle(title);dialog.SetOkButtonLabel("Select folder");
            string initial=PickerPaths.ExistingDirectory(initialPath);
            if(!String.IsNullOrEmpty(initial)){Guid iid=typeof(ShellItem).GUID;SHCreateItemFromParsingName(initial,IntPtr.Zero,ref iid,out folder);dialog.SetFolder(folder);}
            int hr=dialog.Show(owner);if(hr==unchecked((int)0x800704C7))return null;Marshal.ThrowExceptionForHR(hr);
            dialog.GetResult(out result);IntPtr text;result.GetDisplayName(0x80058000,out text);try{return Marshal.PtrToStringUni(text);}finally{Marshal.FreeCoTaskMem(text);}
        }finally{if(result!=null)Marshal.ReleaseComObject(result);if(folder!=null)Marshal.ReleaseComObject(folder);Marshal.ReleaseComObject(dialog);}
    }
}

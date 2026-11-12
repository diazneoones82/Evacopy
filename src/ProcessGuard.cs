// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 diazneoones82
using System;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;

// Closing the worker's last job handle terminates its copy child even on a crash.
sealed class ProcessGuard : IDisposable {
    IntPtr job;
    [StructLayout(LayoutKind.Sequential)] struct Basic { public long PerProcess,PerJob; public uint Flags; public UIntPtr Minimum,Maximum; public uint Active; public UIntPtr Affinity; public uint Priority,Scheduling; }
    [StructLayout(LayoutKind.Sequential)] struct Io { public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] struct Limits { public Basic Basic; public Io Io; public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory; }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateJobObject(IntPtr security,string name);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job,int type,ref Limits limits,uint size);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    public ProcessGuard(Process process){job=CreateJobObject(IntPtr.Zero,null);if(job==IntPtr.Zero)throw new Win32Exception();var limits=new Limits();limits.Basic.Flags=0x2000;if(!SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf(typeof(Limits)))||!AssignProcessToJobObject(job,process.Handle)){int error=Marshal.GetLastWin32Error();Dispose();try{process.Kill();}catch{}throw new Win32Exception(error,"Cannot isolate copy child process.");}}
    public void Dispose(){if(job!=IntPtr.Zero){CloseHandle(job);job=IntPtr.Zero;}}
}

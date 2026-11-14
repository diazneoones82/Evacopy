// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 diazneoones82
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

partial class App {
    const string SettingsKey = @"Software\Evacopy";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    static Mutex instance;
    static Forms.NotifyIcon tray;
    static HwndSource native;
    static IntPtr handle;
    static bool exiting, registered, quitting;
    static readonly uint ShowMessage = RegisterWindowMessage("Evacopy.ShowWindow.v1");
    [DllImport("user32.dll", SetLastError=true)] static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint message, IntPtr w, IntPtr l);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    static bool FirstInstance(){bool created;instance=new Mutex(true,@"Local\Evacopy.Desktop.v1",out created);if(!created){PostMessage(new IntPtr(0xffff),ShowMessage,IntPtr.Zero,IntPtr.Zero);instance.Dispose();}return created;}
    static void Reveal(){window.Show();window.WindowState=WindowState.Normal;window.Activate();window.Topmost=true;window.Topmost=false;}
    static void DesktopSetup(bool startup){
        window.Width=Math.Min(577,SystemParameters.WorkArea.Width-24);
        window.Height=Math.Min(420,SystemParameters.WorkArea.Height-24);
        window.MinWidth=Math.Min(540,window.Width);window.MinHeight=Math.Min(390,window.Height);
        var launch=Get<CheckBox>("LaunchAtLogin");var hotkey=Get<CheckBox>("EnableHotkey");
        try{using(var key=Registry.CurrentUser.OpenSubKey(RunKey))launch.IsChecked=key!=null && key.GetValue("Evacopy")!=null;
            using(var key=Registry.CurrentUser.OpenSubKey(SettingsKey))hotkey.IsChecked=key==null || Convert.ToInt32(key.GetValue("Hotkey",1))!=0;
        }catch(Exception ex){Note("Could not read desktop settings: "+ex.Message);hotkey.IsChecked=true;}
        launch.Click+=delegate{try{using(var key=Registry.CurrentUser.CreateSubKey(RunKey)){if(launch.IsChecked==true)key.SetValue("Evacopy",StartupCommand(),RegistryValueKind.String);else key.DeleteValue("Evacopy",false);}Note(launch.IsChecked==true?"Start with Windows enabled (hidden in tray).":"Start with Windows disabled.");}catch(Exception ex){launch.IsChecked=!(launch.IsChecked==true);Note("Could not change startup: "+ex.Message);}};
        hotkey.Click+=delegate{try{using(var key=Registry.CurrentUser.CreateSubKey(SettingsKey))key.SetValue("Hotkey",hotkey.IsChecked==true?1:0);ConfigureHotkey();}catch(Exception ex){Note("Could not save hotkey setting: "+ex.Message);}};
        window.SourceInitialized+=delegate{
            handle=new WindowInteropHelper(window).Handle;native=HwndSource.FromHwnd(handle);native.AddHook(DesktopMessages);ConfigureHotkey();
            try{int dark=1;DwmSetWindowAttribute(handle,20,ref dark,4);}catch{}
            tray=new Forms.NotifyIcon{Icon=LoadTrayIcon(),Text="Evacopy",Visible=true};
            var menu=new Forms.ContextMenuStrip();menu.Items.Add("Open Evacopy",null,delegate{UI(Reveal);});
            menu.Items.Add("Exit",null,delegate{UI(ExitApplication);});
            tray.ContextMenuStrip=menu;tray.DoubleClick+=delegate{UI(Reveal);};
        };
        window.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;window.Hide();}};
        window.StateChanged+=delegate{if(window.WindowState==WindowState.Minimized)window.Hide();};
        window.Closed+=delegate{if(registered)UnregisterHotKey(handle,1);if(native!=null)native.RemoveHook(DesktopMessages);if(tray!=null){tray.Visible=false;tray.Dispose();}instance.ReleaseMutex();instance.Dispose();};
        window.ContentRendered+=delegate{if(startup)window.Hide();};
    }
    static async void ExitApplication(){
        if(quitting)return;quitting=true;refresh.Stop();window.IsEnabled=false;Get<TabControl>("JobTabs").SelectedIndex=1;Note("Stopping background processes…");
        try{
            try{await System.Threading.Tasks.Task.WhenAll(serviceReady??System.Threading.Tasks.Task.FromResult(0),submissionTask??System.Threading.Tasks.Task.FromResult(0));}catch{}
            await System.Threading.Tasks.Task.Run(()=>Store.StopAll());exiting=true;window.Close();
        }catch(Exception ex){quitting=false;window.IsEnabled=true;Reveal();refresh.Start();MessageBox.Show(window,"Could not stop all background processes: "+ex.Message,"Evacopy");}
    }
    static System.Drawing.Icon LoadTrayIcon(){using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Evacopy.ico")){return stream==null?(System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone():new System.Drawing.Icon(stream);}}
    static string StartupCommand(){return "\""+System.Reflection.Assembly.GetExecutingAssembly().Location+"\" --startup";}
    static void ConfigureHotkey(){
        if(handle==IntPtr.Zero)return;
        if(registered){UnregisterHotKey(handle,1);registered=false;}
        var box=Get<CheckBox>("EnableHotkey");
        if(box.IsChecked!=true){box.Content="Global shortcut disabled";return;}
        registered=RegisterHotKey(handle,1,0x4008,0x43);
        if(registered){box.Content="Open app: Win + C";Note("Global shortcut: Win+C.");return;}
        registered=RegisterHotKey(handle,1,0x4003,0x43);
        box.Content=registered?"Open app: Ctrl + Alt + C (Win+C unavailable)":"Global shortcut unavailable";
        Note(registered?"Windows reserved Win+C; using Ctrl+Alt+C instead.":"Both Win+C and Ctrl+Alt+C are in use. Open from the tray.");
    }
    static IntPtr DesktopMessages(IntPtr hwnd,int message,IntPtr w,IntPtr l,ref bool handled){if((uint)message==ShowMessage || (message==0x312 && w.ToInt32()==1)){Reveal();handled=true;}return IntPtr.Zero;}
}

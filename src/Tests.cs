// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 diazneoones82
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

static class Tests {
    static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
    public static int Unit(){try{
        using(var s=new MemoryStream(System.Text.Encoding.ASCII.GetBytes("123456789")))Assert(App.CrcStream(s,CancellationToken.None)==0xCBF43926,"CRC vector");
        Assert(App.Inside(@"C:\a\b",@"C:\a")&&!App.Inside(@"C:\abc",@"C:\a"),"Path boundaries");
        var window=(Window)XamlReader.Parse(App.Layout);
        using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Evacopy.png"))((Image)window.FindName("BrandIcon")).Source=System.Windows.Media.Imaging.BitmapFrame.Create(stream);
        var grid=(Grid)window.Content;grid.Measure(new Size(571,391));grid.Arrange(new Rect(0,0,571,391));grid.UpdateLayout();
        var start=(Button)window.FindName("Start");Assert(start.ActualHeight>20&&start.TranslatePoint(new Point(0,start.ActualHeight),grid).Y<=grid.ActualHeight,"New job controls fit reference size");Assert(window.Width==577&&window.Height==420,"Reference window dimensions");
        foreach(var name in new[]{"Remove","LaunchAtLogin","EnableHotkey"})Assert(((CheckBox)window.FindName(name)).Foreground.ToString()=="#FFFFFFFF","Checkbox foreground: "+name);
        Assert(window.Title=="Evacopy","Brand title");
        using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Evacopy.ico"))Assert(stream!=null&&stream.Length>1000,"Embedded icon");
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(571,391,96,96,System.Windows.Media.PixelFormats.Pbgra32);var background=new System.Windows.Media.DrawingVisual();using(var drawing=background.RenderOpen())drawing.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5,17,37)),null,new Rect(0,0,571,391));bitmap.Render(background);bitmap.Render(grid);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Evacopy-preview.png")))encoder.Save(file);
        return 0;
    }catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"unit-test-error.txt"),ex.ToString());return 1;}}
    static Reply Send(Request request){var r=Store.Send(request);if(r.Error!=null)throw new Exception(r.Error);return r;}
    static JobSpec Spec(string source,string target,bool delete){return new JobSpec{Sources=new[]{source},Destination=target,DeleteSource=delete,Threads=8,Priority="Normal"};}
    public static int Integration(){string root=Path.GetDirectoryName(Store.Root);try{
        Directory.CreateDirectory(root);string a=Path.Combine(root,"source A"),b=Path.Combine(root,"source B"),da=Path.Combine(root,"target A"),db=Path.Combine(root,"target B");
        foreach(var dir in new[]{a,b}){Directory.CreateDirectory(Path.Combine(dir,"nested","empty"));File.WriteAllText(Path.Combine(dir,"nested","unicode ü.txt"),"Evacopy integrity check");File.WriteAllBytes(Path.Combine(dir,"empty.file"),new byte[0]);using(var f=File.Create(Path.Combine(dir,"payload.bin"))){var data=new byte[1024*1024];new Random(123).NextBytes(data);for(int i=0;i<32;i++)f.Write(data,0,data.Length);}}
        Store.EnsureService();string first=Send(new Request{Command="submit",Job=Spec(a,da,true)}).Id;
        var conflict=Store.Send(new Request{Command="submit",Job=Spec(b,da,false)});Assert(conflict.Error!=null,"Overlapping job rejected");
        string second=Send(new Request{Command="submit",Job=Spec(b,db,false)}).Id;bool concurrent=false,copySeen=false,restarted=false;JobState[] all=null;
        var deadline=DateTime.UtcNow.AddSeconds(90);
        while(DateTime.UtcNow<deadline){all=Send(new Request{Command="list"}).Jobs;var one=all.Single(s=>s.Id==first);var two=all.Single(s=>s.Id==second);if(one.WorkerPid>0&&two.WorkerPid>0){Assert(one.WorkerPid!=two.WorkerPid,"Independent worker PIDs");Assert(one.WorkerPid!=System.Diagnostics.Process.GetCurrentProcess().Id,"Worker separate from client");if(!one.Finished&&!two.Finished)concurrent=true;}copySeen|=all.Any(s=>s.CopyPid>0);if(concurrent&&copySeen&&!restarted){int service=Send(new Request{Command="ping"}).ServicePid;using(var broker=System.Diagnostics.Process.GetProcessById(service)){broker.Kill();broker.WaitForExit();}Store.EnsureService();restarted=true;}if(one.Finished&&two.Finished)break;Thread.Sleep(100);}
        Assert(all!=null&&all.Where(s=>s.Id==first||s.Id==second).All(s=>s.Finished&&s.Successful),"Both copy/CRC jobs completed: "+Store.Json(all));Assert(concurrent,"Jobs overlapped");Assert(restarted,"Supervisor restart recovery");Assert(copySeen,"Separate Robocopy PID observed");Assert(!Directory.Exists(a),"Verified move removed source root");Assert(File.Exists(Path.Combine(b,"payload.bin")),"Copy preserved source");Assert(Directory.Exists(Path.Combine(da,"nested","empty")),"Empty directory copied");Assert(App.Crc(Path.Combine(da,"payload.bin"),CancellationToken.None)==App.Crc(Path.Combine(db,"payload.bin"),CancellationToken.None),"Copied content CRC");
        string third=Send(new Request{Command="submit",Job=Spec(b,Path.Combine(root,"cancel target"),true)}).Id;Send(new Request{Command="cancel",Id=third});JobState cancelled=null;deadline=DateTime.UtcNow.AddSeconds(30);while(DateTime.UtcNow<deadline){cancelled=Send(new Request{Command="list"}).Jobs.Single(s=>s.Id==third);if(cancelled.Finished)break;Thread.Sleep(100);}Assert(cancelled!=null&&cancelled.Phase=="Cancelled","Cancellation state");Assert(File.Exists(Path.Combine(b,"payload.bin")),"Cancellation retained source");
        Send(new Request{Command="shutdown"});File.WriteAllText(Path.Combine(root,"RESULT.txt"),"PASS: concurrent workers, separate Robocopy processes, overlap rejection, supervisor restart recovery, CRC verification, verified move cleanup, empty directories, Unicode paths, source preservation and cancellation.");return 0;
    }catch(Exception ex){Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"ERROR.txt"),ex.ToString());try{Store.Send(new Request{Command="shutdown"});}catch{}return 1;}}
}






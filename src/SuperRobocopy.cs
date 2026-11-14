// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 diazneoones82
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Forms=System.Windows.Forms;
partial class App {
    static Window window;static TextBox source,destination;static TextBlock status,metrics;static ProgressBar progress;
    static Button start,cancel;static CheckBox remove;static string[] selected;
    static ListBox jobs;static Dictionary<string,ListBoxItem> rows=new Dictionary<string,ListBoxItem>();
    static Dictionary<string,JobState> states=new Dictionary<string,JobState>();static bool polling;static DispatcherTimer refresh;static string lastSourcePath;static Task serviceReady,submissionTask;
    const string Xaml = @"<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Title='Evacopy' Width='577' Height='420' MinWidth='540' MinHeight='390' ResizeMode='CanMinimize' WindowStartupLocation='CenterScreen' Background='#051125' Foreground='#FFFFFF' FontFamily='Segoe UI'>
<Window.Resources/>
<Grid Margin='12'><Grid.RowDefinitions><RowDefinition Height='44'/><RowDefinition Height='*'/><RowDefinition Height='52'/></Grid.RowDefinitions>
<DockPanel><Button Name='AddJob' Content='+ ⚙' Height='30' DockPanel.Dock='Right' VerticalAlignment='Top' ToolTip='Choose another source and configure its copy job'/><Image Name='BrandIcon' Width='38' Height='38' Margin='0,0,10,4' DockPanel.Dock='Left'/><StackPanel><TextBlock Text='EVACOPY' Foreground='#00F0FF' FontSize='19' FontWeight='SemiBold'/><TextBlock Text='Fast copies. Verify when needed.' Foreground='#FFFFFF' FontSize='11'/></StackPanel></DockPanel>
<TabControl Grid.Row='1' Name='JobTabs' Background='#051125' BorderThickness='0' Foreground='#FFFFFF'>
<TabItem Header='New job'><Border CornerRadius='10' BorderBrush='#1A9CFF' BorderThickness='1' Padding='10,6' Background='#0A2342'><StackPanel>
<TextBlock Text='SOURCE' FontSize='10' Foreground='#00F0FF'/><DockPanel Margin='0,2,0,4'><Button Name='SourceBrowse' DockPanel.Dock='Right' Content='Browse…' Padding='9,4' Margin='3,0,0,0'><Button.ContextMenu><ContextMenu Placement='Bottom' Background='#0A2342' Foreground='#FFFFFF' BorderBrush='#00F0FF'><MenuItem Header='Folder…'/><MenuItem Header='Files…'/></ContextMenu></Button.ContextMenu></Button><TextBox Name='Source' Padding='6,4' IsReadOnly='True' Text='Choose a folder or files'/></DockPanel>
<TextBlock Text='DESTINATION' FontSize='10' Foreground='#00F0FF'/><DockPanel Margin='0,2,0,4'><Button Name='Browse' Content='Browse…' DockPanel.Dock='Right' Padding='9,4' Margin='3,0,0,0'/><TextBox Name='Destination' Padding='6,4' ToolTip='Type a destination folder; missing folders are created.'/></DockPanel>
<CheckBox Name='IncludeFolder' IsChecked='True' IsEnabled='False' Content='Copy source folder itself (create its name at destination)' FontSize='11' Margin='0,2,0,4' ToolTip='Creates a folder matching the source name. Reuses the destination if it already has that name. Uncheck to copy contents only.'/><StackPanel Orientation='Horizontal'><TextBlock Text='⚙ Threads' FontSize='11' VerticalAlignment='Center'/><ComboBox Name='Threads' SelectedIndex='2' Width='62' Margin='7,0,14,0'><ComboBoxItem Content='8'/><ComboBoxItem Content='16'/><ComboBoxItem Content='32'/><ComboBoxItem Content='64'/><ComboBoxItem Content='128'/></ComboBox><TextBlock Text='CPU priority' FontSize='11' VerticalAlignment='Center'/><ComboBox Name='Priority' SelectedIndex='2' Width='115' Margin='7,0,0,0'><ComboBoxItem Content='Normal'/><ComboBoxItem Content='AboveNormal'/><ComboBoxItem Content='High'/></ComboBox></StackPanel>
<CheckBox Name='VerifyCrc' IsChecked='False' Content='Verify CRC-32 (slower; optional)' FontSize='11' Margin='0,3,0,1' ToolTip='Reads source and destination to verify content. Off for fast copies; automatically enabled when deleting source.'/><CheckBox Name='Remove' Margin='0,3,0,3' FontSize='11' Content='Delete source after copy (CRC required)'/>
<DockPanel><Button Name='Start' Content='Start copy job' DockPanel.Dock='Right' Padding='12,5' Margin='3,0,0,0' BorderBrush='#00F0FF'/><TextBlock Text='Existing destination files may be overwritten.' FontSize='10' VerticalAlignment='Center' TextWrapping='Wrap'/></DockPanel>
</StackPanel></Border></TabItem>
<TabItem Header='Transfers'><Grid><Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='*'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
<StackPanel Margin='0,5,0,6'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><TextBlock Name='Status' Text='Ready when you are' TextTrimming='CharacterEllipsis' FontSize='12' Margin='0,0,8,0'/><TextBlock Name='Metrics' Grid.Column='1' Text='0% · 0 MB/s' Foreground='#00F0FF' FontSize='11'/></Grid><ProgressBar Name='Progress' Height='6' Margin='0,5,0,0' Foreground='#00F0FF' Background='#0A507A' Maximum='100'/></StackPanel>
<ListBox Grid.Row='1' Name='Jobs' Background='#0A2342' Foreground='#FFFFFF' BorderBrush='#0A507A' ScrollViewer.HorizontalScrollBarVisibility='Disabled'/>
<DockPanel Grid.Row='2'><Button Name='Cancel' DockPanel.Dock='Right' Content='Cancel selected' IsEnabled='False' Padding='9,4' Margin='0,4,0,0'/><TextBlock Text='Select a job for progress and cancellation' FontSize='10' VerticalAlignment='Center'/></DockPanel>
</Grid></TabItem></TabControl>
<StackPanel Grid.Row='2' Margin='0,4,0,0'><StackPanel Orientation='Horizontal'><CheckBox Name='LaunchAtLogin' Content='Start with Windows' FontSize='11' Margin='2,4'/><CheckBox Name='EnableHotkey' Content='Open app: Win + C' FontSize='11' Margin='14,4,2,4'/></StackPanel><TextBlock Text='Close to tray · Jobs continue in background · Tray menu to exit' FontSize='10' Foreground='#FFFFFF'/></StackPanel>
</Grid></Window>";
    [STAThread] static int Main(string[] args){
        if(args.Length>0&&args[0]=="--service")return Kernel.Run();
        if(args.Length==2&&args[0]=="--worker")return Worker.Run(args[1]);
        if(args.Length>0&&args[0]=="--self-test")return Tests.Unit();
        if(args.Length>0&&args[0]=="--integration-test")return Tests.Integration();
        if(!FirstInstance())return 0;
        var app=new Application();window=(Window)XamlReader.Parse(Layout);
        source=Get<TextBox>("Source");destination=Get<TextBox>("Destination");status=Get<TextBlock>("Status");metrics=Get<TextBlock>("Metrics");progress=Get<ProgressBar>("Progress");start=Get<Button>("Start");cancel=Get<Button>("Cancel");remove=Get<CheckBox>("Remove");jobs=Get<ListBox>("Jobs");
        try{using(var icon=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Evacopy.ico")){if(icon!=null){window.Icon=System.Windows.Media.Imaging.BitmapFrame.Create(icon);using(var brand=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Evacopy.png"))Get<Image>("BrandIcon").Source=System.Windows.Media.Imaging.BitmapFrame.Create(brand);}}}catch{}
        var sourceBrowse=Get<Button>("SourceBrowse");
        sourceBrowse.Click+=delegate{ShowSourceMenu();};
        ((MenuItem)sourceBrowse.ContextMenu.Items[0]).Click+=delegate{ChooseFolder();};
        ((MenuItem)sourceBrowse.ContextMenu.Items[1]).Click+=delegate{ChooseFiles();};
        Get<Button>("Browse").Click+=delegate{try{string path=FolderPicker.Choose(new System.Windows.Interop.WindowInteropHelper(window).Handle,destination.Text,"Evacopy · Select destination folder");if(path!=null)destination.Text=path;}catch(Exception ex){MessageBox.Show(window,ex.Message,"Evacopy");}};
        Get<Button>("AddJob").Click+=delegate{Get<TabControl>("JobTabs").SelectedIndex=0;if(selected!=null&&selected.Length>0)lastSourcePath=selected[0];selected=null;Get<CheckBox>("IncludeFolder").IsEnabled=false;source.Text="Choose a folder or files";remove.IsChecked=false;ShowSourceMenu();};
        start.Click+=async delegate{if(quitting)return;submissionTask=Submit();await submissionTask;};cancel.Click+=async delegate{var row=jobs.SelectedItem as ListBoxItem;if(row==null)return;string id=(string)row.Tag;try{await Task.Run(()=>Store.Send(new Request{Command="cancel",Id=id}));Note("Cancellation requested for selected job.");}catch(Exception ex){Note(ex.Message);}};
        remove.Checked+=delegate{Get<CheckBox>("VerifyCrc").IsChecked=true;Get<CheckBox>("VerifyCrc").IsEnabled=false;};remove.Unchecked+=delegate{Get<CheckBox>("VerifyCrc").IsEnabled=true;};
        jobs.SelectionChanged+=delegate{ShowSelected();};
        refresh=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(600)};refresh.Tick+=async delegate{await Refresh();};
        window.Loaded+=async delegate{if(quitting)return;try{serviceReady=Task.Run(()=>Store.EnsureService());await serviceReady;if(!quitting){refresh.Start();await Refresh();}}catch(Exception ex){Note("Service: "+ex.Message);}};
        DesktopSetup(args.Contains("--startup"));app.Run(window);return 0;
    }
    static T Get<T>(string name){return (T)window.FindName(name);}
    static void UI(Action action){window.Dispatcher.Invoke(action);}
    static void Note(string text){if(window==null)return;status.Text=text;status.ToolTip=text;}
    static string SourcePickerPath(){return selected!=null&&selected.Length>0?selected[0]:lastSourcePath;}
    static void ShowSourceMenu(){var button=Get<Button>("SourceBrowse");button.ContextMenu.PlacementTarget=button;button.ContextMenu.IsOpen=true;}
    static void ChooseFolder(){try{string path=FolderPicker.Choose(new System.Windows.Interop.WindowInteropHelper(window).Handle,SourcePickerPath(),"Evacopy · Select source folder");if(path!=null){selected=new[]{path};lastSourcePath=path;Get<CheckBox>("IncludeFolder").IsEnabled=true;source.Text=path;}}catch(Exception ex){MessageBox.Show(window,ex.Message,"Evacopy");}}
    static void ChooseFiles(){using(var dialog=new Forms.OpenFileDialog{Multiselect=true,InitialDirectory=PickerPaths.ExistingDirectory(SourcePickerPath()),RestoreDirectory=true,Title="Evacopy · Select source files"}){if(selected!=null&&selected.Length==1&&File.Exists(selected[0]))dialog.FileName=Path.GetFileName(selected[0]);if(dialog.ShowDialog(new NativeOwner(new System.Windows.Interop.WindowInteropHelper(window).Handle))==Forms.DialogResult.OK){selected=dialog.FileNames;lastSourcePath=selected[0];Get<CheckBox>("IncludeFolder").IsEnabled=false;source.Text=String.Join("; ",selected);}}}
    sealed class NativeOwner : Forms.IWin32Window {public IntPtr Handle {get;private set;}public NativeOwner(IntPtr handle){Handle=handle;}}
    static async Task Submit(){
        if(selected==null||String.IsNullOrWhiteSpace(destination.Text)){MessageBox.Show(window,"Choose a source and destination.");return;}
        bool deleting=remove.IsChecked==true;
        if(deleting&&MessageBox.Show(window,"Permanently delete source files and empty source folders after every file verifies?","Evacopy · verified move",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        var spec=new JobSpec{Sources=selected.ToArray(),Destination=destination.Text,DeleteSource=deleting,VerifyCrc=Get<CheckBox>("VerifyCrc").IsChecked==true,IncludeSourceFolder=Get<CheckBox>("IncludeFolder").IsEnabled&&Get<CheckBox>("IncludeFolder").IsChecked==true,Threads=Int32.Parse(((ComboBoxItem)Get<ComboBox>("Threads").SelectedItem).Content.ToString()),Priority=((ComboBoxItem)Get<ComboBox>("Priority").SelectedItem).Content.ToString()};
        start.IsEnabled=false;
        try{var response=await Task.Run(()=>{Store.EnsureService();return Store.Send(new Request{Command="submit",Job=spec});});if(response.Error!=null)throw new IOException(response.Error);await Refresh();ListBoxItem row;if(rows.TryGetValue(response.Id,out row))jobs.SelectedItem=row;Get<TabControl>("JobTabs").SelectedIndex=1;Note("Job started. Use + ⚙ to add another.");}
        catch(Exception ex){MessageBox.Show(window,ex.Message,"Evacopy");}finally{start.IsEnabled=true;}
    }
    static async Task Refresh(){if(polling)return;polling=true;try{var reply=await Task.Run(()=>Store.Send(new Request{Command="list"}));if(reply.Error!=null)throw new IOException(reply.Error);foreach(var state in reply.Jobs??new JobState[0]){states[state.Id]=state;ListBoxItem row;if(!rows.TryGetValue(state.Id,out row)){row=new ListBoxItem{Tag=state.Id,Padding=new Thickness(8)};rows.Add(state.Id,row);jobs.Items.Insert(0,row);}row.Content=new TextBlock{Text=state.Title+"  ·  "+state.Phase+"\n"+state.Percent.ToString("N1")+"%  ·  "+state.Speed.ToString("N1")+" MB/s  ·  Worker "+state.WorkerPid+(state.CopyPid>0?" / Copy "+state.CopyPid:"")+(!String.IsNullOrEmpty(state.Error)?"\n"+state.Error:""),TextWrapping=TextWrapping.Wrap,Foreground=Brushes.White};row.ToolTip=state.Detail+"\n"+state.Error;}if(jobs.SelectedItem==null&&jobs.Items.Count>0)jobs.SelectedIndex=0;ShowSelected();}catch(Exception ex){Note("Service unavailable: "+ex.Message);}finally{polling=false;}}
    static void ShowSelected(){var row=jobs.SelectedItem as ListBoxItem;JobState state;if(row==null||!states.TryGetValue((string)row.Tag,out state))return;progress.Value=state.Percent;metrics.Text=state.Percent.ToString("N1")+"% · "+state.Speed.ToString("N1")+" MB/s";status.Text=state.Phase;status.ToolTip=state.Detail+"\n"+state.Error;cancel.IsEnabled=!state.Finished;}
    internal static string Layout {get{using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Theme.xaml"))using(var reader=new StreamReader(stream)){return System.Text.RegularExpressions.Regex.Replace(Xaml,"<Window.Resources/?>[\\s\\S]*?</Window.Resources>|<Window.Resources/>","<Window.Resources>"+reader.ReadToEnd()+"</Window.Resources>");}}}
}

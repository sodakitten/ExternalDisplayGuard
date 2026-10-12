using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ExternalDisplayGuard {
 public class Config {public bool Enabled=true;public string Mode="external";public string TargetPath="";public string TargetName="";public int ExternalScalePercent=200;}
 public static class Storage {
  public static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ExternalDisplayGuard");
  public static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
  public static string ConfigPath {get{return Path.Combine(Root,"config.json");}}
  public static Config Load(){if(!File.Exists(ConfigPath))return new Config();var c=Json.Deserialize<Config>(File.ReadAllText(ConfigPath));if(c==null || (c.Mode!="external"&&c.Mode!="extend"))throw new InvalidDataException("配置文件无效，请检查 "+ConfigPath);return c;}
  public static void Write(string path,object data){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));File.WriteAllText(path,Json.Serialize(data),new System.Text.UTF8Encoding(false));}
  public static void Save(Config c){Directory.CreateDirectory(Root);string temp=ConfigPath+".tmp";Write(temp,c);if(File.Exists(ConfigPath))File.Replace(temp,ConfigPath,ConfigPath+".bak");else File.Move(temp,ConfigPath);}
  public static void Log(string msg){try {Directory.CreateDirectory(Root);string p=Path.Combine(Root,"guard.log");if(File.Exists(p)&&new FileInfo(p).Length>1024*1024){File.Copy(p,p+".1",true);File.Delete(p);}File.AppendAllText(p,DateTimeOffset.Now.ToString("o")+" "+msg+Environment.NewLine);}catch{} }
  public static void Startup(bool enabled){using(var k=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){if(enabled)k.SetValue("ExternalDisplayGuard","\""+Application.ExecutablePath+"\"");else k.DeleteValue("ExternalDisplayGuard",false);}}
  public static bool AutoStart {get{using(var k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){return k!=null&&k.GetValue("ExternalDisplayGuard")!=null;}}}
 }
 class Receiver: NativeWindow,IDisposable {
  public Action<int> Command;public Action Changed;public uint MessageId;
  public Receiver(){MessageId=NativeDisplay.RegisterWindowMessage("ExternalDisplayGuard.Command.v1");CreateHandle(new CreateParams {Caption="ExternalDisplayGuard.Events",Style=0});}
  protected override void WndProc(ref Message m){if(m.Msg==(int)MessageId){if(Command!=null)Command(m.WParam.ToInt32());}else if(m.Msg==0x7e||m.Msg==0x219){if(Changed!=null)Changed();}base.WndProc(ref m);}
  public void Dispose(){DestroyHandle();}
 }
 class Guard:ApplicationContext {
  Config config;Receiver receiver;NotifyIcon tray;Icon trayIcon,windowIcon;System.Windows.Forms.Timer timer;Form settings;Label state;RadioButton external,extend;CheckBox enabled,startup;ComboBox monitorBox,scaleBox;
  List<Monitor> choices=new List<Monitor>();bool updating,locked,suspended,busy;DateTime pauseUntil=DateTime.MinValue,due=DateTime.UtcNow.AddSeconds(2),nextPoll=DateTime.MinValue,lastApply=DateTime.MinValue;
  string status="正在检测显示器…",lastSignature="",lastReport="";int failures;Queue<DateTime> changes=new Queue<DateTime>(),scaleChanges=new Queue<DateTime>();
  public Guard(Config c){
   config=c;receiver=new Receiver();receiver.Command=Command;receiver.Changed=Schedule;
   int smallIcon=NativeDisplay.GetSystemMetrics(49);int traySize=smallIcon<=16?16:smallIcon<=24?24:smallIcon<=32?32:smallIcon<=48?48:64;
   trayIcon=Artwork.Icon(traySize);windowIcon=Artwork.Icon(32);tray=new NotifyIcon{Icon=trayIcon,Text="外接屏守护",Visible=true};
   var menu=new ContextMenuStrip();menu.Items.Add("打开设置",null,(s,e)=>ShowSettings());menu.Items.Add("仅外接屏",null,(s,e)=>SetMode("external"));menu.Items.Add("双屏扩展 · 外接为主屏",null,(s,e)=>SetMode("extend"));
   menu.Items.Add(new ToolStripSeparator());menu.Items.Add("暂停 10 分钟",null,(s,e)=>Pause());menu.Items.Add("恢复守护",null,(s,e)=>Enable());menu.Items.Add("停用并恢复笔记本屏幕",null,(s,e)=>Internal());menu.Items.Add("打开日志",null,(s,e)=>OpenLog());menu.Items.Add("退出本次运行",null,(s,e)=>ExitThread());
   tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>ShowSettings();timer=new System.Windows.Forms.Timer{Interval=500};timer.Tick+=(s,e)=>Tick();timer.Start();
   SystemEvents.PowerModeChanged+=Power;SystemEvents.SessionSwitch+=Session;Storage.Log("启动；模式="+config.Mode+"；启用="+config.Enabled);
  }
  void Schedule(){due=DateTime.UtcNow.AddSeconds(2);}
  void Power(object sender,PowerModeChangedEventArgs e){receiverCommandOnUi(()=>{suspended=e.Mode==PowerModes.Suspend;if(e.Mode==PowerModes.Resume){suspended=false;due=DateTime.UtcNow.AddSeconds(4);}});}
  void Session(object sender,SessionSwitchEventArgs e){receiverCommandOnUi(()=>{if(e.Reason==SessionSwitchReason.SessionLock)locked=true;else if(e.Reason==SessionSwitchReason.SessionUnlock || e.Reason==SessionSwitchReason.ConsoleConnect){locked=false;due=DateTime.UtcNow.AddSeconds(3);}});}
  readonly Queue<Action> pending=new Queue<Action>();void receiverCommandOnUi(Action a){lock(pending)pending.Enqueue(a);}
  void SetStatus(string s){status=s;tray.Text=("外接屏守护 · "+s).Substring(0,Math.Min(63,("外接屏守护 · "+s).Length));if(state!=null&&!state.IsDisposed)state.Text=s;}
  void Report(){try{Storage.Write(Path.Combine(Storage.Root,"status.json"),new {Updated=DateTimeOffset.Now.ToString("o"),ProcessId=Process.GetCurrentProcess().Id,Enabled=config.Enabled,Mode=config.Mode,Target=config.TargetName,ExternalScalePercent=config.ExternalScalePercent,Status=status,PauseUntil=pauseUntil==DateTime.MinValue?null:pauseUntil.ToLocalTime().ToString("o"),AutoStart=Storage.AutoStart});}catch{} }
  void Tick(){
   lock(pending){while(pending.Count>0)pending.Dequeue()();}
   if(busy)return;var now=DateTime.UtcNow;
   if(!config.Enabled){SetStatus("守护已停用");ReportIfChanged();return;}
   if(locked||suspended||NativeDisplay.GetSystemMetrics(0x1000)!=0){SetStatus("等待本机桌面恢复");ReportIfChanged();return;}
   if(now<pauseUntil){SetStatus("已暂停至 "+pauseUntil.ToLocalTime().ToString("HH:mm:ss"));ReportIfChanged();return;}
   if(now<due || now<lastApply.AddSeconds(5))return;
   if(now<nextPoll)return;nextPoll=now.AddSeconds(5);busy=true;
   try{
    var active=NativeDisplay.Query(false);
    string signature=Storage.Json.Serialize(active.Monitors);
    if(signature!=lastSignature){Storage.Log("显示状态变化 "+signature);lastSignature=signature;}
    var target=NativeDisplay.FindTarget(active,config.TargetPath);
    if(target!=null && NativeDisplay.Matches(active,target.Key,config.Mode)){
     if(string.IsNullOrEmpty(config.TargetPath)){config.TargetPath=target.DevicePath;config.TargetName=target.Name;Storage.Save(config);}
     EnsureExternalScale(active,target);
     failures=0;SetStatus((config.Mode=="external"?"仅外接屏":"双屏扩展，外接为主屏")+" · "+target.Name+(config.Mode=="external"&&config.ExternalScalePercent>0?" · "+config.ExternalScalePercent+"%":""));ReportIfChanged();return;
    }
    var all=NativeDisplay.Query(true);target=NativeDisplay.FindTarget(all,config.TargetPath);
    if(target==null){
     if(string.IsNullOrEmpty(config.TargetPath)&&all.Monitors.Count(m=>m.External&&m.Available)>1)SetStatus("有多台外接屏，请在设置中选择");
     else {
      // When the selected screen is gone, ensure the laptop panel is usable; never force a disconnected target.
      var inner=all.Monitors.FirstOrDefault(m=>m.Internal&&m.Available);
      if(inner!=null&&!active.Monitors.Any(m=>m.Active&&m.Available&&(m.Internal||m.External))){lastApply=now;NativeDisplay.InternalOnly();Storage.Log("外接屏离线，恢复内置屏");}
      SetStatus("目标外接屏未连接，保留可用屏幕");
     }ReportIfChanged();return;
    }
    if(string.IsNullOrEmpty(config.TargetPath)){config.TargetPath=target.DevicePath;config.TargetName=target.Name;Storage.Save(config);}
    if(config.Mode=="extend"&&!all.Monitors.Any(m=>m.Internal&&m.Available)){SetStatus("内置屏不可用；请开盖后使用双屏扩展");ReportIfChanged();return;}
    while(changes.Count>0&&now-changes.Peek()>TimeSpan.FromMinutes(1))changes.Dequeue();
    if(changes.Count>=4){pauseUntil=now.AddMinutes(1);Storage.Log("一分钟内反复切换，自动冷却 60 秒");SetStatus("反复切换，冷却 60 秒");ReportIfChanged();return;}
    lastApply=now;changes.Enqueue(now);Storage.Log("纠正显示模式="+config.Mode+"；目标="+target.Name);
    // Keep an active configuration for recovery if the apply call fails part way through.
    try{NativeDisplay.Apply(all,target,config.Mode);}
    catch(Exception applyError){Storage.Log("切换失败："+applyError.Message);try{NativeDisplay.Restore(active);Storage.Log("已恢复切换前配置");}catch(Exception rollbackError){Storage.Log("原配置恢复失败："+rollbackError.Message);try{NativeDisplay.InternalOnly();}catch(Exception fallbackError){Storage.Log("内置屏恢复失败："+fallbackError.Message);}}throw;}
    // Topology and scaling are separate Windows settings. Re-resolve the source after the topology switch.
    var fresh=NativeDisplay.Query(false);var activated=NativeDisplay.FindTarget(fresh,config.TargetPath);
    if(activated!=null&&NativeDisplay.Matches(fresh,activated.Key,config.Mode))EnsureExternalScale(fresh,activated);
    SetStatus("已应用，等待系统确认…");nextPoll=now.AddSeconds(5);ReportIfChanged();
   }catch(Exception e){failures++;Storage.Log("检查失败："+e.Message);SetStatus("暂未成功："+e.Message);nextPoll=now.AddSeconds(failures>=3?60:10);ReportIfChanged();}
   finally{busy=false;}
  }
  void EnsureExternalScale(Snapshot snapshot,Monitor target){
   if(config.Mode!="external"||config.ExternalScalePercent==0)return;
   if(target.ScalePercent==config.ExternalScalePercent)return;
   var now=DateTime.UtcNow;while(scaleChanges.Count>0&&now-scaleChanges.Peek()>TimeSpan.FromMinutes(1))scaleChanges.Dequeue();
   if(scaleChanges.Count>=4){pauseUntil=now.AddMinutes(1);throw new InvalidOperationException("缩放反复被更改，守护冷却 60 秒。");}
   if(NativeDisplay.SetScale(snapshot,target,config.ExternalScalePercent)){
    scaleChanges.Enqueue(now);Storage.Log("恢复外接屏缩放="+config.ExternalScalePercent+"%；目标="+target.Name);target.ScalePercent=config.ExternalScalePercent;
   }
  }
  void ReportIfChanged(){if(status!=lastReport){lastReport=status;Report();}}
  void SetMode(string m){config.Mode=m;config.Enabled=true;Storage.Save(config);pauseUntil=DateTime.MinValue;changes.Clear();scaleChanges.Clear();failures=0;nextPoll=DateTime.MinValue;lastApply=DateTime.MinValue;Schedule();SetStatus("准备切换模式…");Sync();Report();}
  void Enable(){config.Enabled=true;Storage.Save(config);pauseUntil=DateTime.MinValue;changes.Clear();scaleChanges.Clear();nextPoll=DateTime.MinValue;Schedule();Sync();Report();}
  void Pause(){pauseUntil=DateTime.UtcNow.AddMinutes(10);Storage.Log("用户暂停 10 分钟");Sync();Report();}
  void Internal(){config.Enabled=false;Storage.Save(config);try{NativeDisplay.InternalOnly();SetStatus("守护已停用，已恢复笔记本屏幕");}catch(Exception e){SetStatus(e.Message);Storage.Log(e.Message);}Sync();Report();}
  void OpenLog(){string p=Path.Combine(Storage.Root,"guard.log");if(File.Exists(p))Process.Start(new ProcessStartInfo("notepad.exe","\""+p+"\""){UseShellExecute=true});}
  void Command(int command){switch(command){case 1:ShowSettings();break;case 2:SetMode("external");break;case 3:SetMode("extend");break;case 4:Pause();break;case 5:Enable();break;case 6:config.Enabled=false;Storage.Save(config);Sync();Report();break;case 7:Internal();break;case 8:ExitThread();break;}}
  void Sync(){if(settings==null||settings.IsDisposed)return;updating=true;external.Checked=config.Mode=="external";extend.Checked=config.Mode=="extend";enabled.Checked=config.Enabled;startup.Checked=Storage.AutoStart;state.Text=status;if(scaleBox!=null)scaleBox.SelectedIndex=config.ExternalScalePercent==0?0:Array.IndexOf(NativeDisplay.ScaleSteps,config.ExternalScalePercent)+1;updating=false;}
  void RefreshMonitors(){try{choices=NativeDisplay.Query(true).Monitors.Where(x=>x.External&&x.Available&&!string.IsNullOrEmpty(x.DevicePath)).ToList();updating=true;monitorBox.Items.Clear();foreach(var m in choices)monitorBox.Items.Add(m.Name+"  ["+m.GdiName+"]");monitorBox.SelectedIndex=choices.FindIndex(x=>string.Equals(x.DevicePath,config.TargetPath,StringComparison.OrdinalIgnoreCase));updating=false;}catch(Exception e){updating=false;SetStatus(e.Message);}}
  void ShowSettings(){
   if(settings!=null&&!settings.IsDisposed){settings.Show();NativeDisplay.ShowWindow(settings.Handle,5);settings.Activate();RefreshMonitors();Sync();return;}
   settings=new Form{Text="外接屏守护",Icon=windowIcon,ClientSize=new Size(570,485),StartPosition=FormStartPosition.CenterScreen,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,Font=new Font("Microsoft YaHei UI",10),BackColor=Color.White};
   settings.SuspendLayout();settings.AutoScaleMode=AutoScaleMode.Dpi;settings.AutoScaleDimensions=new SizeF(96,96);
   settings.Controls.Add(new Label{Text="外接屏守护",Location=new Point(24,20),Size=new Size(510,34),Font=new Font("Microsoft YaHei UI",18,FontStyle.Bold)});
   state=new Label{Location=new Point(26,65),AutoSize=true,MaximumSize=new Size(515,60),ForeColor=Color.FromArgb(30,90,140)};settings.Controls.Add(state);
   enabled=new CheckBox{Text="自动保持所选显示模式",Location=new Point(26,126),AutoSize=true};settings.Controls.Add(enabled);
   external=new RadioButton{Text="仅外接屏（默认）",Location=new Point(43,162),AutoSize=true};extend=new RadioButton{Text="双屏扩展，外接屏为主屏",Location=new Point(43,195),AutoSize=true};settings.Controls.Add(external);settings.Controls.Add(extend);
   settings.Controls.Add(new Label{Text="仅外接屏缩放",Location=new Point(43,237),AutoSize=true});scaleBox=new ComboBox{Location=new Point(215,233),Size=new Size(212,30),DropDownStyle=ComboBoxStyle.DropDownList};scaleBox.Items.Add("保持当前缩放");foreach(int value in NativeDisplay.ScaleSteps)scaleBox.Items.Add(value+"%");settings.Controls.Add(scaleBox);
   settings.Controls.Add(new Label{Text="固定的外接显示器",Location=new Point(26,284),AutoSize=true});monitorBox=new ComboBox{Location=new Point(26,312),Size=new Size(401,30),DropDownStyle=ComboBoxStyle.DropDownList};settings.Controls.Add(monitorBox);
   var refresh=new Button{Text="刷新",Location=new Point(443,310),Size=new Size(96,32)};refresh.Click+=(s,e)=>RefreshMonitors();settings.Controls.Add(refresh);
   startup=new CheckBox{Text="登录 Windows 后自动启动",Location=new Point(26,357),AutoSize=true};settings.Controls.Add(startup);
   var pause=new Button{Text="暂停 10 分钟",Location=new Point(26,398),Size=new Size(140,36)};pause.Click+=(s,e)=>Pause();settings.Controls.Add(pause);
   var restore=new Button{Text="停用并恢复笔记本屏",Location=new Point(178,398),Size=new Size(206,36)};restore.Click+=(s,e)=>Internal();settings.Controls.Add(restore);
   var log=new Button{Text="查看日志",Location=new Point(397,398),Size=new Size(142,36)};log.Click+=(s,e)=>OpenLog();settings.Controls.Add(log);
   settings.Controls.Add(new Label{Text="关闭此窗口后继续在托盘运行。拔掉外接屏时保留可用屏幕。",Location=new Point(26,450),Size=new Size(520,25),ForeColor=Color.DimGray,Font=new Font("Microsoft YaHei UI",9)});
   enabled.CheckedChanged+=(s,e)=>{if(updating)return;config.Enabled=enabled.Checked;Storage.Save(config);if(config.Enabled)Enable();Sync();Report();};
   external.CheckedChanged+=(s,e)=>{if(!updating&&external.Checked)SetMode("external");};extend.CheckedChanged+=(s,e)=>{if(!updating&&extend.Checked)SetMode("extend");};
   startup.CheckedChanged+=(s,e)=>{if(!updating){Storage.Startup(startup.Checked);Report();}};
   scaleBox.SelectedIndexChanged+=(s,e)=>{if(updating||scaleBox.SelectedIndex<0)return;config.ExternalScalePercent=scaleBox.SelectedIndex==0?0:NativeDisplay.ScaleSteps[scaleBox.SelectedIndex-1];Storage.Save(config);changes.Clear();scaleChanges.Clear();failures=0;nextPoll=DateTime.MinValue;Schedule();Report();};
   monitorBox.SelectedIndexChanged+=(s,e)=>{if(updating||monitorBox.SelectedIndex<0)return;var m=choices[monitorBox.SelectedIndex];config.TargetPath=m.DevicePath;config.TargetName=m.Name;Storage.Save(config);Enable();};
   Sync();RefreshMonitors();settings.ResumeLayout(true);settings.Show();NativeDisplay.ShowWindow(settings.Handle,5);settings.Activate();
  }
  protected override void ExitThreadCore(){Storage.Log("退出");timer.Stop();timer.Dispose();SystemEvents.PowerModeChanged-=Power;SystemEvents.SessionSwitch-=Session;if(settings!=null&&!settings.IsDisposed)settings.Close();tray.Visible=false;tray.Dispose();trayIcon.Dispose();windowIcon.Dispose();receiver.Dispose();base.ExitThreadCore();}
 }
 static class Program {
  public static uint MessageId=NativeDisplay.RegisterWindowMessage("ExternalDisplayGuard.Command.v1");
  [STAThread] static int Main(string[] args){
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   try{
    if(args.Length>=2&&args[0]=="--diagnose"){Storage.Write(args[1],new{Time=DateTimeOffset.Now.ToString("o"),Config=Storage.Load(),Active=NativeDisplay.Query(false).Monitors,All=NativeDisplay.Query(true).Monitors,Screens=Screen.AllScreens.Select(s=>new{s.DeviceName,s.Primary,Bounds=s.Bounds.ToString()}).ToArray(),StructSizes=new{Path=Marshal.SizeOf(typeof(DisplayPath)),Mode=Marshal.SizeOf(typeof(Mode)),TargetName=Marshal.SizeOf(typeof(TargetName))}});return 0;}
    if(args.Length>=2&&args[0]=="--self-test"){SelfTest.Run(args[1]);return 0;}
    int command=0;if(args.Length>0){switch(args[0]){case "--settings":command=1;break;case "--external":command=2;break;case "--extend":command=3;break;case "--pause":command=4;break;case "--enable":command=5;break;case "--disable":command=6;break;case "--internal":command=7;break;case "--stop":command=8;break;default:throw new ArgumentException("未知参数："+args[0]);}}
    bool first;using(var mutex=new Mutex(true,"Local\\ExternalDisplayGuard.v1",out first)){
     if(!first){
      if(command==0)command=1;
      IntPtr window=IntPtr.Zero;for(int attempt=0;attempt<30&&window==IntPtr.Zero;attempt++){window=NativeDisplay.FindWindow(null,"ExternalDisplayGuard.Events");if(window==IntPtr.Zero)Thread.Sleep(100);}
      if(window==IntPtr.Zero||!NativeDisplay.PostMessage(window,MessageId,new IntPtr(command),IntPtr.Zero))throw new InvalidOperationException("运行中的守护程序未响应，请从托盘退出后重试。");
      return 0;
     }
     if(command==8)return 0;var c=Storage.Load();if(command==2||command==3){c.Mode=command==2?"external":"extend";c.Enabled=true;Storage.Save(c);}if(command==6){c.Enabled=false;Storage.Save(c);}if(command==7){c.Enabled=false;Storage.Save(c);NativeDisplay.InternalOnly();return 0;}
     using(var app=new Guard(c)){if(command==1||command==4||command==5)NativeDisplay.PostMessage(new IntPtr(0xffff),MessageId,new IntPtr(command),IntPtr.Zero);Application.Run(app);}mutex.ReleaseMutex();
    }return 0;
   }catch(Exception e){Storage.Log("致命错误："+e);if(args.Length>=2&&(args[0]=="--diagnose"||args[0]=="--self-test")){Storage.Write(args[1],new{Error=e.ToString()});return 1;}MessageBox.Show(e.Message,"外接屏守护",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
  }
 }
}

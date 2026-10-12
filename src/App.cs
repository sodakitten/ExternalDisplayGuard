using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ExternalDisplayGuard {
 public static class Storage {
  public static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ExternalDisplayGuard");
  public static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
  public static string ConfigPath {get{return Path.Combine(Root,"config.json");}}
  public static Config Load(){return File.Exists(ConfigPath)?ConfigCodec.Parse(File.ReadAllText(ConfigPath)):new Config();}
  public static void Write(string path,object data){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));File.WriteAllText(path,Json.Serialize(data),new System.Text.UTF8Encoding(false));}
  public static void Save(Config c){Directory.CreateDirectory(Root);string temp=ConfigPath+".tmp";Write(temp,c);if(File.Exists(ConfigPath))File.Replace(temp,ConfigPath,ConfigPath+".bak");else File.Move(temp,ConfigPath);}
  public static void Log(string msg){try{Directory.CreateDirectory(Root);string p=Path.Combine(Root,"guard.log");if(File.Exists(p)&&new FileInfo(p).Length>1024*1024){File.Copy(p,p+".1",true);File.Delete(p);}File.AppendAllText(p,DateTimeOffset.Now.ToString("o")+" "+msg+Environment.NewLine);}catch{}}
  public static void Startup(bool enabled){using(var k=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){if(enabled)k.SetValue("ExternalDisplayGuard","\""+Application.ExecutablePath+"\"");else k.DeleteValue("ExternalDisplayGuard",false);}}
  public static bool AutoStart {get{using(var k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))return k!=null&&k.GetValue("ExternalDisplayGuard")!=null;}}
 }
 class Receiver:NativeWindow,IDisposable {
  public Action<int> Command;public Action Changed;public readonly uint MessageId;
  public Receiver(){MessageId=NativeDisplay.RegisterWindowMessage("ExternalDisplayGuard.Command.v2");CreateHandle(new CreateParams{Caption="ExternalDisplayGuard.Events.v2",Style=0});}
  protected override void WndProc(ref Message m){if(m.Msg==(int)MessageId){if(Command!=null)Command(m.WParam.ToInt32());}else if(m.Msg==0x7e||m.Msg==0x219){if(Changed!=null)Changed();}base.WndProc(ref m);}
  public void Dispose(){DestroyHandle();}
 }
 class ScreenChoice {
  public string Id,Name;public override string ToString(){return Name;}
 }
 class Guard:ApplicationContext {
  readonly Config config;readonly NativeBackend backend=new NativeBackend();readonly DisplayCoordinator engine;
  readonly Receiver receiver;readonly NotifyIcon tray;readonly Icon trayIcon,windowIcon;readonly System.Windows.Forms.Timer timer;
  Form settings;Label status,preview;DataGridView grid;ComboBox primary,presets;TextBox profileName;CheckBox enabled,startup;
  Button apply,confirm,cancel;bool updating,locked,suspended,busy;DateTime due=DateTime.UtcNow.AddSeconds(2),next=DateTime.MinValue;string lastStatus="";
  Snapshot inventory;List<ScreenPreference> draftKnown=new List<ScreenPreference>();
  public Guard(Config c){
   config=c;engine=new DisplayCoordinator(c,backend,Storage.Log);engine.Changed=()=>{Storage.Save(config);RebuildMenu();Sync();};
   receiver=new Receiver();receiver.Command=command=>Safe(()=>Command(command));receiver.Changed=()=>{due=DateTime.UtcNow.AddSeconds(2);next=DateTime.MinValue;};
   int pixels=NativeDisplay.GetSystemMetrics(49);trayIcon=Artwork.Icon(pixels<=16?16:pixels<=24?24:pixels<=32?32:pixels<=48?48:64);windowIcon=Artwork.Icon(32);
   tray=new NotifyIcon{Icon=trayIcon,Text="屏幕守护",Visible=true};tray.DoubleClick+=(s,e)=>Safe(ShowSettings);RebuildMenu();
   timer=new System.Windows.Forms.Timer{Interval=500};timer.Tick+=(s,e)=>Tick();timer.Start();
   SystemEvents.PowerModeChanged+=Power;SystemEvents.SessionSwitch+=Session;Storage.Log("v1.1 启动；配置版本="+config.SchemaVersion);
  }
  void RebuildMenu(){
   if(tray==null)return;var menu=new ContextMenuStrip();menu.Items.Add("选择亮哪些屏幕…",null,(s,e)=>Safe(ShowSettings));
   menu.Items.Add("仅所选主屏",null,(s,e)=>Safe(()=>Quick(false,false)));menu.Items.Add("全部物理屏幕扩展",null,(s,e)=>Safe(()=>Quick(true,false)));
   if(config.Saved.Count>0){menu.Items.Add(new ToolStripSeparator());foreach(var item in config.Saved){var copy=item.Clone();menu.Items.Add(copy.Name,null,(s,e)=>Safe(()=>PreviewProfile(copy)));}}
   menu.Items.Add(new ToolStripSeparator());menu.Items.Add("暂停 10 分钟",null,(s,e)=>{engine.Pause(DateTime.UtcNow);Sync();});
   menu.Items.Add("恢复守护",null,(s,e)=>Safe(()=>{engine.Enable();next=DateTime.MinValue;}));menu.Items.Add("停用守护，保留当前显示",null,(s,e)=>{engine.Disable();Sync();});
   menu.Items.Add("查看日志",null,(s,e)=>OpenLog());menu.Items.Add("退出本次运行",null,(s,e)=>ExitThread());
   var old=tray.ContextMenuStrip;tray.ContextMenuStrip=menu;if(old!=null)old.Dispose();
  }
  void Power(object sender,PowerModeChangedEventArgs e){lock(pending){pending.Enqueue(()=>{if(e.Mode==PowerModes.Suspend)suspended=true;if(e.Mode==PowerModes.Resume){suspended=false;due=DateTime.UtcNow.AddSeconds(4);next=DateTime.MinValue;}});}}
  void Session(object sender,SessionSwitchEventArgs e){lock(pending){pending.Enqueue(()=>{if(e.Reason==SessionSwitchReason.SessionLock)locked=true;if(e.Reason==SessionSwitchReason.SessionUnlock||e.Reason==SessionSwitchReason.ConsoleConnect){locked=false;due=DateTime.UtcNow.AddSeconds(2);next=DateTime.MinValue;}});}}
  readonly Queue<Action> pending=new Queue<Action>();
  void Tick(){
   lock(pending){while(pending.Count>0)pending.Dequeue()();}
   if(busy)return;var now=DateTime.UtcNow;
   if(!locked&&!suspended&&now>=due&&(now>=next||engine.PreviewActive&&now>=engine.PreviewDeadline)){
    busy=true;try{engine.Step(now);next=now.AddSeconds(5);}finally{busy=false;}
   }
   string text=locked||suspended?"等待本机桌面恢复":engine.Status;
   if(text!=lastStatus){lastStatus=text;tray.Text=("屏幕守护 · "+text).Substring(0,Math.Min(63,("屏幕守护 · "+text).Length));
    try{Storage.Write(Path.Combine(Storage.Root,"status.json"),new{Updated=DateTimeOffset.Now.ToString("o"),ProcessId=Process.GetCurrentProcess().Id,Enabled=config.Enabled,Profile=config.Current.Name,Selected=config.Current.Screens.Select(x=>new{x.Name,x.ScalePercent}).ToArray(),Status=text,Preview=engine.PreviewActive,AutoStart=Storage.AutoStart});}catch(Exception e){Storage.Log("状态文件写入失败："+e.Message);}}
   Sync();
  }
  void Safe(Action action){try{action();}catch(Exception e){Storage.Log(e.ToString());MessageBox.Show(settings,e.Message,"屏幕守护",MessageBoxButtons.OK,MessageBoxIcon.Information);}}
  void Sync(){
   if(settings==null||settings.IsDisposed)return;updating=true;status.Text=lastStatus.Length==0?engine.Status:lastStatus;enabled.Checked=config.Enabled;startup.Checked=Storage.AutoStart;
   bool testing=engine.PreviewActive;confirm.Visible=cancel.Visible=testing;preview.Visible=testing;
   preview.Text=testing?"请确认画面可见；"+Math.Max(0,(int)Math.Ceiling((engine.PreviewDeadline-DateTime.UtcNow).TotalSeconds))+" 秒后自动还原":"";
   grid.Enabled=primary.Enabled=presets.Enabled=profileName.Enabled=!testing;
   apply.Enabled=!testing&&grid.Rows.Cast<DataGridViewRow>().Any(r=>Convert.ToBoolean(r.Cells[0].Value));updating=false;
  }
  void RefreshScreens(DisplayProfile selection){
   inventory=backend.Query(true);if(config.Legacy!=null&&ConfigCodec.ResolveLegacy(config,inventory)){Storage.Save(config);selection=config.Current.Clone();}
   if(selection==null)selection=config.Current.Clone();draftKnown=config.Known.Select(x=>x.Clone()).ToList();
   var available=inventory.Monitors.Where(m=>MonitorIdentity.IsPhysical(m)&&m.Available).ToList();updating=true;grid.Rows.Clear();
   foreach(var m in available){
    var selected=selection.Screens.FirstOrDefault(p=>MonitorIdentity.Resolve(p,new[]{m})!=null);
    var pref=selected==null?ScreenPreference.From(m):selected.Clone();pref.Name=m.Name;
    if(!draftKnown.Any(p=>MonitorIdentity.Resolve(p,new[]{m})!=null))draftKnown.Add(ScreenPreference.From(m));
    bool check=selected!=null||(selection.Screens.Count==0&&m.Active);AddRow(pref,check,m.Active?(m.X==0&&m.Y==0?"在线 · 当前主屏":"在线"):"在线 · 未启用",m.ScalePercent);
   }
   foreach(var pref in selection.Screens.Where(p=>MonitorIdentity.Resolve(p,available)==null))AddRow(pref.Clone(),true,"暂时离线",0);
   profileName.Text=selection.Name;var actualPrimary=available.FirstOrDefault(m=>m.Active&&m.X==0&&m.Y==0);string wanted=selection.Screens.Count==0&&actualPrimary!=null?MonitorIdentity.Id(actualPrimary):selection.PrimaryId;updating=false;UpdatePrimary(wanted);Sync();
  }
  void AddRow(ScreenPreference pref,bool check,string state,int currentScale){
   int scaleIndex=pref.ScalePercent==0?0:Array.IndexOf(NativeDisplay.ScaleSteps,pref.ScalePercent)+1;
   var scales=ScaleLabels();int index=grid.Rows.Add(check,pref.Name,state,currentScale>0?currentScale+"%":"—",scales[scaleIndex],RotationLabel(pref.Rotation));grid.Rows[index].Tag=pref;
  }
  static string[] ScaleLabels(){return new[]{"保持当前"}.Concat(NativeDisplay.ScaleSteps.Select(x=>x+"%")).ToArray();}
  static string RotationLabel(int r){return new[]{"保持当前","横屏","竖屏（右转）","横屏（反向）","竖屏（左转）"}[r];}
  void UpdatePrimary(string wanted){
   if(updating)return;updating=true;string old=wanted??(primary.SelectedItem as ScreenChoice==null?"":((ScreenChoice)primary.SelectedItem).Id);
   primary.Items.Clear();foreach(DataGridViewRow row in grid.Rows)if(Convert.ToBoolean(row.Cells[0].Value)){var p=(ScreenPreference)row.Tag;primary.Items.Add(new ScreenChoice{Id=p.Id,Name=p.Name});}
   int index=-1;for(int i=0;i<primary.Items.Count;i++)if(MonitorIdentity.Equal(((ScreenChoice)primary.Items[i]).Id,old))index=i;
   if(index<0&&primary.Items.Count>0)index=0;primary.SelectedIndex=index;updating=false;Sync();
  }
  DisplayProfile Draft(){
   grid.EndEdit();var p=new DisplayProfile{Name=string.IsNullOrWhiteSpace(profileName.Text)?"当前选择":profileName.Text.Trim()};
   foreach(DataGridViewRow row in grid.Rows)if(Convert.ToBoolean(row.Cells[0].Value)){
    var pref=((ScreenPreference)row.Tag).Clone();string value=Convert.ToString(row.Cells[4].Value);pref.ScalePercent=value=="保持当前"?0:int.Parse(value.TrimEnd('%'));
    pref.Rotation=Array.IndexOf(new[]{"保持当前","横屏","竖屏（右转）","横屏（反向）","竖屏（左转）"},Convert.ToString(row.Cells[5].Value));p.Screens.Add(pref);
   }
   var main=primary.SelectedItem as ScreenChoice;if(main!=null)p.PrimaryId=main.Id;p.Validate();return p;
  }
  void PreviewProfile(DisplayProfile p){
   ShowSettings();RefreshScreens(p);engine.BeginPreview(p,draftKnown,DateTime.UtcNow);next=DateTime.MinValue;Sync();
  }
  void Quick(bool all,bool legacyPanel){
   var snapshot=backend.Query(true);var physical=snapshot.Monitors.Where(m=>MonitorIdentity.IsPhysical(m)&&m.Available).ToList();
   if(physical.Count==0)throw new InvalidOperationException("当前没有可用物理显示器。");
   var previous=config.Current.Screens.FirstOrDefault(p=>MonitorIdentity.Equal(p.Id,config.Current.PrimaryId));
   var main=previous==null?null:MonitorIdentity.Resolve(previous,physical);
   if(legacyPanel)main=physical.FirstOrDefault(m=>m.Internal)??main;
   main=main??physical.FirstOrDefault(m=>m.Active&&m.X==0&&m.Y==0)??physical[0];
   var profile=new DisplayProfile{Name=all?"全部屏幕":"仅所选主屏"};
   foreach(var m in all?physical:new List<Monitor>{main}){var pref=ScreenPreference.From(m);var old=config.Current.Screens.FirstOrDefault(p=>MonitorIdentity.Resolve(p,new[]{m})!=null);
    pref.ScalePercent=old==null?0:old.ScalePercent;profile.Screens.Add(pref);if(m.Key==main.Key)profile.PrimaryId=pref.Id;}
   PreviewProfile(profile);
  }
  void SavePreset(){
   if(engine.PreviewActive)throw new InvalidOperationException("请先保留或还原当前画面。");
   if(config.Current.Screens.Count==0)throw new InvalidOperationException("请先应用屏幕选择。");string name=profileName.Text.Trim();
   if(name.Length==0)throw new InvalidOperationException("请输入常用组合名称。");
   var copy=config.Current.Clone();copy.Name=name;config.Saved.RemoveAll(x=>MonitorIdentity.Equal(x.Name,name));config.Saved.Add(copy);Storage.Save(config);LoadPresets();RebuildMenu();
  }
  void LoadPresets(){updating=true;presets.Items.Clear();foreach(var p in config.Saved)presets.Items.Add(p.Name);presets.SelectedIndex=-1;updating=false;}
  void CaptureLayout(){
   var p=Draft();var current=backend.Query(false);foreach(var pref in p.Screens){var m=MonitorIdentity.Resolve(pref,current.Monitors);if(m==null)throw new InvalidOperationException("请先在 Windows 中启用所有选中的屏幕，再记住布局。");pref.X=m.X;pref.Y=m.Y;pref.HasPosition=true;}
   RefreshScreens(p);
  }
  void Identify(){
   if(inventory==null)return;foreach(var m in inventory.Monitors.Where(m=>m.Active&&MonitorIdentity.IsPhysical(m))){
    var label=new Form{FormBorderStyle=FormBorderStyle.None,TopMost=true,ShowInTaskbar=false,BackColor=Color.FromArgb(15,40,65),Size=new Size(300,100),StartPosition=FormStartPosition.Manual};
    var screen=Screen.AllScreens.FirstOrDefault(x=>MonitorIdentity.Equal(x.DeviceName,m.GdiName));if(screen==null){label.Dispose();continue;}
    label.Location=new Point(screen.Bounds.X+40,screen.Bounds.Y+40);label.Controls.Add(new Label{Text=m.Name,ForeColor=Color.White,Font=new Font("Microsoft YaHei UI",18,FontStyle.Bold),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter});
    var close=new System.Windows.Forms.Timer{Interval=2500};close.Tick+=(s,e)=>{close.Stop();label.Close();close.Dispose();};label.FormClosed+=(s,e)=>close.Dispose();label.Show();close.Start();
   }
  }
  Button Button(FlowLayoutPanel parent,string text,Action action){var b=new Button{Text=text,AutoSize=true,Margin=new Padding(4),Padding=new Padding(6,4,6,4)};b.Click+=(s,e)=>Safe(action);parent.Controls.Add(b);return b;}
  void ShowSettings(){
   if(settings!=null&&!settings.IsDisposed){settings.Show();NativeDisplay.ShowWindow(settings.Handle,5);settings.Activate();return;}
   settings=new Form{Text="屏幕守护 · 多屏组合",Icon=windowIcon,ClientSize=new Size(900,600),MinimumSize=new Size(720,500),StartPosition=FormStartPosition.CenterScreen,Font=new Font("Microsoft YaHei UI",10),BackColor=Color.White};
   settings.SuspendLayout();settings.AutoScaleMode=AutoScaleMode.Dpi;settings.AutoScaleDimensions=new SizeF(96,96);
   var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(16)};
   root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
   root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));settings.Controls.Add(root);
   status=new Label{Text=engine.Status,AutoSize=true,MaximumSize=new Size(840,65),ForeColor=Color.FromArgb(30,90,140),Margin=new Padding(0,5,0,10)};root.Controls.Add(status,0,0);
   var top=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};root.Controls.Add(top,0,1);
   enabled=new CheckBox{Text="自动保持已应用组合",AutoSize=true,Margin=new Padding(4,12,20,4)};top.Controls.Add(enabled);
   startup=new CheckBox{Text="登录后自动启动",AutoSize=true,Margin=new Padding(4,12,15,4)};top.Controls.Add(startup);
   Button(top,"刷新屏幕",()=>{if(engine.PreviewActive)return;DisplayProfile draft=null;try{draft=Draft();}catch{}RefreshScreens(draft);});
   Button(top,"识别已亮屏幕",Identify);
   grid=new DataGridView{Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.White,SelectionMode=DataGridViewSelectionMode.CellSelect};
   grid.Columns.Add(new DataGridViewCheckBoxColumn{Name="Enabled",HeaderText="亮起",FillWeight=38});
   grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Name",HeaderText="显示器",ReadOnly=true,FillWeight=125});
   grid.Columns.Add(new DataGridViewTextBoxColumn{Name="State",HeaderText="连接状态",ReadOnly=true,FillWeight=85});
   grid.Columns.Add(new DataGridViewTextBoxColumn{Name="CurrentScale",HeaderText="当前缩放",ReadOnly=true,FillWeight=60});
   var scale=new DataGridViewComboBoxColumn{Name="Scale",HeaderText="保持缩放",FillWeight=80};scale.Items.AddRange(ScaleLabels());grid.Columns.Add(scale);
   var rotation=new DataGridViewComboBoxColumn{Name="Rotation",HeaderText="方向",FillWeight=85};rotation.Items.AddRange(new[]{"保持当前","横屏","竖屏（右转）","横屏（反向）","竖屏（左转）"});grid.Columns.Add(rotation);root.Controls.Add(grid,0,2);
   grid.CurrentCellDirtyStateChanged+=(s,e)=>{if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
   grid.CellValueChanged+=(s,e)=>{if(!updating&&e.ColumnIndex==0)UpdatePrimary(null);};
   grid.DataError+=(s,e)=>{e.ThrowException=false;};
   var selection=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};root.Controls.Add(selection,0,3);
   selection.Controls.Add(new Label{Text="主屏",AutoSize=true,Margin=new Padding(4,14,4,4)});
   primary=new ComboBox{Width=240,DropDownStyle=ComboBoxStyle.DropDownList,Margin=new Padding(4,8,10,4)};selection.Controls.Add(primary);
   Button(selection,"全选",()=>{if(engine.PreviewActive)return;updating=true;foreach(DataGridViewRow row in grid.Rows)row.Cells[0].Value=true;updating=false;UpdatePrimary(null);});
   Button(selection,"仅亮主屏",()=>{if(engine.PreviewActive)return;var selected=primary.SelectedItem as ScreenChoice;if(selected==null)return;updating=true;foreach(DataGridViewRow row in grid.Rows)row.Cells[0].Value=MonitorIdentity.Equal(((ScreenPreference)row.Tag).Id,selected.Id);updating=false;UpdatePrimary(selected.Id);});
   Button(selection,"记住当前布局",CaptureLayout);apply=Button(selection,"应用选择",()=>{engine.BeginPreview(Draft(),draftKnown,DateTime.UtcNow);Sync();});
   var saved=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};root.Controls.Add(saved,0,4);
   saved.Controls.Add(new Label{Text="组合名称",AutoSize=true,Margin=new Padding(4,14,4,4)});profileName=new TextBox{Width=160,Margin=new Padding(4,8,8,4)};saved.Controls.Add(profileName);
   Button(saved,"保存已应用组合",SavePreset);presets=new ComboBox{Width=180,DropDownStyle=ComboBoxStyle.DropDownList,Margin=new Padding(4,8,4,4)};saved.Controls.Add(presets);
   Button(saved,"载入",()=>{if(!engine.PreviewActive&&presets.SelectedIndex>=0)RefreshScreens(config.Saved[presets.SelectedIndex].Clone());});
   Button(saved,"删除常用",()=>{if(engine.PreviewActive||presets.SelectedIndex<0)return;config.Saved.RemoveAt(presets.SelectedIndex);Storage.Save(config);LoadPresets();RebuildMenu();});
   var bottom=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};root.Controls.Add(bottom,0,5);
   preview=new Label{AutoSize=true,ForeColor=Color.FromArgb(160,75,10),Margin=new Padding(4,14,4,4)};bottom.Controls.Add(preview);
   confirm=Button(bottom,"保留画面",()=>{engine.ConfirmPreview(DateTime.UtcNow);RefreshScreens(config.Current.Clone());Sync();});
   cancel=Button(bottom,"还原",()=>{engine.CancelPreview();RefreshScreens(config.Current.Clone());Sync();});
   Button(bottom,"暂停 10 分钟",()=>{engine.Pause(DateTime.UtcNow);Sync();});Button(bottom,"查看日志",OpenLog);
   enabled.CheckedChanged+=(s,e)=>{if(updating)return;Safe(()=>{if(enabled.Checked){if(config.Current.Screens.Count==0&&config.Legacy==null)throw new InvalidOperationException("请先选择屏幕并应用。");engine.Enable();next=DateTime.MinValue;}else engine.Disable();Sync();});};
   startup.CheckedChanged+=(s,e)=>{if(!updating)Safe(()=>Storage.Startup(startup.Checked));};
   settings.FormClosing+=(s,e)=>{if(engine.PreviewActive)engine.CancelPreview();};
   settings.Shown+=(s,e)=>{var area=Screen.FromControl(settings).WorkingArea;int width=Math.Max(300,area.Width-20),height=Math.Max(250,area.Height-20);settings.MinimumSize=new Size(Math.Min(settings.MinimumSize.Width,width),Math.Min(settings.MinimumSize.Height,height));settings.Size=new Size(Math.Min(settings.Width,width),Math.Min(settings.Height,height));};
   LoadPresets();RefreshScreens(null);settings.ResumeLayout(true);settings.Show();NativeDisplay.ShowWindow(settings.Handle,5);settings.Activate();Sync();
  }
  void OpenLog(){var path=Path.Combine(Storage.Root,"guard.log");if(File.Exists(path))Process.Start(new ProcessStartInfo("notepad.exe","\""+path+"\""){UseShellExecute=true});}
  public void Command(int command){switch(command){case 1:ShowSettings();break;case 2:Quick(false,false);break;case 3:Quick(true,false);break;case 4:engine.Pause(DateTime.UtcNow);break;case 5:engine.Enable();next=DateTime.MinValue;break;case 6:engine.Disable();break;case 7:Quick(false,true);break;case 8:ExitThread();break;}Sync();}
  protected override void ExitThreadCore(){if(engine.PreviewActive)engine.CancelPreview();timer.Stop();timer.Dispose();SystemEvents.PowerModeChanged-=Power;SystemEvents.SessionSwitch-=Session;if(settings!=null&&!settings.IsDisposed)settings.Close();tray.Visible=false;tray.Dispose();trayIcon.Dispose();windowIcon.Dispose();receiver.Dispose();Storage.Log("退出");base.ExitThreadCore();}
 }
 static class Program {
  [STAThread] static int Main(string[] args){
   // Offline checks run before any UI, message registration, configuration load or real display backend.
   if(args.Length==2&&(args[0]=="--self-test"||args[0]=="--offline-tests"))return OfflineChecks.Run(args[1]);
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   try{
    if(args.Length==2&&args[0]=="--diagnose"){Storage.Write(args[1],new{Time=DateTimeOffset.Now.ToString("o"),Config=Storage.Load(),Active=NativeDisplay.Query(false).Monitors,All=NativeDisplay.Query(true).Monitors});return 0;}
    int command=0;if(args.Length>0){switch(args[0]){case "--settings":command=1;break;case "--single":case "--external":command=2;break;case "--all":case "--extend":command=3;break;case "--pause":command=4;break;case "--enable":command=5;break;case "--disable":command=6;break;case "--internal":command=7;break;case "--stop":command=8;break;default:throw new ArgumentException("未知参数："+args[0]);}}
    bool first;using(var mutex=new Mutex(true,"Local\\ExternalDisplayGuard.v1",out first)){
     if(!first){
      if(command==0)command=1;IntPtr window=IntPtr.Zero;for(int attempt=0;attempt<30&&window==IntPtr.Zero;attempt++){window=NativeDisplay.FindWindow(null,"ExternalDisplayGuard.Events.v2");if(window==IntPtr.Zero)Thread.Sleep(100);}
      if(window==IntPtr.Zero){if(command==8){var old=NativeDisplay.FindWindow(null,"ExternalDisplayGuard.Events");if(old!=IntPtr.Zero){NativeDisplay.PostMessage(old,NativeDisplay.RegisterWindowMessage("ExternalDisplayGuard.Command.v1"),new IntPtr(8),IntPtr.Zero);return 0;}}throw new InvalidOperationException("旧版本仍在运行，请先从托盘退出旧版本，或使用更新脚本。");}
      if(!NativeDisplay.PostMessage(window,NativeDisplay.RegisterWindowMessage("ExternalDisplayGuard.Command.v2"),new IntPtr(command),IntPtr.Zero))throw new InvalidOperationException("运行中的程序未响应。");return 0;
     }
     if(command==8)return 0;var c=Storage.Load();if(command==6)c.Enabled=false;
     using(var app=new Guard(c)){if(command!=0)app.Command(command);else if(c.Current.Screens.Count==0&&c.Legacy==null)app.Command(1);Application.Run(app);}mutex.ReleaseMutex();
    }return 0;
   }catch(Exception e){Storage.Log(e.ToString());if(args.Length==2&&args[0]=="--diagnose"){Storage.Write(args[1],new{Error=e.ToString()});return 1;}MessageBox.Show(e.Message,"屏幕守护",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
  }
 }
}

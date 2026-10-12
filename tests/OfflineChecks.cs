using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;

namespace ExternalDisplayGuard {
 // This fake never calls NativeBackend, user32, WMI, Registry or application configuration storage.
 public class FakeDisplayBackend:IDisplayBackend {
  public Snapshot State;public bool RemoteMode,FailApply,FailRestore,CorruptApply;public int Applied,Restored,ScaleCalls,Queries,RestoreConnections;
  public readonly HashSet<string> BadScales=new HashSet<string>();public bool Remote {get{return RemoteMode;}}
  public FakeDisplayBackend(Snapshot state){State=Copy(state);}
  public static Snapshot Copy(Snapshot s){return new Snapshot{Paths=(DisplayPath[])s.Paths.Clone(),Modes=(Mode[])s.Modes.Clone(),Monitors=s.Monitors.Select(x=>x.Clone()).ToList(),Connections=s.Connections==null?null:s.Connections.Select(x=>x.Clone()).ToList()};}
  public Snapshot Query(bool all){Queries++;var s=Copy(State);if(!all){s.Paths=s.Paths.Where(p=>(p.Flags&1)!=0).ToArray();s.Monitors=s.Monitors.Where(m=>m.Active).ToList();}return s;}
  public void Disconnect(string id){var m=State.Monitors.Single(x=>x.StableId==id);m.Available=m.Active=false;Rebuild(new Dictionary<string,DisplayPath>());}
  void Rebuild(Dictionary<string,DisplayPath> chosen){
   var modes=new List<Mode>();var assignments=new Dictionary<string,DisplayPath>();
   foreach(var m in State.Monitors.Where(x=>x.Active&&x.Available)){
    DisplayPath p;if(!chosen.TryGetValue(m.Key,out p))p=State.Paths.First(x=>NativeDisplay.Key(x)==m.Key&&(x.Flags&1)!=0);
    p.Source.ModeIndex=(uint)modes.Count;modes.Add(new Mode{Type=1,Id=p.Source.Id,Adapter=p.Source.Adapter,Width=m.Width,Height=m.Height,X=m.X,Y=m.Y});
    p.Target.ModeIndex=(uint)modes.Count;modes.Add(new Mode{Type=2,Id=p.Target.Id,Adapter=p.Target.Adapter,U0=12345});p.Flags=1;assignments[m.Key]=p;
   }
   for(int i=0;i<State.Paths.Length;i++){
    var p=State.Paths[i];var m=State.Monitors.First(x=>x.Key==NativeDisplay.Key(p));p.Target.Available=m.Available?1:0;p.Flags=0;p.Source.ModeIndex=p.Target.ModeIndex=uint.MaxValue;
    DisplayPath active;if(assignments.TryGetValue(m.Key,out active)&&NativeDisplay.SourceKey(active)==NativeDisplay.SourceKey(p))p=active;State.Paths[i]=p;
   }State.Modes=modes.ToArray();
  }
  public void Apply(DisplayPlan plan){
   Applied++;var selected=new List<Monitor>();
   foreach(var m in State.Monitors){m.Active=plan.Selection.Screens.Any(x=>x.Key==m.Key)&&m.Available;if(!m.Active)continue;
    var pref=plan.Selection.Preferences[m.Key];if(pref.Rotation>0){if(m.Rotation>0&&(m.Rotation%2)!=(pref.Rotation%2)){uint width=m.Width;m.Width=m.Height;m.Height=width;}m.Rotation=pref.Rotation;}
    if(m.Width==0)m.Width=1920;if(m.Height==0)m.Height=1080;selected.Add(m);
   }
   var selection=new Selection{Screens=selected,Primary=selected.First(x=>x.Key==plan.Selection.Primary.Key),Preferences=plan.Selection.Preferences};var positions=TopologyPlanner.Layout(selection);
   foreach(var m in selected){m.X=positions[m.Key][0];m.Y=positions[m.Key][1];}
   var assigned=plan.Paths.ToDictionary(NativeDisplay.Key,p=>p);
   if(CorruptApply){foreach(var m in selected.Where(x=>x.Key!=selection.Primary.Key))m.Active=false;}
   Rebuild(assigned);if(FailApply)throw new InvalidOperationException("simulated driver failure after partial apply");
  }
  public void Restore(Snapshot before){
   Restored++;RestoreConnections=before.Connections==null?0:before.Connections.Count;if(FailRestore)throw new InvalidOperationException("simulated restore failure");var assignments=new Dictionary<string,DisplayPath>();
   foreach(var m in State.Monitors){var old=before.Monitors.FirstOrDefault(x=>x.Key==m.Key&&x.Active);m.Active=old!=null&&m.Available;if(old==null||!m.Available)continue;
    m.X=old.X;m.Y=old.Y;m.Width=old.Width;m.Height=old.Height;m.Rotation=old.Rotation;m.ScalePercent=old.ScalePercent;assignments[m.Key]=before.Paths.First(x=>NativeDisplay.Key(x)==m.Key&&(x.Flags&1)!=0);}
   Rebuild(assignments);
  }
  public bool SetScale(Snapshot snapshot,Monitor monitor,int percent){ScaleCalls++;if(BadScales.Contains(monitor.StableId))throw new InvalidOperationException("simulated unsupported scale");
   var m=State.Monitors.Single(x=>x.Key==monitor.Key&&x.Available&&x.Active);if(m.ScalePercent==percent)return false;m.ScalePercent=percent;return true;}
 }
 public static class OfflineChecks {
  static List<string> passed;static void Check(bool value,string message){if(!value)throw new Exception("FAILED: "+message);passed.Add(message);}
  static readonly DateTime Now=new DateTime(2026,10,12,0,0,0,DateTimeKind.Utc);
  public static Snapshot Fixture(int count,bool laptop){
   var s=new Snapshot{Monitors=new List<Monitor>(),Modes=new Mode[count*2]};var paths=new List<DisplayPath>();var adapter=new Luid{Low=9};int x=0;
   for(int i=0;i<count;i++){
    int technology=laptop&&i==0?11:5;string id=((char)('A'+i)).ToString();
    var target=new PathTarget{Adapter=adapter,Id=(uint)i+1,Technology=technology,Available=1,Rotation=1,Scaling=1,Refresh=new Rational{Numerator=160,Denominator=1}};
    var m=new Monitor{Key=adapter+":"+target.Id,Name="Mock "+id,DevicePath="fake://"+id,StableId=id,Fingerprint="serial:"+id,Model="model:"+id,Technology=technology,Rotation=1,Internal=NativeDisplay.IsInternal(technology),External=NativeDisplay.IsExternal(technology),Active=true,Available=true,Width=1920,Height=1080,X=x,Y=0,ScalePercent=100,RefreshHz=160};s.Monitors.Add(m);x+=1920;
    s.Modes[i*2]=new Mode{Type=1,Id=(uint)i,Adapter=adapter,Width=m.Width,Height=m.Height,X=m.X,Y=m.Y};s.Modes[i*2+1]=new Mode{Type=2,Id=target.Id,Adapter=adapter,U0=12345};
    for(int source=0;source<count;source++){
     var p=new DisplayPath{Source=new PathSource{Adapter=adapter,Id=(uint)source,ModeIndex=uint.MaxValue},Target=target};p.Target.ModeIndex=uint.MaxValue;
     if(source==i){p.Flags=1;p.Source.ModeIndex=(uint)(i*2);p.Target.ModeIndex=(uint)(i*2+1);}paths.Add(p);
    }
   }s.Paths=paths.ToArray();return s;
  }
  static DisplayProfile Profile(Snapshot snapshot,int mask,string primary){
   var p=new DisplayProfile{Name="Mock selection",PrimaryId=primary};for(int i=0;i<snapshot.Monitors.Count;i++)if((mask&(1<<i))!=0){var pref=ScreenPreference.From(snapshot.Monitors[i]);pref.ScalePercent=new[]{100,125,150,200}[i%4];p.Screens.Add(pref);}return p;
  }
  static Config Configuration(Snapshot snapshot,DisplayProfile profile){return new Config{Enabled=true,Current=profile,Known=snapshot.Monitors.Select(ScreenPreference.From).ToList()};}
  static DisplayCoordinator Engine(Config c,FakeDisplayBackend backend){return new DisplayCoordinator(c,backend,null);}
  static void Exhaustive(int count,bool laptop){
   var fixture=Fixture(count,laptop);int variants=0;
   for(int mask=1;mask<(1<<count);mask++)foreach(var primary in fixture.Monitors.Where((m,i)=>(mask&(1<<i))!=0)){
    var p=Profile(fixture,mask,primary.StableId);var backend=new FakeDisplayBackend(fixture);var engine=Engine(Configuration(fixture,p),backend);engine.Step(Now);
    var active=backend.State.Monitors.Where(x=>x.Active).ToList();
    Check(active.Select(MonitorIdentity.Id).OrderBy(x=>x).SequenceEqual(p.Screens.Select(x=>x.Id).OrderBy(x=>x)),(laptop?"laptop":"desktop")+count+" mask="+mask+" primary="+p.PrimaryId+" exact enabled set");
    var main=active.Single(m=>m.StableId==p.PrimaryId);Check(main.X==0&&main.Y==0&&TopologyPlanner.Matches(backend.Query(false),TopologyPlanner.Resolve(p,engine.Configuration.Known,backend.Query(true))),"independent sources and primary for "+count+"/"+mask+"/"+p.PrimaryId);
    Check(active.All(m=>m.ScalePercent==p.Screens.First(x=>x.Id==m.StableId).ScalePercent),"independent per-screen scaling for "+count+"/"+mask+"/"+p.PrimaryId);
    int applied=backend.Applied,scaled=backend.ScaleCalls;engine.Step(Now.AddSeconds(5));Check(backend.Applied==applied&&backend.ScaleCalls==scaled,"correct state causes no repeated mutation "+count+"/"+mask+"/"+p.PrimaryId);variants++;
   }Check(variants==(count==4?32:12),"all nonempty sets and primary choices covered for "+count+" screens ("+(laptop?"laptop":"desktop")+")");
  }
  static void EdgeCases(){
   var s=Fixture(4,false);var p=Profile(s,7,"B");var backend=new FakeDisplayBackend(s);var c=Configuration(s,p);var engine=Engine(c,backend);engine.Step(Now);
   backend.Disconnect("B");engine.Step(Now.AddSeconds(5));Check(backend.State.Monitors.Where(x=>x.Active).Select(x=>x.StableId).OrderBy(x=>x).SequenceEqual(new[]{"A","C"}),"missing primary uses remaining selected desktop screens");
   Check(c.Current.PrimaryId=="B"&&c.Current.Screens.Count==3,"temporary missing screen never overwrites desired profile");
   var returned=backend.State.Monitors.Single(x=>x.StableId=="B");returned.Available=true;for(int i=0;i<backend.State.Paths.Length;i++){var path=backend.State.Paths[i];if(NativeDisplay.Key(path)==returned.Key)path.Target.Available=1;backend.State.Paths[i]=path;}
   engine.Step(Now.AddSeconds(10));Check(backend.State.Monitors.Single(x=>x.StableId=="B").Active&&backend.State.Monitors.Single(x=>x.StableId=="B").X==0,"reconnected preferred primary returns");
   backend=new FakeDisplayBackend(s);c=Configuration(s,Profile(s,2,"B"));engine=Engine(c,backend);backend.Disconnect("B");engine.Step(Now);Check(backend.State.Monitors.Count(x=>x.Active)==1&&backend.State.Monitors.First(x=>x.Active).StableId=="A","all desired screens missing falls back to known available desktop monitor");
   Check(backend.State.Monitors.First(x=>x.Active).ScalePercent==100,"fallback monitor does not inherit missing screen scale");
   backend=new FakeDisplayBackend(s);c=Configuration(s,Profile(s,1,"A"));c.Known.RemoveAll(x=>x.Id=="D");engine=Engine(c,backend);engine.Step(Now);Check(backend.Applied==0&&backend.ScaleCalls==0&&engine.Status.Contains("新屏幕"),"unknown new screen pauses without turning it off");
   backend=new FakeDisplayBackend(s);backend.RemoteMode=true;engine=Engine(Configuration(s,Profile(s,1,"A")),backend);engine.Step(Now);Check(backend.Queries==0&&backend.Applied==0,"remote session skips all display operations");
   backend=new FakeDisplayBackend(s);backend.State.Monitors[3].Internal=backend.State.Monitors[3].External=false;engine=Engine(Configuration(s,Profile(s,1,"A")),backend);engine.Step(Now);Check(backend.Applied==0&&engine.Status.Contains("虚拟"),"active virtual monitor prevents physical topology override");
   backend=new FakeDisplayBackend(s);c=Configuration(s,Profile(s,3,"A"));engine=Engine(c,backend);engine.Pause(Now);engine.Step(Now.AddSeconds(5));Check(backend.Applied==0&&backend.ScaleCalls==0,"pause suppresses topology and scale changes");
   engine.Disable();engine.Step(Now.AddMinutes(20));Check(backend.Applied==0&&!c.Enabled,"disabled guard has no display changes");
   backend=new FakeDisplayBackend(s);backend.FailApply=true;engine=Engine(Configuration(s,Profile(s,2,"B")),backend);engine.Step(Now);Check(backend.Restored==1&&backend.State.Monitors.Count(x=>x.Active)==4,"partial apply failure restores prior topology");
   int attempts=backend.Applied;engine.Step(Now.AddSeconds(1));Check(backend.Applied==attempts,"failed apply is rate limited");
   backend=new FakeDisplayBackend(s);backend.CorruptApply=true;engine=Engine(Configuration(s,Profile(s,3,"A")),backend);engine.Step(Now);Check(backend.Restored==1&&engine.Status.Contains("未成功"),"driver returning an incomplete set is rejected and rolled back");
   backend=new FakeDisplayBackend(s);backend.FailApply=backend.FailRestore=true;engine=Engine(Configuration(s,Profile(s,3,"A")),backend);engine.Step(Now);attempts=backend.Applied;engine.Step(Now.AddSeconds(20));Check(backend.Applied==attempts,"failed rollback preserves the longer recovery cooldown");
   backend=new FakeDisplayBackend(s);c=Configuration(s,Profile(s,3,"A"));c.Current.Screens[0].ScalePercent=200;backend.BadScales.Add("A");engine=Engine(c,backend);engine.Step(Now);Check(backend.State.Monitors.Count(x=>x.Active)==2&&backend.Restored==0&&engine.Status.Contains("缩放未保持"),"one unsupported scale does not tear down a valid multi-screen topology");
   engine.Step(Now.AddSeconds(5));Check(engine.Status.Contains("缩放未保持"),"scale warning remains visible while retry is delayed");
   var move=s.Monitors[1].Clone();move.DevicePath="fake://new-port";move.Key="another-adapter:99";Check(MonitorIdentity.Resolve(ScreenPreference.From(s.Monitors[1]),new[]{move})==move,"unique EDID identity survives connector and adapter changes");
   var impostor=s.Monitors[1].Clone();impostor.StableId=s.Monitors[1].DevicePath;impostor.Fingerprint="serial:new-device";var oldPref=ScreenPreference.From(s.Monitors[1]);oldPref.Id=oldPref.DevicePath;Check(MonitorIdentity.Resolve(oldPref,new[]{impostor})==null,"a different physical monitor in the same port is not silently substituted");
   var duplicate=s.Monitors[1].Clone();Check(MonitorIdentity.Resolve(ScreenPreference.From(s.Monitors[1]),new[]{s.Monitors[1],duplicate})==null,"ambiguous repeated identities require explicit reassociation");
   var profile=Profile(s,5,"C");var selection=TopologyPlanner.Resolve(profile,s.Monitors.Select(ScreenPreference.From),s);var layout=TopologyPlanner.Layout(selection);Check(layout[s.Monitors[0].Key][0]==-1920&&layout[s.Monitors[2].Key][0]==0,"removing the middle screen compacts the layout gap and preserves relative order");
   profile=Profile(s,3,"A");profile.Screens[0].X=profile.Screens[1].X=0;profile.Screens[0].Y=profile.Screens[1].Y=0;selection=TopologyPlanner.Resolve(profile,s.Monitors.Select(ScreenPreference.From),s);layout=TopologyPlanner.Layout(selection);Check(TopologyPlanner.Connected(selection.Screens,layout),"overlapping saved positions are rearranged into a connected desktop");
   profile=Profile(s,1,"A");profile.Screens[0].Rotation=2;var plan=TopologyPlanner.Build(s,TopologyPlanner.Resolve(profile,s.Monitors.Select(ScreenPreference.From),s));Check(plan.Paths[0].Source.ModeIndex==uint.MaxValue&&plan.Paths[0].Target.ModeIndex==uint.MaxValue&&plan.Paths[0].Target.Rotation==2,"rotation changes invalidate stale source and target timing modes");
   backend=new FakeDisplayBackend(s);engine=Engine(Configuration(s,profile),backend);engine.Step(Now);Check(backend.State.Monitors[0].Rotation==2&&backend.State.Monitors[0].Width==1080,"portrait preference is applied to any desktop monitor");
   var constrained=Fixture(2,false);constrained.Paths=constrained.Paths.Where(x=>x.Source.Id==0).ToArray();bool rejected=false;try{TopologyPlanner.Build(constrained,TopologyPlanner.Resolve(Profile(constrained,3,"A"),constrained.Monitors.Select(ScreenPreference.From),constrained));}catch(InvalidOperationException){rejected=true;}Check(rejected,"insufficient independent sources fail before apply");
   var multi=Fixture(2,false);multi.Paths=multi.Paths.Where(x=>x.Source.Id==0).ToArray();for(int i=0;i<multi.Paths.Length;i++){var path=multi.Paths[i];if(path.Target.Id==2)path.Source.Adapter=new Luid{Low=10};multi.Paths[i]=path;}
   plan=TopologyPlanner.Build(multi,TopologyPlanner.Resolve(Profile(multi,3,"A"),multi.Monitors.Select(ScreenPreference.From),multi));Check(plan.Paths.Select(NativeDisplay.SourceKey).Distinct().Count()==2,"equal source numbers on different adapters remain independent");
   rejected=false;try{new DisplayProfile().Validate();}catch(InvalidOperationException){rejected=true;}Check(rejected,"zero selected screens cannot be applied");
   profile=Profile(s,1,"B");rejected=false;try{profile.Validate();}catch(InvalidOperationException){rejected=true;}Check(rejected,"primary must belong to enabled set");
   profile=Profile(s,1,"A");profile.Screens.Add(profile.Screens[0].Clone());rejected=false;try{profile.Validate();}catch(InvalidOperationException){rejected=true;}Check(rejected,"duplicate selected monitor is rejected");
  }
  static void PreviewAndMigration(){
   var s=Fixture(4,false);var config=new Config{Known=s.Monitors.Select(ScreenPreference.From).ToList()};var backend=new FakeDisplayBackend(s);var engine=Engine(config,backend);int saves=0;engine.Changed=()=>saves++;
   var profile=Profile(s,10,"D");engine.BeginPreview(profile,config.Known,Now);Check(engine.PreviewActive&&backend.State.Monitors.Count(x=>x.Active)==2&&config.Current.Screens.Count==0&&saves==0,"preview applies arbitrary B+D while leaving saved configuration unchanged");
   engine.Step(Now.AddSeconds(16));Check(!engine.PreviewActive&&backend.State.Monitors.Count(x=>x.Active)==4&&!config.Enabled&&saves==0,"preview timeout restores original state without saving");
   engine.BeginPreview(profile,config.Known,Now.AddSeconds(20));engine.ConfirmPreview(Now.AddSeconds(21));Check(config.Enabled&&config.Current.PrimaryId=="D"&&config.Current.Screens.Count==2&&saves==1,"confirm commits desired subset and primary once");
   Check(config.Current.Screens.All(x=>x.HasPosition),"confirmed layout is captured for later restoration");
   engine.BeginPreview(Profile(s,1,"A"),config.Known,Now.AddSeconds(30));engine.CancelPreview();Check(config.Current.PrimaryId=="D"&&backend.State.Monitors.Count(x=>x.Active)==2,"cancel returns to previously confirmed combination");
   engine.BeginPreview(Profile(s,1,"A"),config.Known,Now.AddSeconds(40));bool expired=false;try{engine.ConfirmPreview(Now.AddSeconds(56));}catch(InvalidOperationException){expired=true;}Check(expired&&!engine.PreviewActive&&config.Current.PrimaryId=="D","late confirmation cannot persist an expired preview");
   var fresh=new Config();Check(!fresh.Enabled&&fresh.Current.Screens.Count==0,"new installation does not auto-disable existing desktop screens");
   var old=ConfigCodec.Parse("{\"Enabled\":true,\"Mode\":\"external\",\"TargetPath\":\"fake://B\",\"ExternalScalePercent\":200}");Check(old.Legacy!=null&&old.Enabled,"legacy configuration is recognized without hardware access");
   Check(ConfigCodec.ResolveLegacy(old,s)&&old.Current.PrimaryId=="B"&&old.Current.Screens.Single().ScalePercent==200,"legacy single-screen scaling and exact target are preserved");
   var disabled=ConfigCodec.Parse("{\"Enabled\":false,\"Mode\":\"external\",\"TargetPath\":\"fake://B\"}");ConfigCodec.ResolveLegacy(disabled,s);Check(!disabled.Enabled,"migration preserves disabled state");
   old=ConfigCodec.Parse("{\"Enabled\":true,\"Mode\":\"extend\",\"TargetPath\":\"fake://B\"}");Check(ConfigCodec.ResolveLegacy(old,s)&&old.Current.Screens.Count==2&&old.Current.Screens.All(x=>x.ScalePercent==0),"legacy two-screen migration also works on a desktop without embedded panels");
   var serialized=new JavaScriptSerializer().Serialize(config);var parsed=ConfigCodec.Parse(serialized);Check(parsed.SchemaVersion==2&&parsed.Current.Screens.Count==2&&parsed.Current.PrimaryId=="D","versioned multi-screen configuration round trips");
   var copy=config.Clone();copy.Current.Screens[0].ScalePercent=500;Check(config.Current.Screens[0].ScalePercent!=500,"profile copies do not mutate original configuration");
   backend=new FakeDisplayBackend(s);backend.CorruptApply=true;engine=Engine(new Config(),backend);bool rejected=false;try{engine.BeginPreview(Profile(s,3,"A"),s.Monitors.Select(ScreenPreference.From),Now);}catch(InvalidOperationException){rejected=true;}
   Check(rejected&&!engine.PreviewActive&&backend.Restored==1&&backend.State.Monitors.Count(x=>x.Active)==4,"incomplete initial preview fails and restores before arming a confirmation");
   backend=new FakeDisplayBackend(s);engine=Engine(new Config(),backend);engine.BeginPreview(Profile(s,3,"A"),s.Monitors.Select(ScreenPreference.From),Now);
   var hidden=backend.State.Monitors.Single(x=>x.StableId=="B");hidden.Active=false;for(int i=0;i<backend.State.Paths.Length;i++){var route=backend.State.Paths[i];if(NativeDisplay.Key(route)==hidden.Key)route.Flags=0;backend.State.Paths[i]=route;}
   rejected=false;try{engine.ConfirmPreview(Now.AddSeconds(1));}catch(InvalidOperationException){rejected=true;}Check(rejected&&engine.PreviewActive,"available but unexpectedly disabled selected monitor cannot be confirmed as missing");engine.CancelPreview();
   backend=new FakeDisplayBackend(s);backend.Disconnect("B");engine=Engine(new Config(),backend);rejected=false;try{engine.BeginPreview(Profile(s,2,"B"),s.Monitors.Select(ScreenPreference.From),Now);}catch(InvalidOperationException){rejected=true;}Check(rejected&&backend.Applied==0,"an all-offline manual selection cannot silently apply a fallback instead");
   backend=new FakeDisplayBackend(s);engine=Engine(new Config(),backend);engine.BeginPreview(Profile(s,3,"A"),s.Monitors.Select(ScreenPreference.From),Now);backend.RemoteMode=true;
   rejected=false;try{engine.ConfirmPreview(Now.AddSeconds(1));}catch(InvalidOperationException){rejected=true;}Check(rejected,"confirmation is refused after switching to a remote session");backend.RemoteMode=false;engine.CancelPreview();
   var vertical=Profile(s,3,"B");vertical.Screens[0].X=vertical.Screens[1].X=0;vertical.Screens[0].Y=0;vertical.Screens[1].Y=1080;
   var layout=TopologyPlanner.Layout(TopologyPlanner.Resolve(vertical,s.Monitors.Select(ScreenPreference.From),s));Check(layout[s.Monitors[0].Key][1]==-1080&&layout[s.Monitors[1].Key][1]==0,"a valid vertical layout is retained when primary moves to the lower monitor");
   var plan=TopologyPlanner.Build(s,TopologyPlanner.Resolve(Profile(s,8,"D"),s.Monitors.Select(ScreenPreference.From),s));Check(plan.Modes.Length==2&&plan.Paths[0].Source.ModeIndex==0&&plan.Paths[0].Target.ModeIndex==1&&plan.Modes[1].U0==12345,"unused native modes are removed while selected target timing is preserved");
   backend=new FakeDisplayBackend(s);config=new Config();engine=Engine(config,backend);engine.Changed=()=>{throw new IOException("simulated settings disk failure");};engine.BeginPreview(Profile(s,3,"B"),s.Monitors.Select(ScreenPreference.From),Now);
   rejected=false;try{engine.ConfirmPreview(Now.AddSeconds(1));}catch(IOException){rejected=true;}Check(rejected&&engine.PreviewActive&&config.Current.Screens.Count==0,"failed persistence leaves preview armed and restores previous in-memory configuration");engine.CancelPreview();Check(backend.State.Monitors.Count(x=>x.Active)==4,"preview can still roll back after a settings-save failure");
   backend=new FakeDisplayBackend(s);backend.Apply(TopologyPlanner.Build(s,TopologyPlanner.Resolve(Profile(s,1,"A"),s.Monitors.Select(ScreenPreference.From),s)));engine=Engine(new Config(),backend);engine.BeginPreview(Profile(s,6,"B"),s.Monitors.Select(ScreenPreference.From),Now);engine.CancelPreview();Check(backend.State.Monitors.Single(x=>x.Active).StableId=="A"&&backend.RestoreConnections==4,"rollback knows formerly connected but inactive screens were not newly plugged in");
   backend=new FakeDisplayBackend(s);engine=Engine(new Config(),backend);engine.BeginPreview(Profile(s,3,"A"),s.Monitors.Select(ScreenPreference.From),Now);
   engine.Configuration.Known.Clear(); // Preview retains its own independent baseline.
   engine.ConfirmPreview(Now.AddSeconds(1));Check(engine.Configuration.Known.Count==4,"preview confirmation uses an isolated connection baseline");
  }
  public static int Run(string path){
   passed=new List<string>();int code=0;object report;
   try{
    Check(Marshal.SizeOf(typeof(DisplayPath))==72&&Marshal.SizeOf(typeof(Mode))==64,"native display ABI definitions retained without invoking native APIs");
    Check(Marshal.SizeOf(typeof(SourceScaleGet))==32&&Marshal.SizeOf(typeof(SourceScaleSet))==24,"DPI packet ABI retained");
    Check(NativeDisplay.DecodeScale(-2,1,6).CurrentPercent==175&&NativeDisplay.RelativeScale(NativeDisplay.DecodeScale(-2,1,6),200)==2,"relative DPI arithmetic preserves 175 to 200 behavior");
    Exhaustive(3,false);Exhaustive(4,false);Exhaustive(3,true);Exhaustive(4,true);EdgeCases();PreviewAndMigration();
    report=new{Result="PASS",Count=passed.Count,Backend="FakeDisplayBackend only",NativeDisplayCalls=0,Checks=passed};
   }catch(Exception e){code=1;report=new{Result="FAIL",Count=passed.Count,Backend="FakeDisplayBackend only",NativeDisplayCalls=0,Error=e.ToString(),Checks=passed};}
   string full=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(full));File.WriteAllText(full,new JavaScriptSerializer().Serialize(report),new System.Text.UTF8Encoding(false));return code;
  }
 }
}

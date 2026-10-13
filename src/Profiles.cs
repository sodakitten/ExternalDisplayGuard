using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace ExternalDisplayGuard {
 public class ScreenPreference {
  public string Id="",DevicePath="",Fingerprint="",Model="",Name="";
  public int ScalePercent,Rotation,X,Y,ResolutionWidth,ResolutionHeight; public bool HasPosition;
  public ScreenPreference Clone(){return (ScreenPreference)MemberwiseClone();}
  public static ScreenPreference From(Monitor m){return new ScreenPreference{Id=MonitorIdentity.Id(m),DevicePath=m.DevicePath??"",Fingerprint=m.Fingerprint??"",Model=m.Model??"",Name=m.Name??"",Rotation=m.Rotation,X=m.X,Y=m.Y,HasPosition=m.Active};}
 }
 public class DisplayProfile {
  public string Name="当前选择",PrimaryId="";public List<ScreenPreference> Screens=new List<ScreenPreference>();
  public DisplayProfile Clone(){return new DisplayProfile{Name=Name,PrimaryId=PrimaryId,Screens=Screens.Select(x=>x.Clone()).ToList()};}
  public void Validate(){
   if(Screens==null||Screens.Count==0)throw new InvalidOperationException("至少选择一块屏幕。");
   if(Screens.Any(x=>x==null||string.IsNullOrEmpty(x.Id)))throw new InvalidOperationException("屏幕身份无效，请刷新屏幕列表。");
   if(Screens.Select(x=>x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=Screens.Count)throw new InvalidOperationException("不能重复选择同一块屏幕。");
   if(!Screens.Any(x=>string.Equals(x.Id,PrimaryId,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("主屏必须是已勾选的屏幕。");
   if(Screens.Any(x=>x.ScalePercent!=0&&!NativeDisplay.ScaleSteps.Contains(x.ScalePercent)))throw new InvalidOperationException("缩放倍率无效。");
   if(Screens.Any(x=>x.Rotation<0||x.Rotation>4))throw new InvalidOperationException("屏幕方向无效。");
   foreach(var screen in Screens)ResolutionSetting.Validate(screen.ResolutionWidth,screen.ResolutionHeight);
  }
 }
 public static class ResolutionSetting {
  public static void Validate(int width,int height){if(width==0&&height==0)return;if(width<200||height<200||width>32768||height>32768)throw new InvalidOperationException("分辨率需填写宽×高（200 至 32768 像素），例如 3840×2160；或填写“保持当前”。");}
  public static string Format(int width,int height){return width==0&&height==0?"保持当前":width+"×"+height;}
  public static void Parse(string text,ScreenPreference preference){
   text=(text??"").Trim();if(text.Length==0||text=="保持当前"){preference.ResolutionWidth=preference.ResolutionHeight=0;return;}
   var parts=text.ToLowerInvariant().Replace('×','x').Replace('*','x').Split('x');int width,height;
   if(parts.Length!=2||!int.TryParse(parts[0].Trim(),out width)||!int.TryParse(parts[1].Trim(),out height))throw new InvalidOperationException("分辨率格式应为 3840×2160 或“保持当前”。");
   Validate(width,height);if(width==0||height==0)throw new InvalidOperationException("固定分辨率不能为零；不固定请填写“保持当前”。");preference.ResolutionWidth=width;preference.ResolutionHeight=height;
  }
 }
 public class LegacyConfig {public bool Enabled=true;public string Mode="external",TargetPath="",TargetName="";public int ExternalScalePercent=200;}
 public class EditorScreen {
  public ScreenPreference Preference=new ScreenPreference();public bool Selected;public string ResolutionText="保持当前";
  public EditorScreen Clone(){return new EditorScreen{Preference=Preference.Clone(),Selected=Selected,ResolutionText=ResolutionText};}
 }
 public class EditorState {
  public List<EditorScreen> Rows=new List<EditorScreen>();public string PrimaryId="",ProfileName="当前选择",SelectedPreset="";public int WindowWidth=1120,WindowHeight=640;public bool Maximized;
  public EditorState Clone(){return new EditorState{Rows=Rows.Where(x=>x!=null&&x.Preference!=null).Select(x=>x.Clone()).ToList(),PrimaryId=PrimaryId,ProfileName=ProfileName,SelectedPreset=SelectedPreset,WindowWidth=WindowWidth,WindowHeight=WindowHeight,Maximized=Maximized};}
 }
 public static class PresetLibrary {
  public static void Save(Config config,DisplayProfile draft,string name){
   name=(name??"").Trim();if(name.Length==0)throw new InvalidOperationException("请输入预设名称。");draft.Validate();var copy=draft.Clone();copy.Name=name;
   config.Saved.RemoveAll(x=>MonitorIdentity.Equal(x.Name,name));config.Saved.Add(copy);
  }
  public static DisplayProfile Load(Config config,string name){var p=config.Saved.FirstOrDefault(x=>MonitorIdentity.Equal(x.Name,name));if(p==null)throw new InvalidOperationException("预设不存在。");return p.Clone();}
 }
 public class Config {
  public int SchemaVersion=2;public bool Enabled;public DisplayProfile Current=new DisplayProfile();
  public List<DisplayProfile> Saved=new List<DisplayProfile>();public List<ScreenPreference> Known=new List<ScreenPreference>();public LegacyConfig Legacy;
  public EditorState Editor=new EditorState();
  public void Assign(Config other){SchemaVersion=other.SchemaVersion;Enabled=other.Enabled;Current=other.Current.Clone();Saved=other.Saved.Select(x=>x.Clone()).ToList();Known=other.Known.Select(x=>x.Clone()).ToList();Legacy=other.Legacy;Editor=(other.Editor??new EditorState()).Clone();}
  public Config Clone(){var c=new Config();c.Assign(this);return c;}
 }
 public static class ConfigCodec {
  public static Config Parse(string json){
   var serializer=new JavaScriptSerializer();var raw=serializer.Deserialize<Dictionary<string,object>>(json);
   if(raw==null)throw new InvalidOperationException("配置文件无效。");
   if(!raw.ContainsKey("SchemaVersion")){
    var old=serializer.Deserialize<LegacyConfig>(json);if(old.Mode!="external"&&old.Mode!="extend")throw new InvalidOperationException("旧显示模式无效。");
    return new Config{Enabled=old.Enabled,Legacy=old};
   }
   var c=serializer.Deserialize<Config>(json);
   if(c.SchemaVersion!=2||c.Current==null||c.Saved==null||c.Known==null)throw new InvalidOperationException("不支持此配置版本。");
   if(c.Editor==null)c.Editor=new EditorState();if(c.Editor.Rows==null)c.Editor.Rows=new List<EditorScreen>();c.Editor.Rows=c.Editor.Rows.Where(x=>x!=null&&x.Preference!=null&&!string.IsNullOrEmpty(x.Preference.Id)).ToList();
   foreach(var p in c.Saved)p.Validate();if(c.Current.Screens.Count>0)c.Current.Validate();return c;
  }
  public static bool ResolveLegacy(Config c,Snapshot snapshot){
   if(c.Legacy==null)return false;var old=c.Legacy;
   var physical=snapshot.Monitors.Where(x=>MonitorIdentity.IsPhysical(x)&&x.Available).ToList();
   Monitor target=null;
   if(!string.IsNullOrEmpty(old.TargetPath))target=physical.SingleOrDefault(x=>MonitorIdentity.Equal(x.DevicePath,old.TargetPath));
   else {var ext=physical.Where(x=>x.External).ToList();if(ext.Count==1)target=ext[0];}
   if(target==null)return false;
   var p=new DisplayProfile{Name=old.Mode=="external"?"旧版单屏":"旧版扩展"};var first=ScreenPreference.From(target);first.ScalePercent=old.Mode=="external"?old.ExternalScalePercent:0;p.Screens.Add(first);p.PrimaryId=first.Id;
   if(old.Mode=="extend"){
    var second=physical.Where(x=>x.Key!=target.Key).OrderByDescending(x=>x.Internal).ThenByDescending(x=>x.Active).FirstOrDefault();
    if(second==null)return false;p.Screens.Add(ScreenPreference.From(second));
   }
   p.Validate();c.Current=p;c.Known=physical.Select(ScreenPreference.From).ToList();c.Legacy=null;return true;
  }
 }
 public static class MonitorIdentity {
  public static bool Equal(string a,string b){return string.Equals(a,b,StringComparison.OrdinalIgnoreCase);}
  public static bool IsPhysical(Monitor m){return (m.Internal||m.External)&&!string.IsNullOrEmpty(m.DevicePath);}
  public static string Id(Monitor m){return string.IsNullOrEmpty(m.StableId)?m.DevicePath:m.StableId;}
  public static Monitor Resolve(ScreenPreference p,IEnumerable<Monitor> monitors){
   var candidates=monitors.Where(x=>IsPhysical(x)&&x.Available).ToList();
   var ids=candidates.Where(x=>Equal(Id(x),p.Id)&&(string.IsNullOrEmpty(p.Fingerprint)||Equal(x.Fingerprint,p.Fingerprint))&&(string.IsNullOrEmpty(p.Model)||Equal(x.Model,p.Model))).ToList();if(ids.Count==1)return ids[0];if(ids.Count>1)return null;
   var paths=candidates.Where(x=>Equal(x.DevicePath,p.DevicePath)&&
    (string.IsNullOrEmpty(p.Fingerprint)||Equal(x.Fingerprint,p.Fingerprint))&&
    (string.IsNullOrEmpty(p.Model)||Equal(x.Model,p.Model))).ToList();
   return paths.Count==1?paths[0]:null;
  }
 }
 public class Selection {
  public List<Monitor> Screens=new List<Monitor>();public Dictionary<string,ScreenPreference> Preferences=new Dictionary<string,ScreenPreference>();
  public Monitor Primary;public int Missing;public bool Fallback;
 }
 public class DisplayPlan {public Snapshot Snapshot;public Selection Selection;public DisplayPath[] Paths;public Mode[] Modes;public string ConnectionToken;}
 public static class TopologyPlanner {
  public static Selection Resolve(DisplayProfile profile,IEnumerable<ScreenPreference> known,Snapshot snapshot){
   profile.Validate();var result=new Selection();
   foreach(var p in profile.Screens){var m=MonitorIdentity.Resolve(p,snapshot.Monitors);if(m==null){result.Missing++;continue;}
    if(result.Screens.Any(x=>x.Key==m.Key))throw new InvalidOperationException("屏幕身份发生冲突，请重新关联屏幕。");result.Screens.Add(m);result.Preferences[m.Key]=p;}
   if(result.Screens.Count==0){
    var fallback=known.Select(x=>MonitorIdentity.Resolve(x,snapshot.Monitors)).Where(x=>x!=null).OrderByDescending(x=>x.Active).FirstOrDefault();
    if(fallback==null)return result;result.Screens.Add(fallback);result.Preferences[fallback.Key]=ScreenPreference.From(fallback);result.Fallback=true;
   }
   var desired=profile.Screens.First(x=>MonitorIdentity.Equal(x.Id,profile.PrimaryId));
   result.Primary=MonitorIdentity.Resolve(desired,result.Screens)??result.Screens[0];return result;
  }
  public static string Token(Snapshot s){return string.Join("|",s.Monitors.Where(x=>x.Available).Select(x=>x.Key+":"+MonitorIdentity.Id(x)).OrderBy(x=>x,StringComparer.Ordinal).ToArray());}
  public static DisplayPlan Build(Snapshot snapshot,Selection selection){
   if(selection.Screens.Count==0||selection.Primary==null)throw new InvalidOperationException("没有可用的所选屏幕。");
   var ordered=selection.Screens.OrderByDescending(x=>x.Key==selection.Primary.Key).ToArray();var candidates=new Dictionary<string,List<DisplayPath>>();
   foreach(var m in ordered){var options=snapshot.Paths.Where(p=>NativeDisplay.Key(p)==m.Key&&p.Target.Available!=0).OrderByDescending(p=>(p.Flags&1)!=0).ToList();
    if(options.Count==0)throw new InvalidOperationException("没有可用的显示路径："+m.Name);candidates[m.Key]=options;}
   var assignment=new Dictionary<string,DisplayPath>();int visited=0;
   if(!Assign(ordered.OrderBy(m=>candidates[m.Key].Count).ToArray(),0,candidates,assignment,new HashSet<string>(),ref visited))throw new InvalidOperationException("硬件没有足够的独立显示路径支持这个组合。");
   var paths=ordered.Select(m=>assignment[m.Key]).ToArray();var modes=(Mode[])snapshot.Modes.Clone();var additions=new List<Mode>();
   for(int i=0;i<paths.Length;i++){
    var p=paths[i];var pref=selection.Preferences[NativeDisplay.Key(p)];bool rotating=pref.Rotation>0&&pref.Rotation!=p.Target.Rotation;
    bool source=!rotating&&(p.Flags&1)!=0&&ValidMode(modes,p.Source.ModeIndex,1,p.Source.Id,p.Source.Adapter);
    bool target=source&&ValidMode(modes,p.Target.ModeIndex,2,p.Target.Id,p.Target.Adapter);
    bool fixedResolution=pref.ResolutionWidth>0;
    if(fixedResolution){
     bool changed=!source||modes[p.Source.ModeIndex].Width!=(uint)pref.ResolutionWidth||modes[p.Source.ModeIndex].Height!=(uint)pref.ResolutionHeight;
     if(!source){p.Source.ModeIndex=(uint)(modes.Length+additions.Count);additions.Add(new Mode{Type=1,Id=p.Source.Id,Adapter=p.Source.Adapter,PixelFormat=4,Width=(uint)pref.ResolutionWidth,Height=(uint)pref.ResolutionHeight,X=selection.Screens.First(m=>m.Key==NativeDisplay.Key(p)).X,Y=selection.Screens.First(m=>m.Key==NativeDisplay.Key(p)).Y});}
     else {modes[p.Source.ModeIndex].Width=(uint)pref.ResolutionWidth;modes[p.Source.ModeIndex].Height=(uint)pref.ResolutionHeight;if(modes[p.Source.ModeIndex].PixelFormat<1||modes[p.Source.ModeIndex].PixelFormat>4)modes[p.Source.ModeIndex].PixelFormat=4;}
     source=true;if(changed)target=false;
    }
    p.Flags=1;if(!source)p.Source.ModeIndex=uint.MaxValue;if(!target){p.Target.ModeIndex=uint.MaxValue;p.Target.Refresh=new Rational();p.Target.Scanline=0;}
    if(pref.Rotation>0)p.Target.Rotation=(uint)pref.Rotation;if(p.Target.Rotation==0)p.Target.Rotation=1;if(p.Target.Scaling==0)p.Target.Scaling=128;paths[i]=p;
   }
   modes=modes.Concat(additions).ToArray();
   // Supply only referenced modes; inactive routes must not accidentally reuse an unrelated source's timing.
   var indices=paths.SelectMany(p=>new[]{p.Source.ModeIndex,p.Target.ModeIndex}).Where(i=>i!=uint.MaxValue).Distinct().ToList();
   var compact=indices.Select(i=>modes[i]).ToArray();for(int i=0;i<paths.Length;i++){var p=paths[i];if(p.Source.ModeIndex!=uint.MaxValue)p.Source.ModeIndex=(uint)indices.IndexOf(p.Source.ModeIndex);if(p.Target.ModeIndex!=uint.MaxValue)p.Target.ModeIndex=(uint)indices.IndexOf(p.Target.ModeIndex);paths[i]=p;}
   return new DisplayPlan{Snapshot=snapshot,Selection=selection,Paths=paths,Modes=compact,ConnectionToken=Token(snapshot)};
  }
  static bool Assign(Monitor[] monitors,int index,Dictionary<string,List<DisplayPath>> candidates,Dictionary<string,DisplayPath> chosen,HashSet<string> used,ref int visited){
   if(index==monitors.Length)return true;if(++visited>20000)return false;
   string key=monitors[index].Key;foreach(var path in candidates[key]){string source=NativeDisplay.SourceKey(path);if(!used.Add(source))continue;chosen[key]=path;
    if(Assign(monitors,index+1,candidates,chosen,used,ref visited))return true;used.Remove(source);chosen.Remove(key);}return false;
  }
  public static bool ValidMode(Mode[] modes,uint index,uint type,uint id,Luid adapter){return index<modes.Length&&modes[index].Type==type&&modes[index].Id==id&&modes[index].Adapter.ToString()==adapter.ToString();}
  public static void PositionSuppliedModes(DisplayPath[] paths,Mode[] modes,Selection selection){
   if(!paths.All(p=>ValidMode(modes,p.Source.ModeIndex,1,p.Source.Id,p.Source.Adapter)))return;
   var projected=new Selection{Primary=selection.Primary,Preferences=selection.Preferences};
   foreach(var screen in selection.Screens){var copy=screen.Clone();var path=paths.Single(p=>NativeDisplay.Key(p)==copy.Key);copy.Width=modes[path.Source.ModeIndex].Width;copy.Height=modes[path.Source.ModeIndex].Height;projected.Screens.Add(copy);}
   var positions=Layout(projected);foreach(var path in paths){modes[path.Source.ModeIndex].X=positions[NativeDisplay.Key(path)][0];modes[path.Source.ModeIndex].Y=positions[NativeDisplay.Key(path)][1];}
  }
  public static Dictionary<string,int[]> Layout(Selection selection){
   var screens=selection.Screens;var positions=new Dictionary<string,int[]>();
   if(screens.Any(x=>x.Width==0||x.Height==0))throw new InvalidOperationException("显示尺寸尚未稳定，请稍后重试。");
   bool saved=screens.All(x=>selection.Preferences[x.Key].HasPosition);
   foreach(var m in screens){var p=selection.Preferences[m.Key];positions[m.Key]=saved?new[]{p.X,p.Y}:new[]{m.X,m.Y};}
   if(!Connected(screens,positions)){
    int x=0;foreach(var m in screens.OrderBy(m=>positions[m.Key][1]).ThenBy(m=>positions[m.Key][0])){positions[m.Key]=new[]{x,0};x=checked(x+(int)m.Width);}
   }
   int dx=positions[selection.Primary.Key][0],dy=positions[selection.Primary.Key][1];foreach(var point in positions.Values){point[0]-=dx;point[1]-=dy;}return positions;
  }
  public static bool Connected(List<Monitor> monitors,Dictionary<string,int[]> positions){
   var edges=new Dictionary<string,List<string>>();foreach(var m in monitors)edges[m.Key]=new List<string>();
   for(int i=0;i<monitors.Count;i++)for(int j=i+1;j<monitors.Count;j++){
    var a=monitors[i];var b=monitors[j];var ap=positions[a.Key];var bp=positions[b.Key];long ar=ap[0]+(long)a.Width,ab=ap[1]+(long)a.Height,br=bp[0]+(long)b.Width,bb=bp[1]+(long)b.Height;
    bool xOverlap=Math.Max(ap[0],bp[0])<Math.Min(ar,br),yOverlap=Math.Max(ap[1],bp[1])<Math.Min(ab,bb);
    if(xOverlap&&yOverlap)return false;if(((ar==bp[0]||br==ap[0])&&yOverlap)||((ab==bp[1]||bb==ap[1])&&xOverlap)){edges[a.Key].Add(b.Key);edges[b.Key].Add(a.Key);}
   }
   if(monitors.Count==0)return false;var found=new HashSet<string>();var queue=new Queue<string>();queue.Enqueue(monitors[0].Key);
   while(queue.Count>0){var k=queue.Dequeue();if(!found.Add(k))continue;foreach(var next in edges[k])queue.Enqueue(next);}return found.Count==monitors.Count;
  }
  public static bool Matches(Snapshot snapshot,Selection selection){
   var active=snapshot.Monitors.Where(x=>x.Active).ToList();if(active.Count!=selection.Screens.Count||selection.Primary==null||active.Any(x=>!x.Available))return false;
   if(active.Any(x=>!selection.Screens.Any(y=>y.Key==x.Key)))return false;
   if(snapshot.Paths.Where(x=>(x.Flags&1)!=0).Select(NativeDisplay.SourceKey).Distinct().Count()!=active.Count)return false;
   var primary=active.First(x=>x.Key==selection.Primary.Key);if(primary.X!=0||primary.Y!=0)return false;
   var actualSelection=new Selection{Screens=active,Primary=primary,Preferences=selection.Preferences};var layout=Layout(actualSelection);
   foreach(var m in active){var pref=selection.Preferences[m.Key];if(m.X!=layout[m.Key][0]||m.Y!=layout[m.Key][1]||(pref.Rotation>0&&m.Rotation!=pref.Rotation)||(pref.ResolutionWidth>0&&(m.Width!=(uint)pref.ResolutionWidth||m.Height!=(uint)pref.ResolutionHeight)))return false;}return true;
  }
  public static void Verify(Snapshot snapshot,Selection selection){
   foreach(var wanted in selection.Screens){var pref=selection.Preferences[wanted.Key];if(pref.ResolutionWidth==0)continue;var actual=snapshot.Monitors.FirstOrDefault(m=>m.Key==wanted.Key&&m.Active);
    if(actual!=null&&(actual.Width!=(uint)pref.ResolutionWidth||actual.Height!=(uint)pref.ResolutionHeight))throw new InvalidOperationException(wanted.Name+" 分辨率未达到 "+ResolutionSetting.Format(pref.ResolutionWidth,pref.ResolutionHeight)+"；实际 "+actual.Width+"×"+actual.Height+"。");
   }
   if(!Matches(snapshot,selection))throw new InvalidOperationException("系统未确认完整的屏幕组合、布局或方向。");
  }
 }
}

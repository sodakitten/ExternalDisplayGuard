using System;
using System.Collections.Generic;
using System.Linq;

namespace ExternalDisplayGuard {
 public interface IDisplayBackend {
  bool Remote {get;} Snapshot Query(bool all);void Apply(DisplayPlan plan);void Restore(Snapshot before);bool SetScale(Snapshot snapshot,Monitor monitor,int percent);
 }
 public class NativeBackend:IDisplayBackend {
  public bool Remote {get{return NativeDisplay.GetSystemMetrics(0x1000)!=0;}}
  public Snapshot Query(bool all){return NativeDisplay.Query(all);}
  public bool SetScale(Snapshot snapshot,Monitor monitor,int percent){var fresh=Query(false);var target=MonitorIdentity.Resolve(ScreenPreference.From(monitor),fresh.Monitors);if(target==null)throw new InvalidOperationException("缩放前目标屏幕已变化，请稍后重试。");return NativeDisplay.SetScale(fresh,target,percent);}
  public void Apply(DisplayPlan plan){
   if(TopologyPlanner.Token(Query(true))!=plan.ConnectionToken)throw new InvalidOperationException("连接状态已变化，请重试。");
   var paths=(DisplayPath[])plan.Paths.Clone();var modes=(Mode[])plan.Modes.Clone();
   TopologyPlanner.PositionSuppliedModes(paths,modes,plan.Selection);
   bool allowChanges=!plan.Selection.Preferences.Values.Any(p=>p.ResolutionWidth>0);NativeDisplay.ApplyPaths(paths,modes,allowChanges);
   var fresh=Query(false);var actual=new Selection{Preferences=plan.Selection.Preferences};
   foreach(var wanted in plan.Selection.Screens){var m=fresh.Monitors.SingleOrDefault(x=>x.Key==wanted.Key&&x.Available&&x.Active);if(m==null)throw new InvalidOperationException("应用后屏幕未激活："+wanted.Name);actual.Screens.Add(m);}
   actual.Primary=actual.Screens.Single(x=>x.Key==plan.Selection.Primary.Key);
   if(fresh.Paths.Length!=paths.Length||fresh.Paths.Select(NativeDisplay.SourceKey).Distinct().Count()!=paths.Length)throw new InvalidOperationException("驱动未建立所选的独立扩展屏幕。");
   var layout=TopologyPlanner.Layout(actual);bool move=actual.Screens.Any(m=>m.X!=layout[m.Key][0]||m.Y!=layout[m.Key][1]);
   if(move){var positioned=(Mode[])fresh.Modes.Clone();var active=fresh.Paths.OrderByDescending(x=>NativeDisplay.Key(x)==actual.Primary.Key).ToArray();
    for(int i=0;i<active.Length;i++){var p=active[i];if(!TopologyPlanner.ValidMode(positioned,p.Source.ModeIndex,1,p.Source.Id,p.Source.Adapter))throw new InvalidOperationException("驱动未提供稳定的屏幕位置。");p.Flags=1;active[i]=p;positioned[p.Source.ModeIndex].X=layout[NativeDisplay.Key(p)][0];positioned[p.Source.ModeIndex].Y=layout[NativeDisplay.Key(p)][1];}
    NativeDisplay.ApplyPaths(active,positioned,allowChanges);
   }
  }
  public void Restore(Snapshot before){
   var now=Query(true);var old=before.Monitors.Where(m=>MonitorIdentity.IsPhysical(m)&&m.Active).Select(m=>{var p=ScreenPreference.From(m);p.ResolutionWidth=(int)m.Width;p.ResolutionHeight=(int)m.Height;return p;}).ToList();
   if(now.Monitors.Any(m=>m.Active&&!MonitorIdentity.IsPhysical(m)))throw new InvalidOperationException("虚拟或远程桌面已激活，保留当前输出并暂停还原。");
   var connectedBefore=before.Connections??before.Monitors;
   if(now.Monitors.Any(m=>MonitorIdentity.IsPhysical(m)&&m.Available&&!connectedBefore.Any(x=>MonitorIdentity.Resolve(ScreenPreference.From(x),new[]{m})!=null)))throw new InvalidOperationException("出现新的显示器，保留当前输出并暂停还原。");
   if(old.Count==0)return;
   var primary=before.Monitors.FirstOrDefault(m=>m.Active&&m.X==0&&m.Y==0&&MonitorIdentity.IsPhysical(m));var profile=new DisplayProfile{Screens=old,PrimaryId=primary==null?old[0].Id:MonitorIdentity.Id(primary)};
   bool restored=false;if(before.Paths.All(p=>now.Paths.Any(x=>NativeDisplay.Key(x)==NativeDisplay.Key(p)&&x.Target.Available!=0))){try{NativeDisplay.Restore(before);restored=TopologyPlanner.Matches(Query(false),TopologyPlanner.Resolve(profile,before.Monitors.Select(ScreenPreference.From),Query(true)));}catch{}}
   if(!restored){
    now=Query(true);
    var selection=TopologyPlanner.Resolve(profile,before.Monitors.Where(MonitorIdentity.IsPhysical).Select(ScreenPreference.From),now);
    if(selection.Screens.Count>0){Apply(TopologyPlanner.Build(now,selection));TopologyPlanner.Verify(Query(false),selection);}
   }
   var active=Query(false);foreach(var previous in before.Monitors.Where(m=>m.Active&&MonitorIdentity.IsPhysical(m)&&m.ScalePercent>0)){
    var current=MonitorIdentity.Resolve(ScreenPreference.From(previous),active.Monitors);if(current!=null)SetScale(active,current,previous.ScalePercent);
   }
  }
 }
 public class DisplayCoordinator {
  readonly IDisplayBackend backend;readonly Action<string> log;public Config Configuration {get;private set;}
  Config pending;Snapshot previewBefore;DateTime deadline,pauseUntil,retryUntil;readonly Queue<DateTime> topologyChanges=new Queue<DateTime>();
  readonly Dictionary<string,DateTime> scaleRetry=new Dictionary<string,DateTime>();readonly Dictionary<string,Queue<DateTime>> scaleChanges=new Dictionary<string,Queue<DateTime>>();
  readonly Dictionary<string,string> scaleFailures=new Dictionary<string,string>();
  public string Status {get;private set;}public bool PreviewActive {get{return pending!=null;}}public DateTime PreviewDeadline {get{return deadline;}}public Action Changed;
  public DateTime PauseUntil {get{return pauseUntil;}}
  public DisplayCoordinator(Config config,IDisplayBackend backend,Action<string> log){Configuration=config;this.backend=backend;this.log=log??delegate{};Status="等待配置屏幕组合";}
  void Save(){if(Changed!=null)Changed();}
  void Record(string s){Status=s;log(s);}
  public string Step(DateTime now){
   if(PreviewActive&&now>=deadline){CancelPreview();return Status;}
   var c=pending??Configuration;if(!c.Enabled){Status="守护已停用";return Status;}
   if(now<pauseUntil){Status="暂停至 "+pauseUntil.ToLocalTime().ToString("HH:mm:ss");return Status;}
   if(now<retryUntil)return Status;if(backend.Remote){Status="远程会话：守护暂停";return Status;}
   try{
    var all=backend.Query(true);
    if(c.Legacy!=null){if(!ConfigCodec.ResolveLegacy(c,all)){Status="等待旧版目标屏幕；可打开设置重新选择";return Status;}if(!PreviewActive)Save();}
    if(c.Current.Screens.Count==0){Status="请选择亮哪些屏幕并应用";return Status;}
    if(all.Monitors.Any(m=>m.Active&&!MonitorIdentity.IsPhysical(m))){Status="虚拟显示屏正在使用：拓扑守护暂停";return Status;}
    var unknown=all.Monitors.Where(m=>MonitorIdentity.IsPhysical(m)&&m.Available&&!c.Known.Any(k=>MonitorIdentity.Resolve(k,new[]{m})!=null)).ToList();
    if(unknown.Count>0){Status="检测到新屏幕：请打开设置选择组合";return Status;}
    var selection=TopologyPlanner.Resolve(c.Current,c.Known,all);
    if(selection.Screens.Count==0){Status="所选屏幕均不在线，等待可用屏幕";return Status;}
    var active=backend.Query(false);
    active.Connections=all.Monitors.Where(m=>m.Available).Select(m=>m.Clone()).ToList();
    if(!TopologyPlanner.Matches(active,selection)){
     while(topologyChanges.Count>0&&now-topologyChanges.Peek()>TimeSpan.FromMinutes(1))topologyChanges.Dequeue();
     if(topologyChanges.Count>=4){retryUntil=now.AddMinutes(1);Status="显示模式反复变化，冷却 60 秒";return Status;}
     var plan=TopologyPlanner.Build(all,selection);topologyChanges.Enqueue(now);
     try{backend.Apply(plan);TopologyPlanner.Verify(backend.Query(false),selection);}catch(Exception e){try{backend.Restore(active);}catch(Exception restore){retryUntil=now.AddMinutes(1);Record("恢复失败，保留现有输出："+restore.Message);}throw new InvalidOperationException("切换未成功："+e.Message,e);}
     active=backend.Query(false);selection=TopologyPlanner.Resolve(c.Current,c.Known,backend.Query(true));Record("已恢复屏幕组合："+c.Current.Name);
    }
    var errors=new List<string>();
    foreach(var m in selection.Screens){var pref=selection.Preferences[m.Key];if(pref.ScalePercent==0||m.ScalePercent==pref.ScalePercent){scaleFailures.Remove(pref.Id);continue;}
     DateTime next;if(scaleRetry.TryGetValue(pref.Id,out next)&&now<next){string warning;if(scaleFailures.TryGetValue(pref.Id,out warning))errors.Add(warning);continue;}
     Queue<DateTime> changes;if(!scaleChanges.TryGetValue(pref.Id,out changes)){changes=new Queue<DateTime>();scaleChanges[pref.Id]=changes;}
     while(changes.Count>0&&now-changes.Peek()>TimeSpan.FromMinutes(1))changes.Dequeue();
     if(changes.Count>=4){scaleRetry[pref.Id]=now.AddMinutes(1);scaleFailures[pref.Id]=m.Name+" 的缩放反复变化，已冷却";errors.Add(scaleFailures[pref.Id]);continue;}
     try{if(backend.SetScale(active,m,pref.ScalePercent)){changes.Enqueue(now);Record("已恢复 "+m.Name+" 缩放 "+pref.ScalePercent+"%");}scaleFailures.Remove(pref.Id);}
     catch(Exception e){scaleRetry[pref.Id]=now.AddMinutes(1);scaleFailures[pref.Id]=m.Name+" 缩放未保持："+e.Message;errors.Add(scaleFailures[pref.Id]);}
    }
    if(errors.Count>0){Status=string.Join("；",errors.ToArray());return Status;}
    Status=c.Current.Name+" · "+selection.Screens.Count+" 块屏 · 主屏："+selection.Primary.Name+(selection.Missing>0?"（"+selection.Missing+" 块暂离线）":"");return Status;
   }catch(Exception e){if(retryUntil<now.AddSeconds(10))retryUntil=now.AddSeconds(10);Record("暂未成功："+e.Message);return Status;}
  }
  public void BeginPreview(DisplayProfile profile,IEnumerable<ScreenPreference> known,DateTime now){
   profile.Validate();if(backend.Remote)throw new InvalidOperationException("请在本机桌面应用屏幕组合。");
   if(PreviewActive)CancelPreview();var before=backend.Query(false);if(before.Monitors.Any(m=>m.Active&&!MonitorIdentity.IsPhysical(m)))throw new InvalidOperationException("请先暂停虚拟显示屏，再应用物理屏幕组合。");
   var all=backend.Query(true);before.Connections=all.Monitors.Where(m=>m.Available).Select(m=>m.Clone()).ToList();var candidate=Configuration.Clone();candidate.Legacy=null;candidate.Enabled=true;candidate.Current=profile.Clone();candidate.Known=known.Select(x=>x.Clone()).ToList();
   foreach(var m in all.Monitors.Where(x=>x.Available&&MonitorIdentity.IsPhysical(x)))if(!candidate.Known.Any(k=>MonitorIdentity.Resolve(k,new[]{m})!=null))candidate.Known.Add(ScreenPreference.From(m));
   var selected=TopologyPlanner.Resolve(candidate.Current,candidate.Known,all);if(selected.Screens.Count==0||selected.Fallback)throw new InvalidOperationException("至少选择一块当前在线的屏幕。");
   var plan=TopologyPlanner.Build(all,selected);
   try{backend.Apply(plan);TopologyPlanner.Verify(backend.Query(false),selected);}catch{try{backend.Restore(before);}catch(Exception e){Record("恢复失败："+e.Message);retryUntil=now.AddMinutes(1);}throw;}
   previewBefore=before;pending=candidate;deadline=now.AddSeconds(15);pauseUntil=retryUntil=DateTime.MinValue;topologyChanges.Clear();scaleRetry.Clear();scaleChanges.Clear();scaleFailures.Clear();Step(now);
  }
  public void ConfirmPreview(DateTime now){
   if(!PreviewActive)return;
   if(now>=deadline){CancelPreview();throw new InvalidOperationException("确认已超时，已尝试还原。");}
   if(backend.Remote)throw new InvalidOperationException("请在本机桌面确认。");var all=backend.Query(true);
   if(all.Monitors.Any(m=>m.Active&&!MonitorIdentity.IsPhysical(m))||all.Monitors.Any(m=>MonitorIdentity.IsPhysical(m)&&m.Available&&!pending.Known.Any(k=>MonitorIdentity.Resolve(k,new[]{m})!=null)))throw new InvalidOperationException("显示器连接已变化，请还原后重新选择。");
   var active=backend.Query(false);var selection=TopologyPlanner.Resolve(pending.Current,pending.Known,all);
   if(!TopologyPlanner.Matches(active,selection))throw new InvalidOperationException("系统尚未稳定显示所选组合，请稍后确认或还原。");
   if(selection.Missing==0)foreach(var pref in pending.Current.Screens){var m=MonitorIdentity.Resolve(pref,active.Monitors);if(m!=null){pref.HasPosition=true;pref.X=m.X;pref.Y=m.Y;}}
   var previous=Configuration.Clone();Configuration.Assign(pending);try{Save();}catch{Configuration.Assign(previous);throw;}
   pending=null;previewBefore=null;Record("已保存并守护当前屏幕组合");
  }
  public void CancelPreview(){
   if(!PreviewActive)return;var before=previewBefore;pending=null;previewBefore=null;
   try{backend.Restore(before);Record("已还原应用前的显示配置");}catch(Exception e){pauseUntil=DateTime.UtcNow.AddMinutes(1);Record("还原未完成，守护已暂停："+e.Message);}
  }
  public void Pause(DateTime now){if(PreviewActive)CancelPreview();pauseUntil=now.AddMinutes(10);Status="守护暂停 10 分钟";}
  public void Enable(){pauseUntil=retryUntil=DateTime.MinValue;topologyChanges.Clear();scaleChanges.Clear();scaleRetry.Clear();scaleFailures.Clear();Configuration.Enabled=true;Save();}
  public void Disable(){if(PreviewActive)CancelPreview();Configuration.Enabled=false;Save();Status="守护已停用，保留当前显示配置";}
 }
}

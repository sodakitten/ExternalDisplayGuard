using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace ExternalDisplayGuard {
 [StructLayout(LayoutKind.Sequential)] public struct Luid { public uint Low; public int High; public override string ToString(){return High+":"+Low;} }
 [StructLayout(LayoutKind.Sequential)] public struct Rational { public uint Numerator, Denominator; }
 [StructLayout(LayoutKind.Sequential)] public struct PathSource { public Luid Adapter; public uint Id, ModeIndex, Status; }
 [StructLayout(LayoutKind.Sequential)] public struct PathTarget { public Luid Adapter; public uint Id, ModeIndex; public int Technology; public uint Rotation, Scaling; public Rational Refresh; public uint Scanline; public int Available; public uint Status; }
 [StructLayout(LayoutKind.Sequential)] public struct DisplayPath { public PathSource Source; public PathTarget Target; public uint Flags; }
 [StructLayout(LayoutKind.Explicit, Size=64)] public struct Mode {
  [FieldOffset(0)] public uint Type; [FieldOffset(4)] public uint Id; [FieldOffset(8)] public Luid Adapter;
  // Preserve the entire native union, including target timing data.
  [FieldOffset(16)] public ulong U0; [FieldOffset(24)] public ulong U1; [FieldOffset(32)] public ulong U2;
  [FieldOffset(40)] public ulong U3; [FieldOffset(48)] public ulong U4; [FieldOffset(56)] public ulong U5;
  [FieldOffset(16)] public uint Width; [FieldOffset(20)] public uint Height;
  [FieldOffset(28)] public int X; [FieldOffset(32)] public int Y;
 }
 [StructLayout(LayoutKind.Sequential)] public struct DeviceHeader { public uint Type, Size; public Luid Adapter; public uint Id; }
 [StructLayout(LayoutKind.Sequential)] public struct SourceScaleGet { public DeviceHeader Header; public int MinimumRelative, CurrentRelative, MaximumRelative; }
 [StructLayout(LayoutKind.Sequential)] public struct SourceScaleSet { public DeviceHeader Header; public int Relative; }
 public class ScaleState { public int CurrentPercent, RecommendedPercent, MaximumPercent; public int MinimumRelative, CurrentRelative, MaximumRelative; }
 [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct TargetName {
  public DeviceHeader Header; public uint Flags; public int Technology; public ushort Manufacturer, Product; public uint Connector;
  [MarshalAs(UnmanagedType.ByValTStr, SizeConst=64)] public string Friendly;
  [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DevicePath;
 }
 [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct SourceName {
  public DeviceHeader Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string GdiName;
 }
 public class Monitor {
  public string Key, Name, DevicePath, GdiName,StableId,Fingerprint,Model; public int Technology,Rotation; public bool Active, Available, Internal, External;
  public uint Width, Height; public int X, Y, ScalePercent; public double RefreshHz;
  public Monitor Clone(){return (Monitor)MemberwiseClone();}
 }
 public class Snapshot { public DisplayPath[] Paths; public Mode[] Modes; public List<Monitor> Monitors; public List<Monitor> Connections; }
 public static class NativeDisplay {
  [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
  [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] DisplayPath[] pathArray, ref uint modes, [Out] Mode[] modeArray, IntPtr topology);
  [DllImport("user32.dll")] static extern int SetDisplayConfig(uint paths, [In] DisplayPath[] pathArray, uint modes, [In] Mode[] modeArray, uint flags);
  [DllImport("user32.dll", EntryPoint="DisplayConfigGetDeviceInfo")] static extern int GetTargetName(ref TargetName name);
  [DllImport("user32.dll", EntryPoint="DisplayConfigGetDeviceInfo")] static extern int GetSourceName(ref SourceName name);
  // Source DPI packet types -3 / -4 are undocumented Windows extensions. Validate ranges and read back every change.
  [DllImport("user32.dll", EntryPoint="DisplayConfigGetDeviceInfo")] static extern int GetSourceScale(ref SourceScaleGet scale);
  [DllImport("user32.dll", EntryPoint="DisplayConfigSetDeviceInfo")] static extern int SetSourceScale(ref SourceScaleSet scale);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string className,string caption);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int command);
  public static string Key(DisplayPath p){return p.Target.Adapter+":"+p.Target.Id;}
  public static string SourceKey(DisplayPath p){return p.Source.Adapter+":"+p.Source.Id;}
  public static bool IsInternal(int t){return t==6 || t==11 || t==13 || t==int.MinValue;}
  // Conservative: physical connectors only. Indirect/virtual/Miracast targets are not auto-selected.
  public static bool IsExternal(int t){return t==0 || t==4 || t==5 || t==10 || t==12 || t==18;}
  public static Snapshot Query(bool all){
   for(int attempt=0;attempt<6;attempt++){
    uint pc,mc; int e=GetDisplayConfigBufferSizes(all?1u:2u,out pc,out mc); Check(e,"GetDisplayConfigBufferSizes");
    var paths=new DisplayPath[Math.Max(pc,1)];var modes=new Mode[Math.Max(mc,1)];
    e=QueryDisplayConfig(all?1u:2u,ref pc,paths,ref mc,modes,IntPtr.Zero);if(e==122)continue;Check(e,"QueryDisplayConfig");
    Array.Resize(ref paths,(int)pc);Array.Resize(ref modes,(int)mc);
    var s=new Snapshot {Paths=paths,Modes=modes,Monitors=new List<Monitor>()};
    foreach(var group in paths.GroupBy(Key)){
     var p=group.OrderByDescending(x=>(x.Flags&1)!=0).First();
     var n=new TargetName();n.Header=new DeviceHeader{Type=2,Size=(uint)Marshal.SizeOf(typeof(TargetName)),Adapter=p.Target.Adapter,Id=p.Target.Id};
     int ne=GetTargetName(ref n);
     var sn=new SourceName();sn.Header=new DeviceHeader{Type=1,Size=(uint)Marshal.SizeOf(typeof(SourceName)),Adapter=p.Source.Adapter,Id=p.Source.Id};GetSourceName(ref sn);
     var m=new Monitor {Key=Key(p),Name=ne==0?n.Friendly:"未知显示器",DevicePath=ne==0?n.DevicePath:"",GdiName=sn.GdiName,
      Technology=p.Target.Technology,Rotation=(int)p.Target.Rotation,Model=ne==0?n.Manufacturer.ToString("X4")+":"+n.Product.ToString("X4"):"",Active=group.Any(x=>(x.Flags&1)!=0),Available=group.Any(x=>x.Target.Available!=0),Internal=IsInternal(p.Target.Technology),External=IsExternal(p.Target.Technology),
      RefreshHz=p.Target.Refresh.Denominator==0?0:(double)p.Target.Refresh.Numerator/p.Target.Refresh.Denominator};
     if((p.Flags&1)!=0&&p.Source.ModeIndex<modes.Length){var mode=modes[p.Source.ModeIndex];if(mode.Type==1&&mode.Id==p.Source.Id&&mode.Adapter.ToString()==p.Source.Adapter.ToString()){m.Width=mode.Width;m.Height=mode.Height;m.X=mode.X;m.Y=mode.Y;}}
     if(m.Active&&m.Available){try{m.ScalePercent=ReadScale(s,m).CurrentPercent;}catch{m.ScalePercent=0;}}
     s.Monitors.Add(m);
    } HardwareIdentity.Enrich(s.Monitors);return s;
   }throw new InvalidOperationException("显示配置持续变化，请稍后重试。");
  }
  public static Monitor FindTarget(Snapshot s,string devicePath){
   var available=s.Monitors.Where(x=>x.External&&x.Available&&!string.IsNullOrEmpty(x.DevicePath)).ToList();
   if(!string.IsNullOrEmpty(devicePath))return available.FirstOrDefault(x=>string.Equals(x.DevicePath,devicePath,StringComparison.OrdinalIgnoreCase));
   return available.Count==1?available[0]:null;
  }
  public static bool Matches(Snapshot s,string key,string mode){
   var a=s.Monitors.Where(x=>x.Active).ToList();var t=a.FirstOrDefault(x=>x.Key==key&&x.Available);
   if(t==null || t.X!=0 || t.Y!=0)return false;
   if(mode=="external")return a.Count==1;
   return a.Count==2 && a.Count(x=>x.Internal&&x.Available)==1 && s.Paths.Where(x=>(x.Flags&1)!=0).Select(SourceKey).Distinct().Count()==2;
  }
  public static readonly int[] ScaleSteps={100,125,150,175,200,225,250,300,350,400,450,500};
  public static ScaleState DecodeScale(int minimum,int current,int maximum){
   long recommended=-(long)minimum,last=recommended+maximum,index=recommended+current;
   if(minimum>0||maximum<0||current<minimum||current>maximum||recommended>=ScaleSteps.Length||last>=ScaleSteps.Length||index<0||index>=ScaleSteps.Length)
    throw new InvalidOperationException("Windows 返回了无法识别的缩放范围；可能启用了自定义缩放。");
   return new ScaleState{CurrentPercent=ScaleSteps[(int)index],RecommendedPercent=ScaleSteps[(int)recommended],MaximumPercent=ScaleSteps[(int)last],MinimumRelative=minimum,CurrentRelative=current,MaximumRelative=maximum};
  }
  public static int RelativeScale(ScaleState state,int percent){
   int index=Array.IndexOf(ScaleSteps,percent),recommended=Array.IndexOf(ScaleSteps,state.RecommendedPercent);
   int relative=index-recommended;
   if(index<0||recommended<0||relative<state.MinimumRelative||relative>state.MaximumRelative)
    throw new ArgumentOutOfRangeException("percent","目标屏幕不支持 "+percent+"% 缩放。");
   return relative;
  }
  static DisplayPath ActiveScalePath(Snapshot snapshot,Monitor monitor){
   var matches=snapshot.Paths.Where(p=>Key(p)==monitor.Key&&(p.Flags&1)!=0&&p.Target.Available!=0).ToArray();
   if(matches.Length!=1)throw new InvalidOperationException("目标屏幕尚未稳定激活，稍后重试缩放。");
   return matches[0];
  }
  public static ScaleState ReadScale(Snapshot snapshot,Monitor monitor){
   var p=ActiveScalePath(snapshot,monitor);
   var packet=new SourceScaleGet{Header=new DeviceHeader{Type=unchecked((uint)-3),Size=(uint)Marshal.SizeOf(typeof(SourceScaleGet)),Adapter=p.Source.Adapter,Id=p.Source.Id}};
   Check(GetSourceScale(ref packet),"读取屏幕缩放");
   return DecodeScale(packet.MinimumRelative,packet.CurrentRelative,packet.MaximumRelative);
  }
  public static bool SetScale(Snapshot snapshot,Monitor monitor,int percent){
   var p=ActiveScalePath(snapshot,monitor);var current=ReadScale(snapshot,monitor);
   if(current.CurrentPercent==percent)return false;
   var packet=new SourceScaleSet{Header=new DeviceHeader{Type=unchecked((uint)-4),Size=(uint)Marshal.SizeOf(typeof(SourceScaleSet)),Adapter=p.Source.Adapter,Id=p.Source.Id},Relative=RelativeScale(current,percent)};
   Check(SetSourceScale(ref packet),"设置屏幕缩放");
   if(ReadScale(snapshot,monitor).CurrentPercent!=percent)throw new InvalidOperationException("系统尚未确认 "+percent+"% 缩放，稍后重试。");
   return true;
  }
  static DisplayPath Invalidated(DisplayPath p){
   p.Flags=1;p.Source.ModeIndex=uint.MaxValue;p.Target.ModeIndex=uint.MaxValue;
   p.Target.Refresh=new Rational();p.Target.Scanline=0;
   if(p.Target.Rotation==0)p.Target.Rotation=1;if(p.Target.Scaling==0)p.Target.Scaling=128;
   return p;
  }
  static DisplayPath[] Choose(Snapshot s,Monitor external,bool extend){
   var ext=s.Paths.Where(p=>Key(p)==external.Key&&p.Target.Available!=0).OrderByDescending(p=>(p.Flags&1)!=0).ToArray();
   if(!extend)return new[]{ext.First()};
   var inner=s.Paths.Where(p=>IsInternal(p.Target.Technology)&&p.Target.Available!=0).OrderByDescending(p=>(p.Flags&1)!=0).ToArray();
   foreach(var e in ext)foreach(var i in inner)if(SourceKey(e)!=SourceKey(i))return new[]{e,i};
   throw new InvalidOperationException("当前未找到可用的内置屏扩展路径（合盖时请先开盖）。");
  }
  static Mode[] Positioned(Mode[] original,DisplayPath[] paths){
   var modes=(Mode[])original.Clone();uint index=paths[0].Source.ModeIndex;
   if(index>=modes.Length || modes[index].Type!=1)throw new InvalidOperationException("缺少有效的主屏模式。");
   int dx=modes[index].X,dy=modes[index].Y;
   foreach(uint id in paths.Select(x=>x.Source.ModeIndex).Distinct()){
    if(id>=modes.Length || modes[id].Type!=1)throw new InvalidOperationException("缺少有效的屏幕布局。");
    modes[id].X-=dx;modes[id].Y-=dy;
   }return modes;
  }
  public static void ApplyPaths(DisplayPath[] paths,Mode[] modes){
   uint count=modes==null?0u:(uint)modes.Length;
   if(count==0)modes=null;
   Check(SetDisplayConfig((uint)paths.Length,paths,count,modes,0x20|0x40|0x400),"验证显示配置");
   Check(SetDisplayConfig((uint)paths.Length,paths,count,modes,0x20|0x80|0x200|0x400),"应用显示配置");
  }
  public static void Apply(Snapshot s,Monitor target,string mode){
   var paths=Choose(s,target,mode=="extend");
   bool preserve=paths.All(p=>(p.Flags&1)!=0&&p.Source.ModeIndex<s.Modes.Length&&p.Target.ModeIndex<s.Modes.Length) && paths.Select(SourceKey).Distinct().Count()==paths.Length;
   if(preserve){for(int i=0;i<paths.Length;i++)paths[i].Flags=1;ApplyPaths(paths,Positioned(s.Modes,paths));}
   else {
    // Keep timings/resolution for already active outputs when adding an inactive panel.
    for(int i=0;i<paths.Length;i++){
     if((paths[i].Flags&1)!=0&&paths[i].Source.ModeIndex<s.Modes.Length&&paths[i].Target.ModeIndex<s.Modes.Length)paths[i].Flags=1;
     else paths[i]=Invalidated(paths[i]);
    }
    bool anySpecified=paths.Any(p=>p.Source.ModeIndex!=uint.MaxValue);
    ApplyPaths(paths,anySpecified?s.Modes:null);
    var fresh=Query(false);var selected=fresh.Paths.Where(p=>(p.Flags&1)!=0).OrderByDescending(p=>Key(p)==target.Key).ToArray();
    if(selected.Length!=paths.Length || Key(selected[0])!=target.Key)throw new InvalidOperationException("应用后目标屏幕未激活。");
    var primary=fresh.Monitors.First(x=>x.Key==target.Key);
    if(primary.X!=0 || primary.Y!=0){for(int i=0;i<selected.Length;i++)selected[i].Flags=1;ApplyPaths(selected,Positioned(fresh.Modes,selected));}
   }
  }
  public static void Restore(Snapshot s){
   if(s.Paths.Length==0)return;
   Check(SetDisplayConfig((uint)s.Paths.Length,s.Paths,(uint)s.Modes.Length,s.Modes,0x20|0x80|0x200|0x400),"恢复原显示配置");
  }
  public static void InternalOnly(){Check(SetDisplayConfig(0,null,0,null,0x80|1),"恢复笔记本屏幕");}
  public static void Check(int e,string operation){if(e!=0)throw new Win32Exception(e,operation+"失败 ("+e+"): "+new Win32Exception(e).Message);}
 }
}

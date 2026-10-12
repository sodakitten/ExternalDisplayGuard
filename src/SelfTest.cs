using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ExternalDisplayGuard {
 static class SelfTest {
  static List<string> passed=new List<string>();
  static void Assert(bool ok,string name){if(!ok)throw new Exception("FAILED: "+name);passed.Add(name);}
  static Monitor M(string k,int tech,bool available,bool active,int x=0){return new Monitor{Key=k,Technology=tech,External=NativeDisplay.IsExternal(tech),Internal=NativeDisplay.IsInternal(tech),Available=available,Active=active,X=x,DevicePath="device-"+k};}
  static Snapshot S(params Monitor[] m){return new Snapshot{Monitors=m.ToList(),Paths=new DisplayPath[0],Modes=new Mode[0]};}
  public static void Run(string path){
   foreach(int size in new[]{16,32,48,256})using(var icon=Artwork.Icon(size))Assert(icon.Width==size&&icon.Height==size,"Embedded artwork loads at "+size+" pixels");
   Assert(Marshal.SizeOf(typeof(DisplayPath))==72,"Win32 path ABI = 72 bytes");
   Assert(Marshal.SizeOf(typeof(Mode))==64,"Win32 mode union ABI = 64 bytes");
   Assert(Marshal.SizeOf(typeof(TargetName))==420,"Win32 target name ABI = 420 bytes");
   Assert(Marshal.SizeOf(typeof(SourceScaleGet))==32&&Marshal.SizeOf(typeof(SourceScaleSet))==24,"Source DPI packet ABI = 32 / 24 bytes");
   var scale=NativeDisplay.DecodeScale(-2,1,6);
   Assert(scale.CurrentPercent==175&&scale.RecommendedPercent==150,"Decode 175% with 150% recommended scaling");
   Assert(NativeDisplay.RelativeScale(scale,200)==2,"Set 200% relative to recommended 150%, not relative to current 175%");
   Assert(NativeDisplay.RelativeScale(NativeDisplay.DecodeScale(-3,0,5),200)==1,"Scaling offset recalculates when source recommendation changes");
   bool rejected=false;try{NativeDisplay.RelativeScale(scale,180);}catch(ArgumentOutOfRangeException){rejected=true;}Assert(rejected,"Reject unsupported percentages without rounding or clamping");
   rejected=false;try{NativeDisplay.RelativeScale(NativeDisplay.DecodeScale(-2,1,1),200);}catch(ArgumentOutOfRangeException){rejected=true;}Assert(rejected,"Reject scale outside the monitor range");
   rejected=false;try{NativeDisplay.DecodeScale(-2,int.MaxValue,6);}catch(InvalidOperationException){rejected=true;}Assert(rejected,"Reject invalid or custom scale state safely");
   Assert(Storage.Json.Deserialize<Config>("{\"Enabled\":true,\"Mode\":\"external\"}").ExternalScalePercent==200,"Existing configurations migrate to 200% without losing display choice");
   Assert(Marshal.OffsetOf(typeof(Mode),"X").ToInt32()==28&&Marshal.OffsetOf(typeof(Mode),"Y").ToInt32()==32,"Source position native offsets");
   Assert(new[]{6,11,13,int.MinValue}.All(NativeDisplay.IsInternal),"Embedded panel classifications");
   Assert(!NativeDisplay.IsExternal(17)&&!NativeDisplay.IsExternal(15)&&!NativeDisplay.IsExternal(16),"Do not auto-select virtual / wireless / indirect outputs");
   var ext=M("ext",5,true,true);var inner=M("inner",11,true,true);var virtualScreen=M("virtual",17,true,true);
   Assert(NativeDisplay.FindTarget(S(ext,inner,virtualScreen),"")==ext,"Select only the physical external screen");
   Assert(NativeDisplay.FindTarget(S(ext,M("other",10,true,true)),"")==null,"Multiple physical external screens require selection");
   Assert(NativeDisplay.FindTarget(S(ext),"device-gone")==null,"Never substitute another screen for the saved identity");
   Assert(NativeDisplay.FindTarget(S(M("ext",5,false,true)),"device-ext")==null,"Disconnected but transiently active screen is excluded");
   Assert(NativeDisplay.FindTarget(S(ext),"DEVICE-EXT")==ext,"Saved identity comparison ignores case");
   Assert(NativeDisplay.Matches(S(ext),ext.Key,"external"),"Single external screen matches external-only");
   Assert(!NativeDisplay.Matches(S(ext,inner),ext.Key,"external"),"Extended desktop does not match external-only");
   Assert(!NativeDisplay.Matches(S(ext,virtualScreen),ext.Key,"external"),"Unexpected active virtual screen does not match external-only");
   Assert(!NativeDisplay.Matches(S(M("ext",5,true,true,1920)),ext.Key,"external"),"External screen must be primary at origin");
   var a=new Luid{Low=9};var ep=new DisplayPath{Source=new PathSource{Adapter=a,Id=0},Target=new PathTarget{Adapter=a,Id=1,Technology=5,Available=1},Flags=1};
   var ip=new DisplayPath{Source=new PathSource{Adapter=a,Id=1},Target=new PathTarget{Adapter=a,Id=2,Technology=11,Available=1},Flags=1};
   ext.Key=NativeDisplay.Key(ep);inner.Key=NativeDisplay.Key(ip);
   var extended=S(ext,inner);extended.Paths=new[]{ep,ip};
   Assert(NativeDisplay.Matches(extended,ext.Key,"extend"),"Extended mode requires two independent source surfaces");
   ip.Source.Id=0;extended.Paths=new[]{ep,ip};Assert(!NativeDisplay.Matches(extended,ext.Key,"extend"),"Clone mode is rejected as extended mode");
   // Preserve opaque target timing bytes while translating only source positions.
   ep.Source.ModeIndex=0;ip.Source.ModeIndex=1;
   var modes=new[]{new Mode{Type=1,X=1920,Y=0,Width=2560,Height=1440},new Mode{Type=1,X=0,Y=0,Width=1920,Height=1080},new Mode{Type=2,U0=0x123456789abcdef0,U5=0xfedcba9876543210}};
   var translated=(Mode[])typeof(NativeDisplay).GetMethod("Positioned",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{modes,new[]{ep,ip}});
   Assert(translated[0].X==0&&translated[1].X==-1920&&modes[0].X==1920,"Translate desktop to external primary without mutating snapshot");
   Assert(translated[2].U0==modes[2].U0&&translated[2].U5==modes[2].U5,"Preserve complete target signal timing union");
   Storage.Write(path,new{Result="PASS",Count=passed.Count,Checks=passed});
  }
 }
}

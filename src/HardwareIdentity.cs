using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace ExternalDisplayGuard {
 public static class HardwareIdentity {
  static string Text(object value){var chars=value as ushort[];return chars==null?"":new string(chars.Where(x=>x>0&&x<127).Select(x=>(char)x).ToArray()).Trim();}
  static string Connection(string instance){int end=instance.LastIndexOf('_');return end>=0?instance.Substring(0,end):instance;}
  public static void Enrich(List<Monitor> monitors){
   var fingerprints=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   try{using(var search=new ManagementObjectSearcher(@"root\wmi","SELECT InstanceName, ManufacturerName, ProductCodeID, SerialNumberID FROM WmiMonitorID"))using(var results=search.Get()){
    foreach(ManagementObject record in results)using(record){string serial=Text(record["SerialNumberID"]);if(string.IsNullOrWhiteSpace(serial)||serial.All(c=>c=='0'))continue;
     fingerprints[Connection((string)record["InstanceName"])]=Text(record["ManufacturerName"])+":"+Text(record["ProductCodeID"])+":"+serial;}
   }}catch { /* A usable connection identity remains when EDID serial information is unavailable. */ }
   foreach(var m in monitors){m.StableId=m.DevicePath;m.Fingerprint="";if(string.IsNullOrEmpty(m.DevicePath))continue;
    var parts=m.DevicePath.Split('#');if(parts.Length<3)continue;string connection="DISPLAY\\"+parts[1]+"\\"+parts[2];string fingerprint;
    if(fingerprints.TryGetValue(connection,out fingerprint))m.Fingerprint=fingerprint;
   }
   foreach(var m in monitors.Where(x=>!string.IsNullOrEmpty(x.Fingerprint))){if(monitors.Count(x=>MonitorIdentity.IsPhysical(x)&&x.Available&&x.Fingerprint==m.Fingerprint)==1)m.StableId="edid:"+m.Fingerprint;}
  }
 }
}

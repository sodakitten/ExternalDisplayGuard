using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ExternalDisplayGuard {
 static class Artwork {
  [DllImport("user32.dll")] static extern IntPtr CreateIconFromResourceEx(byte[] data,uint length,bool icon,uint version,int width,int height,uint flags);
  [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
  public static Icon Icon(int size){
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("ExternalDisplayGuard.App.ico")){
    if(stream==null)throw new InvalidOperationException("程序图标资源缺失，请重新构建。");
    // Framework Icon treats a 256px ICO directory width (encoded as zero) inconsistently.
    // Select the exact PNG frame and let Windows decode it, including its alpha channel.
    using(var reader=new BinaryReader(stream)){
     if(reader.ReadUInt16()!=0||reader.ReadUInt16()!=1)throw new InvalidDataException("图标格式无效。");
     int count=reader.ReadUInt16();
     for(int i=0;i<count;i++){
      int width=reader.ReadByte(),height=reader.ReadByte();if(width==0)width=256;if(height==0)height=256;
      reader.ReadByte();reader.ReadByte();reader.ReadUInt16();reader.ReadUInt16();int length=reader.ReadInt32(),offset=reader.ReadInt32();
      if(width!=size||height!=size)continue;
      if(offset<0||length<=0||offset+(long)length>stream.Length)throw new InvalidDataException("图标数据损坏。");
      stream.Position=offset;var data=reader.ReadBytes(length);IntPtr handle=CreateIconFromResourceEx(data,(uint)data.Length,true,0x00030000,size,size,0);
      if(handle==IntPtr.Zero)throw new InvalidOperationException("Windows 无法读取程序图标。");
      try{using(var icon=System.Drawing.Icon.FromHandle(handle))return (Icon)icon.Clone();}finally{DestroyIcon(handle);}
     }throw new InvalidDataException("程序图标缺少所需尺寸："+size);
    }
   }
  }
}
}

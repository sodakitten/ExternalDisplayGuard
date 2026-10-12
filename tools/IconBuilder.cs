using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// Code-native vector artwork: draw every ICO size separately for crisp small Windows icons.
static class IconBuilder {
 static GraphicsPath Round(float x,float y,float w,float h,float r){
  var p=new GraphicsPath();float d=r*2;p.AddArc(x,y,d,d,180,90);p.AddArc(x+w-d,y,d,d,270,90);p.AddArc(x+w-d,y+h-d,d,d,0,90);p.AddArc(x,y+h-d,d,d,90,90);p.CloseFigure();return p;
 }
 static void FillRound(Graphics g,Brush b,float x,float y,float w,float h,float r){using(var p=Round(x,y,w,h,r))g.FillPath(b,p);}
 static Bitmap Render(int size){
  var b=new Bitmap(size,size,PixelFormat.Format32bppArgb);
  using(var g=Graphics.FromImage(b)){
   g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.CompositingQuality=CompositingQuality.HighQuality;g.ScaleTransform(size/256f,size/256f);
   using(var gradient=new LinearGradientBrush(new PointF(8,8),new PointF(215,248),Color.FromArgb(28,53,87),Color.FromArgb(8,19,33)))FillRound(g,gradient,8,8,240,240,54);
   using(var outline=new Pen(Color.FromArgb(31,255,255,255),2))using(var p=Round(9,9,238,238,53))g.DrawPath(outline,p);
   using(var white=new SolidBrush(Color.FromArgb(233,245,255))){
    FillRound(g,white,41,52,174,124,13);FillRound(g,white,117,173,22,25,3);FillRound(g,white,91,198,74,10,5);
   }
   using(var gradient=new LinearGradientBrush(new PointF(50,61),new PointF(206,160),Color.FromArgb(22,63,100),Color.FromArgb(9,32,57)))FillRound(g,gradient,50,61,156,99,6);
   if(size>=32)using(var highlight=new Pen(Color.FromArgb(190,86,207,255),5)){highlight.StartCap=LineCap.Round;highlight.EndCap=LineCap.Round;g.DrawLine(highlight,60,72,144,72);g.DrawLine(highlight,60,83,109,83);}
   using(var shield=new GraphicsPath()){
    shield.AddLines(new[]{new PointF(191,132),new PointF(225,146),new PointF(223,180)});
    shield.AddBezier(223,180,218,202,204,212,191,220);shield.AddBezier(191,220,178,212,164,202,159,180);
    shield.AddLine(159,180,157,146);shield.CloseFigure();
    using(var gradient=new LinearGradientBrush(new PointF(175,132),new PointF(210,220),Color.FromArgb(66,238,208),Color.FromArgb(17,181,168)))g.FillPath(gradient,shield);
    using(var pen=new Pen(Color.FromArgb(10,27,45),7)){pen.LineJoin=LineJoin.Round;g.DrawPath(pen,shield);}
   }
   using(var check=new Pen(Color.White,size<=24?12:10)){check.StartCap=LineCap.Round;check.EndCap=LineCap.Round;check.LineJoin=LineJoin.Round;g.DrawLines(check,new[]{new PointF(175,175),new PointF(186,187),new PointF(208,163)});}
  }return b;
 }
 static int Main(string[] args){
  string root=args[0];Directory.CreateDirectory(root);int[] sizes={16,24,32,48,64,128,256};var frames=new List<byte[]>();
  foreach(int size in sizes)using(var b=Render(size))using(var stream=new MemoryStream()){
   b.Save(stream,ImageFormat.Png);frames.Add(stream.ToArray());if(size==256)b.Save(Path.Combine(root,"app-icon.png"),ImageFormat.Png);
  }
  using(var stream=File.Create(Path.Combine(root,"app-icon.ico")))using(var w=new BinaryWriter(stream)){
   w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;
   for(int i=0;i<sizes.Length;i++){w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)0);w.Write((byte)0);w.Write((ushort)1);w.Write((ushort)32);w.Write(frames[i].Length);w.Write(offset);offset+=frames[i].Length;}
   foreach(var data in frames)w.Write(data);
  }
  using(var preview=new Bitmap(940,320,PixelFormat.Format32bppArgb))using(var g=Graphics.FromImage(preview)){
   g.Clear(Color.FromArgb(241,245,249));using(var b=Render(256))g.DrawImageUnscaled(b,32,32);
   using(var font=new Font("Segoe UI",13))using(var brush=new SolidBrush(Color.FromArgb(40,55,74))){
    g.DrawString("External Display Guard",font,brush,330,40);int x=340;
    foreach(int size in new[]{16,24,32,48,64,128}){using(var b=Render(size))g.DrawImageUnscaled(b,x,110+(128-size)/2);g.DrawString(size+" px",font,brush,x,255);x+=88;}
   }preview.Save(Path.Combine(root,"icon-preview.png"),ImageFormat.Png);
  }return 0;
 }
}

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskCalendar {
public static partial class SelfTests {
 static void CheckWindowCorners(Window window,Border frame,string label){
  Check(frame.BorderThickness==new Thickness(0)&&frame.CornerRadius==new CornerRadius(0)&&frame.SnapsToDevicePixels&&frame.UseLayoutRounding,label+"不叠加自绘描边与圆角，采用像素对齐填充");
  window.UpdateLayout();var background=((SolidColorBrush)frame.Background).Color;
  foreach(double scale in new[]{1.0,1.25,1.5,2.0}){
   int width=(int)Math.Floor(frame.ActualWidth*scale),height=(int)Math.Floor(frame.ActualHeight*scale);var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen()){dc.PushTransform(new ScaleTransform(scale,scale));dc.DrawRectangle(new VisualBrush(frame),null,new Rect(0,0,frame.ActualWidth,frame.ActualHeight));dc.Pop();}
   var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);bool uniform=true;string values="";foreach(var point in new[]{new Int32Rect(0,0,1,1),new Int32Rect(width-1,0,1,1),new Int32Rect(0,height-1,1,1),new Int32Rect(width-1,height-1,1,1)}){byte[] pixel=new byte[4];bitmap.CopyPixels(point,pixel,4,0);values+=BitConverter.ToString(pixel)+" ";uniform=uniform&&Math.Abs(pixel[3]-background.A)<=1&&pixel[0]<=background.B+1&&pixel[1]<=background.G+1&&pixel[2]<=background.R+1;}
   Check(uniform,label+"在 "+(scale*100)+"% 绘制缩放下四角没有浅色描边或透明缺口 "+frame.ActualWidth+"x"+frame.ActualHeight+" bg="+background+" samples="+values);
  }
 }
}
}

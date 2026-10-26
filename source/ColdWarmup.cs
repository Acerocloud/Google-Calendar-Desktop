using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace DeskCalendar {
public partial class CalendarWindow {
 DispatcherOperation coldWarmup;bool coldWarmupDone;
 void CancelColdWarmup(){if(coldWarmup!=null&&coldWarmup.Status==DispatcherOperationStatus.Pending)coldWarmup.Abort();coldWarmup=null;}
 void QueueColdWarmup(){
  if(rendering||coldWarmupDone||exiting||coldWarmup!=null)return;
  coldWarmup=Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>{
   coldWarmup=null;if(IsVisible||exiting||suspended||sessionLocked)return;
   try{WarmHiddenCalendar();}catch{ /* Warmup is optional; normal opening remains available. */ }
  }));
 }
 void WarmHiddenCalendar(){
  if(coldWarmupDone||IsVisible||exiting)return;
  new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();PrepareTasksWindow(false);WarmSurface(surface,Width,Height);var frame=tasksWindow==null?null:tasksWindow.Tag as Border;if(frame!=null)WarmSurface(frame,tasksWindow.Width,tasksWindow.Height);coldWarmupDone=true;
 }
 static void WarmSurface(FrameworkElement root,double width,double height){
  if(root==null)return;var size=new Size(Math.Max(1,width),Math.Max(1,height));root.Measure(size);root.Arrange(new Rect(size));root.UpdateLayout();
  // One transient raster pass primes text/control rendering without showing,
  // activating or moving either desktop window. No bitmap is retained.
  var pixels=new RenderTargetBitmap((int)Math.Ceiling(Math.Min(1600,size.Width)),(int)Math.Ceiling(Math.Min(1300,size.Height)),96,96,PixelFormats.Pbgra32);pixels.Render(root);
 }
}
}

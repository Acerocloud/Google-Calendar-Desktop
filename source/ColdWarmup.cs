using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;
namespace DeskCalendar {
public partial class CalendarWindow {
 DispatcherOperation coldWarmup;bool coldWarmupDone,coldPreparing;
 [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
 [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);
 [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr window,IntPtr region,bool redraw);
 static bool MaskColdWindow(Window window,bool hide){var hwnd=new WindowInteropHelper(window).EnsureHandle();if(!hide)return SetWindowRgn(hwnd,IntPtr.Zero,false)!=0;var region=CreateRectRgn(0,0,0,0);if(region==IntPtr.Zero)return false;if(SetWindowRgn(hwnd,region,false)!=0)return true;DeleteObject(region);return false;}
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
  new WindowInteropHelper(this).EnsureHandle();PrepareTasksWindow(false);
  bool mainMask=false,taskMask=false;bool activation=ShowActivated;
  coldPreparing=true;
  try{
   mainMask=MaskColdWindow(this,true);taskMask=MaskColdWindow(tasksWindow,true);
   if(!mainMask||!taskMask)throw new InvalidOperationException("Cannot prepare invisible window regions.");
   ShowActivated=false;tasksWindow.ShowActivated=false;
   // Execute the exact final-size, final-monitor show path with empty native
   // regions: no pixels, hit-test area or activation, but full WPF Loaded/JIT.
   ShowCalendar();if(!tasksWindow.IsVisible)ShowTasks();Render();RenderTasks();UpdateLayout();tasksWindow.UpdateLayout();
   WarmSurface(surface,Width,Height);WarmSurface((Border)tasksWindow.Tag,tasksWindow.Width,tasksWindow.Height);NativeRect bounds;if(GetWindowRect(new WindowInteropHelper(this).Handle,out bounds)){StartCalendarRise(new System.Drawing.Rectangle(bounds.Left,bounds.Top,bounds.Right-bounds.Left,bounds.Bottom-bounds.Top),0);popupRise.Stop(false);}coldWarmupDone=true;
  }finally{
   Hide();tasksWindow.Hide();if(mainMask)MaskColdWindow(this,false);if(taskMask)MaskColdWindow(tasksWindow,false);
   ShowActivated=activation;tasksWindow.ShowActivated=true;resize.Stop();saveTimer.Stop();dirtySize=false;coldPreparing=false;Schedule();
  }
 }
 static void WarmSurface(FrameworkElement root,double width,double height){
  if(root==null)return;var size=new Size(Math.Max(1,width),Math.Max(1,height));root.Measure(size);root.Arrange(new Rect(size));root.UpdateLayout();
  // One transient raster pass primes text/control rendering without showing,
  // activating or moving either desktop window. No bitmap is retained.
  var pixels=new RenderTargetBitmap((int)Math.Ceiling(Math.Min(1600,size.Width)),(int)Math.Ceiling(Math.Min(1300,size.Height)),96,96,PixelFormats.Pbgra32);pixels.Render(root);
 }
}
}

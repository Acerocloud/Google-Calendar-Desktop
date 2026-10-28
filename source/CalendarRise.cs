using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Interop;
namespace DeskCalendar {
public partial class CalendarWindow {
 bool deferredRiseRender,deferredRiseTasks;
 void FinishCalendarRise(){bool render=deferredRiseRender,tasks=deferredRiseTasks;deferredRiseRender=deferredRiseTasks=false;if(render)Render();if(tasks)RenderTasks();UpdateTasksReveal();}
 void StartCalendarRise(Rectangle target,int distance){if(tasksReveal!=null)tasksReveal.IsOpen=false;
  IntPtr main=new WindowInteropHelper(this).Handle,companion=IntPtr.Zero;int offsetX=0;
  if(tasksWindow!=null&&tasksWindow.IsVisible){companion=new WindowInteropHelper(tasksWindow).Handle;NativeRect a,b;if(GetWindowRect(main,out a)&&GetWindowRect(companion,out b))offsetX=b.Left-a.Left;}
  var expectedForeground=Native.GetForegroundWindow();popupRise.BeforeStart=coldPreparing?null:new Action(()=>{if(!IsVisible||exiting)return;var foreground=Native.GetForegroundWindow();uint owner;Native.GetWindowThreadProcessId(foreground,out owner);if(foreground!=expectedForeground&&owner!=(uint)System.Diagnostics.Process.GetCurrentProcess().Id){HidePopup();return;}if(!IsActive&&(tasksWindow==null||!tasksWindow.IsActive))Activate();});
  popupRise.WarmupFrames=coldWarmupDone?0:2;popupRise.Start(target,distance,r=>{linkingTasks=true;try{Native.MovePair(main,companion,r.X,r.Y,offsetX);}finally{linkingTasks=false;}});
 }
}

}

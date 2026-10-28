using System;
using System.Windows.Interop;
using Forms=System.Windows.Forms;

namespace DeskCalendar {
public partial class CalendarWindow {
 bool positioningHome;
 void AnchorHome(){
  if(positioningHome||linkingTasks||coldPreparing||rendering||!IsVisible||popupRise.Running)return;
  positioningHome=true;
  try{
   var hwnd=new WindowInteropHelper(this).Handle;var screen=Forms.Screen.FromHandle(hwnd);var work=screen.WorkingArea;
   double scale=Native.MonitorScale(new System.Drawing.Point(work.Left+work.Width/2,work.Top+work.Height/2));
   Native.Position(hwnd,PopupPlacement.HomeBounds(work,(int)Math.Round(Width*scale),(int)Math.Round(Height*scale),(int)Math.Round(((tasksWindow==null?prefs.TasksWidth:tasksWindow.Width)+10)*scale),(int)(8*scale)));
   AlignTasks();
  }finally{positioningHome=false;}
 }
}
}

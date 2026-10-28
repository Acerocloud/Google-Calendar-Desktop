using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace DeskCalendar {
public static partial class SelfTests {
 static void HomePlacementChecks(CalendarWindow window,Window tasks){
  var work=new System.Drawing.Rectangle(-1920,0,1920,1040);
  var bounds=PopupPlacement.HomeBounds(work,760,650,310,8);
  Check(bounds.Right==work.Right-8&&bounds.Bottom==work.Bottom-8,"两框整体锚定负坐标显示器右下角，预留左侧待办宽度");
  var small=PopupPlacement.HomeBounds(new System.Drawing.Rectangle(0,0,800,600),900,800,310,8);
  Check(small.Left==8&&small.Top==8&&small.Width==784&&small.Height==584,"小屏幕定位计算不越过工作区");
  Check(typeof(Preferences).GetField("TasksCollapsed")==null&&!Descendants(tasks).OfType<Button>().Any(b=>b.Name=="CollapseTasks"),"移除待办隐藏设置与入口");
  var field=typeof(CalendarWindow).GetField("rendering",BindingFlags.Instance|BindingFlags.NonPublic);field.SetValue(window,false);
  try{
   window.ShowCalendar();PumpMotion(280);Pump(window);
   var screen=System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle);var area=screen.WorkingArea;double scale=Native.MonitorScale(new System.Drawing.Point(area.Left+area.Width/2,area.Top+area.Height/2));
   Action verify=()=>{Check(tasks.IsVisible&&Math.Abs((window.Left+window.Width)*scale-area.Right+(int)(8*scale))<=2&&Math.Abs((window.Top+window.Height)*scale-area.Bottom+(int)(8*scale))<=2,"真实窗口显示及调整尺寸后保持两框右下角锚定");};
   verify();window.Width-=20;window.Height-=20;PumpMotion(120);verify();tasks.Width+=10;PumpMotion(120);verify();
   window.Left+=40;Pump(window);verify();
  }finally{field.SetValue(window,true);}
 }
}
}

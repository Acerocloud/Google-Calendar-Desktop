using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace DeskCalendar {
public static partial class SelfTests {
 static void TasksCollapseChecks(CalendarWindow window,Window tasks){
  var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(CalendarWindow);
  var prefs=(Preferences)type.GetField("prefs",flags).GetValue(window);
  var toggle=type.GetMethod("SetTasksCollapsed",flags);var savedWidth=tasks.Width;var savedLeft=window.Left;var mainWidth=window.Width;
  window.Show();window.Activate();type.GetMethod("ShowTasks",flags).Invoke(window,null);Pump(window);
  Descendants(tasks).OfType<Button>().Single(b=>b.Name=="CollapseTasks").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(window);
  var reveal=(Popup)type.GetField("tasksReveal",flags).GetValue(window);
  Check(prefs.TasksCollapsed&&!tasks.IsVisible&&window.IsVisible&&reveal.IsOpen,"收起箭头只隐藏待办，在日历左侧显示展开页签 collapsed="+prefs.TasksCollapsed+" task="+tasks.IsVisible+" main="+window.IsVisible+" reveal="+(reveal==null?"null":reveal.IsOpen.ToString())+" editor="+(type.GetField("taskEditor",flags).GetValue(window)!=null));
  type.GetMethod("ShowTasks",flags).Invoke(window,null);Check(!tasks.IsVisible,"重新打开主界面时不会擅自展开已收起的待办");
  window.Height+=12;Pump(window);Check(reveal.VerticalOffset==window.ActualHeight-48&&tasks.Height==window.Height,"隐藏后的页签跟随日历底边，待办仍保持高度绑定");
  type.GetMethod("Save",flags).Invoke(window,null);var store=(Store)type.GetField("store",flags).GetValue(window);Check(store.Read<Preferences>("settings.json",false).TasksCollapsed,"待办收起状态写入现有设置");
  Descendants(reveal.Child).OfType<Button>().Single(b=>b.Name=="ExpandTasks").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(window);
  Check(!prefs.TasksCollapsed&&tasks.IsVisible&&!reveal.IsOpen&&tasks.Width==savedWidth&&window.Left==savedLeft&&window.Width==mainWidth,"展开恢复原待办宽度，日历位置尺寸不变，展开页签隐藏");
  type.GetField("rendering",flags).SetValue(window,false);
  try{
   toggle.Invoke(window,new object[]{true});PumpMotion(260);Pump(window);
   var motion=(PopupRise)type.GetField("tasksSlide",flags).GetValue(window);
   Check(!motion.Running&&!tasks.IsVisible&&reveal.IsOpen&&!HasInvisibleColdRegion(tasks),"收起动画完成释放帧订阅并清除原生裁剪，不残留空窗口");
   toggle.Invoke(window,new object[]{false});PumpMotion(260);Pump(window);
   Check(!motion.Running&&tasks.IsVisible&&!reveal.IsOpen&&!HasInvisibleColdRegion(tasks)&&Math.Abs(tasks.Left+tasks.Width+10-window.Left)<1,"展开动画恢复完整窗口和两栏间距，不改变主页位置");
   toggle.Invoke(window,new object[]{true});window.HidePopup();PumpMotion(220);Check(!window.IsVisible&&!tasks.IsVisible&&!reveal.IsOpen&&!motion.Running,"动画中关闭日历同时停止动画并隐藏侧边页签");
   window.ShowCalendar();PumpMotion(260);Pump(window);Check(!tasks.IsVisible&&reveal.IsOpen&&prefs.TasksCollapsed,"托盘再次打开时保持待办折叠，并恢复展开页签");
   toggle.Invoke(window,new object[]{false});PumpMotion(260);Pump(window);
  }finally{type.GetField("rendering",flags).SetValue(window,true);}
 }
}
}

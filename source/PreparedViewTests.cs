using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace DeskCalendar {
public static partial class SelfTests {
 static void PreparedViewChecks(CalendarWindow window){
  var ev=new CalendarEvent{Start="2026-10-25T09:00:00+08:00",End="2026-10-25T09:30:00+08:00"};Check(ev.DurationMinutes==30&&!ev.IsPoint,"解析时间缓存保留原始偏移的持续时间语义");ev.End="2026-10-25T10:00:00+08:00";Check(ev.DurationMinutes==60,"结束时间改变时自动失效缓存");ev.Start=ev.End;Check(ev.IsPoint&&ev.DurationMinutes==0,"开始时间改变时更新点事件判定");ev.AllDay=true;ev.Start="2026-10-25";ev.End="2026-10-27";Check(ev.DurationMinutes==2880&&!ev.IsPoint,"切换全天语义时重新解析日期");Check(!Store.Json.Serialize(ev).Contains("parsedStart"),"解析缓存不写入用户日程文件");
  var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(CalendarWindow);var clear=type.GetMethod("ClearPreparedViews",flags);var queue=type.GetMethod("QueuePreparedViews",flags);var entries=(IDictionary)type.GetField("preparedViews",flags).GetValue(window);clear.Invoke(window,null);var prefs=(Preferences)type.GetField("prefs",flags).GetValue(window);string original=prefs.View;var anchor=type.GetField("anchor",flags).GetValue(window);var timeline=type.GetField("timelineViewer",flags).GetValue(window);
  queue.Invoke(window,null);PumpMotion(180);Check(entries.Count<=4&&prefs.View==original&&Equals(type.GetField("anchor",flags).GetValue(window),anchor)&&ReferenceEquals(type.GetField("timelineViewer",flags).GetValue(window),timeline),"空闲预准备最多四页且不改变当前日期、视图和时间轴");
  if(entries.Count>0){string key=entries.Keys.Cast<string>().First();string target=key.StartsWith("week")?"week":key.StartsWith("quarter")?"quarter":"month";if(target!=original){prefs.View=target;int count=entries.Count;bool used=(bool)type.GetMethod("UsePreparedView",flags).Invoke(window,null);Check(used&&entries.Count==count-1,"切换复用已准备视图并移出准备池，不重复重建");prefs.View=original;}}
  clear.Invoke(window,null);Check(entries.Count==0,"清理时释放全部准备视图并取消空闲任务");
  queue.Invoke(window,null);window.HidePopup();PumpMotion(40);Check(entries.Count==0&&!window.IsVisible,"收起日历后不保留准备视图或继续后台构建");window.ShowCalendar();
 }
}
}

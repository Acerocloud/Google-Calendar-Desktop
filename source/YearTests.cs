using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace DeskCalendar {
public static partial class SelfTests {
 static void YearChecks(CalendarWindow window){
  Check(CalendarMath.PeriodTitle(new DateTime(2026,10,26),"quarter")=="2026 Q4"&&CalendarMath.PeriodTitle(new DateTime(2027,1,1),"quarter")=="2027 Q1","季度标题采用 YYYY QN 格式");Check(CalendarMath.PeriodTitle(new DateTime(2026,10,26),"week")=="10 月 26 日 - 11 月 1 日","周标题使用英文短横线");
  Check(CalendarMath.Navigation(new DateTime(2028,2,29),"year",1)==new DateTime(2029,2,28)&&CalendarMath.Navigation(new DateTime(2028,2,29),"year",-1)==new DateTime(2027,2,28),"年导航前后一年且正确处理闰日");Check(MotionPolicy.Direction("quarter","year")==1&&MotionPolicy.Direction("year","week")==-1,"年视图纳入滑动切换顺序");
  var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(CalendarWindow);var prefs=(Preferences)type.GetField("prefs",flags).GetValue(window);var oldAnchor=type.GetField("anchor",flags).GetValue(window);var oldCache=type.GetField("cache",flags).GetValue(window);string oldView=prefs.View;var oldSelection=prefs.Selected;
  try{
   var cache=new CacheData();cache.Events.Add(new CalendarEvent{Id="all",CalendarId="year-test",Title="跨月全天",AllDay=true,Start="2028-02-28",End="2028-03-02",Color="#82A8CD"});cache.Events.Add(new CalendarEvent{Id="point",CalendarId="year-test",Title="闰日事件",Start=new DateTimeOffset(new DateTime(2028,2,29,10,0,0)).ToString("o"),End=new DateTimeOffset(new DateTime(2028,2,29,10,0,0)).ToString("o"),Color="#82A8CD"});type.GetField("cache",flags).SetValue(window,cache);prefs.Selected=new System.Collections.Generic.List<string>{"year-test"};prefs.View="year";type.GetField("anchor",flags).SetValue(window,new DateTime(2028,6,1));type.GetMethod("Render",flags,null,Type.EmptyTypes,null).Invoke(window,null);Pump(window);
   Check(Descendants(window).OfType<Button>().Any(b=>Convert.ToString(b.Tag)=="year"),"主页提供年分段选项");Check(((TextBlock)type.GetField("period",flags).GetValue(window)).Text=="2028 年","年视图标题显示对应年份");var year=(UIElement)((ContentControl)type.GetField("content",flags).GetValue(window)).Content;var grid=Descendants(year).OfType<UniformGrid>().Single(g=>g.Name=="YearMonths");Check(grid.Columns==4&&grid.Rows==3&&grid.Children.Count==12,"年视图完整展示十二个月的 4x3 布局");var days=Descendants(year).OfType<Button>().Where(b=>b.Name=="YearDay").ToList();Check(days.Count==366&&days.All(b=>((DateTime)b.Tag).Year==2028),"闰年包含 366 个本年日期，不显示前后年份补位日期");Check(Convert.ToString(days.Single(b=>(DateTime)b.Tag==new DateTime(2028,2,29)).ToolTip).Contains("2 项日程")&&Convert.ToString(days.Single(b=>(DateTime)b.Tag==new DateTime(2028,3,1)).ToolTip).Contains("1 项日程"),"年视图正确标记点事件、跨月和全天日程");
   var range=(Tuple<DateTime,DateTime>)type.GetMethod("Range",flags).Invoke(window,null);Check(range.Item1==new DateTime(2027,12,25)&&range.Item2==new DateTime(2029,1,8),"年视图同步覆盖全年并保留边界缓冲");
   var marked=days.Single(b=>(DateTime)b.Tag==new DateTime(2028,2,29));var empty=days.Single(b=>(DateTime)b.Tag==new DateTime(2028,2,27));var markedCell=(Grid)marked.Parent;var emptyCell=(Grid)empty.Parent;double pixel=1/System.Windows.Media.VisualTreeHelper.GetDpi(marked).DpiScaleY;Check(markedCell.RowDefinitions[1].Height.Value==5&&emptyCell.RowDefinitions[1].Height.Value==5&&Math.Abs(marked.TranslatePoint(new Point(0,marked.ActualHeight/2),markedCell).Y-empty.TranslatePoint(new Point(0,empty.ActualHeight/2),emptyCell).Y)<=pixel,"年度有无日程采用相同标记留白，日期数字保持垂直对齐");
   Check(marked.FontWeight==FontWeights.SemiBold&&empty.FontWeight==FontWeights.Normal&&Equals(marked.Content,"29")&&Equals(empty.Content,"27"),"年度日期使用相同文本结构，有日程仅改变字重与颜色");
   var yearDot=Descendants(markedCell).OfType<System.Windows.Shapes.Ellipse>().Single();Check(yearDot.Width==3.5&&yearDot.Height==3.5&&Grid.GetRow(yearDot)==1,"年度圆点放大至 3.5 DIP，并独立放在数字下方");
   var mini=(UIElement)type.GetMethod("MonthView",flags).Invoke(window,new object[]{new DateTime(2028,2,1),true});var quarterDates=Descendants(mini).OfType<Button>().ToList();var busyQuarter=quarterDates.Single(b=>Equals(b.Content,"29"));var quietQuarter=quarterDates.Single(b=>Equals(b.Content,"27"));var quarterDot=Descendants((DependencyObject)busyQuarter.Parent).OfType<System.Windows.Shapes.Ellipse>().Single();Check(busyQuarter.FontWeight==marked.FontWeight&&quietQuarter.FontWeight==empty.FontWeight&&quarterDot.Width==yearDot.Width&&quarterDot.Height==yearDot.Height&&((System.Windows.Media.SolidColorBrush)quarterDot.Fill).Color==((System.Windows.Media.SolidColorBrush)yearDot.Fill).Color,"季度与年度统一有日程字重、圆点颜色和大小");
   var leap=days.Single(b=>(DateTime)b.Tag==new DateTime(2028,2,29));leap.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var detail=(Window)type.GetField("dayDetail",flags).GetValue(window);Check(detail!=null&&detail.IsVisible,"点击年历日期打开当天日程");detail.Close();
   Descendants(year).OfType<Button>().First(b=>b.Name=="YearMonth").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(prefs.View=="month"&&((DateTime)type.GetField("anchor",flags).GetValue(window)).Month==1,"点击年历月份进入对应月视图");
   prefs.View="year";type.GetMethod("QueuePreparedViews",flags).Invoke(window,null);var entries=(System.Collections.IDictionary)type.GetField("preparedViews",flags).GetValue(window);Check(entries.Count==0,"年视图不额外准备前后整年页面");
  }finally{prefs.View=oldView;prefs.Selected=oldSelection;type.GetField("anchor",flags).SetValue(window,oldAnchor);type.GetField("cache",flags).SetValue(window,oldCache);type.GetMethod("Render",flags,null,Type.EmptyTypes,null).Invoke(window,null);}
 }
}
}

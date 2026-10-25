using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DeskCalendar {
public partial class CalendarWindow {
 UIElement YearView(){
  var months=new UniformGrid{Name="YearMonths",Columns=4,Rows=3,MinHeight=414,Height=Math.Max(414,content==null?0:content.ActualHeight)};int year=anchor.Year;var events=VisibleEvents().ToList();var byDay=new Dictionary<DateTime,List<CalendarEvent>>();
  DateTime first=new DateTime(year,1,1),limit=first.AddYears(1);foreach(var ev in events){DateTime start=ev.StartLocal.Date<first?first:ev.StartLocal.Date;DateTime end=ev.IsPoint?ev.StartLocal.Date.AddDays(1):ev.EndLocal.TimeOfDay==TimeSpan.Zero?ev.EndLocal.Date:ev.EndLocal.Date.AddDays(1);if(end>limit)end=limit;for(DateTime day=start;day<end;day=day.AddDays(1)){List<CalendarEvent> list;if(!byDay.TryGetValue(day,out list)){list=new List<CalendarEvent>();byDay[day]=list;}list.Add(ev);}}
  for(int m=1;m<=12;m++){
   DateTime month=new DateTime(year,m,1);var card=new Grid{Margin=new Thickness(5,3,5,3),Tag=month};card.RowDefinitions.Add(new RowDefinition{Height=new GridLength(24)});card.RowDefinitions.Add(new RowDefinition{Height=new GridLength(16)});card.RowDefinitions.Add(new RowDefinition());
   var heading=Button(m+" 月","查看 "+month.ToString("yyyy年M月"),()=>{int direction=month.CompareTo(anchor);anchor=month;prefs.View="month";QueuePreferencesSave();Navigate(direction==0?-1:direction);});heading.Name="YearMonth";heading.FontSize=12;heading.MinHeight=0;heading.Padding=new Thickness(0);heading.Margin=new Thickness(0);heading.HorizontalAlignment=HorizontalAlignment.Left;card.Children.Add(heading);
   var weekdays=new UniformGrid{Columns=7};foreach(string label in new[]{"一","二","三","四","五","六","日"}){var text=Text(label,9,muted);text.HorizontalAlignment=HorizontalAlignment.Center;weekdays.Children.Add(text);}Grid.SetRow(weekdays,1);card.Children.Add(weekdays);
   var days=new UniformGrid{Columns=7,Rows=6};int offset=((int)month.DayOfWeek+6)%7,total=DateTime.DaysInMonth(year,m);for(int i=0;i<42;i++){int number=i-offset+1;if(number<1||number>total){days.Children.Add(new Border{IsHitTestVisible=false});continue;}DateTime day=month.AddDays(number-1);List<CalendarEvent> items;byDay.TryGetValue(day,out items);int count=items==null?0:items.Count;var button=Button(number.ToString(),day.ToString("yyyy年M月d日")+"，"+count+" 项日程",()=>OpenDay(day));button.Name="YearDay";button.Tag=day;button.FontSize=10;button.FontWeight=count>0?FontWeights.SemiBold:FontWeights.Normal;button.MinHeight=button.MinWidth=0;button.MaxWidth=24;button.MaxHeight=22;button.Padding=new Thickness(0);button.Margin=new Thickness(0);button.Foreground=count>0?foreground:muted;
    if(day==DateTime.Today){button.Background=foreground;button.Foreground=prefs.Theme=="light"?Brushes.White:BrushColor("#202122");button.Content=number.ToString();}days.Children.Add(CompactDateCell(button,count>0));
   }Grid.SetRow(days,2);card.Children.Add(days);months.Children.Add(card);
  }
  return new ScrollViewer{Name="YearViewport",Content=months,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
 }
}
}

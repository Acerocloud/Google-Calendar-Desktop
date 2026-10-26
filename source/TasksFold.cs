using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
namespace DeskCalendar {
public partial class CalendarWindow {
 void AddCompletedTasks(StackPanel body,List<TodoItem> items){
  var completed=items.Where(t=>t.Completed).OrderByDescending(t=>t.CompletionTime??DateTimeOffset.MinValue).ToList();var section=new StackPanel();var frame=new Border{Name="CompletedTasksSection",Background=hover,CornerRadius=new CornerRadius(5),Margin=new Thickness(0,16,0,0),Child=section,HorizontalAlignment=HorizontalAlignment.Stretch};body.Children.Add(frame);
  var now=DateTimeOffset.Now;int recentCount=completed.Count(t=>t.RecentlyCompleted(now));
  Func<UIElement> build=()=>{var groups=new StackPanel();AddCompletedGroup(groups,"近 7 天已完成",completed.Where(t=>t.RecentlyCompleted(now)).ToList(),recentTasksOpen,v=>recentTasksOpen=v,"RecentTasks");AddCompletedGroup(groups,"历史已完成",completed.Where(t=>!t.RecentlyCompleted(now)).ToList(),historyTasksOpen,v=>historyTasksOpen=v,"HistoryTasks");return groups;};
  var reveal=TaskFold(completedTasksOpen,build);Button toggle=null;toggle=Button((completedTasksOpen?"⌄":"›")+"  已完成  "+recentCount,"展开或收起已完成",()=>{completedTasksOpen=!completedTasksOpen;toggle.Content=(completedTasksOpen?"⌄":"›")+"  已完成  "+recentCount;AnimateTaskFold(reveal,completedTasksOpen,build);});toggle.Name="CompletedTasksToggle";toggle.HorizontalAlignment=HorizontalAlignment.Stretch;toggle.HorizontalContentAlignment=HorizontalAlignment.Left;toggle.Padding=new Thickness(12,10,12,10);toggle.Margin=new Thickness(0);section.Children.Add(toggle);section.Children.Add(reveal);
 }
 void AddCompletedGroup(StackPanel section,string label,List<TodoItem> items,bool expanded,Action<bool> changed,string name){
  Func<UIElement> build=()=>{var rows=new StackPanel{Margin=new Thickness(8,4,8,4)};if(items.Count==0)rows.Children.Add(Text("暂无记录",12,muted));foreach(var item in items)rows.Children.Add(TaskRow(item));return rows;};var reveal=TaskFold(expanded,build);Button header=null;header=Button((expanded?"⌄":"›")+"  "+label+"  "+items.Count,label,()=>{expanded=!expanded;changed(expanded);header.Content=(expanded?"⌄":"›")+"  "+label+"  "+items.Count;AnimateTaskFold(reveal,expanded,build);});header.Name=name;header.HorizontalContentAlignment=HorizontalAlignment.Left;header.Padding=new Thickness(12,8,12,8);header.Margin=new Thickness(0);section.Children.Add(header);section.Children.Add(reveal);
 }
 Border TaskFold(bool expanded,Func<UIElement> build){var frame=new Border{ClipToBounds=true,Visibility=expanded?Visibility.Visible:Visibility.Collapsed,Child=expanded?build():null,Tag=0};frame.Unloaded+=delegate{frame.Tag=(int)frame.Tag+1;frame.BeginAnimation(FrameworkElement.HeightProperty,null);};return frame;}
 void AnimateTaskFold(Border frame,bool expanded,Func<UIElement> build){
  int version=(int)frame.Tag+1;frame.Tag=version;double start=frame.Visibility==Visibility.Visible?frame.ActualHeight:0;frame.BeginAnimation(FrameworkElement.HeightProperty,null);
  if(expanded&&frame.Child==null)frame.Child=build();frame.Visibility=Visibility.Visible;
  double target=0;if(expanded){double width=Math.Max(1,((FrameworkElement)frame.Parent).ActualWidth);frame.Child.Measure(new Size(width,double.PositiveInfinity));target=frame.Child.DesiredSize.Height;}
  Action finish=()=>{if((int)frame.Tag!=version)return;frame.BeginAnimation(FrameworkElement.HeightProperty,null);frame.Height=double.NaN;if(!expanded){frame.Visibility=Visibility.Collapsed;frame.Child=null;}};
  if(!MotionAllowed){finish();return;}frame.Height=start;var slide=new DoubleAnimation(start,target,TimeSpan.FromMilliseconds(160)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.HoldEnd};slide.Completed+=delegate{finish();};frame.BeginAnimation(FrameworkElement.HeightProperty,slide);
 }
}
}

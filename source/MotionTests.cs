using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DeskCalendar {
public static partial class SelfTests {
 static void PumpMotion(int milliseconds){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(milliseconds)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();try{Dispatcher.PushFrame(frame);}finally{timer.Stop();}}
 static void MotionChecks(CalendarWindow window){
  Check(MotionPolicy.Enabled(true,false,false)&&!MotionPolicy.Enabled(false,false,false)&&!MotionPolicy.Enabled(true,true,false)&&!MotionPolicy.Enabled(true,false,true),"遵循系统动画、节电与远程桌面降级策略");
  Check(MotionPolicy.Direction("week","quarter")==1&&MotionPolicy.Direction("quarter","month")==-1&&MotionPolicy.Direction("month","month")==0,"视图切换方向遵循周月季顺序");
  Check(MotionPolicy.PageMilliseconds==200&&MotionPolicy.SlideMilliseconds==200,"翻页和周月季切换均使用 200 毫秒过渡");
  Check(MotionPolicy.Ease(0)==0&&MotionPolicy.Ease(1)==1&&MotionPolicy.Ease(.5)>.5,"上拉动效使用减速曲线并有准确起终点");
  var rise=new PopupRise();var placements=new List<System.Drawing.Rectangle>();var target=new System.Drawing.Rectangle(-800,200,760,650);rise.Start(target,20,r=>placements.Add(r));Check(rise.Running&&placements.First().Top==220,"展开从最终位置下方开始上拉");PumpMotion(280);Check(!rise.Running&&placements.Last()==target&&placements.All(p=>p.Left==target.Left&&p.Top>=target.Top&&p.Top<=target.Top+20),"上拉结束精确归位且移除帧回调");
  rise.Start(target,20,r=>placements.Add(r));rise.Stop(true);int count=placements.Count;PumpMotion(40);Check(!rise.Running&&placements.Count==count&&placements.Last()==target,"提前关闭或交互时结束上拉且不残留回调");
  var flags=BindingFlags.Instance|BindingFlags.NonPublic;var content=(ContentControl)typeof(CalendarWindow).GetField("content",flags).GetValue(window);var viewport=(Grid)typeof(CalendarWindow).GetField("calendarViewport",flags).GetValue(window);var start=typeof(CalendarWindow).GetMethod("StartViewSlide",flags);var stop=typeof(CalendarWindow).GetMethod("StopViewSlide",flags);
  var previous=new Grid();start.Invoke(window,new object[]{previous,1,null,0.0,false});Pump(window);Check(viewport.Children.Count==2&&viewport.ClipToBounds&&content.RenderTransform is TransformGroup,"滑动使用受裁剪的内容层，保留外框与毛玻璃");var outgoing=(ContentControl)typeof(CalendarWindow).GetField("outgoingView",flags).GetValue(window);Check(!outgoing.IsHitTestVisible&&ReferenceEquals(outgoing.Content,previous),"旧视图仅参与显示，不响应日程点击");Check(ReferenceEquals(((TransformGroup)content.RenderTransform).Children.Last(),outgoing.RenderTransform)&&content.Opacity==1&&outgoing.Opacity==1&&!content.HasAnimatedProperties&&!outgoing.HasAnimatedProperties,"周月季切换共用单一轨道，不叠加透明度动效");PumpMotion(280);Check(viewport.Children.Count==1&&content.RenderTransform==Transform.Identity&&content.Opacity==1&&!content.HasAnimatedProperties,"滑动结束移除旧视图并释放动画时钟");Check(content.CacheMode==null&&outgoing.CacheMode==null,"动效结束释放两页临时位图缓存");
  start.Invoke(window,new object[]{new Grid(),1,null,0.0,false});start.Invoke(window,new object[]{new Grid(),-1,null,0.0,false});Check(viewport.Children.Count==2,"快速连续切换不会叠加多层旧视图");stop.Invoke(window,null);Check(viewport.Children.Count==1&&content.Opacity==1&&!content.HasAnimatedProperties,"取消滑动恢复完整可交互的新视图");
  var weekly=(UIElement)typeof(CalendarWindow).GetMethod("WeekView",flags).Invoke(window,null);content.Content=weekly;Pump(window);var timeline=(ScrollViewer)typeof(CalendarWindow).GetField("timelineViewer",flags).GetValue(window);timeline.ScrollToVerticalOffset(780);Pump(window);double offset=timeline.VerticalOffset;content.Content=new Grid();start.Invoke(window,new object[]{weekly,1,timeline,offset,false});Pump(window);Check(Math.Abs(timeline.VerticalOffset-offset)<1,"周视图退出动画保留原滚动位置，不跳回九点");stop.Invoke(window,null);
  content.Content=new Grid();Pump(window);double width=content.ActualWidth;start.Invoke(window,new object[]{new Grid(),1,null,0.0,true});Pump(window);outgoing=(ContentControl)typeof(CalendarWindow).GetField("outgoingView",flags).GetValue(window);var track=(TranslateTransform)typeof(CalendarWindow).GetField("incomingShift",flags).GetValue(window);var page=(TransformGroup)content.RenderTransform;
  Check(ReferenceEquals(outgoing.RenderTransform,track)&&ReferenceEquals(page.Children.Last(),track)&&Math.Abs(((TranslateTransform)page.Children.First()).X-width)<1,"翻页两页相邻排列且共用同一动画轨道，右箭头下一页从右侧进入并向左翻");
  PumpMotion(60);double movement=track.X;Check(movement<0&&movement>-width&&content.Opacity==1&&outgoing.Opacity==1&&!content.HasAnimatedProperties&&!outgoing.HasAnimatedProperties,"翻页中途持续显示两页完整不透明内容，不做淡出");
  Check(Math.Abs(movement)>width*.4,"翻页起步立即产生明显位移，不再使用缓慢起步曲线");
  Check(Math.Abs((page.Transform(new Point()).X-outgoing.RenderTransform.Transform(new Point()).X)-width)<.01,"移动中两页间距保持一整页，不出现缝隙或速度不同步");
  var rendered=content.Content;typeof(CalendarWindow).GetMethod("RenderContent",flags,null,Type.EmptyTypes,null).Invoke(window,null);Check(ReferenceEquals(content.Content,rendered)&&(bool)typeof(CalendarWindow).GetField("redrawAfterSlide",flags).GetValue(window),"翻页期间同步刷新延后绘制，避免中途打断");PumpMotion(280);Check(viewport.Children.Count==1&&content.RenderTransform==Transform.Identity&&viewport.Clip==null&&!(bool)typeof(CalendarWindow).GetField("pageSliding",flags).GetValue(window),"整页翻页完成后清理轨道并补绘最新数据");
  start.Invoke(window,new object[]{new Grid(),-1,null,0.0,true});page=(TransformGroup)content.RenderTransform;Check(((TranslateTransform)page.Children.First()).X<0,"上一页采用相反方向整页滑动");stop.Invoke(window,null);
  var month=(UIElement)typeof(CalendarWindow).GetMethod("MonthView",flags).Invoke(window,new object[]{DateTime.Today,false});content.Content=month;Pump(window);var quarter=(UIElement)typeof(CalendarWindow).GetMethod("QuarterView",flags).Invoke(window,null);content.Content=quarter;start.Invoke(window,new object[]{month,1,null,0.0,false});Pump(window);Check(month.IsMeasureValid&&quarter.IsMeasureValid&&((FrameworkElement)quarter).ActualWidth>0,"月到季切换先完成两页实际布局，再执行共享轨道动画");stop.Invoke(window,null);
  typeof(CalendarWindow).GetField("timelineMinute",flags).SetValue(window,540.0);weekly=(UIElement)typeof(CalendarWindow).GetMethod("WeekView",flags).Invoke(window,null);content.Content=weekly;start.Invoke(window,new object[]{quarter,-1,null,0.0,false});Pump(window);timeline=(ScrollViewer)typeof(CalendarWindow).GetField("timelineViewer",flags).GetValue(window);var preferences=(Preferences)typeof(CalendarWindow).GetField("prefs",flags).GetValue(window);Check(Math.Abs(timeline.VerticalOffset-540*preferences.TimelineZoom)<1,"切入周视图前预定位到九点，避免滑动中再次跳转滚动位置");stop.Invoke(window,null);
 }
}
}

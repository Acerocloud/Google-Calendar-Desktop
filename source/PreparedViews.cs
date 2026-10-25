using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DeskCalendar {
public partial class CalendarWindow {
 sealed class PreparedView {public UIElement Root;public Canvas Canvas;public Line Now;public ScrollViewer Timeline;public double Width;public DateTime Week;}
 readonly Dictionary<string,PreparedView> preparedViews=new Dictionary<string,PreparedView>();DispatcherOperation prepareOperation;int prepareGeneration;bool navigationRendering;
 string ViewKey(string view,DateTime date){DateTime first=view=="week"?CalendarMath.Monday(date):view=="quarter"?CalendarMath.Quarter(date):CalendarMath.Month(date);return view+first.ToString("yyyyMMdd")+":"+ActualWidth.ToString(CultureInfo.InvariantCulture)+":"+ActualHeight.ToString(CultureInfo.InvariantCulture)+":"+prefs.TimelineZoom.ToString(CultureInfo.InvariantCulture);}
 void ClearPreparedViews(){prepareGeneration++;if(prepareOperation!=null&&prepareOperation.Status==DispatcherOperationStatus.Pending)prepareOperation.Abort();prepareOperation=null;preparedViews.Clear();}
 bool UsePreparedView(){PreparedView view;string key=ViewKey(prefs.View,anchor);if(!preparedViews.TryGetValue(key,out view))return false;preparedViews.Remove(key);weekCanvas=view.Canvas;nowLine=view.Now;timelineViewer=view.Timeline;weekWidth=view.Width;weekStart=view.Week;content.Content=view.Root;if(view.Now!=null)UpdateNow();return true;}
 void QueuePreparedViews(){
  if(popupRise.Running||!IsVisible||exiting||suspended||sessionLocked||pageSliding||content==null||content.ActualWidth<=0||Battery()||BatterySaving()||!MotionPolicy.Enabled(SystemParameters.ClientAreaAnimation,false,false))return;
  if(prepareOperation!=null&&prepareOperation.Status==DispatcherOperationStatus.Pending)return;
  var targets=new List<Tuple<string,DateTime>>();foreach(string view in new[]{"week","month","quarter"})if(view!=prefs.View)targets.Add(Tuple.Create(view,anchor));targets.Add(Tuple.Create(prefs.View,CalendarMath.Navigation(anchor,prefs.View,1)));targets.Add(Tuple.Create(prefs.View,CalendarMath.Navigation(anchor,prefs.View,-1)));
  var wanted=new HashSet<string>(targets.Select(t=>ViewKey(t.Item1,t.Item2)));foreach(string key in preparedViews.Keys.Where(k=>!wanted.Contains(k)).ToArray())preparedViews.Remove(key);
  var target=targets.FirstOrDefault(t=>!preparedViews.ContainsKey(ViewKey(t.Item1,t.Item2)));if(target==null)return;int generation=prepareGeneration;
  prepareOperation=Dispatcher.BeginInvoke(new Action(()=>{
   prepareOperation=null;if(generation!=prepareGeneration||!IsVisible||pageSliding||exiting||suspended||sessionLocked)return;
   string oldView=prefs.View;DateTime oldAnchor=anchor,oldWeek=weekStart;Canvas oldCanvas=weekCanvas;Line oldNow=nowLine;ScrollViewer oldTimeline=timelineViewer;double oldWidth=weekWidth,oldMinute=timelineMinute;
   try{prefs.View=target.Item1;anchor=target.Item2;timelineMinute=540;weekCanvas=null;nowLine=null;timelineViewer=null;var root=(FrameworkElement)(prefs.View=="week"?WeekView():prefs.View=="quarter"?QuarterView():MonthView(anchor,false));root.Resources=Resources;root.UseLayoutRounding=true;root.SnapsToDevicePixels=true;root.SetValue(TextElement.FontFamilyProperty,FontFamily);root.SetValue(TextElement.FontSizeProperty,FontSize);var size=new Size(content.ActualWidth,content.ActualHeight);root.Measure(size);root.Arrange(new Rect(size));preparedViews[ViewKey(prefs.View,anchor)]=new PreparedView{Root=root,Canvas=weekCanvas,Now=nowLine,Timeline=timelineViewer,Width=weekWidth,Week=weekStart};}
   catch{preparedViews.Clear();return;}
   finally{prefs.View=oldView;anchor=oldAnchor;weekStart=oldWeek;weekCanvas=oldCanvas;nowLine=oldNow;timelineViewer=oldTimeline;weekWidth=oldWidth;timelineMinute=oldMinute;}
   QueuePreparedViews();
  }),DispatcherPriority.ApplicationIdle);
 }
}
}

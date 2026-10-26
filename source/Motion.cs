using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Forms=System.Windows.Forms;

namespace DeskCalendar {
public static class MotionPolicy {
 public const int RiseMilliseconds=170,SlideMilliseconds=200,PageMilliseconds=200;
 public static bool Enabled(bool systemAnimations,bool batterySaver,bool remote){return systemAnimations&&!batterySaver&&!remote;}
 public static double Ease(double progress){double t=Math.Max(0,Math.Min(1,progress));return 1-Math.Pow(1-t,3);}
 public static int Direction(string from,string to){return Math.Sign(Array.IndexOf(new[]{"week","month","quarter","year"},to)-Array.IndexOf(new[]{"week","month","quarter","year"},from));}
}
// The native window moves as a whole, retaining DWM acrylic, corner clipping and shadow.
// The rendering subscription exists only during the short opening motion.
public sealed class PopupRise {
 public int DurationMilliseconds=MotionPolicy.RiseMilliseconds;public int WarmupFrames;
 readonly Stopwatch watch=new Stopwatch();Action<System.Drawing.Rectangle> place;System.Drawing.Rectangle target;int distance,lastOffset;EventHandler frame;
 public event EventHandler Completed;public bool Running {get{return frame!=null;}}
 public void Start(System.Drawing.Rectangle bounds,int pixels,Action<System.Drawing.Rectangle> position){
  Stop(false);target=bounds;distance=pixels;place=position;watch.Reset();lastOffset=pixels;int settling=WarmupFrames;frame=delegate{if(settling>0){settling--;return;}if(!watch.IsRunning)watch.Start();int offset=(int)Math.Round(distance*(1-MotionPolicy.Ease(watch.Elapsed.TotalMilliseconds/DurationMilliseconds)));if(watch.ElapsedMilliseconds>=DurationMilliseconds){Stop(true);return;}if(offset!=lastOffset){lastOffset=offset;var current=target;current.Y+=offset;place(current);}};CompositionTarget.Rendering+=frame;var start=target;start.Y+=distance;place(start);
 }
 public void Stop(bool finish){if(frame==null)return;CompositionTarget.Rendering-=frame;frame=null;watch.Stop();var action=place;place=null;if(finish&&action!=null)action(target);if(finish&&Completed!=null)Completed(this,EventArgs.Empty);}
}
public partial class CalendarWindow {
 readonly PopupRise popupRise=new PopupRise();Grid calendarViewport;ContentControl outgoingView;TranslateTransform incomingShift,outgoingShift;int slideGeneration;bool pageSliding,redrawAfterSlide;
 bool MotionAllowed {get{return !rendering&&!suspended&&!sessionLocked&&MotionPolicy.Enabled(SystemParameters.ClientAreaAnimation,BatterySaving(),Forms.SystemInformation.TerminalServerSession);}}
 void QueuePreferencesSave(){saveTimer.Stop();saveTimer.Start();}
 void SwitchView(string kind){if(prefs.View==kind)return;int direction=MotionPolicy.Direction(prefs.View,kind);prefs.View=kind;QueuePreferencesSave();Navigate(direction);}
 void StartViewSlide(UIElement previous,int direction,ScrollViewer previousTimeline,double previousOffset,bool paging=false){
  if(previous==null||calendarViewport==null||content.Content==null)return;StopViewSlide();if(timelineViewer!=null&&content.IsAncestorOf(timelineViewer))timelineViewer.ScrollToVerticalOffset(timelineMinute*prefs.TimelineZoom);int generation=++slideGeneration;
  double width=Math.Max(1,content.ActualWidth),pageOffset=Math.Sign(direction)*width;pageSliding=true;
  outgoingView=new ContentControl{Content=previous,Margin=content.Margin,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,IsHitTestVisible=false,Focusable=false};
  if(previousTimeline!=null&&previous.IsAncestorOf(previousTimeline))outgoingView.Loaded+=delegate{previousTimeline.ScrollToVerticalOffset(previousOffset);};
  calendarViewport.Children.Insert(0,outgoingView);calendarViewport.UpdateLayout();
  calendarViewport.Clip=new RectangleGeometry(new Rect(content.Margin.Left,content.Margin.Top,width,Math.Max(1,content.ActualHeight)));
  incomingShift=outgoingShift=new TranslateTransform();outgoingView.RenderTransform=incomingShift;var group=new TransformGroup();group.Children.Add(new TranslateTransform(pageOffset,0));group.Children.Add(incomingShift);content.RenderTransform=group;content.Opacity=outgoingView.Opacity=1;
  var push=new DoubleAnimation(0,-pageOffset,TimeSpan.FromMilliseconds(paging?MotionPolicy.PageMilliseconds:MotionPolicy.SlideMilliseconds)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.HoldEnd};push.Completed+=delegate{CompleteSlide(generation);};incomingShift.BeginAnimation(TranslateTransform.XProperty,push);
 }
 void StopViewSlide(){
  slideGeneration++;pageSliding=false;redrawAfterSlide=false;if(calendarViewport!=null)calendarViewport.Clip=null;if(incomingShift!=null)incomingShift.BeginAnimation(TranslateTransform.XProperty,null);if(outgoingShift!=null)outgoingShift.BeginAnimation(TranslateTransform.XProperty,null);if(content!=null){content.CacheMode=null;content.BeginAnimation(UIElement.OpacityProperty,null);content.Opacity=1;content.RenderTransform=Transform.Identity;}if(outgoingView!=null){outgoingView.CacheMode=null;outgoingView.BeginAnimation(UIElement.OpacityProperty,null);outgoingView.Content=null;if(calendarViewport!=null)calendarViewport.Children.Remove(outgoingView);}outgoingView=null;incomingShift=outgoingShift=null;
 }
 void CompleteSlide(int generation){if(generation!=slideGeneration)return;bool redraw=redrawAfterSlide;StopViewSlide();if(redraw)Render();QueuePreparedViews();}
 void StopMotion(){bool redraw=redrawAfterSlide;popupRise.Stop(true);StopViewSlide();if(redraw)Render();QueuePreparedViews();}
}
}

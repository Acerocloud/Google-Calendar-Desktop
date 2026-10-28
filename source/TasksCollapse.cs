using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DeskCalendar {
public partial class CalendarWindow {
 readonly PopupRise tasksSlide=new PopupRise{DurationMilliseconds=200};
 Popup tasksReveal;bool tasksSliding,deferredCollapseTasks,placingCalendar;
 void UpdateTasksReveal(){
  bool visible=prefs.TasksCollapsed&&IsVisible&&!coldPreparing&&!popupRise.Running&&!tasksSliding&&!placingCalendar;
  if(!visible){if(tasksReveal!=null)tasksReveal.IsOpen=false;return;}
  if(tasksReveal==null){
   var arrow=Icon("\uE76B","展开待办",()=>SetTasksCollapsed(false));arrow.Name="ExpandTasks";arrow.Width=26;arrow.Height=36;arrow.Margin=new Thickness(0);arrow.FontSize=12;
   var frame=new Border{Width=28,Height=38,CornerRadius=new CornerRadius(6),Background=MaterialBrush(),Child=arrow};
   tasksReveal=new Popup{PlacementTarget=this,Placement=PlacementMode.Relative,StaysOpen=true,AllowsTransparency=true,PopupAnimation=PopupAnimation.None,Child=frame};
   tasksReveal.Opened+=delegate{var source=PresentationSource.FromVisual(frame) as HwndSource;if(source!=null){source.CompositionTarget.BackgroundColor=Colors.Transparent;Native.Dark(source.Handle,true);Native.StableBlur(source.Handle,GlassEnabled());}if(MotionAllowed){var shift=new TranslateTransform();frame.RenderTransform=shift;shift.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(12,0,TimeSpan.FromMilliseconds(150)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});}else frame.RenderTransform=Transform.Identity;};
  }
  tasksReveal.HorizontalOffset=-24;tasksReveal.VerticalOffset=Math.Max(0,ActualHeight-48);
  var surface=tasksReveal.Child as Border;if(surface!=null){surface.Resources=Resources;surface.Background=MaterialBrush();}
  tasksReveal.IsOpen=true;
 }
 void StopTasksSlide(){if(tasksSliding)tasksSlide.Stop(true);}
 void SetTasksCollapsed(bool collapsed){
  if(tasksSliding||prefs.TasksCollapsed==collapsed||taskEditor!=null)return;
  popupRise.Stop(true);prefs.TasksCollapsed=collapsed;QueuePreferencesSave();
  if(tasksReveal!=null)tasksReveal.IsOpen=false;
  if(tasksWindow==null)PrepareTasksWindow(false);
  foreach(var selector in tasksRoot.Children){var header=selector as StackPanel;if(header!=null)foreach(var child in header.Children){var combo=child as ComboBox;if(combo!=null)combo.IsDropDownOpen=false;}}
  if(!IsVisible||!MotionAllowed){FinishTasksCollapse();return;}
  AlignTasks();var handle=new WindowInteropHelper(tasksWindow).EnsureHandle();var main=new WindowInteropHelper(this).Handle;NativeRect bounds,mainBounds;
  if(!GetWindowRect(handle,out bounds)||!GetWindowRect(main,out mainBounds)){FinishTasksCollapse();return;}
  int width=bounds.Right-bounds.Left,height=bounds.Bottom-bounds.Top,distance=mainBounds.Left-bounds.Left;
  if(distance<=0){FinishTasksCollapse();return;}
  tasksSliding=true;tasksRoot.IsEnabled=false;tasksWindow.ShowActivated=false;
  Action<int> place=offset=>{linkingTasks=true;try{int x=bounds.Left+offset;int visible=Math.Max(0,Math.Min(width,mainBounds.Left-x));var region=CreateRectRgn(0,0,visible,height);if(SetWindowRgn(handle,region,false)==0)DeleteObject(region);Native.Position(handle,new System.Drawing.Rectangle(x,bounds.Top,width,height));}finally{linkingTasks=false;}};
  if(!collapsed){place(distance);linkingTasks=true;try{tasksWindow.Show();}finally{linkingTasks=false;}}
  EventHandler finished=null;finished=delegate{tasksSlide.Completed-=finished;FinishTasksCollapse();};tasksSlide.Completed+=finished;
  tasksSlide.Start(new System.Drawing.Rectangle(0,0,1,1),distance,r=>place(collapsed?distance-r.Y:r.Y));
 }
 void FinishTasksCollapse(){
  if(tasksWindow==null)return;
  if(prefs.TasksCollapsed){if(tasksWindow.IsActive&&IsVisible)Activate();tasksWindow.Hide();}
  tasksSliding=false;if(deferredCollapseTasks){deferredCollapseTasks=false;RenderTasks();}SetWindowRgn(new WindowInteropHelper(tasksWindow).EnsureHandle(),IntPtr.Zero,false);tasksRoot.IsEnabled=true;AlignTasks();
  if(!prefs.TasksCollapsed&&IsVisible)PrepareTasksWindow(true);
  UpdateTasksReveal();
 }
}
}

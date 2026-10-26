using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
namespace DeskCalendar {
public partial class CalendarWindow {
 void AttachTaskMenuDismiss(ComboBox selector){
  Window companion=null;bool attached=false;Action detach=null;
  EventHandler deactivate=delegate{selector.IsDropDownOpen=false;};
  PreProcessInputEventHandler outside=delegate(object sender,PreProcessInputEventArgs args){
   var mouse=args.StagingItem.Input as MouseButtonEventArgs;if(mouse==null||mouse.ButtonState!=MouseButtonState.Pressed||mouse.RoutedEvent!=Mouse.PreviewMouseDownEvent||!selector.IsDropDownOpen)return;
   var popup=(Popup)selector.Template.FindName("PART_Popup",selector);var panel=popup.Child as FrameworkElement;var position=System.Windows.Forms.Cursor.Position;var point=new Point(position.X,position.Y);
   var header=selector.Template.FindName("TaskListRaisedSection",selector) as FrameworkElement;
   if(!InsideTaskMenu(panel,point)&&!InsideTaskMenu(header,point))selector.IsDropDownOpen=false;
  };
  detach=()=>{if(!attached)return;attached=false;InputManager.Current.PreProcessInput-=outside;Deactivated-=deactivate;if(companion!=null)companion.Deactivated-=deactivate;companion=null;};
  selector.DropDownOpened+=delegate{detach();attached=true;companion=Window.GetWindow(selector);InputManager.Current.PreProcessInput+=outside;Deactivated+=deactivate;if(companion!=null&&companion!=this)companion.Deactivated+=deactivate;};
  selector.DropDownClosed+=delegate{detach();};selector.Unloaded+=delegate{selector.IsDropDownOpen=false;detach();};
 }
 static bool InsideTaskMenu(FrameworkElement element,Point point){if(element==null||!element.IsVisible||PresentationSource.FromVisual(element)==null)return false;var start=element.PointToScreen(new Point(0,0));var end=element.PointToScreen(new Point(element.ActualWidth,element.ActualHeight));return new Rect(start,end).Contains(point);}
}
}

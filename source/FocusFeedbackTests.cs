using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DeskCalendar {
public static partial class SelfTests {
 static void FocusFeedbackChecks(CalendarWindow window){
  var flags=BindingFlags.Instance|BindingFlags.NonPublic;var dialog=(Window)typeof(CalendarWindow).GetMethod("Dialog",flags).Invoke(window,new object[]{"焦点验证",380.0,280.0});var body=new StackPanel();var button=new Button{Content="按钮",BorderThickness=new Thickness(0)};var check=new CheckBox{Content="选项"};var input=new TextBox{Text="editable text"};body.Children.Add(button);body.Children.Add(check);body.Children.Add(input);typeof(CalendarWindow).GetMethod("SetDialogContent",flags).Invoke(window,new object[]{dialog,body});Exception failure=null;
  window.Dispatcher.BeginInvoke(new Action(()=>{try{
   InputFeedback.PointerMode();button.Focus();Pump(dialog);var border=(Border)button.Template.FindName("bg",button);Check(button.IsKeyboardFocused&&border.BorderThickness==new Thickness(0)&&button.FocusVisualStyle==null,"鼠标模式焦点仍在按钮时也不残留描边或虚线框 focused="+button.IsKeyboardFocused);
   dialog.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(dialog),Environment.TickCount,Key.Tab){RoutedEvent=UIElement.PreviewKeyDownEvent});button.Focus();Pump(dialog);Check(!InputFeedback.GetKeyboardMode(button)&&button.FocusVisualStyle==null&&border.BorderThickness==new Thickness(0),"键盘 Tab 不显示额外焦点白框");InputFeedback.PointerMode();Pump(dialog);Check(button.IsKeyboardFocused&&button.FocusVisualStyle==null&&border.BorderThickness==new Thickness(0),"返回鼠标模式立即撤销焦点框，不破坏键盘焦点");
   check.Focus();Pump(dialog);var box=(Border)check.Template.FindName("box",check);Check(check.IsKeyboardFocused&&box.BorderThickness==new Thickness(1)&&check.FocusVisualStyle==null,"小窗勾选框鼠标操作后只保留设计边框");InputFeedback.SetKeyboardMode(dialog,true);Pump(dialog);Check(box.BorderThickness==new Thickness(1)&&check.FocusVisualStyle==null,"小窗键盘操作不叠加焦点框");InputFeedback.PointerMode();input.Focus();input.Select(1,3);Pump(dialog);InputFeedback.PointerMode();Check(input.IsKeyboardFocused&&input.SelectionStart==1&&input.SelectionLength==3,"移除焦点框不清除输入框焦点、光标或选区");
  }catch(Exception ex){failure=ex;}finally{dialog.Close();}}),DispatcherPriority.ApplicationIdle);
  dialog.ShowDialog();if(failure!=null)throw failure;InputFeedback.PointerMode();var main=Descendants(window).OfType<Button>().Single(b=>b.Name=="LockShortcut");Check(main.FocusVisualStyle==null&&((Border)main.Template.FindName("bg",main)).BorderThickness==new Thickness(0),"小窗返回主页后不重新留下按钮焦点框");
 }
}
}

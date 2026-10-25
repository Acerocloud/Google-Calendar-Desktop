using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Data;

namespace DeskCalendar {
public static class InputFeedback {
 public static readonly DependencyProperty KeyboardModeProperty=DependencyProperty.RegisterAttached("KeyboardMode",typeof(bool),typeof(InputFeedback),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.Inherits));
 public static bool GetKeyboardMode(DependencyObject value){return (bool)value.GetValue(KeyboardModeProperty);}
 public static void SetKeyboardMode(DependencyObject value,bool mode){value.SetValue(KeyboardModeProperty,mode);}
 public static void Attach(Window window){
  window.AddHandler(UIElement.PreviewMouseDownEvent,new MouseButtonEventHandler((s,e)=>PointerMode()),true);
  window.AddHandler(UIElement.PreviewKeyDownEvent,new KeyEventHandler((s,e)=>{for(Window current=window;current!=null;current=current.Owner)SetKeyboardMode(current,true);}),true);
 }
 public static void PointerMode(){if(Application.Current!=null)foreach(Window window in Application.Current.Windows)SetKeyboardMode(window,false);}
 public static void AttachPopup(Popup popup,FrameworkElement target){
  var root=popup.Child as FrameworkElement;if(root==null)return;root.SetBinding(KeyboardModeProperty,new Binding{Source=target,Path=new PropertyPath(KeyboardModeProperty)});root.AddHandler(UIElement.PreviewMouseDownEvent,new MouseButtonEventHandler((s,e)=>PointerMode()),true);root.AddHandler(UIElement.PreviewKeyDownEvent,new KeyEventHandler((s,e)=>{var window=Window.GetWindow(target);if(window!=null)SetKeyboardMode(window,true);}),true);
 }
 public static ControlTemplate ConfigureTemplate(ControlTemplate template){
  if(template==null)return null;if(template.IsSealed)template=(ControlTemplate)XamlReader.Parse(XamlWriter.Save(template));
  foreach(var focus in template.Triggers.OfType<Trigger>().Where(t=>t.Property==UIElement.IsKeyboardFocusedProperty).ToArray()){
   var conditional=new MultiTrigger();conditional.Conditions.Add(new Condition(UIElement.IsKeyboardFocusedProperty,true));conditional.Conditions.Add(new Condition(KeyboardModeProperty,true));foreach(Setter setter in focus.Setters)conditional.Setters.Add(new Setter(setter.Property,setter.Value,setter.TargetName));template.Triggers.Remove(focus);template.Triggers.Add(conditional);
  }return template;
 }
 public static void ConfigureResources(ResourceDictionary resources){
  var keyboardRing=(Style)XamlReader.Parse("<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Control'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Control'><Border CornerRadius='4' BorderThickness='1' BorderBrush='{DynamicResource ControlText}' Margin='1' SnapsToDevicePixels='True'/></ControlTemplate></Setter.Value></Setter></Style>");
  foreach(var type in new[]{typeof(Button),typeof(CheckBox),typeof(ComboBox),typeof(ComboBoxItem),typeof(Slider),typeof(TextBox),typeof(ToggleButton)}){
   var existing=resources[type] as Style;var style=existing==null?new Style(type):new Style(type,existing);style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty,null));var keyboard=new Trigger{Property=KeyboardModeProperty,Value=true};keyboard.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty,keyboardRing));style.Triggers.Add(keyboard);
   var setter=existing==null?null:existing.Setters.OfType<Setter>().FirstOrDefault(x=>x.Property==Control.TemplateProperty);if(setter!=null)style.Setters.Add(new Setter(Control.TemplateProperty,ConfigureTemplate(setter.Value as ControlTemplate)));resources[type]=style;
  }
 }
}
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace DeskCalendar {
public partial class CalendarWindow {
 void ConfigureToolTips(){
  var style=new Style(typeof(ToolTip));
  style.Setters.Add(new Setter(Control.BackgroundProperty,MaterialBrush()));
  style.Setters.Add(new Setter(Control.ForegroundProperty,foreground));
  style.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));
  style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(12,9,12,9)));
  style.Setters.Add(new Setter(Control.FontSizeProperty,12.0));
  style.Setters.Add(new Setter(Control.FontFamilyProperty,FontFamily));
  style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty,360.0));
  style.Setters.Add(new Setter(Control.TemplateProperty,(ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ToolTip'><Border CornerRadius='8' BorderThickness='0' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}' SnapsToDevicePixels='True'><ContentPresenter Content='{TemplateBinding Content}'><ContentPresenter.ContentTemplate><DataTemplate><TextBlock Text='{Binding}' TextWrapping='Wrap'/></DataTemplate></ContentPresenter.ContentTemplate></ContentPresenter></Border></ControlTemplate>")));
  style.Setters.Add(new EventSetter(System.Windows.Controls.ToolTip.OpenedEvent,new RoutedEventHandler(PrepareToolTipMaterial)));
  Resources[typeof(ToolTip)]=style;
 }
 void PrepareToolTipMaterial(object sender,RoutedEventArgs args){
  var tip=sender as ToolTip;if(tip==null)return;
  tip.Background=MaterialBrush();tip.Foreground=foreground;
  var source=PresentationSource.FromVisual(tip) as HwndSource;if(source==null)return;
  if(source.CompositionTarget!=null)source.CompositionTarget.BackgroundColor=Colors.Transparent;
  Native.Dark(source.Handle,true);
  if(!Native.StableBlur(source.Handle,GlassEnabled()))Native.Backdrop(source.Handle,GlassEnabled());
 }
}
}

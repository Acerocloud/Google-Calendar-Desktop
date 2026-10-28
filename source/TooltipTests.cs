using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace DeskCalendar {
public static partial class SelfTests {
 static void TooltipChecks(CalendarWindow window){
  var target=Descendants(window).OfType<Button>().First(b=>b.ToolTip is string);
  var tip=new ToolTip{Resources=window.Resources,Content=target.ToolTip,PlacementTarget=target};
  try{
   tip.IsOpen=true;Pump(window);tip.ApplyTemplate();tip.UpdateLayout();
   Check(tip.IsOpen&&PresentationSource.FromVisual(tip) is HwndSource,"真实提示弹层成功创建 HWND 并打开，不仅解析未展开的模板");
   var border=VisualNodes(tip).OfType<Border>().First();
   Check(border.CornerRadius.TopLeft==8&&border.BorderThickness==new Thickness(0),"实际展开的提示使用八像素圆角且无描边");
   tip.IsOpen=false;Pump(window);tip.Content=string.Concat(Enumerable.Repeat("这是较长的日程备注与地点说明。",15));tip.IsOpen=true;Pump(window);tip.UpdateLayout();
   Check(tip.ActualWidth<=361&&tip.ActualHeight>40&&VisualNodes(tip).OfType<TextBlock>().Any(t=>t.TextWrapping==TextWrapping.Wrap),"同一提示反复关闭打开，长文字在限定宽度内换行，不再触发模板或空引用错误");
  }finally{tip.IsOpen=false;Pump(window);}
 }
}
}

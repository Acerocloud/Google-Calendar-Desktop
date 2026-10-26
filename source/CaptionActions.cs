using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
namespace DeskCalendar {
public partial class CalendarWindow {
 void AddCaptionBrowser(Window window,string url,string label){
  var caption=(Grid)((DockPanel)((Border)window.Tag).Child).Children[0];var title=caption.Children.OfType<TextBlock>().First();caption.Children.Remove(title);var group=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};group.Children.Add(title);var browser=Icon("\uE774",label,()=>OpenUrl(url));browser.Name="CaptionBrowser";browser.IsEnabled=!demo&&!string.IsNullOrEmpty(url);if(string.IsNullOrEmpty(url))browser.ToolTip="请刷新以获取原始链接";browser.Margin=new Thickness(6,0,0,0);group.Children.Add(browser);caption.Children.Add(group);
 }
 void AddCaptionRight(Window window,Button action,string name){var caption=(Grid)((DockPanel)((Border)window.Tag).Child).Children[0];int last=caption.ColumnDefinitions.Count-1;caption.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});foreach(UIElement child in caption.Children)if(Grid.GetColumn(child)>=last)Grid.SetColumn(child,Grid.GetColumn(child)+1);Grid.SetColumn(action,last);action.Name=name;caption.Children.Add(action);}
}
}

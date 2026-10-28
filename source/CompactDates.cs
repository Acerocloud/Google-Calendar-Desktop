using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace DeskCalendar {
public partial class CalendarWindow {
 void AddCalendarDivider(Grid panel,int row,string name){var divider=new Border{Name=name,BorderBrush=line,BorderThickness=new Thickness(0,0,0,1),IsHitTestVisible=false,UseLayoutRounding=true,SnapsToDevicePixels=true};Grid.SetRow(divider,row);panel.Children.Add(divider);}
 UIElement CompactDateCell(Button date,bool hasEvents,double height=double.NaN,double markerGap=0){
  // Always reserve an identical marker row, so an event never moves the date label.
  // Center one shared, fixed-width column instead of rounding the constrained
  // date button and the marker independently inside a fractional-width day cell.
  var cell=new Grid{Height=height,HorizontalAlignment=HorizontalAlignment.Center};
  if(!double.IsInfinity(date.MaxWidth))cell.Width=date.MaxWidth;
  date.HorizontalAlignment=HorizontalAlignment.Stretch;
  cell.RowDefinitions.Add(new RowDefinition());cell.RowDefinitions.Add(new RowDefinition{Height=new GridLength(5+markerGap)});cell.Children.Add(date);
  if(hasEvents){var dot=new Ellipse{Name="CompactEventDot",Margin=new Thickness(0,markerGap,0,0),Width=3.5,Height=3.5,Fill=SystemParameters.HighContrast?SystemColors.HighlightBrush:BrushColor("#82A8CD"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};Grid.SetRow(dot,1);cell.Children.Add(dot);}return cell;
 }
}
}

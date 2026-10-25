using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace DeskCalendar {
public partial class CalendarWindow {
 UIElement CompactDateCell(Button date,bool hasEvents,double height=double.NaN){
  // Always reserve an identical marker row, so an event never moves the date label.
  var cell=new Grid{Height=height};cell.RowDefinitions.Add(new RowDefinition());cell.RowDefinitions.Add(new RowDefinition{Height=new GridLength(5)});cell.Children.Add(date);
  if(hasEvents){var dot=new Ellipse{Name="CompactEventDot",Width=3.5,Height=3.5,Fill=SystemParameters.HighContrast?SystemColors.HighlightBrush:BrushColor("#82A8CD"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};Grid.SetRow(dot,1);cell.Children.Add(dot);}return cell;
 }
}
}

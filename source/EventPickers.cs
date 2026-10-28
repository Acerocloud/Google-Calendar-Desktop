using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace DeskCalendar {
// Uses WPF controls and the existing glass palette; no web runtime or background timer.
public sealed class EventDatePicker:Button {
 public DateTime SelectedDate {get;private set;} public event EventHandler SelectedDateChanged;
 public void SetDate(DateTime date){if(SelectedDate==date.Date)return;SelectedDate=date.Date;UpdateLabel();if(SelectedDateChanged!=null)SelectedDateChanged(this,EventArgs.Empty);}
 public Popup CalendarPopup {get;private set;}
 bool suppressReopen;int calendarLevel;
 DateTime month;readonly Grid calendar=new Grid();readonly Brush text,muted,line,hover;readonly ResourceDictionary palette;
 public EventDatePicker(DateTime value,ResourceDictionary resources,Brush foreground,Brush secondary,Brush border,Brush fill){
  Style=(Style)resources[typeof(Button)];palette=resources;text=foreground;muted=secondary;line=border;hover=fill;SelectedDate=value.Date;month=new DateTime(value.Year,value.Month,1);Resources=resources;Foreground=text;BorderBrush=line;BorderThickness=new Thickness(1);Background=hover;MinHeight=36;Padding=new Thickness(10,6,10,6);HorizontalContentAlignment=HorizontalAlignment.Stretch;
  calendar.Resources=palette;CalendarPopup=new Popup{PlacementTarget=this,Placement=PlacementMode.Bottom,StaysOpen=false,AllowsTransparency=true,PopupAnimation=PopupAnimation.None,Child=new Border{Width=292,Margin=new Thickness(0,4,0,4),Padding=new Thickness(10),CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),BorderBrush=line,Background=(Brush)resources["ControlPopup"],Child=calendar}};
  // An outside press closes a captured Popup before the owner's Click fires.
  // Remember that close so the same press cannot immediately reopen it.
  CalendarPopup.Closed+=delegate{if(IsMouseOver&&System.Windows.Input.Mouse.LeftButton==System.Windows.Input.MouseButtonState.Pressed)suppressReopen=true;};
  MouseLeave+=delegate{suppressReopen=false;};
  InputFeedback.AttachPopup(CalendarPopup,this);Click+=delegate{if(suppressReopen){suppressReopen=false;return;}if(CalendarPopup.IsOpen){CalendarPopup.IsOpen=false;return;}month=new DateTime(SelectedDate.Year,SelectedDate.Month,1);calendarLevel=0;RenderCalendar();CalendarPopup.IsOpen=true;};Unloaded+=delegate{CalendarPopup.IsOpen=false;suppressReopen=false;};UpdateLabel();
 }
 void UpdateLabel(){var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.Children.Add(new TextBlock{Text=SelectedDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),VerticalAlignment=VerticalAlignment.Center});var icon=new TextBlock{Text="\uE787",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=14,Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(icon,1);row.Children.Add(icon);Content=row;System.Windows.Automation.AutomationProperties.SetName(this,"选择日期 "+SelectedDate.ToString("yyyy-MM-dd"));}
 Button DayButton(string label,Action action){var b=new Button{Content=label,Foreground=text,Background=Brushes.Transparent,BorderThickness=new Thickness(0),MinHeight=32,Padding=new Thickness(0)};b.Click+=delegate{action();};return b;}
 void MoveCalendar(int direction){
  if(calendarLevel==0){if(direction<0&&(month.Year>1||month.Month>1)||direction>0&&(month.Year<9999||month.Month<12))month=month.AddMonths(direction);}
  else month=new DateTime(Math.Max(1,Math.Min(9999,month.Year+direction*(calendarLevel==1?1:10))),month.Month,1);
  RenderCalendar();
 }
 void RenderCalendar(){
  calendar.Children.Clear();calendar.RowDefinitions.Clear();calendar.RowDefinitions.Add(new RowDefinition{Height=new GridLength(36)});calendar.RowDefinitions.Add(new RowDefinition{Height=new GridLength(28)});calendar.RowDefinitions.Add(new RowDefinition{Height=new GridLength(204)});calendar.RowDefinitions.Add(new RowDefinition{Height=new GridLength(36)});
  int decade=Math.Max(1,month.Year/10*10);
  var header=new Grid();header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(32)});header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(32)});
  var previous=DayButton("‹",()=>MoveCalendar(-1));previous.Name="CalendarPrevious";previous.ToolTip=calendarLevel==0?"上个月":calendarLevel==1?"上一年":"上十年";header.Children.Add(previous);
  var caption=DayButton(calendarLevel==0?month.ToString("yyyy 年 M 月"):calendarLevel==1?month.ToString("yyyy 年"):decade+" - "+Math.Min(9999,decade+9),()=>{if(calendarLevel<2){calendarLevel++;RenderCalendar();}});caption.Name="CalendarPeriod";caption.ToolTip=calendarLevel==0?"选择月份":calendarLevel==1?"选择年份":"年份范围";Grid.SetColumn(caption,1);header.Children.Add(caption);
  var next=DayButton("›",()=>MoveCalendar(1));next.Name="CalendarNext";next.ToolTip=calendarLevel==0?"下个月":calendarLevel==1?"下一年":"下十年";Grid.SetColumn(next,2);header.Children.Add(next);calendar.Children.Add(header);
  var today=DayButton("今天",()=>{SetDate(DateTime.Today);CalendarPopup.IsOpen=false;});Grid.SetRow(today,3);calendar.Children.Add(today);
  if(calendarLevel>0){
   var choices=new UniformGrid{Columns=4,Rows=3,Margin=new Thickness(0,8,0,8)};
   for(int i=0;i<12;i++){
    int value=calendarLevel==1?i+1:decade-1+i;
    if(value<1||value>9999){choices.Children.Add(new Border());continue;}
    bool choosingMonth=calendarLevel==1;var choice=DayButton(choosingMonth?value+" 月":value.ToString(),()=>{month=new DateTime(choosingMonth?month.Year:value,choosingMonth?value:month.Month,1);calendarLevel=choosingMonth?0:1;RenderCalendar();});
    choice.Name=choosingMonth?"CalendarChooseMonth":"CalendarChooseYear";choice.Tag=value;choice.Margin=new Thickness(3);
    if(value==(choosingMonth?month.Month:month.Year)){choice.Background=text;choice.Foreground=(Brush)palette["ControlInverse"];}
    choices.Children.Add(choice);
   }
   Grid.SetRow(choices,1);Grid.SetRowSpan(choices,2);calendar.Children.Add(choices);return;
  }
  var weekdays=new UniformGrid{Columns=7};foreach(string day in new[]{"一","二","三","四","五","六","日"})weekdays.Children.Add(new TextBlock{Text=day,Foreground=muted,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});Grid.SetRow(weekdays,1);calendar.Children.Add(weekdays);
  var days=new UniformGrid{Columns=7,Rows=6};int offset=((int)month.DayOfWeek+6)%7;int total=DateTime.DaysInMonth(month.Year,month.Month);for(int i=0;i<42;i++){int day=i-offset+1;if(day<1||day>total){days.Children.Add(new Border());continue;}DateTime selected=month.AddDays(day-1);var b=DayButton(day.ToString(),()=>{SetDate(selected);CalendarPopup.IsOpen=false;});b.Tag=selected;b.Margin=new Thickness(2);if(selected==SelectedDate){b.Background=text;b.Foreground=(Brush)palette["ControlInverse"];}if(selected==DateTime.Today){b.BorderThickness=new Thickness(1);b.BorderBrush=line;}days.Children.Add(b);}Grid.SetRow(days,2);calendar.Children.Add(days);
 }
}
public sealed class EventTimePicker:Grid {
 public event EventHandler SelectedTimeChanged;
 public void SetTime(DateTime value){Hours.SelectedIndex=value.Hour;Minutes.SelectedIndex=value.Minute;}
 public ComboBox Hours {get;private set;}public ComboBox Minutes {get;private set;}
 public TimeSpan SelectedTime {get{return new TimeSpan(Math.Max(0,Hours.SelectedIndex),Math.Max(0,Minutes.SelectedIndex),0);}}
 public EventTimePicker(DateTime value,ResourceDictionary resources,Brush foreground){
  Resources=resources;ColumnDefinitions.Add(new ColumnDefinition());ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(10)});ColumnDefinitions.Add(new ColumnDefinition());MinHeight=36;
  Hours=new ComboBox{MinHeight=36,ToolTip="小时 · 24 小时制"};Minutes=new ComboBox{MinHeight=36,ToolTip="分钟"};for(int i=0;i<24;i++)Hours.Items.Add(i.ToString("00"));for(int i=0;i<60;i++)Minutes.Items.Add(i.ToString("00"));Hours.SelectedIndex=value.Hour;Minutes.SelectedIndex=value.Minute;Hours.SelectionChanged+=delegate{if(SelectedTimeChanged!=null)SelectedTimeChanged(this,EventArgs.Empty);};Minutes.SelectionChanged+=delegate{if(SelectedTimeChanged!=null)SelectedTimeChanged(this,EventArgs.Empty);};Children.Add(Hours);var colon=new TextBlock{Text=":",Foreground=foreground,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(colon,1);Children.Add(colon);Grid.SetColumn(Minutes,2);Children.Add(Minutes);System.Windows.Automation.AutomationProperties.SetName(Hours,"选择小时");System.Windows.Automation.AutomationProperties.SetName(Minutes,"选择分钟");
 }
}
}

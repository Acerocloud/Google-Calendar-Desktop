using System;
using System.Linq;
using System.Windows.Controls;

namespace DeskCalendar {
public partial class CalendarWindow {
 void BindEventDuration(StackPanel form,CheckBox allDay,EventDatePicker startDate,EventDatePicker endDate,EventTimePicker startTime,EventTimePicker endTime,double initialMinutes=30){
  var duration=new ComboBox{Name="EventDuration",MinHeight=36};foreach(int minutes in new[]{15,30,45,60,90,120,180,240,480,1440})duration.Items.Add(new ComboBoxItem{Content=DurationLabel(minutes),Tag=(double)minutes});var initial=duration.Items.OfType<ComboBoxItem>().FirstOrDefault(i=>(double)i.Tag==initialMinutes);if(initial==null){initial=new ComboBoxItem{Content=DurationLabel(initialMinutes),Tag=initialMinutes};duration.Items.Add(initial);}duration.SelectedItem=initial;duration.IsEnabled=allDay.IsChecked!=true;EventField(form,"持续时间",duration);
  double currentMinutes=Math.Max(0,initialMinutes);bool updating=false;
  Action updateDuration=()=>{
   if(updating||allDay.IsChecked==true)return;updating=true;
   try{double minutes=((endDate.SelectedDate+endTime.SelectedTime)-(startDate.SelectedDate+startTime.SelectedTime)).TotalMinutes;
    var item=duration.Items.OfType<ComboBoxItem>().FirstOrDefault(i=>i.Tag is double&&(double)i.Tag==minutes);if(item==null){item=new ComboBoxItem{Content=minutes>0?DurationLabel(minutes):"结束须晚于开始",Tag=minutes};duration.Items.Add(item);}duration.SelectedItem=item;if(minutes>0)currentMinutes=minutes;
   }finally{updating=false;}
  };
  Action updateEnd=()=>{
   if(updating||allDay.IsChecked==true)return;updating=true;
   try{DateTime end=(startDate.SelectedDate+startTime.SelectedTime).AddMinutes(currentMinutes);endDate.SetDate(end);endTime.SetTime(end);}catch(ArgumentOutOfRangeException){}finally{updating=false;}updateDuration();
  };
  duration.SelectionChanged+=delegate{if(updating||allDay.IsChecked==true)return;var item=duration.SelectedItem as ComboBoxItem;if(item!=null&&item.Tag is double&&(double)item.Tag>0){currentMinutes=(double)item.Tag;updateEnd();}};
  startDate.SelectedDateChanged+=delegate{updateEnd();};startTime.SelectedTimeChanged+=delegate{updateEnd();};endDate.SelectedDateChanged+=delegate{updateDuration();};endTime.SelectedTimeChanged+=delegate{updateDuration();};
  allDay.Checked+=delegate{duration.IsEnabled=false;};allDay.Unchecked+=delegate{duration.IsEnabled=true;updateDuration();};
 }
 static string DurationLabel(double minutes){if(minutes<60)return minutes.ToString("0")+" 分钟";int hours=(int)(minutes/60);int rest=(int)(minutes%60);return hours+" 小时"+(rest==0?"":" "+rest+" 分钟");}
}
}

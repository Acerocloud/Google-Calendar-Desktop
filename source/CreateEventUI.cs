using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DeskCalendar {
public partial class CalendarWindow {
 TextBox EventInput(string name,string value,bool multiline=false){
  var box=new TextBox{Name=name,Text=value,Foreground=foreground,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(10,8,10,8),MinHeight=36,VerticalContentAlignment=VerticalAlignment.Center,AcceptsReturn=multiline,TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap};
  box.CaretBrush=foreground;box.SelectionBrush=hover;if(multiline){box.Height=76;box.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;}
  var template=new ControlTemplate(typeof(TextBox));var host=new FrameworkElementFactory(typeof(ScrollViewer));host.Name="PART_ContentHost";template.VisualTree=host;box.Template=template;return box;
 }
 void EventField(StackPanel panel,string label,Control control){var text=Text(label,12,muted);text.Margin=new Thickness(0,12,0,6);panel.Children.Add(text);if(control is TextBox)panel.Children.Add(new Border{BorderBrush=line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(5),Background=hover,Child=control});else panel.Children.Add(control);}
 void EventTimeRow(StackPanel panel,string label,FrameworkElement date,FrameworkElement time){
  var caption=Text(label,12,muted);caption.Margin=new Thickness(0,12,0,6);panel.Children.Add(caption);var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(166)});date.Margin=new Thickness(0,0,10,0);Grid.SetColumn(time,1);row.Children.Add(date);row.Children.Add(time);panel.Children.Add(row);
 }
 void OpenCreateEvent(DateTime day){OpenEventEditor(day,null);}
 void OpenEditEvent(CalendarEvent original){OpenEventEditor(original.StartLocal.Date,original);}
 void OpenEventEditor(DateTime day,CalendarEvent original){
  bool editing=original!=null;Window detailOwner=editing?eventDetail:null;CalendarEvent savedEvent=null;
  if(!editing){if(eventDetail!=null)eventDetail.Close();if(dayDetail!=null)dayDetail.Close();}
  var w=Dialog(editing?"编辑日程":"新增日程",440,720);if(detailOwner!=null)w.Owner=detailOwner;var panel=new StackPanel{Margin=new Thickness(22,0,22,18)};var form=new StackPanel();
  var message=Wrap("",12,muted);message.Margin=new Thickness(0,10,0,6);panel.Children.Add(message);
  var calendar=new ComboBox{Name="EventCalendar",MinHeight=36};
  Action<List<CalendarInfo>> fill=items=>{calendar.Items.Clear();foreach(var cal in items.Where(c=>c.Writable&&(!editing||c.Id==original.CalendarId)).OrderByDescending(c=>c.Primary)){var item=new ComboBoxItem{Content=cal.Name,Tag=cal};calendar.Items.Add(item);}calendar.SelectedIndex=calendar.Items.Count>0?0:-1;};fill(cache.Calendars);calendar.IsEnabled=!editing;
  var auth=SettingsButton("Google 登录","在浏览器中登录并授权日程读写",()=>{});auth.Name="AuthorizeCreate";panel.Children.Add(auth);panel.Children.Add(form);
  bool ready=demo||(google!=null&&google.Connected&&google.CanWrite&&!authExpired);bool working=false,confirmed=false;if(ready&&calendar.Items.Count==0&&!demo)ready=false;var draft=new EventDraft();
  var title=EventInput("EventTitle",editing?original.Title:"");EventField(form,"标题",title);EventField(form,"日历",calendar);
  DateTime start=day.Date.AddHours(9);if(day.Date==DateTime.Today){start=DateTime.Now.AddMinutes(30);start=new DateTime(start.Year,start.Month,start.Day,start.Hour,start.Minute/30*30,0);}DateTime end=start.AddMinutes(30);if(editing){start=original.StartLocal;end=original.AllDay?original.EndLocal.AddDays(-1):original.EndLocal;}
  var allDay=Check("全天",editing&&original.AllDay,v=>{});allDay.Name="EventAllDay";form.Children.Add(allDay);
  var startDate=new EventDatePicker(start,Resources,foreground,muted,line,hover){Name="EventStartDate"};var endDate=new EventDatePicker(end,Resources,foreground,muted,line,hover){Name="EventEndDate"};var startTime=new EventTimePicker(editing&&original.AllDay?start.Date.AddHours(9):start,Resources,foreground){Name="EventStartTime"};var endTime=new EventTimePicker(editing&&original.AllDay?end.Date.AddHours(9).AddMinutes(30):end,Resources,foreground){Name="EventEndTime"};
  BindEventDuration(form,allDay,startDate,endDate,startTime,endTime,editing&&!original.AllDay?((end.Date+endTime.SelectedTime)-(start.Date+startTime.SelectedTime)).TotalMinutes:30);
  EventTimeRow(form,"开始",startDate,startTime);EventTimeRow(form,"结束（全天时包含当天）",endDate,endTime);
  allDay.Checked+=delegate{startTime.IsEnabled=endTime.IsEnabled=false;};allDay.Unchecked+=delegate{startTime.IsEnabled=endTime.IsEnabled=true;};
  startTime.IsEnabled=endTime.IsEnabled=allDay.IsChecked!=true;
  var location=EventInput("EventLocation",editing?original.Location:"");var description=EventInput("EventDescription",editing?original.Description:"",true);EventField(form,"地点（可选）",location);EventField(form,"备注（可选）",description);
  var reminder=new ComboBox{Name="EventReminder",MinHeight=36};if(editing)reminder.Items.Add("保持原提醒");foreach(string s in new[]{"使用日历默认提醒","不提醒","提前 10 分钟","提前 30 分钟","提前 1 小时"})reminder.Items.Add(s);reminder.SelectedIndex=0;EventField(form,"提醒",reminder);form.Children.Add(Wrap("按 Windows 本地时区保存 · "+TimeZoneInfo.Local.DisplayName,11,muted));
  var save=SettingsButton("保存","保存到 Google 日历",()=>{});save.Name="SaveEvent";var cancel=SettingsButton("取消","放弃修改",()=>w.Close());var buttons=new Grid{Margin=new Thickness(22,8,22,14)};buttons.ColumnDefinitions.Add(new ColumnDefinition());buttons.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(10)});buttons.ColumnDefinitions.Add(new ColumnDefinition());Grid.SetColumn(cancel,2);buttons.Children.Add(save);buttons.Children.Add(cancel);
  Action state=()=>{auth.Visibility=ready?Visibility.Collapsed:Visibility.Visible;auth.Content=google!=null&&google.CanWrite&&!authExpired?"重新读取日历":"Google 登录";auth.IsEnabled=!working&&!demo&&google!=null&&!busy;form.IsEnabled=ready&&!working&&!confirmed;save.IsEnabled=ready&&!working&&!demo&&calendar.Items.Count>0;cancel.IsEnabled=!working;};
  message.Text=demo?"示例模式，不会保存到 Google。":editing?(!string.IsNullOrEmpty(original.RecurringId)?"重复日程 · 仅修改本次":""):ready?(calendar.Items.Count==0?"没有可写入的日历，请刷新或检查账号权限。":""):"请登录 Google 后新增日程。";
  using(var ct=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)){
   auth.Click+=async delegate{
    if(working||busy||google==null)return;working=true;busy=true;poll.Stop();state();message.Text="请在浏览器中登录 Google…";
    try{if(!google.Connected||!google.CanWrite||authExpired){await google.LoginAsync(ct.Token);authExpired=false;cache=new CacheData();store.Delete("calendar.bin");prefs.Selected.Clear();prefs.SelectionSaved=false;metadataRead=DateTime.MinValue;}var calendars=await google.CalendarsAsync(ct.Token);cache.Calendars=calendars;fill(calendars);ready=calendar.Items.Count>0;message.Text=calendar.Items.Count==0?"没有可写入的日历。":"已授权，可以新增日程。";}
    catch(Exception ex){message.Text=ex is OperationCanceledException?"授权已取消或超时，可重新尝试。":Friendly(ex);}
    finally{working=false;busy=false;state();Render();Schedule();}
   };
   save.Click+=async delegate{
    if(working||busy||demo)return;
    if(!confirmed){
     try{DateTime a=startDate.SelectedDate,b=endDate.SelectedDate;bool all=allDay.IsChecked==true;TimeSpan at=all?TimeSpan.Zero:startTime.SelectedTime,bt=all?TimeSpan.Zero:endTime.SelectedTime;
      draft.Title=title.Text;draft.Location=location.Text;draft.Description=description.Text;draft.Start=a+at;draft.End=b+bt;draft.AllDay=all;draft.AllowPoint=editing&&original.IsPoint;int reminderIndex=reminder.SelectedIndex-(editing?1:0);draft.Reminder=reminderIndex<0?-1:new[]{-1,-2,10,30,60}[reminderIndex];draft.Payload();
     }catch(Exception ex){message.Text=ex.Message;return;}
    }
    var selected=calendar.SelectedItem as ComboBoxItem;if(selected==null){message.Text="请选择日历。";return;}var cal=(CalendarInfo)selected.Tag;
    working=true;busy=true;poll.Stop();state();message.Text="正在保存…";
    try{var ev=editing?await google.EditEventAsync(cal,original,draft,reminder.SelectedIndex==0,ct.Token):await google.CreateEventAsync(cal,draft,ct.Token);savedEvent=ev;cache.Events.RemoveAll(e=>e.CalendarId==ev.CalendarId&&e.Id==ev.Id);cache.Events.Add(ev);if(!prefs.Selected.Contains(cal.Id))prefs.Selected.Add(cal.Id);prefs.SelectionSaved=true;Save();store.Write("calendar.bin",cache,true);anchor=draft.Start.Date;working=false;w.Close();}
    catch(Exception ex){confirmed=!editing;if(ex is LoginRequiredException){authExpired=true;ready=false;confirmed=false;}message.Text=Friendly(ex)+(editing?"":"\n再次保存会确认同一条日程；取消后请先检查 Google 日历。");}
    finally{working=false;busy=false;state();Render();Schedule();if(!w.IsVisible&&!rendering)QueueSync();}
   };
   w.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(working)e.Cancel=true;};state();var layout=new DockPanel();DockPanel.SetDock(buttons,Dock.Bottom);layout.Children.Add(buttons);layout.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});SetDialogContent(w,layout);w.ShowDialog();
  }
  if(editing&&savedEvent!=null){if(detailOwner!=null)detailOwner.Close();OpenEvent(savedEvent);}
 }
}
}

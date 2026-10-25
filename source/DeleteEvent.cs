using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DeskCalendar {
public partial class GoogleService {
 public async Task DeleteEventAsync(CalendarInfo calendar,CalendarEvent ev,CancellationToken ct){
  if(!CanWrite)throw new Exception("请重新登录 Google 并授予日程读写权限。");
  if(calendar==null||!calendar.Writable||ev==null||ev.CalendarId!=calendar.Id||string.IsNullOrEmpty(ev.Id))throw new Exception("无权删除该日历的日程。");
  string session=accountSession;string url="https://www.googleapis.com/calendar/v3/calendars/"+Uri.EscapeDataString(calendar.Id)+"/events/"+Uri.EscapeDataString(ev.Id)+"?sendUpdates=none";
  for(int attempt=0;attempt<2;attempt++)using(var request=new HttpRequestMessage(HttpMethod.Delete,url)){
   request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",await Access(ct));if(!string.IsNullOrEmpty(ev.Etag))request.Headers.TryAddWithoutValidation("If-Match",ev.Etag);
   using(var response=await http.SendAsync(request,ct)){
    if(session!=accountSession)throw new OperationCanceledException();
    if(response.StatusCode==HttpStatusCode.Unauthorized&&attempt==0){token.Expiry=0;continue;}if(response.StatusCode==HttpStatusCode.Unauthorized)throw new LoginRequiredException();
    if(response.IsSuccessStatusCode||response.StatusCode==HttpStatusCode.Gone){responses.Clear();return;}
    if(response.StatusCode==HttpStatusCode.PreconditionFailed)throw new Exception("这条日程已有变更，请刷新后再删除。");
    if(response.StatusCode==HttpStatusCode.Forbidden)throw new Exception("无权删除这条日程，请检查 Google 授权和日历权限。");
    if(response.StatusCode==HttpStatusCode.NotFound)throw new Exception("找不到这条日程或无法访问，请刷新日历。");
    throw new Exception("删除未确认（"+(int)response.StatusCode+"），请稍后重试。");
   }
  }
  throw new LoginRequiredException();
 }
}
public partial class CalendarWindow {
 void AddDeleteAction(StackPanel panel,Window owner,CalendarEvent ev){
  var caption=(Grid)((DockPanel)((Border)owner.Tag).Child).Children[0];caption.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var close=caption.Children.OfType<Button>().Single();Grid.SetColumn(close,2);var remove=DeleteIcon(ev,owner,owner.Close);remove.Name="DeleteEvent";Grid.SetColumn(remove,1);caption.Children.Add(remove);
 }
 Button DeleteIcon(CalendarEvent ev,Window owner,Action completed){
  var calendar=cache.Calendars.FirstOrDefault(c=>c.Id==ev.CalendarId);var button=Icon("\uE74D","删除日程",()=>ConfirmDeleteEvent(owner,ev,completed));button.Name="DeleteDayEvent";button.Tag=ev;button.IsEnabled=!demo&&google!=null&&google.CanWrite&&calendar!=null&&calendar.Writable;
  if(!button.IsEnabled)button.ToolTip=demo?"示例日程不能删除":google!=null&&!google.CanWrite?"请先在设置中重新登录，授予日程读写权限":"此日历没有删除权限";return button;
 }
 void ConfirmDeleteEvent(Window owner,CalendarEvent ev,Action completed){
  var calendar=cache.Calendars.FirstOrDefault(c=>c.Id==ev.CalendarId);var w=Dialog("删除日程",370,280);w.Owner=owner;w.MinHeight=240;var panel=new StackPanel{Margin=new Thickness(22,10,22,18)};panel.Name="DeleteConfirmation";panel.Children.Add(Wrap("删除“"+ev.Title+"”？",16,foreground));panel.Children.Add(Wrap(!string.IsNullOrEmpty(ev.RecurringId)?"这是重复日程，只删除本次。":"这条日程也会从 Google 日历中删除。",12,muted));var status=Wrap("",12,muted);status.Margin=new Thickness(0,10,0,6);panel.Children.Add(status);
  bool deleting=false,deleted=false;var yes=SettingsButton("删除","确认删除当前日程",()=>{});yes.Name="ConfirmDeleteEvent";var no=SettingsButton("取消","保留日程",w.Close);no.Name="CancelDeleteEvent";var buttons=new Grid();buttons.ColumnDefinitions.Add(new ColumnDefinition());buttons.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(10)});buttons.ColumnDefinitions.Add(new ColumnDefinition());buttons.Children.Add(yes);Grid.SetColumn(no,2);buttons.Children.Add(no);panel.Children.Add(buttons);
  w.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(deleting)e.Cancel=true;};
  yes.Click+=async delegate{
   if(deleting||busy||demo)return;deleting=true;busy=true;poll.Stop();yes.IsEnabled=no.IsEnabled=false;status.Text="正在删除…";
   try{await google.DeleteEventAsync(calendar,ev,lifetime.Token);cache.Events.RemoveAll(e=>e.CalendarId==ev.CalendarId&&e.Id==ev.Id);store.Write("calendar.bin",cache,true);deleted=true;deleting=false;w.Close();}
   catch(Exception ex){if(ex is LoginRequiredException)authExpired=true;status.Text=Friendly(ex);}
   finally{deleting=false;busy=false;yes.IsEnabled=no.IsEnabled=true;Render();Schedule();}
  };
  SetDialogContent(w,new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});w.ShowDialog();if(deleted){completed();if(!rendering)QueueSync();}
 }
 void FillDayEvents(StackPanel panel,Window owner,DateTime day){
  panel.Children.Clear();var events=CalendarMath.ForDay(VisibleEvents(),day);if(events.Count==0)panel.Children.Add(Text("无日程",14,muted));
  foreach(var ev in events){var current=ev;var stack=new StackPanel();stack.Children.Add(Text(CalendarMath.TimeText(ev,day),11,muted));stack.Children.Add(Wrap(ev.Title,14,foreground));if(!string.IsNullOrEmpty(ev.Location))stack.Children.Add(Text(ev.Location,11,muted));var open=Button("",Tooltip(ev,day),()=>OpenEvent(current));open.Content=stack;open.HorizontalContentAlignment=HorizontalAlignment.Stretch;open.Padding=new Thickness(10);open.BorderBrush=BrushColor(ev.Color);open.BorderThickness=new Thickness(2,0,0,0);var row=new Grid{Margin=new Thickness(0,0,0,8)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.Children.Add(open);var remove=DeleteIcon(current,owner,()=>FillDayEvents(panel,owner,day));remove.VerticalAlignment=VerticalAlignment.Center;remove.Margin=new Thickness(6,0,0,0);Grid.SetColumn(remove,1);row.Children.Add(remove);panel.Children.Add(row);}
 }
}
}
using System;
using System.IO;
using Path=System.IO.Path;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DeskCalendar {
public static partial class SelfTests {
 static void CheckDeletePrompt(Window owner,Action trigger,bool accept){
  Exception failure=null;owner.Dispatcher.BeginInvoke(new Action(()=>{Window confirm=null;try{confirm=owner.OwnedWindows.Cast<Window>().Single(w=>w.Title=="删除日程");Check(confirm.IsVisible&&confirm.Owner==owner&&Descendants(confirm).OfType<Button>().Any(b=>b.Name=="ConfirmDeleteEvent"),"垃圾箱点击后弹出所属窗口的删除确认小窗");Descendants(confirm).OfType<Button>().Single(b=>b.Name==(accept?"ConfirmDeleteEvent":"CancelDeleteEvent")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(owner);}catch(Exception ex){failure=ex;}finally{if(confirm!=null&&confirm.IsVisible)confirm.Close();}}),DispatcherPriority.ApplicationIdle);trigger();if(failure!=null)throw failure;
 }
 static void CreateChecks(string directory){
  var draft=new EventDraft{Title=" 测试日程 ",Start=new DateTime(2026,10,25,23,0,0),End=new DateTime(2026,10,26,1,0,0),Location="地点",Description="备注"};
  var body=draft.Payload();Check(Store.Text(body,"summary")=="测试日程"&&Store.Text(Store.Map(body,"start"),"dateTime").Contains("T23:00:00"),"新增日程保留标题、跨天时间与本地偏移");
  draft.AllDay=true;draft.End=draft.Start.Date;body=draft.Payload();Check(Store.Text(Store.Map(body,"end"),"date")=="2026-10-26","单日全天事件结束日期按 Google 排他语义加一天");
  draft.End=draft.Start.AddDays(2);Check(Store.Text(Store.Map(draft.Payload(),"end"),"date")=="2026-10-28","多日全天事件包含用户选择的结束日期");
  draft.Reminder=-2;Check(((object[])Store.Map(draft.Payload(),"reminders")["overrides"]).Length==0,"不提醒保存为空提醒列表");draft.Reminder=30;Check(!(bool)Store.Map(draft.Payload(),"reminders")["useDefault"],"自定义提醒不沿用默认值");
  bool rejected=false;draft.Title=" ";try{draft.Payload();}catch{rejected=true;}Check(rejected,"新增日程拒绝空白标题");draft.Title="测试";draft.AllDay=false;draft.End=draft.Start;rejected=false;try{draft.Payload();}catch{rejected=true;}Check(rejected,"定时日程拒绝结束时间不晚于开始时间");draft.End=draft.Start.AddHours(1);
  var store=new Store(directory);store.Write("tokens.bin",new TokenData{Access="test",Scope="https://www.googleapis.com/auth/calendar.events",Expiry=DateTime.UtcNow.AddHours(1).Ticks},true);
  var handler=new CreateHandler();using(var google=new GoogleService(store,handler)){
   Check(google.CanWrite,"识别新增写入授权");var calendars=google.CalendarsAsync(CancellationToken.None).GetAwaiter().GetResult();Check(calendars.Count(c=>c.Writable)==2&&!calendars.Last().Writable,"只有 owner 与 writer 日历可新增");
   var cal=calendars.First();var ev=google.CreateEventAsync(cal,draft,CancellationToken.None).GetAwaiter().GetResult();Check(ev.Id==draft.Id&&ev.Title=="测试"&&handler.Posts==1&&handler.Bearer,"新增调用官方 POST 接口并带授权");
   handler.Conflict=true;var retried=google.CreateEventAsync(cal,draft,CancellationToken.None).GetAwaiter().GetResult();Check(retried.Id==draft.Id&&handler.Reads==1,"同一 ID 冲突时读取已有结果防止重试重复创建");
   int posts=handler.Posts;rejected=false;try{google.CreateEventAsync(calendars.Last(),draft,CancellationToken.None).GetAwaiter().GetResult();}catch{rejected=true;}Check(rejected&&handler.Posts==posts,"只读日历在发送前拦截");
   Check(GoogleService.Scopes.Split(' ').Contains("https://www.googleapis.com/auth/calendar.events")&&!GoogleService.Scopes.Contains("events.readonly"),"首次 Google 登录一次申请日程读写权限");
   ev.Etag="\"test-revision\"";ev.Id="repeat_20261025T150000Z";google.DeleteEventAsync(cal,ev,CancellationToken.None).GetAwaiter().GetResult();Check(handler.Deletes==1&&handler.DeletedPath.EndsWith("/repeat_20261025T150000Z")&&handler.Match==ev.Etag,"删除重复日程只发送实例 ID 并携带版本条件");
   handler.DeleteStatus=HttpStatusCode.Gone;google.DeleteEventAsync(cal,ev,CancellationToken.None).GetAwaiter().GetResult();Check(handler.Deletes==2,"已删除日程返回 410 时按成功处理");
   handler.DeleteStatus=HttpStatusCode.PreconditionFailed;rejected=false;try{google.DeleteEventAsync(cal,ev,CancellationToken.None).GetAwaiter().GetResult();}catch(Exception ex){rejected=ex.Message.Contains("已有变更");}Check(rejected,"日程变更冲突时阻止删除并提示刷新");
   int deletes=handler.Deletes;rejected=false;try{google.DeleteEventAsync(calendars.Last(),ev,CancellationToken.None).GetAwaiter().GetResult();}catch{rejected=true;}Check(rejected&&handler.Deletes==deletes,"只读日历不发送删除请求");
   handler.DeleteStatus=HttpStatusCode.Forbidden;rejected=false;try{google.DeleteEventAsync(cal,ev,CancellationToken.None).GetAwaiter().GetResult();}catch(Exception ex){rejected=ex.Message.Contains("无权删除");}Check(rejected,"Google 拒绝删除时向用户报告权限错误");
  }
  store.Write("tokens.bin",new TokenData{Access="test",Expiry=DateTime.UtcNow.AddHours(1).Ticks},true);handler=new CreateHandler();using(var google=new GoogleService(store,handler)){rejected=false;try{google.CreateEventAsync(new CalendarInfo{Id="test",AccessRole="owner"},draft,CancellationToken.None).GetAwaiter().GetResult();}catch{rejected=true;}Check(!google.CanWrite&&rejected&&handler.Posts==0,"旧只读令牌不会尝试写入");rejected=false;try{google.DeleteEventAsync(new CalendarInfo{Id="test",AccessRole="owner"},new CalendarEvent{Id="event",CalendarId="test"},CancellationToken.None).GetAwaiter().GetResult();}catch{rejected=true;}Check(rejected&&handler.Deletes==0,"旧只读令牌不会发送删除请求");}
 }
 static void CreateWidgetChecks(CalendarWindow window){
  var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
  Check(Descendants(window).OfType<Button>().Any(b=>b.Name=="AddEvent"),"主页提供新增日程快捷按钮");
  typeof(CalendarWindow).GetMethod("OpenDay",flags).Invoke(window,new object[]{DateTime.Today});var day=(Window)typeof(CalendarWindow).GetField("dayDetail",flags).GetValue(window);Check(Descendants(day).OfType<Button>().Any(b=>b.Name=="AddDayEvent"),"单日日程窗口提供新增按钮");day.Close();
  Exception error=null;DateTime selected=new DateTime(2026,11,4);
  window.Dispatcher.BeginInvoke(new Action(()=>{Window editor=null;try{editor=window.OwnedWindows.Cast<Window>().Single(w=>w.Title=="新增日程");Check(Descendants(editor).OfType<EventDatePicker>().Single(b=>b.Name=="EventStartDate").SelectedDate==selected,"从某天新增时预填所选日期");Check(Descendants(editor).OfType<Button>().Any(b=>b.Name=="AuthorizeCreate"&&b.IsVisible),"旧只读登录显示单独写入授权入口");Check(!Descendants(editor).OfType<Button>().Single(b=>b.Name=="SaveEvent").IsEnabled,"未授权不能保存日程");Check(Native.BackdropType(new System.Windows.Interop.WindowInteropHelper(editor).Handle)==Native.BackdropType(new System.Windows.Interop.WindowInteropHelper(window).Handle),"新增窗口与主页使用一致毛玻璃材质");}catch(Exception ex){error=ex;}finally{if(editor!=null)editor.Close();}}),DispatcherPriority.ApplicationIdle);
  typeof(CalendarWindow).GetMethod("OpenCreateEvent",flags).Invoke(window,new object[]{selected});if(error!=null)throw error;
  var realGoogle=(GoogleService)typeof(CalendarWindow).GetField("google",flags).GetValue(window);var dataStore=new Store(Path.Combine(((Store)typeof(CalendarWindow).GetField("store",flags).GetValue(window)).DirectoryPath,"create-ui"));dataStore.Write("tokens.bin",new TokenData{Access="test",Scope="https://www.googleapis.com/auth/calendar.events",Expiry=DateTime.UtcNow.AddHours(1).Ticks},true);var handler=new CreateHandler();
  using(var mockGoogle=new GoogleService(dataStore,handler)){
   var cached=(CacheData)typeof(CalendarWindow).GetField("cache",flags).GetValue(window);var cal=cached.Calendars.First();cal.AccessRole="owner";typeof(CalendarWindow).GetField("google",flags).SetValue(window,mockGoogle);
   window.Dispatcher.BeginInvoke(new Action(()=>{Window editor=null;try{
    editor=window.OwnedWindows.Cast<Window>().Single(w=>w.Title=="新增日程");var save=Descendants(editor).OfType<Button>().Single(b=>b.Name=="SaveEvent");Check(save.IsEnabled,"已授权且存在可写日历时允许保存");save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(editor.IsVisible&&handler.Posts==0,"空标题在表单内提示，不发送请求");
    var datePicker=Descendants(editor).OfType<EventDatePicker>().Single(b=>b.Name=="EventStartDate");datePicker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(window);Check(datePicker.CalendarPopup.IsOpen&&((System.Windows.Controls.Border)datePicker.CalendarPopup.Child).Background==window.Resources["ControlPopup"],"日期选择框展开统一深色月历");var dayButton=Descendants(datePicker.CalendarPopup.Child).OfType<Button>().Single(b=>b.Tag is DateTime&&(DateTime)b.Tag==selected);dayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(datePicker.SelectedDate==selected&&!datePicker.CalendarPopup.IsOpen,"点击月历日期后更新选择并收起");
    var startPicker=Descendants(editor).OfType<EventTimePicker>().Single(b=>b.Name=="EventStartTime");var endPicker=Descendants(editor).OfType<EventTimePicker>().Single(b=>b.Name=="EventEndTime");var endDate=Descendants(editor).OfType<EventDatePicker>().Single(b=>b.Name=="EventEndDate");var duration=Descendants(editor).OfType<ComboBox>().Single(b=>b.Name=="EventDuration");Check((double)((ComboBoxItem)duration.SelectedItem).Tag==30&&endPicker.SelectedTime-startPicker.SelectedTime==TimeSpan.FromMinutes(30),"新增日程默认持续半小时");duration.SelectedItem=duration.Items.OfType<ComboBoxItem>().Single(i=>(double)i.Tag==90);Check(endPicker.SelectedTime==new TimeSpan(10,30,0),"选择持续时间自动推迟结束时间");startPicker.SetTime(selected.Date.AddHours(23).AddMinutes(45));Check(endDate.SelectedDate==selected.AddDays(1)&&endPicker.SelectedTime==new TimeSpan(1,15,0),"修改开始时间保持持续时间并处理跨午夜");endPicker.SetTime(selected.Date.AddMinutes(5));Check((double)((ComboBoxItem)duration.SelectedItem).Tag==20,"手动修改结束时间自动生成精确持续分钟数");datePicker.SetDate(selected.AddDays(2));Check(endDate.SelectedDate==selected.AddDays(3),"修改开始日期时结束日期同步移动");datePicker.SetDate(selected);duration.SelectedItem=duration.Items.OfType<ComboBoxItem>().First(i=>(double)i.Tag==30);
    startPicker.Hours.SelectedIndex=11;startPicker.Minutes.SelectedIndex=45;endDate.SetDate(selected);endPicker.Hours.SelectedIndex=12;endPicker.Minutes.SelectedIndex=15;Check(startPicker.Hours.Items.Count==24&&startPicker.Minutes.Items.Count==60&&startPicker.SelectedTime==new TimeSpan(11,45,0),"时间选择框支持全部小时和分钟");
    var allDay=Descendants(editor).OfType<CheckBox>().Single(c=>c.Name=="EventAllDay");allDay.IsChecked=true;Check(!startPicker.IsEnabled&&!endPicker.IsEnabled,"全天日程禁用时间选择");allDay.IsChecked=false;
    Descendants(editor).OfType<TextBox>().Single(b=>b.Name=="EventTitle").Text="新增 UI 测试";editor.UpdateLayout();
    string capture=Path.Combine(dataStore.DirectoryPath,"create-event.png");var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)editor.ActualWidth,(int)editor.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render((System.Windows.Media.Visual)editor.Content);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var file=File.Create(capture))encoder.Save(file);File.WriteAllText(Path.Combine(dataStore.DirectoryPath,"capture-path.txt"),capture);
    save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(window);Check(handler.Posts==1&&!editor.IsVisible&&cached.Events.Any(e=>e.Title=="新增 UI 测试"),"保存成功后关闭新增窗口并即时更新日程缓存 posts="+handler.Posts+" visible="+editor.IsVisible+" text="+string.Join(" / ",Descendants(editor).OfType<TextBlock>().Select(t=>t.Text)));
    var created=cached.Events.Single(e=>e.Title=="新增 UI 测试");Check(created.StartLocal.Hour==11&&created.StartLocal.Minute==45&&created.EndLocal.Minute==15,"选择的小时分钟正确写入保存结果");
    typeof(CalendarWindow).GetMethod("OpenEvent",flags).Invoke(window,new object[]{created});var detail=window.OwnedWindows.Cast<Window>().Single(w=>w.Title=="日程详情");Check(Descendants(detail).OfType<Button>().Any(b=>b.Name=="EditEvent"&&b.IsEnabled),"可写日程详情提供编辑图标");var delete=Descendants(detail).OfType<Button>().Single(b=>b.Name=="DeleteEvent");Check(delete.IsEnabled&&Convert.ToString(delete.Content)=="\uE74D"&&delete.Parent is Grid,"详情右上角使用垃圾箱图标");
    CheckDeletePrompt(detail,()=>delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),false);Check(handler.Deletes==0&&detail.IsVisible,"取消删除不会发送请求或关闭详情");
    CheckDeletePrompt(detail,()=>delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),true);Pump(window);Check(handler.Deletes==1&&!detail.IsVisible&&!cached.Events.Any(e=>e.Id==created.Id),"确认删除后关闭详情并即时移除缓存日程");
    created.Id="quick-delete-test";cached.Events.Add(created);typeof(CalendarWindow).GetMethod("OpenDay",flags).Invoke(window,new object[]{created.StartLocal.Date});var list=window.OwnedWindows.Cast<Window>().Single();var quick=Descendants(list).OfType<Button>().Single(b=>b.Name=="DeleteDayEvent"&&((CalendarEvent)b.Tag).Id==created.Id);Check(Convert.ToString(quick.Content)=="\uE74D"&&Grid.GetColumn(quick)==1,"单日列表每条事件右侧提供垃圾箱");CheckDeletePrompt(list,()=>quick.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),true);Pump(window);Check(handler.Deletes==2&&list.IsVisible&&!Descendants(list).OfType<Button>().Any(b=>b.Name=="DeleteDayEvent"&&((CalendarEvent)b.Tag).Id==created.Id),"快捷删除后保持当天窗口并刷新列表");list.Close();
   }catch(Exception ex){error=ex;}finally{if(editor!=null&&editor.IsVisible)editor.Close();}}),DispatcherPriority.ApplicationIdle);
   try{typeof(CalendarWindow).GetMethod("OpenCreateEvent",flags).Invoke(window,new object[]{selected});if(error!=null)throw error;}finally{typeof(CalendarWindow).GetField("google",flags).SetValue(window,realGoogle);}
  }
  ((TokenData)typeof(GoogleService).GetField("token",flags).GetValue(realGoogle)).Scope="https://www.googleapis.com/auth/calendar.events";
  var preferences=(Preferences)typeof(CalendarWindow).GetField("prefs",flags).GetValue(window);preferences.Selected.Add("demo");
  var month=(Grid)typeof(CalendarWindow).GetMethod("MonthView",flags).Invoke(window,new object[]{DateTime.Today,false});month.Resources=window.Resources;
  foreach(var size in new[]{new Size(700,480),new Size(500,680),new Size(900,380)}){month.Measure(size);month.Arrange(new Rect(size));month.UpdateLayout();var dots=Descendants(month).OfType<Ellipse>().Where(e=>e.Name=="MonthEventDot").ToList();Check(dots.Count>0&&dots.All(d=>Math.Abs(d.TranslatePoint(new Point(1.5,1.5),(Grid)d.Parent).Y-9)<.1),"月历在 "+size+" 尺寸下日程圆点保持行内垂直居中");}
 }
}
class CreateHandler:HttpMessageHandler {
 public int Posts,Reads,Deletes;public bool Bearer,Conflict;public string DeletedPath,Match;public HttpStatusCode DeleteStatus=HttpStatusCode.NoContent;string saved;
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
  if(request.RequestUri.AbsolutePath.EndsWith("calendarList"))return Reply(HttpStatusCode.OK,"{\"items\":[{\"id\":\"owner\",\"summary\":\"Owner\",\"accessRole\":\"owner\"},{\"id\":\"writer\",\"accessRole\":\"writer\"},{\"id\":\"reader\",\"accessRole\":\"reader\"}]}");
  if(request.Method==HttpMethod.Post){Posts++;Bearer=request.Headers.Authorization!=null&&request.Headers.Authorization.Parameter=="test";saved=await request.Content.ReadAsStringAsync();return Reply(Conflict?HttpStatusCode.Conflict:HttpStatusCode.OK,saved);}
  if(request.Method==HttpMethod.Delete){Deletes++;DeletedPath=request.RequestUri.AbsolutePath;Match=request.Headers.Contains("If-Match")?request.Headers.GetValues("If-Match").Single():null;return Reply(DeleteStatus,"");}
  Reads++;return Reply(HttpStatusCode.OK,saved);
 }
 static HttpResponseMessage Reply(HttpStatusCode code,string body){return new HttpResponseMessage(code){Content=new StringContent(body)};}
}
}

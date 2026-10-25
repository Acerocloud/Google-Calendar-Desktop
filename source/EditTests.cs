using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DeskCalendar {
public static partial class SelfTests {
 static CalendarEvent EditableFixture(){return new CalendarEvent{Id="series_20261026T010000Z",RecurringId="series",Etag="\"v1\"",CalendarId="work",CalendarName="工作",Color="#123456",Title="原日程",Location="会议室",Description="原备注",Start="2026-10-26T09:00:30+08:00",End="2026-10-26T10:15:40+08:00"};}
 static void EditChecks(string directory){
  var original=EditableFixture();var draft=new EventDraft{Title=original.Title,Location=original.Location,Description=original.Description,Start=new DateTime(2026,10,26,9,0,0),End=new DateTime(2026,10,26,10,15,0)};
  // Fixtures use local values so the same tests work outside UTC+08 as well.
  draft.Start=original.StartLocal.Date.AddHours(original.StartLocal.Hour).AddMinutes(original.StartLocal.Minute);draft.End=original.EndLocal.Date.AddHours(original.EndLocal.Hour).AddMinutes(original.EndLocal.Minute);
  Check(GoogleService.EditPayload(original,draft,true).Count==0,"不改动时不发送更新，并保留秒数精度");draft.Title="改后的标题";var patch=GoogleService.EditPayload(original,draft,true);Check(patch.Count==1&&Store.Text(patch,"summary")==draft.Title&&!patch.ContainsKey("id")&&!patch.ContainsKey("reminders"),"局部修改仅发送变更字段，保留原提醒与会议信息");
  var all=new CalendarEvent{Title="全天",AllDay=true,Start="2026-10-26",End="2026-10-28"};var allDraft=new EventDraft{Title="全天改名",AllDay=true,Start=new DateTime(2026,10,26),End=new DateTime(2026,10,27)};Check(GoogleService.EditPayload(all,allDraft,true).Count==1,"编辑全天日程名称不会缩短日期范围");
  var point=EditableFixture();point.End=point.Start;var pointDraft=new EventDraft{Title="点事件改名",Location=point.Location,Description=point.Description,Start=draft.Start,End=draft.Start,AllowPoint=true};var pointPatch=GoogleService.EditPayload(point,pointDraft,true);Check(pointPatch.Count==1&&!pointPatch.ContainsKey("start")&&!pointPatch.ContainsKey("end"),"点事件仅改标题时保留原始零时长与秒数");
  draft.AllDay=true;draft.Start=new DateTime(2026,10,26);draft.End=new DateTime(2026,10,27);patch=GoogleService.EditPayload(original,draft,false);Check(Store.Text(Store.Map(patch,"end"),"date")=="2026-10-28"&&Store.Map(patch,"start")["dateTime"]==null,"修改为全天时使用排他结束日期并清除旧时间类型");draft.AllDay=false;draft.Start=original.StartLocal.AddMinutes(30);draft.End=original.EndLocal.AddMinutes(30);
  var store=new Store(directory);store.Write("tokens.bin",new TokenData{Access="test",Scope="https://www.googleapis.com/auth/calendar.events",Expiry=DateTime.UtcNow.AddHours(1).Ticks},true);var handler=new EditHandler(original);var cal=new CalendarInfo{Id="work",Name="工作",AccessRole="owner",Color="#123456"};
  using(var google=new GoogleService(store,handler)){
   var result=google.EditEventAsync(cal,original,draft,true,CancellationToken.None).GetAwaiter().GetResult();Check(handler.Patches==1&&handler.Match==original.Etag&&handler.Path.EndsWith("/"+original.Id)&&result.Id==original.Id&&result.RecurringId=="series"&&result.Title==draft.Title,"PATCH 编辑原实例，带 If-Match 且不新建或修改整个系列");Check(original.Title=="原日程","服务返回新对象，失败前不修改原缓存对象");
   handler.Status=HttpStatusCode.PreconditionFailed;bool rejected=false;try{google.EditEventAsync(cal,original,draft,true,CancellationToken.None).GetAwaiter().GetResult();}catch(Exception ex){rejected=ex.Message.Contains("其他地方修改");}Check(rejected,"外部修改冲突拒绝覆盖并提示刷新");int requests=handler.Patches;rejected=false;try{google.EditEventAsync(new CalendarInfo{Id="work",AccessRole="reader"},original,draft,true,CancellationToken.None).GetAwaiter().GetResult();}catch{rejected=true;}Check(rejected&&requests==handler.Patches,"只读日历不会发送编辑请求");
   original.Etag=null;rejected=false;try{google.EditEventAsync(cal,original,draft,true,CancellationToken.None).GetAwaiter().GetResult();}catch(Exception ex){rejected=ex.Message.Contains("刷新");}Check(rejected&&requests==handler.Patches,"缺失版本信息的旧缓存要求先刷新");
  }
  EditWidgetChecks(Path.Combine(directory,"ui"));
 }
 static void EditWidgetChecks(string directory){
  var app=Application.Current??new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var store=new Store(directory);var original=EditableFixture();var handler=new EditHandler(original);var cache=new CacheData();cache.Calendars.Add(new CalendarInfo{Id="work",Name="工作",AccessRole="owner",Color="#123456"});cache.Events.Add(original);store.Write("calendar.bin",cache,true);store.Write("settings.json",new Preferences{Selected=new List<string>{"work"},SelectionSaved=true},false);store.Write("tokens.bin",new TokenData{Access="test",Scope="https://www.googleapis.com/auth/calendar.events",Expiry=DateTime.UtcNow.AddHours(1).Ticks},true);
  var window=new CalendarWindow(store,false,true);var flags=BindingFlags.Instance|BindingFlags.NonPublic;var initial=(GoogleService)typeof(CalendarWindow).GetField("google",flags).GetValue(window);Exception error=null;using(var google=new GoogleService(store,handler)){
   typeof(CalendarWindow).GetField("google",flags).SetValue(window,google);window.ShowCalendar();typeof(CalendarWindow).GetMethod("OpenEvent",flags).Invoke(window,new object[]{original});var details=window.OwnedWindows.Cast<Window>().Single();var edit=Descendants(details).OfType<Button>().Single(b=>b.Name=="EditEvent");Check(edit.IsEnabled&&Grid.GetColumn(edit)==1,"编辑图标位于详情标题栏右侧垃圾箱之前");
   window.Dispatcher.BeginInvoke(new Action(()=>{Window editor=null;try{editor=details.OwnedWindows.Cast<Window>().Single(w=>w.Title=="编辑日程");Check(Descendants(editor).OfType<TextBox>().Single(b=>b.Name=="EventTitle").Text==original.Title&&Descendants(editor).OfType<TextBox>().Single(b=>b.Name=="EventLocation").Text==original.Location,"编辑表单预填原标题与地点");Check(!Descendants(editor).OfType<ComboBox>().Single(c=>c.Name=="EventCalendar").IsEnabled&&Convert.ToString(Descendants(editor).OfType<ComboBox>().Single(c=>c.Name=="EventReminder").SelectedItem)=="保持原提醒","编辑固定所属日历且默认保留原提醒");Check((double)((ComboBoxItem)Descendants(editor).OfType<ComboBox>().Single(c=>c.Name=="EventDuration").SelectedItem).Tag==75,"编辑按原时间预填持续时长");editor.Close();}catch(Exception ex){error=ex;}finally{if(editor!=null&&editor.IsVisible)editor.Close();}}),DispatcherPriority.ApplicationIdle);
   edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(error!=null)throw error;Check(handler.Patches==0&&details.IsVisible,"取消编辑不写入 Google，保留原详情");
   window.Dispatcher.BeginInvoke(new Action(()=>{Window editor=null;try{editor=details.OwnedWindows.Cast<Window>().Single(w=>w.Title=="编辑日程");Descendants(editor).OfType<TextBox>().Single(b=>b.Name=="EventTitle").Text="UI 编辑成功";Descendants(editor).OfType<Button>().Single(b=>b.Name=="SaveEvent").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(window);}catch(Exception ex){error=ex;}finally{if(editor!=null&&editor.IsVisible)editor.Close();}}),DispatcherPriority.ApplicationIdle);
   edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(error!=null)throw error;var saved=store.Read<CacheData>("calendar.bin",true);Check(handler.Patches==1&&saved.Events.Count==1&&saved.Events[0].Id==original.Id&&saved.Events[0].Title=="UI 编辑成功","保存编辑更新同一个缓存事件，不产生重复日程");var updated=window.OwnedWindows.Cast<Window>().Single(w=>w.Title=="日程详情");Check(Descendants(updated).OfType<TextBlock>().Any(t=>t.Text=="UI 编辑成功"),"保存后详情显示更新结果");updated.Close();typeof(CalendarWindow).GetField("exiting",flags).SetValue(window,true);window.Close();
  }initial.Dispose();
 }
}
class EditHandler:HttpMessageHandler {
 readonly CalendarEvent original;public int Patches;public string Match,Path;public HttpStatusCode Status=HttpStatusCode.OK;
 public EditHandler(CalendarEvent value){original=value;}
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
  if(request.Method.Method!="PATCH")throw new Exception("Unexpected test request");Patches++;Path=request.RequestUri.AbsolutePath;Match=request.Headers.GetValues("If-Match").Single();var patch=Store.Object(await request.Content.ReadAsStringAsync());var data=new Dictionary<string,object>{{"id",original.Id},{"etag","\"v2\""},{"recurringEventId",original.RecurringId},{"summary",original.Title},{"location",original.Location},{"description",original.Description},{"start",new Dictionary<string,object>{{"dateTime",original.Start}}},{"end",new Dictionary<string,object>{{"dateTime",original.End}}}};foreach(var item in patch)data[item.Key]=item.Value;return new HttpResponseMessage(Status){Content=new StringContent(Store.Json.Serialize(data))};
 }
}
}

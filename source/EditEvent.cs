using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DeskCalendar {
public partial class GoogleService {
 static DateTime Minute(DateTime value){return new DateTime(value.Year,value.Month,value.Day,value.Hour,value.Minute,0);}
 public static Dictionary<string,object> EditPayload(CalendarEvent original,EventDraft draft,bool keepReminders){
  var body=draft.Payload();body.Remove("id");if(keepReminders)body.Remove("reminders");
  if(draft.Title.Trim()==original.Title)body.Remove("summary");if((draft.Location??"")==(original.Location??""))body.Remove("location");if((draft.Description??"")==(original.Description??""))body.Remove("description");
  bool sameKind=draft.AllDay==original.AllDay;
  if(sameKind&&(draft.AllDay?draft.Start.Date==original.StartLocal.Date:draft.Start==Minute(original.StartLocal)))body.Remove("start");
  if(sameKind&&(draft.AllDay?draft.End.Date.AddDays(1)==original.EndLocal.Date:draft.End==Minute(original.EndLocal)))body.Remove("end");
  foreach(string key in new[]{"start","end"}){object raw;if(body.TryGetValue(key,out raw)){var date=(Dictionary<string,object>)raw;date[draft.AllDay?"dateTime":"date"]=null;if(draft.AllDay)date["timeZone"]=null;}}
  return body;
 }
 public async Task<CalendarEvent> EditEventAsync(CalendarInfo cal,CalendarEvent original,EventDraft draft,bool keepReminders,CancellationToken ct){
  if(!CanWrite)throw new Exception("请重新登录并授予日程读写权限。");if(cal==null||!cal.Writable||original==null||original.CalendarId!=cal.Id||string.IsNullOrEmpty(original.Id))throw new Exception("无权编辑该日历的日程。");
  var patch=EditPayload(original,draft,keepReminders);if(patch.Count==0)return original;string session=accountSession;string url="https://www.googleapis.com/calendar/v3/calendars/"+Uri.EscapeDataString(cal.Id)+"/events/"+Uri.EscapeDataString(original.Id);string etag=original.Etag;
  if(string.IsNullOrEmpty(etag))throw new Exception("请先刷新日历，获取最新日程后再编辑。");
  for(int attempt=0;attempt<2;attempt++)using(var request=new HttpRequestMessage(new HttpMethod("PATCH"),url+"?sendUpdates=none")){
   request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",await Access(ct));request.Headers.TryAddWithoutValidation("If-Match",etag);request.Content=new StringContent(Store.Json.Serialize(patch),Encoding.UTF8,"application/json");
   using(var response=await http.SendAsync(request,ct)){
    string text=await response.Content.ReadAsStringAsync();if(session!=accountSession)throw new OperationCanceledException();if(response.StatusCode==HttpStatusCode.Unauthorized&&attempt==0){token.Expiry=0;continue;}if(response.StatusCode==HttpStatusCode.Unauthorized)throw new LoginRequiredException();
    if(response.StatusCode==HttpStatusCode.PreconditionFailed||response.StatusCode==HttpStatusCode.Conflict)throw new Exception("这条日程已在其他地方修改，请关闭编辑、刷新日历后重试。");
    if(response.StatusCode==HttpStatusCode.Forbidden)throw new Exception("无权编辑这条日程，请检查 Google 授权和日历权限。");if(response.StatusCode==HttpStatusCode.NotFound||response.StatusCode==HttpStatusCode.Gone)throw new Exception("日程已删除或无法访问，请刷新日历。");
    if(!response.IsSuccessStatusCode)throw new Exception("保存未确认（"+(int)response.StatusCode+"），请刷新 Google 日历确认结果后再试。");var data=Store.Object(text);var a=Store.Map(data,"start");var b=Store.Map(data,"end");if(a==null||b==null||Store.Text(data,"id")!=original.Id)throw new Exception("保存结果不完整，请刷新日历确认。");bool all=Store.Text(a,"date")!="";responses.Clear();
    return new CalendarEvent{Id=original.Id,Etag=Store.Text(data,"etag"),RecurringId=Store.Text(data,"recurringEventId"),CalendarId=cal.Id,CalendarName=cal.Name,Color=original.Color,Title=Store.Text(data,"summary"),Location=Store.Text(data,"location"),Description=Store.Text(data,"description"),Link=Store.Text(data,"htmlLink"),AllDay=all,Start=Store.Text(a,all?"date":"dateTime"),End=Store.Text(b,all?"date":"dateTime")};
   }
  }
  throw new LoginRequiredException();
 }
}
public partial class CalendarWindow {
 void AddEditAction(Window owner,CalendarEvent ev){
  var caption=(Grid)((DockPanel)((Border)owner.Tag).Child).Children[0];caption.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});foreach(UIElement child in caption.Children)if(Grid.GetColumn(child)>0)Grid.SetColumn(child,Grid.GetColumn(child)+1);
  var edit=Icon("\uE70F","编辑日程",()=>OpenEditEvent(ev));edit.Name="EditEvent";var calendar=cache.Calendars.FirstOrDefault(c=>c.Id==ev.CalendarId);edit.IsEnabled=!demo&&google!=null&&google.CanWrite&&calendar!=null&&calendar.Writable;if(!edit.IsEnabled)edit.ToolTip="需要日程读写授权与日历编辑权限";Grid.SetColumn(edit,1);caption.Children.Add(edit);
 }
}
}

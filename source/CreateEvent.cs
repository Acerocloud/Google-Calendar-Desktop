using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DeskCalendar {
public class EventDraft {
 public string Id=Guid.NewGuid().ToString("N"),Title,Location,Description;
 public DateTime Start,End; public bool AllDay,AllowPoint; public int Reminder=-1;
 public Dictionary<string,object> Payload(){
  if(string.IsNullOrWhiteSpace(Title))throw new Exception("请填写日程标题。");
  if(AllDay?End.Date<Start.Date:AllowPoint?End<Start:End<=Start)throw new Exception("结束时间须晚于开始时间。");
  if(!AllDay&&(TimeZoneInfo.Local.IsInvalidTime(Start)||TimeZoneInfo.Local.IsInvalidTime(End)))throw new Exception("所选时间处于夏令时跳转区间，请换一个时间。");
  if(!AllDay&&(TimeZoneInfo.Local.IsAmbiguousTime(Start)||TimeZoneInfo.Local.IsAmbiguousTime(End)))throw new Exception("所选时间在夏令时切换时出现两次，请在 Google 日历中创建。");
  if(Reminder < -2 || Reminder>40320)throw new Exception("提醒时间无效。");
  var start=new Dictionary<string,object>();var end=new Dictionary<string,object>();
  if(AllDay){start["date"]=Start.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);end["date"]=End.Date.AddDays(1).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);}
  else{start["dateTime"]=new DateTimeOffset(DateTime.SpecifyKind(Start,DateTimeKind.Local)).ToString("o");end["dateTime"]=new DateTimeOffset(DateTime.SpecifyKind(End,DateTimeKind.Local)).ToString("o");}
  var reminders=new Dictionary<string,object>{{"useDefault",Reminder==-1}};
  if(Reminder!=-1)reminders["overrides"]=Reminder==-2?new object[0]:new object[]{new Dictionary<string,object>{{"method","popup"},{"minutes",Reminder}}};
  return new Dictionary<string,object>{{"id",Id},{"summary",Title.Trim()},{"location",Location??""},{"description",Description??""},{"start",start},{"end",end},{"reminders",reminders}};
 }
}
public partial class GoogleService {
 public bool CanWrite {get{return (token.Scope??"").Split(' ').Contains("https://www.googleapis.com/auth/calendar.events");}}
 public async Task<CalendarEvent> CreateEventAsync(CalendarInfo cal,EventDraft draft,CancellationToken ct){
  if(!CanWrite)throw new Exception("请先允许新增日程。");if(cal==null||!cal.Writable)throw new Exception("请选择有写入权限的日历。");
  string body=Store.Json.Serialize(draft.Payload());string session=accountSession;
  string url="https://www.googleapis.com/calendar/v3/calendars/"+Uri.EscapeDataString(cal.Id)+"/events";
  Dictionary<string,object> data=null;
  // The same client-generated ID is reused after an uncertain response, preventing duplicate creates.
  for(int attempt=0;attempt<2;attempt++){
   using(var request=new HttpRequestMessage(HttpMethod.Post,url)){
    request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",await Access(ct));request.Content=new StringContent(body,Encoding.UTF8,"application/json");
    using(var response=await http.SendAsync(request,ct)){
     string text=await response.Content.ReadAsStringAsync();if(session!=accountSession)throw new OperationCanceledException();
     if(response.StatusCode==HttpStatusCode.Unauthorized&&attempt==0){token.Expiry=0;continue;}
     if(response.StatusCode==HttpStatusCode.Unauthorized)throw new LoginRequiredException();
     if(response.StatusCode==HttpStatusCode.Conflict){responses.Clear();data=await Get(url+"/"+Uri.EscapeDataString(draft.Id),ct);break;}
     if(response.StatusCode==HttpStatusCode.Forbidden)throw new Exception("无法写入该日历，请检查写入授权和日历权限。");
     if(!response.IsSuccessStatusCode)throw new Exception("保存未确认（"+(int)response.StatusCode+"），请稍后重试。相同表单不会重复创建日程。");
     data=Store.Object(text);break;
    }
   }
  }
  if(data==null)throw new LoginRequiredException();responses.Clear();
  var a=Store.Map(data,"start");var b=Store.Map(data,"end");if(a==null||b==null)throw new Exception("保存结果不完整，请重试确认。");bool all=Store.Text(a,"date")!="";
  return new CalendarEvent{Id=Store.Text(data,"id"),Etag=Store.Text(data,"etag"),RecurringId=Store.Text(data,"recurringEventId"),CalendarId=cal.Id,CalendarName=cal.Name,Color=cal.Color,Title=Store.Text(data,"summary"),Location=Store.Text(data,"location"),Description=Store.Text(data,"description"),Link=Store.Text(data,"htmlLink"),AllDay=all,Start=Store.Text(a,all?"date":"dateTime"),End=Store.Text(b,all?"date":"dateTime")};
 }
}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DeskCalendar {
class VersionMismatchException:Exception {public VersionMismatchException(string message):base(message){} }
class TaskMissingException:Exception {public TaskMissingException():base("这条待办已删除或无法访问。"){} }
class ResourceConflictException:Exception {
 public TodoItem TaskItem;public CalendarEvent EventItem;
 public ResourceConflictException(TodoItem item):base("这条待办的同一内容已有变更，已自动更新列表。请核对后重试。"){TaskItem=item;}
 public ResourceConflictException(CalendarEvent item):base("这条日程的同一内容已有变更，已自动更新日历。请核对后重试。"){EventItem=item;}
}
public partial class GoogleService {
 public async Task<CalendarEvent> ReadSeriesAsync(CalendarInfo calendar,CalendarEvent occurrence,CancellationToken ct){
  if(!CanWrite||calendar==null||!calendar.Writable||occurrence==null||calendar.Id!=occurrence.CalendarId||string.IsNullOrEmpty(occurrence.RecurringId))
   throw new Exception("无法编辑此周期日程的整个系列。");
  string session=accountSession;
  var master=await FreshEvent(calendar,new CalendarEvent{Id=occurrence.RecurringId,Color=occurrence.Color},ct);
  if(session!=accountSession)throw new OperationCanceledException();
  return master;
 }
 static Dictionary<string,object> TaskValues(TodoItem item){return new Dictionary<string,object>{{"title",item.Title??""},{"notes",item.Notes??""},{"due",item.Date.HasValue?item.Date.Value.ToString("yyyy-MM-dd"):""},{"status",item.Completed?"completed":"needsAction"}};}
 static string TaskValue(string key,object raw){string value=raw==null?"":Convert.ToString(raw);return key=="due"&&value.Length>=10?value.Substring(0,10):value;}
 static Dictionary<string,object> TaskChanges(TodoItem original,Dictionary<string,object> fields){var result=new Dictionary<string,object>();var before=TaskValues(original);foreach(var pair in fields){if(pair.Key=="completed")continue;object value;if(!before.TryGetValue(pair.Key,out value)||TaskValue(pair.Key,value)!=TaskValue(pair.Key,pair.Value))result[pair.Key]=pair.Value;}if(result.ContainsKey("status")&&fields.ContainsKey("completed"))result["completed"]=fields["completed"];return result;}
 static Dictionary<string,object> RebaseTask(TodoItem original,TodoItem fresh,Dictionary<string,object> patch){var before=TaskValues(original);var now=TaskValues(fresh);var result=new Dictionary<string,object>();foreach(var pair in patch){if(pair.Key=="completed")continue;string oldValue=TaskValue(pair.Key,before.ContainsKey(pair.Key)?before[pair.Key]:null),newValue=TaskValue(pair.Key,now.ContainsKey(pair.Key)?now[pair.Key]:null),wanted=TaskValue(pair.Key,pair.Value);if(newValue==wanted)continue;if(newValue!=oldValue)throw new ResourceConflictException(fresh);result[pair.Key]=pair.Value;}if(result.ContainsKey("status")&&patch.ContainsKey("completed"))result["completed"]=patch["completed"];return result;}
 async Task<TodoItem> FreshTask(string path,string list,string id,CancellationToken ct){var data=await TaskRequest(path,HttpMethod.Get,null,null,ct);var fresh=TodoItem.Parse(data,list);if(fresh.Id!=id||string.IsNullOrEmpty(fresh.Etag))throw new Exception("无法确认待办最新版本，请稍后重试。");return fresh;}
 public async Task<TodoItem> SaveTaskAsync(string list,TodoItem original,Dictionary<string,object> fields,CancellationToken ct){
  string path="lists/"+Uri.EscapeDataString(list)+"/tasks";
  if(original==null)return TodoItem.Parse(await TaskRequest(path,HttpMethod.Post,fields,null,ct),list);
  path+="/"+Uri.EscapeDataString(original.Id);var patch=TaskChanges(original,fields);if(patch.Count==0)return await FreshTask(path,list,original.Id,ct);
  string session=accountSession;var current=original;
  for(int attempt=0;attempt<2;attempt++){
   if(string.IsNullOrEmpty(current.Etag)){current=await FreshTask(path,list,original.Id,ct);if(session!=accountSession)throw new OperationCanceledException();patch=RebaseTask(original,current,patch);if(patch.Count==0)return current;}
   try{return TodoItem.Parse(await TaskRequest(path,new HttpMethod("PATCH"),patch,current.Etag,ct),list);}
   catch(VersionMismatchException){}
   current=await FreshTask(path,list,original.Id,ct);if(session!=accountSession)throw new OperationCanceledException();patch=RebaseTask(original,current,patch);if(patch.Count==0)return current;if(attempt==1)throw new ResourceConflictException(current);
  }
  throw new ResourceConflictException(current);
 }
 static string EventValue(CalendarEvent item,string key){switch(key){case "summary":return item.Title??"";case "location":return item.Location??"";case "description":return item.Description??"";case "start":case "end":return EventDateValue(item.AllDay,key=="start"?item.Start:item.End);default:return "unknown";}}
 public async Task DeleteTaskAsync(TodoItem item,CancellationToken ct){
  string session=accountSession,path="lists/"+Uri.EscapeDataString(item.ListId)+"/tasks/"+Uri.EscapeDataString(item.Id);var current=item;
  for(int attempt=0;attempt<2;attempt++){
  if(string.IsNullOrEmpty(current.Etag)){try{current=await FreshTask(path,item.ListId,item.Id,ct);}catch(TaskMissingException){return;}if(session!=accountSession)throw new OperationCanceledException();if(TaskValues(item).Any(p=>TaskValue(p.Key,p.Value)!=TaskValue(p.Key,TaskValues(current)[p.Key])))throw new ResourceConflictException(current);}
  try{await TaskRequest(path,HttpMethod.Delete,null,current.Etag,ct);return;}
  catch(VersionMismatchException){}
  try{current=await FreshTask(path,item.ListId,item.Id,ct);}catch(TaskMissingException){return;}if(session!=accountSession)throw new OperationCanceledException();if(attempt==1||TaskValues(item).Any(p=>TaskValue(p.Key,p.Value)!=TaskValue(p.Key,TaskValues(current)[p.Key])))throw new ResourceConflictException(current);}
 }
 static string EventDateValue(bool allDay,string value){DateTimeOffset time;if(allDay)return "date:"+value;return DateTimeOffset.TryParse(value,out time)?"time:"+time.UtcDateTime.Ticks:"time:"+value;}
 static string EventPatchValue(string key,object value){if(key!="start"&&key!="end")return value==null?"":Convert.ToString(value);var data=(Dictionary<string,object>)value;string date=Store.Text(data,"date");return EventDateValue(date!="",date!=""?date:Store.Text(data,"dateTime"));}
 async Task<CalendarEvent> FreshEvent(CalendarInfo cal,CalendarEvent original,CancellationToken ct){string url="https://www.googleapis.com/calendar/v3/calendars/"+Uri.EscapeDataString(cal.Id)+"/events/"+Uri.EscapeDataString(original.Id);var data=await Get(url,ct,true);if(Store.Text(data,"id")!=original.Id||Store.Map(data,"start")==null||Store.Map(data,"end")==null||string.IsNullOrEmpty(Store.Text(data,"etag")))throw new Exception("无法确认日程最新版本，请稍后重试。");var a=Store.Map(data,"start");var b=Store.Map(data,"end");bool all=Store.Text(a,"date")!="";return new CalendarEvent{Id=original.Id,Etag=Store.Text(data,"etag"),RecurringId=Store.Text(data,"recurringEventId"),StartZone=Store.Text(a,"timeZone"),EndZone=Store.Text(b,"timeZone"),CalendarId=cal.Id,CalendarName=cal.Name,Color=original.Color,Title=Store.Text(data,"summary"),Location=Store.Text(data,"location"),Description=Store.Text(data,"description"),Link=Store.Text(data,"htmlLink"),AllDay=all,Start=Store.Text(a,all?"date":"dateTime"),End=Store.Text(b,all?"date":"dateTime")};}
 public async Task<CalendarEvent> EditEventAsync(CalendarInfo cal,CalendarEvent original,EventDraft draft,bool keepReminders,CancellationToken ct){
  if(!CanWrite||cal==null||!cal.Writable||original==null||original.CalendarId!=cal.Id||string.IsNullOrEmpty(original.Id))throw new Exception("无权编辑该日历的日程。");
  var patch=EditPayload(original,draft,keepReminders);if(patch.Count==0)return original;string session=accountSession;var current=original;
  for(int attempt=0;attempt<2;attempt++){
   if(string.IsNullOrEmpty(current.Etag)){current=await FreshEvent(cal,original,ct);if(session!=accountSession)throw new OperationCanceledException();patch=RebaseEvent(original,current,patch);if(patch.Count==0)return current;}
   try{return await WriteEventPatch(cal,original,patch,current.Etag,ct);}
   catch(VersionMismatchException){}
   responses.Clear();current=await FreshEvent(cal,original,ct);if(session!=accountSession)throw new OperationCanceledException();patch=RebaseEvent(original,current,patch);if(patch.Count==0)return current;if(attempt==1)throw new ResourceConflictException(current);
  }
  throw new ResourceConflictException(current);
 }
 static Dictionary<string,object> RebaseEvent(CalendarEvent original,CalendarEvent fresh,Dictionary<string,object> patch){var result=new Dictionary<string,object>();foreach(var pair in patch){if(pair.Key=="reminders")throw new ResourceConflictException(fresh);string now=EventValue(fresh,pair.Key),before=EventValue(original,pair.Key),wanted=EventPatchValue(pair.Key,pair.Value);if(now==wanted)continue;if(now!=before)throw new ResourceConflictException(fresh);result[pair.Key]=pair.Value;}return result;}
}
}

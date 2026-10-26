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
public class TaskListInfo { public string Id; public string Title {get;set;} public override string ToString(){return Title;} }
public class TodoItem {
 public string Id,ListId,Title,Notes,Due,Etag,CompletedAt,WebViewLink; public bool Completed;
 public DateTimeOffset? CompletionTime {get{DateTimeOffset value;return DateTimeOffset.TryParse(CompletedAt,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out value)?value:(DateTimeOffset?)null;}}
 public bool RecentlyCompleted(DateTimeOffset now){return Completed&&CompletionTime.HasValue&&CompletionTime.Value>=now.AddDays(-7);}
 public string DateCaption(DateTime today){var date=Completed?(CompletionTime.HasValue?CompletionTime.Value.LocalDateTime:(DateTime?)null):Date;if(!date.HasValue)return Completed?"完成时间未知":"未安排日期";return (Completed?"完成于 ":"")+date.Value.ToString(date.Value.Year<today.Year?"yyyy年M月d日":"M月d日",CultureInfo.InvariantCulture);}
 public DateTime? Date {get {DateTime value;return !string.IsNullOrEmpty(Due)&&DateTime.TryParseExact(Due.Substring(0,Math.Min(10,Due.Length)),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out value)?value.Date:(DateTime?)null;}}
 public static TodoItem Parse(Dictionary<string,object> d,string list){return new TodoItem{Id=Store.Text(d,"id"),ListId=list,Title=Store.Text(d,"title"),Notes=Store.Text(d,"notes"),Due=Store.Text(d,"due"),Etag=Store.Text(d,"etag"),CompletedAt=Store.Text(d,"completed"),WebViewLink=Store.Text(d,"webViewLink"),Completed=Store.Text(d,"status")=="completed"};}
 public static Dictionary<string,object> Fields(string title,string notes,DateTime? date){if(string.IsNullOrWhiteSpace(title))throw new Exception("请填写待办标题。");if(title.Trim().Length>1024)throw new Exception("标题过长。");return new Dictionary<string,object>{{"title",title.Trim()},{"notes",notes??""},{"due",date.HasValue?date.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)+"T00:00:00.000Z":null}};}
}
public class TodoCache { public List<TaskListInfo> Lists=new List<TaskListInfo>(); public List<TodoItem> Items=new List<TodoItem>(); }
public partial class GoogleService {
 internal const string TasksScope="https://www.googleapis.com/auth/tasks";
 public bool CanTasks {get{return (token.Scope??"").Split(' ').Contains(TasksScope);}}
 const string TasksRoot="https://tasks.googleapis.com/tasks/v1/";
 async Task<Dictionary<string,object>> TaskRequest(string path,HttpMethod method,Dictionary<string,object> payload,string etag,CancellationToken ct){
  if(!CanTasks)throw new Exception("请先授权 Google Tasks。");string session=accountSession;
  for(int attempt=0;attempt<2;attempt++)using(var request=new HttpRequestMessage(method,TasksRoot+path)){
   request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",await Access(ct));
   if(!string.IsNullOrEmpty(etag))request.Headers.TryAddWithoutValidation("If-Match",etag);
   if(payload!=null)request.Content=new StringContent(Store.Json.Serialize(payload),Encoding.UTF8,"application/json");
   using(var response=await http.SendAsync(request,ct)){
    var body=await response.Content.ReadAsStringAsync();if(session!=accountSession)throw new OperationCanceledException();
    if(response.StatusCode==HttpStatusCode.Unauthorized&&attempt==0){token.Expiry=0;continue;}
    if(response.StatusCode==HttpStatusCode.Unauthorized)throw new LoginRequiredException();
    if(response.StatusCode==HttpStatusCode.PreconditionFailed)throw new Exception("此待办已在其他设备修改，请刷新后重试。");
    if(method==HttpMethod.Delete&&(response.StatusCode==HttpStatusCode.NotFound||response.StatusCode==HttpStatusCode.Gone))return new Dictionary<string,object>();
    if((int)response.StatusCode==429)throw new RetryException(TimeSpan.FromMinutes(5));
    if(response.StatusCode==HttpStatusCode.Forbidden)throw new Exception("无法访问待办，请确认 Google Cloud 已启用 Tasks API，且账号已授予 Tasks 权限。");
    if(!response.IsSuccessStatusCode)throw new Exception("待办请求失败（"+(int)response.StatusCode+"）。请刷新后确认结果。");
    return string.IsNullOrWhiteSpace(body)?new Dictionary<string,object>():Store.Object(body);
   }
  }
  throw new LoginRequiredException();
 }
 public async Task<TodoCache> ReadTasksAsync(CancellationToken ct){
  var result=new TodoCache();string page="";
  do{var d=await TaskRequest("users/@me/lists?maxResults=100"+page,HttpMethod.Get,null,null,ct);foreach(var x in Store.Items(d))result.Lists.Add(new TaskListInfo{Id=Store.Text(x,"id"),Title=Store.Text(x,"title")});string next=Store.Text(d,"nextPageToken");page=next==""?"":"&pageToken="+Uri.EscapeDataString(next);}while(page!="");
  foreach(var list in result.Lists){page="";do{var d=await TaskRequest("lists/"+Uri.EscapeDataString(list.Id)+"/tasks?maxResults=100&showCompleted=true&showHidden=true&showDeleted=false"+page,HttpMethod.Get,null,null,ct);foreach(var x in Store.Items(d))if(Store.Text(x,"deleted")!="True"&&Store.Text(x,"deleted")!="true")result.Items.Add(TodoItem.Parse(x,list.Id));string next=Store.Text(d,"nextPageToken");page=next==""?"":"&pageToken="+Uri.EscapeDataString(next);}while(page!="");}
  return result;
 }
 public async Task<TodoItem> SaveTaskAsync(string list,TodoItem original,Dictionary<string,object> fields,CancellationToken ct){var d=await TaskRequest("lists/"+Uri.EscapeDataString(list)+"/tasks"+(original==null?"":"/"+Uri.EscapeDataString(original.Id)),original==null?HttpMethod.Post:new HttpMethod("PATCH"),fields,original==null?null:original.Etag,ct);return TodoItem.Parse(d,list);}
 public async Task DeleteTaskAsync(TodoItem item,CancellationToken ct){await TaskRequest("lists/"+Uri.EscapeDataString(item.ListId)+"/tasks/"+Uri.EscapeDataString(item.Id),HttpMethod.Delete,null,item.Etag,ct);}
}
}

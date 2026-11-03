using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DeskCalendar {
public static partial class SelfTests {
 static void ConflictRecoveryChecks(string path){
  var store=new Store(path);store.Write("tokens.bin",new TokenData{Access="test",Scope=GoogleService.Scopes,Expiry=DateTime.UtcNow.AddHours(1).Ticks},true);
  var original=new TodoItem{Id="item",ListId="list",Title="原任务",Notes="原备注",Etag="\"v1\""};
  using(var handler=new RecoveryHandler())using(var google=new GoogleService(store,handler)){
   handler.Data["notes"]="其他设备的备注";
   var saved=google.SaveTaskAsync("list",original,TodoItem.Fields("新任务","原备注",null),CancellationToken.None).GetAwaiter().GetResult();
   Check(handler.Writes==2&&handler.Reads==1&&saved.Title=="新任务"&&saved.Notes=="其他设备的备注"&&handler.LastPatch.Count==1&&handler.LastPatch.ContainsKey("title"),"待办版本变化自动读取并重试，只改标题，不覆盖其他设备备注");
  }
  using(var handler=new RecoveryHandler())using(var google=new GoogleService(store,handler)){
   handler.Data["title"]="其他设备的任务名";ResourceConflictException conflict=null;try{google.SaveTaskAsync("list",original,TodoItem.Fields("新任务","原备注",null),CancellationToken.None).GetAwaiter().GetResult();}catch(ResourceConflictException ex){conflict=ex;}
   Check(conflict!=null&&conflict.TaskItem.Title=="其他设备的任务名"&&handler.Writes==1,"待办同字段真正冲突不覆盖，携带最新对象供界面自动更新");
   var saved=google.SaveTaskAsync("list",conflict.TaskItem,TodoItem.Fields("核对后保存","原备注",null),CancellationToken.None).GetAwaiter().GetResult();Check(saved.Title=="核对后保存"&&handler.Writes==2,"真正冲突后以最新对象再次保存即可完成，无需手动刷新");
  }
  using(var handler=new RecoveryHandler())using(var google=new GoogleService(store,handler)){
   handler.Data["status"]="completed";var result=google.SaveTaskAsync("list",original,new Dictionary<string,object>{{"status","completed"}},CancellationToken.None).GetAwaiter().GetResult();
   Check(result.Completed&&handler.Writes==1&&handler.Reads==1,"其他设备已完成同一待办时直接接受最新结果，不重复写入");
  }
  using(var handler=new RecoveryHandler())using(var google=new GoogleService(store,handler)){
   google.DeleteTaskAsync(original,CancellationToken.None).GetAwaiter().GetResult();Check(handler.Writes==2&&handler.Reads==1,"待办仅版本变化但内容不变时，删除自动更新版本并有条件重试");
  }
  using(var handler=new RecoveryHandler{AlwaysConflict=true})using(var google=new GoogleService(store,handler)){
   bool failed=false;try{google.SaveTaskAsync("list",original,TodoItem.Fields("新任务","原备注",null),CancellationToken.None).GetAwaiter().GetResult();}catch(ResourceConflictException){failed=true;}
   Check(failed&&handler.Writes==2&&handler.Reads==2,"持续变化最多尝试两次写入，避免无限重试和重复副作用");
  }
  using(var handler=new RecoveryHandler())using(var google=new GoogleService(store,handler)){
   handler.Data["title"]="新任务";var result=google.SaveTaskAsync("list",original,TodoItem.Fields("新任务","原备注",null),CancellationToken.None).GetAwaiter().GetResult();Check(result.Title=="新任务"&&handler.Writes==1,"标题已是目标值时无需重复写入");
  }
  using(var handler=new RecoveryHandler())using(var google=new GoogleService(store,handler)){
   handler.Data["notes"]="远程新备注";var saved=google.SaveTaskAsync("list",original,TodoItem.Fields(original.Title,original.Notes,null),CancellationToken.None).GetAwaiter().GetResult();Check(handler.Writes==0&&saved.Notes=="远程新备注","没有修改时只刷新该待办，不发送无意义 PATCH 或覆盖远程内容");
  }
  using(var handler=new RecoveryHandler())using(var google=new GoogleService(store,handler)){
   var unversioned=new TodoItem{Id="item",ListId="list",Title=original.Title,Notes=original.Notes};google.DeleteTaskAsync(unversioned,CancellationToken.None).GetAwaiter().GetResult();Check(handler.Reads==1&&handler.Writes==1,"缺少版本的旧待办先读取再带版本删除，不执行无条件写入");
  }
  var ev=EditableFixture();var cal=new CalendarInfo{Id=ev.CalendarId,Name="工作",AccessRole="owner"};
  var draft=new EventDraft{Title="新日程",Location=ev.Location,Description=ev.Description,Start=ev.StartLocal.AddSeconds(-ev.StartLocal.Second),End=ev.EndLocal.AddSeconds(-ev.EndLocal.Second)};
  using(var handler=new RecoveryHandler(ev))using(var google=new GoogleService(store,handler)){
   handler.Data["description"]="远程备注";var result=google.EditEventAsync(cal,ev,draft,true,CancellationToken.None).GetAwaiter().GetResult();Check(result.Title=="新日程"&&result.Description=="远程备注"&&handler.Writes==2&&handler.Reads==1&&!handler.ConditionalRead,"日程自动读取未经缓存的最新版本，仅合并本次字段改动");
  }
  using(var handler=new RecoveryHandler(ev))using(var google=new GoogleService(store,handler)){
   handler.Data["summary"]="远程日程名";bool failed=false;try{google.EditEventAsync(cal,ev,draft,true,CancellationToken.None).GetAwaiter().GetResult();}catch(ResourceConflictException ex){failed=ex.EventItem.Title=="远程日程名";}Check(failed&&handler.Writes==1,"日程同字段冲突保留远程内容并返回最新事件");
  }
  using(var handler=new RecoveryHandler(ev))using(var google=new GoogleService(store,handler)){
   var old=EditableFixture();old.Etag=null;var result=google.EditEventAsync(cal,old,draft,true,CancellationToken.None).GetAwaiter().GetResult();Check(result.Title=="新日程"&&handler.Reads==1&&handler.Writes==1,"旧缓存缺少版本时先自动获取最新版本，再有条件保存");
  }
 }
}
class RecoveryHandler:HttpMessageHandler {
 public Dictionary<string,object> Data;public Dictionary<string,object> LastPatch;public int Reads,Writes;public bool AlwaysConflict,ConditionalRead;
 public RecoveryHandler(){Data=new Dictionary<string,object>{{"id","item"},{"etag","\"v2\""},{"title","原任务"},{"notes","原备注"},{"status","needsAction"}};}
 public RecoveryHandler(CalendarEvent ev){Data=new Dictionary<string,object>{{"id",ev.Id},{"etag","\"v2\""},{"summary",ev.Title},{"location",ev.Location},{"description",ev.Description},{"start",new Dictionary<string,object>{{"dateTime",ev.Start}}},{"end",new Dictionary<string,object>{{"dateTime",ev.End}}}};}
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
  if(request.Method==HttpMethod.Get){Reads++;ConditionalRead=request.Headers.Contains("If-None-Match");return Reply(HttpStatusCode.OK,Data);}
  Writes++;if(!request.Headers.Contains("If-Match"))throw new Exception("Mutation must remain conditional");string etag=request.Headers.GetValues("If-Match").Single();
  if(AlwaysConflict||etag!=Store.Text(Data,"etag"))return Reply(HttpStatusCode.PreconditionFailed,new Dictionary<string,object>());
  if(request.Method==HttpMethod.Delete)return Reply(HttpStatusCode.NoContent,new Dictionary<string,object>());
  LastPatch=Store.Object(await request.Content.ReadAsStringAsync());foreach(var pair in LastPatch)Data[pair.Key]=pair.Value;Data["etag"]="\"v3\"";return Reply(HttpStatusCode.OK,Data);
 }
 static HttpResponseMessage Reply(HttpStatusCode status,Dictionary<string,object> data){return new HttpResponseMessage(status){Content=new StringContent(Store.Json.Serialize(data))};}
}
}

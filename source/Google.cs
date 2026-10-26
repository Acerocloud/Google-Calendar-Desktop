using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DeskCalendar {
public class LoginRequiredException:Exception { public LoginRequiredException():base("登录已过期，请重新登录。"){} }
public class RetryException:Exception { public TimeSpan Delay;public RetryException(TimeSpan d):base("Google 暂时限制了请求，请稍后重试。") {Delay=d;} }
public partial class GoogleService:IDisposable {
 internal const string Scopes="https://www.googleapis.com/auth/calendar.events https://www.googleapis.com/auth/calendar.calendarlist.readonly https://www.googleapis.com/auth/tasks";
 readonly Store store;
 readonly HttpClient http;
 TokenData token;
 Credentials client;
 string accountSession=Guid.NewGuid().ToString("N");
 Dictionary<string,string> eventColors=new Dictionary<string,string>();
 class SavedResponse {public string Etag;public Dictionary<string,object> Data;}
 readonly Dictionary<string,SavedResponse> responses=new Dictionary<string,SavedResponse>();
 public bool Connected {get{return !string.IsNullOrEmpty(token.Refresh)||(!string.IsNullOrEmpty(token.Access)&&token.Expiry>DateTime.UtcNow.Ticks);}}
 public bool Configured {get{return !string.IsNullOrEmpty(client.ClientId);}}
 public GoogleService(Store s):this(s,null){}
 public GoogleService(Store s,HttpMessageHandler handler){store=s;http=handler==null?new HttpClient():new HttpClient(handler);http.Timeout=TimeSpan.FromSeconds(35);client=s.Read<Credentials>("client.bin",true);if(string.IsNullOrEmpty(client.ClientId)){using(var resource=typeof(GoogleService).Assembly.GetManifestResourceStream("DeskCalendar.OAuthClient")){if(resource!=null)using(var reader=new StreamReader(resource))client=ParseClient(reader.ReadToEnd());}}token=s.Read<TokenData>("tokens.bin",true);ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;}
 public void Import(string path){
  var c=ParseClient(File.ReadAllText(path));
  if(Connected)throw new Exception("更换客户端前，请先断开当前账号。 ");
  store.Write("client.bin",c,true);client=c;
 }
 static Credentials ParseClient(string json){
  var root=Store.Object(json);var installed=Store.Map(root,"installed");
  if(installed==null)throw new Exception("请选择 Google Cloud 的“桌面应用”OAuth 客户端 JSON，不支持 Web 客户端。 ");
  var c=new Credentials{ClientId=Store.Text(installed,"client_id"),ClientSecret=Store.Text(installed,"client_secret")};
  if(!c.ClientId.EndsWith(".apps.googleusercontent.com",StringComparison.Ordinal))throw new Exception("客户端 ID 格式不正确。 ");
  return c;
 }
 static string B64(byte[] b){return Convert.ToBase64String(b).TrimEnd('=').Replace('+','-').Replace('/','_');}
 static string RandomString(){byte[] b=new byte[32];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(b);return B64(b);}
 static Dictionary<string,string> Query(string s){var d=new Dictionary<string,string>();foreach(string item in s.TrimStart('?').Split('&')){var a=item.Split(new[]{'='},2);if(a.Length==2)d[Uri.UnescapeDataString(a[0])]=Uri.UnescapeDataString(a[1].Replace("+"," "));}return d;}
 public async Task LoginAsync(CancellationToken cancel){
  if(!Configured)throw new Exception("应用缺少 Google 登录配置，请使用完整安装包。 ");
  string session=accountSession, verifier=RandomString(),state=RandomString(),challenge;
  using(var sha=SHA256.Create())challenge=B64(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
  var listener=new TcpListener(IPAddress.Loopback,0);listener.Start(4);
  int port=((IPEndPoint)listener.LocalEndpoint).Port;
  string callback="http://127.0.0.1:"+port+"/oauth2/callback";
  string url="https://accounts.google.com/o/oauth2/v2/auth?client_id="+Uri.EscapeDataString(client.ClientId)+"&redirect_uri="+Uri.EscapeDataString(callback)+"&response_type=code&scope="+Uri.EscapeDataString(Scopes)+"&access_type=offline&prompt=consent&code_challenge_method=S256&code_challenge="+challenge+"&state="+state;
  using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel)){
   timeout.CancelAfter(TimeSpan.FromMinutes(3));
   using(timeout.Token.Register(()=>listener.Stop())){
    try {
     Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
     string code=null;
     while(code==null){
      TcpClient connection=await listener.AcceptTcpClientAsync();
      using(connection){using(var stream=connection.GetStream()){
       byte[] buffer=new byte[8192];int size=0;
       while(size<buffer.Length){var read=stream.ReadAsync(buffer,size,buffer.Length-size,timeout.Token);var winner=await Task.WhenAny(read,Task.Delay(5000,timeout.Token));if(winner!=read)break;int n=await read;if(n==0)break;size+=n;if(Encoding.ASCII.GetString(buffer,0,size).Contains("\r\n\r\n"))break;}
       string request=Encoding.ASCII.GetString(buffer,0,size);string first=request.Split('\n')[0].Trim();var parts=first.Split(' ');Dictionary<string,string> values=null;
       if(parts.Length>=2&&parts[0]=="GET") {Uri uri;if(Uri.TryCreate("http://127.0.0.1"+parts[1],UriKind.Absolute,out uri)&&uri.AbsolutePath=="/oauth2/callback")values=Query(uri.Query);}
       string returned,err;bool valid=values!=null&&values.TryGetValue("state",out returned)&&returned==state;
       string message=valid?"已收到授权结果。请返回日历应用。":"无效的授权回调。请返回日历重新登录。";
       byte[] body=Encoding.UTF8.GetBytes("<!doctype html><meta charset=utf-8><title>日历</title><p>"+message+"</p>");
       byte[] header=Encoding.ASCII.GetBytes("HTTP/1.1 "+(valid?"200 OK":"400 Bad Request")+"\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: "+body.Length+"\r\nCache-Control: no-store\r\nContent-Security-Policy: default-src 'none'\r\nConnection: close\r\n\r\n");
       await stream.WriteAsync(header,0,header.Length,timeout.Token);await stream.WriteAsync(body,0,body.Length,timeout.Token);
       if(!valid)continue;
       if(values.TryGetValue("error",out err))throw new Exception("授权未完成。你可以稍后重新登录。 ");
       values.TryGetValue("code",out code);
      }}
     }
     var data=new Dictionary<string,string>{{"client_id",client.ClientId},{"code",code},{"code_verifier",verifier},{"redirect_uri",callback},{"grant_type","authorization_code"}};
     if(!string.IsNullOrEmpty(client.ClientSecret))data["client_secret"]=client.ClientSecret;
     var result=await TokenRequest(data,timeout.Token);if(session!=accountSession)throw new OperationCanceledException();
     string granted=Store.Text(result,"scope");if(!granted.Split(' ').Contains("https://www.googleapis.com/auth/calendar.events")||!granted.Split(' ').Contains("https://www.googleapis.com/auth/calendar.calendarlist.readonly"))throw new Exception("未授予日程读写或日历列表权限，请重新登录并勾选两项权限。原有登录已保留。");
     token=new TokenData();responses.Clear();ApplyToken(result);store.Write("tokens.bin",token,true);
    }catch(ObjectDisposedException){throw new OperationCanceledException("登录已取消或超时。");}
     catch(SocketException){if(timeout.IsCancellationRequested)throw new OperationCanceledException("登录已取消或超时。");throw;}
    finally{listener.Stop();}
   }
  }
 }
 async Task<Dictionary<string,object>> TokenRequest(Dictionary<string,string> data,CancellationToken ct){
  using(var body=new FormUrlEncodedContent(data))using(var response=await http.PostAsync("https://oauth2.googleapis.com/token",body,ct)){
   string text=await response.Content.ReadAsStringAsync();
   if(!response.IsSuccessStatusCode){if(response.StatusCode==HttpStatusCode.BadRequest&&text.Contains("invalid_grant"))throw new LoginRequiredException();throw new Exception("Google 登录请求失败（"+(int)response.StatusCode+"）。请检查客户端设置、网络和系统时间。 ");}
   return Store.Object(text);
  }
 }
 void ApplyToken(Dictionary<string,object> data){string scope=Store.Text(data,"scope");if(scope!="")token.Scope=scope;token.Access=Store.Text(data,"access_token");string refresh=Store.Text(data,"refresh_token");if(refresh!="")token.Refresh=refresh;int seconds;if(!int.TryParse(Store.Text(data,"expires_in"),out seconds))seconds=3600;token.Expiry=DateTime.UtcNow.AddSeconds(seconds-60).Ticks;}
 async Task<string> Access(CancellationToken ct){
  if(!string.IsNullOrEmpty(token.Access)&&token.Expiry>DateTime.UtcNow.Ticks)return token.Access;
  if(string.IsNullOrEmpty(token.Refresh))throw new LoginRequiredException();
  string session=accountSession;
  var d=new Dictionary<string,string>{{"client_id",client.ClientId},{"refresh_token",token.Refresh},{"grant_type","refresh_token"}};
  if(!string.IsNullOrEmpty(client.ClientSecret))d["client_secret"]=client.ClientSecret;
  var result=await TokenRequest(d,ct);if(session!=accountSession)throw new OperationCanceledException();ApplyToken(result);store.Write("tokens.bin",token,true);return token.Access;
 }
 async Task<Dictionary<string,object>> Get(string url,CancellationToken ct){
  for(int attempt=0;attempt<2;attempt++){
   string access=await Access(ct);
   using(var request=new HttpRequestMessage(HttpMethod.Get,url)){
    request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",access);
    SavedResponse saved;responses.TryGetValue(url,out saved);if(saved!=null)request.Headers.TryAddWithoutValidation("If-None-Match",saved.Etag);
    using(var response=await http.SendAsync(request,ct)){
     if(response.StatusCode==HttpStatusCode.NotModified&&saved!=null)return saved.Data;
     string text=await response.Content.ReadAsStringAsync();
     if(response.StatusCode==HttpStatusCode.Unauthorized&&attempt==0){token.Expiry=0;continue;}
     if(response.StatusCode==HttpStatusCode.Unauthorized)throw new LoginRequiredException();
     if((int)response.StatusCode==429||(response.StatusCode==HttpStatusCode.Forbidden&&(text.Contains("rateLimitExceeded")||text.Contains("userRateLimitExceeded")))){
      TimeSpan delay=TimeSpan.FromMinutes(5);if(response.Headers.RetryAfter!=null){if(response.Headers.RetryAfter.Delta.HasValue)delay=response.Headers.RetryAfter.Delta.Value;else if(response.Headers.RetryAfter.Date.HasValue)delay=response.Headers.RetryAfter.Date.Value-DateTimeOffset.UtcNow;}throw new RetryException(delay<TimeSpan.FromSeconds(30)?TimeSpan.FromSeconds(30):delay);
     }
     if(!response.IsSuccessStatusCode)throw new Exception("读取日历失败（"+(int)response.StatusCode+"）。请检查 Calendar API 是否启用及账号权限。 ");
     var parsed=Store.Object(text);if(response.Headers.ETag!=null&&Store.Text(parsed,"nextPageToken")==""&&!url.Contains("pageToken=")){if(responses.Count>=16)responses.Clear();responses[url]=new SavedResponse{Etag=response.Headers.ETag.ToString(),Data=parsed};}return parsed;
    }
   }
  }
  throw new LoginRequiredException();
 }
 public async Task<List<CalendarInfo>> CalendarsAsync(CancellationToken ct){
  var list=new List<CalendarInfo>();string page="";
  do {var d=await Get("https://www.googleapis.com/calendar/v3/users/me/calendarList?maxResults=250"+(page==""?"":"&pageToken="+Uri.EscapeDataString(page)),ct);
   foreach(var x in Store.Items(d))list.Add(new CalendarInfo{Id=Store.Text(x,"id"),Name=Store.Text(x,"summary"),Color=Store.Text(x,"backgroundColor"),AccessRole=Store.Text(x,"accessRole"),Primary=Store.Text(x,"primary").ToLowerInvariant()=="true"});page=Store.Text(d,"nextPageToken");
  }while(page!="");
  return list;
 }
 public async Task ColorsAsync(CancellationToken ct){var d=await Get("https://www.googleapis.com/calendar/v3/colors",ct);var map=Store.Map(d,"event");if(map==null)return;var result=new Dictionary<string,string>();foreach(var item in map){var v=item.Value as Dictionary<string,object>;if(v!=null)result[item.Key]=Store.Text(v,"background");}eventColors=result;}
 public async Task<List<CalendarEvent>> EventsAsync(CalendarInfo cal,DateTime start,DateTime end,CancellationToken ct){
  var result=new List<CalendarEvent>();string page="";
  string url="https://www.googleapis.com/calendar/v3/calendars/"+Uri.EscapeDataString(cal.Id)+"/events?singleEvents=true&orderBy=startTime&maxResults=2500&timeMin="+Uri.EscapeDataString(new DateTimeOffset(start).ToString("o"))+"&timeMax="+Uri.EscapeDataString(new DateTimeOffset(end).ToString("o"))+"&fields="+Uri.EscapeDataString("items(id,etag,recurringEventId,status,summary,location,description,htmlLink,colorId,start,end),nextPageToken");
  do {var data=await Get(url+(page==""?"":"&pageToken="+Uri.EscapeDataString(page)),ct);
   foreach(var x in Store.Items(data)){
    if(Store.Text(x,"status")=="cancelled")continue;var a=Store.Map(x,"start");var b=Store.Map(x,"end");if(a==null||b==null)continue;
    bool all=Store.Text(a,"date")!="";string color; if(!eventColors.TryGetValue(Store.Text(x,"colorId"),out color))color=cal.Color;
    var e=new CalendarEvent{Id=Store.Text(x,"id"),Etag=Store.Text(x,"etag"),RecurringId=Store.Text(x,"recurringEventId"),CalendarId=cal.Id,CalendarName=cal.Name,Color=color,Title=Store.Text(x,"summary"),Location=Store.Text(x,"location"),Description=Store.Text(x,"description"),Link=Store.Text(x,"htmlLink"),AllDay=all,Start=Store.Text(a,all?"date":"dateTime"),End=Store.Text(b,all?"date":"dateTime")};
    if(e.Title=="")e.Title="忙碌";
    if(e.ValidDuration)result.Add(e);
   }
   page=Store.Text(data,"nextPageToken");
  }while(page!="");
  return result.GroupBy(e=>e.Id).Select(g=>g.First()).ToList();
 }
 public void Clear(){accountSession=Guid.NewGuid().ToString("N");responses.Clear();token=new TokenData();store.Delete("tokens.bin");}
 public async Task RevokeAsync(string refresh){if(string.IsNullOrEmpty(refresh))return;try{using(var b=new FormUrlEncodedContent(new Dictionary<string,string>{{"token",refresh}}))using(var r=await http.PostAsync("https://oauth2.googleapis.com/revoke",b)){} }catch(HttpRequestException){}catch(TaskCanceledException){} }
 public string RefreshToken {get{return token.Refresh;}}
 public void Dispose(){http.Dispose();}
}
}

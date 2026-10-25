using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DeskCalendar {
public static class UpdateTests {
 static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
 static string Release(string hash,bool manifest,bool pre=false,bool wrong=false){
  var assets=new List<object>{new{name="DeskCalendar-Setup-99.0.0-x64.exe",size=4,browser_download_url="https://github.com/"+(wrong?"other/repo":UpdateService.Repository)+"/releases/download/v99.0.0/DeskCalendar-Setup-99.0.0-x64.exe",digest=hash==null?null:"sha256:"+hash}};
  if(manifest)assets.Add(new{name="SHA256SUMS-99.0.0.txt",browser_download_url="https://github.com/"+UpdateService.Repository+"/releases/download/v99.0.0/SHA256SUMS-99.0.0.txt"});
  return Store.Json.Serialize(new{tag_name="v99.0.0",draft=false,prerelease=pre,body="Update fixture",assets=assets});
 }
 public static void Run(Action<bool,string> check,string directory){
  Directory.CreateDirectory(directory);byte[] data=Encoding.ASCII.GetBytes("TEST");string hash=Hash(data);
  check(UpdateService.ParseVersion("v1.0.14")==new Version(1,0,14,0)&&UpdateService.ParseVersion("bad")==null,"更新版本解析和四段版本归一化");
  check(UpdateService.Interval==TimeSpan.FromHours(6),"自动更新检查间隔为六小时");
  check(UpdateService.ParseRelease(Release(hash,false),new Version(100,0,0,0))==null,"不提示旧版本或降级");
  check(UpdateService.ParseRelease(Release(hash,false,true),new Version(1,0,0,0))==null,"跳过预发布版本");
  var release=UpdateService.ParseRelease(Release(hash,false),new Version(1,0,0,0));check(release!=null&&release.Hash==hash&&release.Size==4,"选择 x64 安装包并读取 GitHub digest");
  bool rejected=false;try{UpdateService.ParseRelease(Release(hash,false,false,true),new Version(1,0,0,0));}catch{rejected=true;}check(rejected,"拒绝其他仓库的安装包");
  rejected=false;try{UpdateService.ParseRelease(Release(null,false),new Version(1,0,0,0));}catch{rejected=true;}check(rejected,"缺少校验信息时拒绝更新");
  rejected=false;try{UpdateService.ValidateAssetUrl("http://github.com/"+UpdateService.Repository+"/releases/download/a/b");}catch{rejected=true;}check(rejected,"拒绝非 HTTPS 更新地址");
  check(UpdateService.FindHash(hash+"  fixture.exe\r\n","fixture.exe")==hash,"解析 SHA256SUMS 校验清单");
  var handler=new UpdateFixtureHandler{Metadata=Release(hash,false),Data=data};
  using(var service=new UpdateService(handler)){
   var available=service.CheckAsync(CancellationToken.None).GetAwaiter().GetResult();check(available!=null&&handler.Requests==1,"自动检查只请求元数据，不下载安装包");
   string path=service.DownloadAsync(available,Path.Combine(directory,"ok"),null,CancellationToken.None).GetAwaiter().GetResult();check(UpdateService.Verify(path,hash)&&!File.Exists(path+".partial"),"下载完成并通过哈希后原子转为安装包");
   check(!UpdateService.Verify(path,new string('0',64)),"错误哈希不能通过校验");
  }
  handler=new UpdateFixtureHandler{Metadata=Release(null,true),Data=data,Manifest=hash+"  DeskCalendar-Setup-99.0.0-x64.exe"};
  using(var service=new UpdateService(handler)){
   var available=service.CheckAsync(CancellationToken.None).GetAwaiter().GetResult();string path=service.DownloadAsync(available,Path.Combine(directory,"manifest"),null,CancellationToken.None).GetAwaiter().GetResult();check(UpdateService.Verify(path,hash)&&handler.Requests==3,"无 digest 时下载校验清单再校验安装包");
  }
  handler=new UpdateFixtureHandler{Metadata=Release(new string('0',64),false),Data=data};
  using(var service=new UpdateService(handler)){
   var available=service.CheckAsync(CancellationToken.None).GetAwaiter().GetResult();rejected=false;try{service.DownloadAsync(available,Path.Combine(directory,"bad-hash"),null,CancellationToken.None).GetAwaiter().GetResult();}catch{rejected=true;}
   check(rejected&&!Directory.GetFiles(Path.Combine(directory,"bad-hash")).Any(),"校验失败删除残缺文件，不生成可执行安装包");
  }
  handler=new UpdateFixtureHandler{Metadata=Release(hash,false),Data=new byte[]{1,2}};
  using(var service=new UpdateService(handler)){
   var available=service.CheckAsync(CancellationToken.None).GetAwaiter().GetResult();rejected=false;try{service.DownloadAsync(available,Path.Combine(directory,"truncated"),null,CancellationToken.None).GetAwaiter().GetResult();}catch{rejected=true;}
   check(rejected,"拒绝不完整安装包");
  }
  handler=new UpdateFixtureHandler{Redirect="https://example.com/payload.exe"};
  using(var service=new UpdateService(handler)){rejected=false;try{service.CheckAsync(CancellationToken.None).GetAwaiter().GetResult();}catch{rejected=true;}check(rejected&&handler.Requests==1,"不访问白名单以外的更新重定向");}
  handler=new UpdateFixtureHandler{Metadata=Release(hash,false),Data=data};
  using(var service=new UpdateService(handler)){
   var cancel=new CancellationTokenSource();cancel.Cancel();rejected=false;try{service.CheckAsync(cancel.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){rejected=true;}check(rejected,"更新请求支持退出和取消");cancel.Dispose();
  }
 }
 sealed class UpdateFixtureHandler:HttpMessageHandler {
  public string Metadata,Manifest,Redirect;public byte[] Data;public int Requests;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
   ct.ThrowIfCancellationRequested();Requests++;if(Redirect!=null){var redirect=new HttpResponseMessage(HttpStatusCode.Redirect);redirect.Headers.Location=new Uri(Redirect);return Task.FromResult(redirect);}
   var content=request.RequestUri.Host=="api.github.com"?new StringContent(Metadata):request.RequestUri.AbsolutePath.EndsWith(".txt")?new StringContent(Manifest):(HttpContent)new ByteArrayContent(Data);
   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content});
  }
 }
}
}

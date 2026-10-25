using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace DeskCalendar {
public sealed class ReleaseUpdate {
 public Version Version;public string Tag,Name,Notes,InstallerUrl,Hash,ChecksumUrl;public long Size;
}
public sealed class UpdateService:IDisposable {
 public const string Repository="Acerocloud/Google-Calendar-Desktop";
 public const string Endpoint="https://api.github.com/repos/"+Repository+"/releases/latest";
 public static readonly TimeSpan Interval=TimeSpan.FromHours(6);
 public static readonly Version Current=Assembly.GetExecutingAssembly().GetName().Version;
 public const long MaxInstallerBytes=100*1024*1024;
 readonly HttpClient http;
 public UpdateService():this(new HttpClientHandler{AllowAutoRedirect=false}){}
 public UpdateService(HttpMessageHandler handler){http=new HttpClient(handler);http.Timeout=TimeSpan.FromMinutes(10);http.DefaultRequestHeaders.UserAgent.ParseAdd("DeskCalendar/"+Current);http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;}
 public static Version ParseVersion(string value){Version v;string s=(value??"").Trim();if(s.StartsWith("v",StringComparison.OrdinalIgnoreCase))s=s.Substring(1);if(!Version.TryParse(s,out v)||v.Major<0||v.Minor<0)return null;return new Version(v.Major,v.Minor,Math.Max(0,v.Build),Math.Max(0,v.Revision));}
 static bool Flag(Dictionary<string,object> data,string key){object value;return data.TryGetValue(key,out value)&&value is bool&&(bool)value;}
 public static ReleaseUpdate ParseRelease(string json,Version current){
  var root=Store.Object(json);if(Flag(root,"draft")||Flag(root,"prerelease"))return null;
  string tag=Store.Text(root,"tag_name");Version version=ParseVersion(tag);if(version==null||version<=ParseVersion(current.ToString()))return null;
  string suffix=tag.StartsWith("v",StringComparison.OrdinalIgnoreCase)?tag.Substring(1):tag;
  string expected="DeskCalendar-Setup-"+suffix+"-x64.exe";object raw;var assets=root.TryGetValue("assets",out raw)?raw as IEnumerable:null;if(assets==null)return null;
  ReleaseUpdate update=null;string checksum=null;
  foreach(object value in assets){var asset=value as Dictionary<string,object>;if(asset==null)continue;string name=Store.Text(asset,"name"),url=Store.Text(asset,"browser_download_url");
   if(name=="SHA256SUMS-"+suffix+".txt"||name=="SHA256SUMS.txt"){ValidateAssetUrl(url);checksum=url;}
   if(name!=expected)continue;ValidateAssetUrl(url);long size;if(!long.TryParse(Store.Text(asset,"size"),out size)||size<=0||size>MaxInstallerBytes)throw new Exception("安装包大小无效。");
   string digest=Store.Text(asset,"digest"),hash=digest.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase)?digest.Substring(7):null;
   if(hash!=null&&!Regex.IsMatch(hash,"\\A[0-9a-fA-F]{64}\\z"))throw new Exception("更新校验信息无效。");
   update=new ReleaseUpdate{Version=version,Tag=tag,Name=name,InstallerUrl=url,Hash=hash,Size=size,Notes=Store.Text(root,"body")};
  }
  if(update==null)return null;update.ChecksumUrl=checksum;if(update.Hash==null&&checksum==null)throw new Exception("该版本缺少 SHA256 校验文件，无法安全更新。");return update;
 }
 public static void ValidateAssetUrl(string value){Uri uri;if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||uri.Scheme!="https"||!uri.IsDefaultPort||uri.Host!="github.com"||!uri.AbsolutePath.StartsWith("/"+Repository+"/releases/download/",StringComparison.Ordinal)||!string.IsNullOrEmpty(uri.UserInfo))throw new Exception("更新下载地址不属于本项目。");}
 static void ValidateTransport(Uri uri){string host=uri.Host;if(uri.Scheme!="https"||!uri.IsDefaultPort||!string.IsNullOrEmpty(uri.UserInfo)||!(host=="github.com"||host=="api.github.com"||host=="release-assets.githubusercontent.com"||host=="objects.githubusercontent.com"))throw new Exception("更新重定向地址不受支持。");}
 async Task<HttpResponseMessage> Get(string url,CancellationToken ct){
  Uri uri=new Uri(url);for(int redirects=0;redirects<6;redirects++){ValidateTransport(uri);var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,ct);int status=(int)response.StatusCode;
   if(status>=300&&status<=399){Uri location=response.Headers.Location;response.Dispose();if(location==null)throw new Exception("更新下载重定向无效。");uri=location.IsAbsoluteUri?location:new Uri(uri,location);continue;}
   if(response.StatusCode==HttpStatusCode.NotFound){response.Dispose();throw new Exception("尚未发布可用的更新版本。");}
   if(status==403||status==429){response.Dispose();throw new Exception("GitHub 暂时限制请求，请稍后再试。");}
   if(!response.IsSuccessStatusCode){response.Dispose();throw new Exception("更新服务器暂时不可用。");}return response;
  }throw new Exception("更新重定向次数过多。");
 }
 async Task<string> Text(string url,int limit,CancellationToken ct){using(var response=await Get(url,ct))using(var stream=await response.Content.ReadAsStreamAsync())using(var buffer=new MemoryStream()){byte[] chunk=new byte[8192];int count;while((count=await stream.ReadAsync(chunk,0,chunk.Length,ct))>0){if(buffer.Length+count>limit)throw new Exception("更新信息过大。");buffer.Write(chunk,0,count);}return Encoding.UTF8.GetString(buffer.ToArray());}}
 public async Task<ReleaseUpdate> CheckAsync(CancellationToken ct){return ParseRelease(await Text(Endpoint,1024*1024,ct),Current);}
 public static string FindHash(string manifest,string name){foreach(string line in manifest.Split('\n')){var match=Regex.Match(line.Trim().TrimStart('\uFEFF'),"\\A([0-9a-fA-F]{64})\\s+\\*?(.+)\\z");if(match.Success&&match.Groups[2].Value==name)return match.Groups[1].Value.ToLowerInvariant();}throw new Exception("校验文件中找不到安装包。");}
 public static bool Verify(string path,string expected){if(!Regex.IsMatch(expected??"","\\A[0-9a-fA-F]{64}\\z"))return false;using(var sha=SHA256.Create())using(var stream=File.OpenRead(path)){string actual=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");return string.Equals(actual,expected,StringComparison.OrdinalIgnoreCase);}}
 public async Task<string> DownloadAsync(ReleaseUpdate update,string directory,IProgress<int> progress,CancellationToken ct){
  ValidateAssetUrl(update.InstallerUrl);if(update.Hash==null){ValidateAssetUrl(update.ChecksumUrl);update.Hash=FindHash(await Text(update.ChecksumUrl,128*1024,ct),update.Name);}
  if(Path.GetFileName(update.Name)!=update.Name||!update.Name.EndsWith("-x64.exe",StringComparison.Ordinal))throw new Exception("安装包文件名无效。");Directory.CreateDirectory(directory);string path=Path.Combine(directory,update.Name),partial=path+".partial";
  try{
   using(var response=await Get(update.InstallerUrl,ct))using(var input=await response.Content.ReadAsStreamAsync())using(var output=new FileStream(partial,FileMode.Create,FileAccess.Write,FileShare.None,65536,true)){long total=0;int count,last=-1;byte[] chunk=new byte[65536];while((count=await input.ReadAsync(chunk,0,chunk.Length,ct))>0){total+=count;if(total>MaxInstallerBytes||total>update.Size)throw new Exception("安装包大小与发布信息不符。");await output.WriteAsync(chunk,0,count,ct);int percent=(int)(total*100/update.Size);if(percent!=last){last=percent;if(progress!=null)progress.Report(percent);}}if(total!=update.Size)throw new Exception("安装包下载不完整。");}
   ct.ThrowIfCancellationRequested();if(!Verify(partial,update.Hash))throw new Exception("安装包校验失败，请稍后重试。");if(File.Exists(path))File.Delete(path);File.Move(partial,path);return path;
  }finally{if(File.Exists(partial))File.Delete(partial);}
 }
 public static string Quote(string path){if(path==null||path.IndexOf('"')>=0)throw new Exception("更新路径无效。");return "\""+path+"\"";}
 public static void LaunchHelper(string installer,string hash){
  if(!Verify(installer,hash))throw new Exception("安装包校验失败。");string directory=Path.GetDirectoryName(installer),original=Assembly.GetExecutingAssembly().Location,helper=Path.Combine(directory,"DeskCalendar.UpdateHelper.exe");File.Copy(original,helper,true);if(File.Exists(original+".config"))File.Copy(original+".config",helper+".config",true);
  var start=new ProcessStartInfo(helper,"--apply-update "+Process.GetCurrentProcess().Id+" "+Quote(installer)+" "+hash+" "+Quote(original)){UseShellExecute=false,CreateNoWindow=true};var process=Process.Start(start);if(process==null)throw new Exception("无法启动更新程序。");process.Dispose();
 }
 public static int ApplyUpdate(string[] args){
  string old=args.Length>4?args[4]:null;
  try{
   int parent;if(args.Length!=5||!int.TryParse(args[1],out parent)||!Verify(args[2],args[3]))throw new Exception("更新参数或安装包校验无效。");
   try{using(var process=Process.GetProcessById(parent)){if(!process.WaitForExit(45000))throw new Exception("日历尚未退出，请退出后重新更新。");}}catch(ArgumentException){}
   using(var process=Process.Start(new ProcessStartInfo(args[2],"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /UPDATE=1"){UseShellExecute=false,CreateNoWindow=true})){process.WaitForExit();if(process.ExitCode!=0)throw new Exception("安装未完成（"+process.ExitCode+"）。");}
   string installed=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","DeskCalendar","DeskCalendar.exe");
   using(var key=Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{C95D2893-0C50-4D15-8D45-7306843A0427}_is1")){if(key!=null){string location=key.GetValue("InstallLocation") as string;if(!string.IsNullOrEmpty(location))installed=Path.Combine(location,"DeskCalendar.exe");}}
   if(!File.Exists(installed))throw new Exception("安装后找不到日历程序。");Process.Start(new ProcessStartInfo(installed){UseShellExecute=true});return 0;
  }catch(Exception ex){System.Windows.MessageBox.Show("更新未完成："+ex.Message+"\n原有账号和设置未清除。","日历更新");if(old!=null&&File.Exists(old))try{Process.Start(new ProcessStartInfo(old){UseShellExecute=true});}catch{}return 1;}
 }
 public void Dispose(){http.Dispose();}
}
}

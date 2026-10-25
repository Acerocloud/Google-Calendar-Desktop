using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace DeskCalendar {
public class Preferences {
 public double TimelineZoom=1;
 public double Left=double.NaN, Top=double.NaN, Width=760, Height=650;
 public string View="month", Theme="dark";
 public bool Pinned=false, Locked=false, Startup=false, Glass=true;
 public int Tint=56;
 public List<string> Selected=new List<string>();
 public bool SelectionSaved=false;
}
public class CalendarInfo { public string Id, Name, Color, AccessRole; public bool Primary; public bool Writable {get{return AccessRole=="owner"||AccessRole=="writer";}} }
public class CalendarEvent {
 public string Etag,RecurringId; public string Id, CalendarId, CalendarName, Color, Title, Location, Description, Link;
 public string Start, End;
 public bool AllDay;
 string parsedStart,parsedEnd;bool parsedAllDay,datesReady;TimeZoneInfo parsedZone;DateTime startLocal,endLocal;DateTimeOffset startOffset,endOffset;
 void EnsureDates(){
  var zone=TimeZoneInfo.Local;if(datesReady&&parsedStart==Start&&parsedEnd==End&&parsedAllDay==AllDay&&ReferenceEquals(parsedZone,zone))return;
  if(AllDay){startLocal=DateTime.ParseExact(Start,"yyyy-MM-dd",CultureInfo.InvariantCulture);endLocal=DateTime.ParseExact(End,"yyyy-MM-dd",CultureInfo.InvariantCulture);}
  else{startOffset=DateTimeOffset.Parse(Start,CultureInfo.InvariantCulture);endOffset=DateTimeOffset.Parse(End,CultureInfo.InvariantCulture);startLocal=startOffset.LocalDateTime;endLocal=endOffset.LocalDateTime;}
  parsedStart=Start;parsedEnd=End;parsedAllDay=AllDay;parsedZone=zone;datesReady=true;
 }
 public DateTime StartLocal {get{EnsureDates();return startLocal;}}
 public DateTime EndLocal {get{EnsureDates();return endLocal;}}
 public bool IsPoint {get{EnsureDates();return !AllDay&&endOffset==startOffset;}}
 public bool ValidDuration {get{EnsureDates();return AllDay?endLocal>startLocal:endOffset>=startOffset;}}
 public double DurationMinutes {get{EnsureDates();return AllDay?(endLocal-startLocal).TotalMinutes:(endOffset-startOffset).TotalMinutes;}}
 public bool InRange(DateTime start,DateTime end) {return IsPoint?StartLocal>=start&&StartLocal<end:StartLocal<end&&EndLocal>start;}
 public bool OnDay(DateTime day) { return InRange(day.Date,day.Date.AddDays(1)); }
 public bool Ongoing(DateTime now) { return !AllDay && StartLocal<=now && EndLocal>now; }
}
public class CacheData {
 public List<CalendarInfo> Calendars=new List<CalendarInfo>();
 public List<CalendarEvent> Events=new List<CalendarEvent>();
 public Dictionary<string,string> Updated=new Dictionary<string,string>();
 public string RangeStart, RangeEnd;
}
public class Credentials { public string ClientId, ClientSecret; }
public class TokenData { public string Access, Refresh, Scope; public long Expiry; }

public class Store {
 public readonly string DirectoryPath;
 static readonly byte[] Entropy=Encoding.UTF8.GetBytes("DeskCalendar.local.v1");
 public static readonly JavaScriptSerializer Json=new JavaScriptSerializer { MaxJsonLength=16*1024*1024, RecursionLimit=100 };
 public Store(string path) { DirectoryPath=path; Directory.CreateDirectory(path); }
 public T Read<T>(string file, bool encrypted) where T:class,new() {
  string path=Path.Combine(DirectoryPath,file);
  if(!File.Exists(path))return new T();
  byte[] bytes=File.ReadAllBytes(path);
  if(encrypted)bytes=ProtectedData.Unprotect(bytes,Entropy,DataProtectionScope.CurrentUser);
  return Json.Deserialize<T>(Encoding.UTF8.GetString(bytes)) ?? new T();
 }
 public void Write<T>(string file,T value,bool encrypted) {
  byte[] bytes=Encoding.UTF8.GetBytes(Json.Serialize(value));
  if(encrypted)bytes=ProtectedData.Protect(bytes,Entropy,DataProtectionScope.CurrentUser);
  string path=Path.Combine(DirectoryPath,file),tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try { File.WriteAllBytes(tmp,bytes);if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path); }
  finally { if(File.Exists(tmp))File.Delete(tmp); }
 }
 public void Delete(string file){string p=Path.Combine(DirectoryPath,file);if(File.Exists(p))File.Delete(p);}
 public static Dictionary<string,object> Object(string text){return Json.Deserialize<Dictionary<string,object>>(text);}
 public static string Text(Dictionary<string,object> o,string key){object v;return o!=null&&o.TryGetValue(key,out v)&&v!=null?Convert.ToString(v,CultureInfo.InvariantCulture):"";}
 public static Dictionary<string,object> Map(Dictionary<string,object> o,string key){object v;return o.TryGetValue(key,out v)?v as Dictionary<string,object>:null;}
 public static IEnumerable<Dictionary<string,object>> Items(Dictionary<string,object> o){object a;if(!o.TryGetValue("items",out a)||!(a is System.Collections.IEnumerable))return Enumerable.Empty<Dictionary<string,object>>();return ((System.Collections.IEnumerable)a).Cast<object>().OfType<Dictionary<string,object>>();}
}

public static class CalendarMath {
 public static DateTime Monday(DateTime d){return d.Date.AddDays(-(((int)d.DayOfWeek+6)%7));}
 public static DateTime Quarter(DateTime d){return new DateTime(d.Year,((d.Month-1)/3)*3+1,1);}
 public static DateTime Month(DateTime d){return new DateTime(d.Year,d.Month,1);}
 public static List<CalendarEvent> ForDay(IEnumerable<CalendarEvent> events,DateTime day){return events.Where(e=>e.OnDay(day)).OrderBy(e=>!e.AllDay).ThenBy(e=>e.StartLocal).ToList();}
 public static double Clip(double value,double low,double high){return Math.Max(low,Math.Min(high,value));}
 public static string TimeText(CalendarEvent e,DateTime day){
  if(e.AllDay)return "全天";
  if(e.IsPoint)return e.StartLocal.ToString("HH:mm");
  string start=e.StartLocal.Date<day.Date?"前日":e.StartLocal.ToString("HH:mm");
  string end=e.EndLocal.Date>day.Date?"次日 "+e.EndLocal.ToString("HH:mm"):e.EndLocal.ToString("HH:mm");
  return start+"–"+end;
 }
 public static double PollMinutes(bool visible,bool battery,int failures){return Math.Min(60,Math.Max(visible?(battery?15:5):30, failures==0?0:Math.Pow(2,Math.Min(6,failures))));}
 public static DateTime Navigation(DateTime d,string view,int delta){return view=="week"?d.AddDays(7*delta):view=="quarter"?d.AddMonths(3*delta):d.AddMonths(delta);}
 public static CacheData Demo(){
  CacheData c=new CacheData();var cal=new CalendarInfo{Id="demo",Name="示例",Color="#82A8CD",Primary=true};c.Calendars.Add(cal);
  DateTime today=DateTime.Today;
  for(int i=-3;i<8;i++){DateTime d=today.AddDays(i);c.Events.Add(new CalendarEvent{Id="demo"+i,CalendarId="demo",CalendarName="示例",Title=i==0?"设计评审":"项目会议",Location="会议室",Color=cal.Color,Start=new DateTimeOffset(d.AddHours(14)).ToString("o"),End=new DateTimeOffset(d.AddHours(15)).ToString("o"),Link="https://calendar.google.com/",Description="此事件用于预览，不来自真实账号。"});}
  c.Events.Add(new CalendarEvent{Id="all",CalendarId="demo",CalendarName="示例",Title="休息",Color="#95B5A1",AllDay=true,Start=today.AddDays(1).ToString("yyyy-MM-dd"),End=today.AddDays(3).ToString("yyyy-MM-dd")});return c;
 }
}
}

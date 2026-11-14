using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskCalendar {
public enum RepeatUnit { Day, Week, Month, Quarter, Year }
public partial class EventDraft {
 [DllImport("icu.dll", CallingConvention=CallingConvention.Cdecl, CharSet=CharSet.Unicode, ExactSpelling=true)]
 static extern int ucal_getTimeZoneIDForWindowsID(string windowsId,int length,IntPtr region,StringBuilder id,int capacity,ref int status);

 void AddRepeatRule(Dictionary<string,object> payload,Dictionary<string,object> start,Dictionary<string,object> end){
  DateTime through=RepeatThrough.Value.Date;
  if(through<Start.Date)throw new Exception("周期结束日期不能早于周期开始日期。");
  if(RepeatInterval<1||RepeatInterval>99)throw new Exception("周期间隔请选择 1 到 99。");
  string frequency;
  int interval=RepeatInterval;
  switch(RepeatEvery){
   case RepeatUnit.Day: frequency="DAILY";break;
   case RepeatUnit.Week: frequency="WEEKLY";break;
   case RepeatUnit.Month: frequency="MONTHLY";break;
   case RepeatUnit.Quarter: frequency="MONTHLY";interval*=3;break;
   case RepeatUnit.Year: frequency="YEARLY";break;
   default: throw new Exception("请选择有效的周期单位。");
  }
  string until=through.ToString("yyyyMMdd",CultureInfo.InvariantCulture);
  if(!AllDay){
   var zone=TimeZoneInfo.Local;
   var zoneId=new StringBuilder(128);
   int status=0;
   try{ucal_getTimeZoneIDForWindowsID(zone.Id,zone.Id.Length,IntPtr.Zero,zoneId,zoneId.Capacity,ref status);}
   catch(DllNotFoundException){status=1;}
   catch(EntryPointNotFoundException){status=1;}
   if(status>0||zoneId.Length==0)throw new Exception("无法识别 Windows 时区，请检查时区设置后重试。");
   start["timeZone"]=end["timeZone"]=zoneId.ToString();
   DateTime cutoff=through.AddHours(23).AddMinutes(59).AddSeconds(59);
   until=new DateTimeOffset(cutoff,zone.GetUtcOffset(cutoff)).UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'",CultureInfo.InvariantCulture);
  }
  string rule="RRULE:FREQ="+frequency+";INTERVAL="+interval.ToString(CultureInfo.InvariantCulture);
  if(RepeatEvery==RepeatUnit.Week)rule+=";BYDAY="+new[]{"SU","MO","TU","WE","TH","FR","SA"}[(int)Start.DayOfWeek];
  payload["recurrence"]=new[]{rule+";UNTIL="+until};
 }
}
}

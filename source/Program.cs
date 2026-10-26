using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace DeskCalendar {
public static class Program {
 [STAThread] public static int Main(string[] args){
  if(args.Length>0&&args[0]=="--apply-update")return UpdateService.ApplyUpdate(args);
  bool demo=args.Contains("--demo"),test=args.Contains("--self-test"),visual=args.Contains("--visual-qa");string output=Argument(args,"--render"),data=Argument(args,"--data"),benchmark=Argument(args,"--benchmark");
  if(Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetExecutingAssembly().Location).EndsWith(".VisualQA",StringComparison.Ordinal)){demo=true;visual=true;data=Path.Combine(Path.GetTempPath(),"DeskCalendar-VisualQA-v107");}
#if VISUAL_QA
  demo=true;visual=true;data=Path.Combine(Path.GetTempPath(),"DeskCalendar-VisualQA-v107c" );
#endif
#if LOGIN_QA
  demo=false;visual=true;data=Path.Combine(Path.GetTempPath(),"DeskCalendar-LoginQA-v107" );
#endif
  if(test&&string.IsNullOrEmpty(data))data=Path.Combine(Path.GetTempPath(),"DeskCalendar-tests-"+Guid.NewGuid().ToString("N"));
  if(string.IsNullOrEmpty(data))data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),demo?"DeskCalendar-Demo":"DeskCalendar");
  try {
   if(test){SelfTests.Run(data);return 0;}
   bool created;using(var mutex=new Mutex(true,"Local\\DeskCalendar-"+WindowsIdentity.GetCurrent().User.Value+(demo?"-demo":"")+(visual?"-visual-"+Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetExecutingAssembly().Location):""),out created)){
    if(!created){MessageBox.Show("日历已在运行，请双击右下角托盘图标显示窗口。","日历");return 0;}
    var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
    app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e){MessageBox.Show("操作未完成："+e.Exception.Message,"日历");e.Handled=true;};
    var window=new CalendarWindow(new Store(data),demo,output!=null||visual);if(visual)window.Title="日历 · 视觉验证";app.MainWindow=window;
    if(output!=null){window.ShowActivated=false;window.Loaded+=delegate{window.Dispatcher.BeginInvoke(new Action(()=>{try{window.RenderTo(output);}finally{window.Exit();}}),DispatcherPriority.ApplicationIdle);};}
    if(benchmark!=null){window.ShowActivated=false;window.Loaded+=async delegate{await Benchmark(window,benchmark);};}
#if VISUAL_QA
    var fixture=new Window{Title="背景测试 · 示例",Width=880,Height=760,WindowStartupLocation=WindowStartupLocation.CenterScreen};var pattern=new System.Windows.Controls.Primitives.UniformGrid{Columns=8,Rows=8};for(int p=0;p<64;p++)pattern.Children.Add(new System.Windows.Controls.Border{Background=new System.Windows.Media.SolidColorBrush(p%2==0?System.Windows.Media.Color.FromRgb(255,170,100):System.Windows.Media.Color.FromRgb(90,180,255))});fixture.Content=pattern;fixture.Show();
#endif
    if(output!=null||benchmark!=null||visual){window.Show();if(visual)window.ShowTasks();}else{window.StartInTray();if(demo)window.ShowCalendar();}app.Run();return 0;
   }
  }catch(Exception ex){
   string error="启动失败："+ex.Message;if(test||output!=null){Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"error.txt"),test?ex.ToString():error);return 1;}MessageBox.Show(error,"日历");return 1;
  }
 }
 static string Argument(string[] args,string name){int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?Path.GetFullPath(args[i+1]):null;}
 static async Task Benchmark(CalendarWindow window,string path){
  await Task.Delay(5000);var p=System.Diagnostics.Process.GetCurrentProcess();var measurements=new Dictionary<string,object>();
  foreach(string mode in new[]{"visible","hidden"}){if(mode=="hidden")window.Hide();p.Refresh();TimeSpan cpu=p.TotalProcessorTime;DateTime started=DateTime.UtcNow;await Task.Delay(20000);p.Refresh();double cpuPct=(p.TotalProcessorTime-cpu).TotalSeconds/(DateTime.UtcNow-started).TotalSeconds*100;measurements[mode]=new {seconds=(DateTime.UtcNow-started).TotalSeconds,cpuPercentOneCore=Math.Round(cpuPct,3),cpuPercentMachine=Math.Round(cpuPct/Environment.ProcessorCount,4),workingSetMB=Math.Round(p.WorkingSet64/1048576.0,2),privateMB=Math.Round(p.PrivateMemorySize64/1048576.0,2)};}
  measurements["logicalProcessors"]=Environment.ProcessorCount;measurements["os"]=Environment.OSVersion.Version.ToString();
  measurements["demo"]=window.DemoMode;measurements["note"]="示例数据的空闲测量，不包含真实账号网络同步、GPU 功耗和电池续航。";File.WriteAllText(path,Store.Json.Serialize(measurements));window.Exit();
 }
}

public static partial class SelfTests {
 static int count;static void Check(bool ok,string description){if(!ok)throw new Exception("测试失败："+description);count++;}
 public static void Run(string directory){
  if(File.Exists(Path.Combine(directory,"tokens.bin"))||File.Exists(Path.Combine(directory,"client.bin")))throw new Exception("测试目录中包含登录信息，请指定新的空目录。 ");
  var store=new Store(directory);DateTime d=new DateTime(2026,10,24);
  var all=new CalendarEvent{AllDay=true,Start="2026-10-24",End="2026-10-26",Title="跨天"};
  Check(all.OnDay(d)&&all.OnDay(d.AddDays(1))&&!all.OnDay(d.AddDays(2)),"全天结束日不包含在范围内");
  var overnight=new CalendarEvent{Start=new DateTimeOffset(d.AddHours(23)).ToString("o"),End=new DateTimeOffset(d.AddDays(1).AddHours(1)).ToString("o")};
  Check(overnight.OnDay(d)&&overnight.OnDay(d.AddDays(1))&&!overnight.OnDay(d.AddDays(2)),"跨午夜日期分段");
  var dst=new CalendarEvent{Start="2026-11-01T01:30:00-04:00",End="2026-11-01T01:30:00-05:00"};Check(dst.ValidDuration&&dst.DurationMinutes==60,"夏令时回拨同一钟点仍是有效一小时事件");
  var point=new CalendarEvent{Start=new DateTimeOffset(d.AddHours(14)).ToString("o"),End=new DateTimeOffset(d.AddHours(14)).ToString("o")};
  Check(point.ValidDuration&&point.IsPoint&&CalendarMath.ForDay(new[]{point},d).Count==1,"开始结束相同的时间点事件不被过滤");
  var midnightPoint=new CalendarEvent{Start=new DateTimeOffset(d).ToString("o"),End=new DateTimeOffset(d).ToString("o")};
  Check(midnightPoint.OnDay(d)&&!midnightPoint.OnDay(d.AddDays(-1))&&midnightPoint.InRange(d,d.AddDays(1)),"零时长午夜事件归属及快照替换范围");
  Check(CalendarMath.TimeText(point,d)=="14:00"&&point.DurationMinutes==0,"时间点显示起始时刻且不改原始时长");
  var pointLayout=Overlaps.Arrange(new[]{point},d);Check(pointLayout.Count==1&&pointLayout[0].End>pointLayout[0].Start,"周视图时间点具有可点击占位");
  Check(CalendarMath.Monday(new DateTime(2027,1,1))==new DateTime(2026,12,28),"跨年周起点");
  Check(CalendarMath.Quarter(new DateTime(2027,1,1))==new DateTime(2027,1,1),"自然季度起点");
  Check(CalendarMath.Navigation(new DateTime(2026,12,1),"quarter",1)==new DateTime(2027,3,1),"跨年季度导航");
  Check(CalendarMath.ForDay(new[]{overnight,all},d).First()==all,"全天优先排序");
  var a=new CalendarEvent{Start=new DateTimeOffset(d.AddHours(9)).ToString("o"),End=new DateTimeOffset(d.AddHours(11)).ToString("o")};
  var b=new CalendarEvent{Start=new DateTimeOffset(d.AddHours(10)).ToString("o"),End=new DateTimeOffset(d.AddHours(12)).ToString("o")};
  var c=new CalendarEvent{Start=new DateTimeOffset(d.AddHours(11)).ToString("o"),End=new DateTimeOffset(d.AddHours(13)).ToString("o")};
  var lanes=Overlaps.Arrange(new[]{a,b,c},d);Check(lanes.All(s=>s.Count==2)&&lanes[0].Lane==lanes[2].Lane,"重叠事件分列及列复用");
  Check(CalendarMath.PollMinutes(true,false,0)==5&&CalendarMath.PollMinutes(true,true,0)==15&&CalendarMath.PollMinutes(false,false,0)==30,"供电与可见性同步间隔");
  Check(CalendarMath.PollMinutes(true,false,6)==60,"网络错误退避");
  store.Write("test.bin",new TokenData{Refresh="test-secret"},true);Check(store.Read<TokenData>("test.bin",true).Refresh=="test-secret","DPAPI 当前用户保护可往返");
  Check(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory,"test.bin"))).Contains("test-secret"),"令牌不以明文存储");
  store.Write("test.bin",new TokenData{Refresh="updated-secret"},true);Check(store.Read<TokenData>("test.bin",true).Refresh=="updated-secret","原子替换现有文件");
  var cache=CalendarMath.Demo();store.Write("cache-test.bin",cache,true);var restored=store.Read<CacheData>("cache-test.bin",true);Check(restored.Events.Count==cache.Events.Count&&restored.Calendars.Count==1,"日程快照序列化");
  var pref=new Preferences{Left=100,Top=100,Selected=new List<string>{"a"},SelectionSaved=true};store.Write("test-settings.json",pref,false);Check(store.Read<Preferences>("test-settings.json",false).Selected[0]=="a","日历选择保存");
  using(var google=new GoogleService(store)){
   Check(google.Configured&&!google.Connected,"全新安装使用内置客户端，无需导入且尚未登录");
   string bad=Path.Combine(directory,"bad-client.json");File.WriteAllText(bad,"{\"web\":{\"client_id\":\"x\"}}");bool rejected=false;try{google.Import(bad);}catch{rejected=true;}Check(rejected,"拒绝 Web OAuth 客户端");
   string config=Path.Combine(directory,"test-client.json");File.WriteAllText(config,"{\"installed\":{\"client_id\":\"test.apps.googleusercontent.com\",\"client_secret\":\"test-only\"}}");google.Import(config);Check(google.Configured,"桌面客户端配置导入");google.Clear();Check(!google.Connected,"断开账号清除登录状态");
  }
  store.Write("tokens.bin",new TokenData{Access="test-token",Refresh="test-refresh",Expiry=DateTime.UtcNow.AddHours(1).Ticks},true);
  var handler=new MockCalendarHandler();using(var google=new GoogleService(store,handler)){
   var calendars=google.CalendarsAsync(CancellationToken.None).GetAwaiter().GetResult();Check(calendars.Count==1&&calendars[0].Primary,"Google 日历列表解析");
   google.ColorsAsync(CancellationToken.None).GetAwaiter().GetResult();google.ColorsAsync(CancellationToken.None).GetAwaiter().GetResult();Check(handler.NotModified==1,"ETag 条件请求复用未变更内容");
   var events=google.EventsAsync(calendars[0],d,d.AddDays(7),CancellationToken.None).GetAwaiter().GetResult();Check(events.Count==3&&events.Count(e=>e.AllDay)==1,"分页、取消事件过滤及实例去重");
   Check(events.Any(e=>e.Id=="point"&&e.IsPoint),"Google API 零时长事件保留到本地模型");
   Check(events.First(e=>!e.AllDay).Color=="#AABBCC","事件独立颜色优先于日历颜色");
   Check(handler.Authenticated,"Google 请求携带 Bearer 令牌");
   handler.FailSecondPage=true;bool partial=false;try{google.EventsAsync(calendars[0],d,d.AddDays(7),CancellationToken.None).GetAwaiter().GetResult();}catch(HttpRequestException){partial=true;}Check(partial,"分页失败不返回部分结果");
   handler.FailSecondPage=false;handler.Throttled=true;bool limited=false;try{google.CalendarsAsync(CancellationToken.None).GetAwaiter().GetResult();}catch(RetryException ex){limited=ex.Delay>=TimeSpan.FromMinutes(10);}Check(limited,"限流尊重 Retry-After");
   handler.Throttled=false;handler.UnauthorizedOnce=true;Check(google.CalendarsAsync(CancellationToken.None).GetAwaiter().GetResult().Count==1&&handler.TokenRequests==1,"401 后刷新令牌并重试原请求");
   Check(handler.LastTokenClientId=="test.apps.googleusercontent.com","升级保留本地客户端，刷新旧令牌时不切换客户端");
   google.Clear();Check(!File.Exists(Path.Combine(directory,"tokens.bin")),"断开后删除加密令牌文件");
  }
  store.Write("tokens.bin",new TokenData{Refresh="expired-test-refresh",Expiry=0},true);using(var google=new GoogleService(store,new MockCalendarHandler{InvalidGrant=true})){bool expired=false;try{google.CalendarsAsync(CancellationToken.None).GetAwaiter().GetResult();}catch(LoginRequiredException){expired=true;}Check(expired,"刷新令牌失效进入重新登录状态");google.Clear();}
  store.Delete("test.bin");store.Delete("cache-test.bin");store.Delete("client.bin");store.Delete("test-settings.json");store.Delete("test-client.json");store.Delete("bad-client.json");
  using(var google=new GoogleService(store)){Check(google.Configured&&!google.Connected,"清除本地配置后仍可用内置客户端重新登录");}
  store.Write("tokens.bin",new TokenData{Refresh="test-refresh",Expiry=0},true);
  var delayed=new MockCalendarHandler{HoldToken=true};using(var google=new GoogleService(store,delayed)){
   var pending=google.CalendarsAsync(CancellationToken.None);string refresh=google.RefreshToken;google.Clear();
   delayed.PendingToken.SetResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"access_token\":\"late-token\",\"expires_in\":3600}")});
   bool canceled=false;try{pending.GetAwaiter().GetResult();}catch(OperationCanceledException){canceled=true;}
   Check(canceled&&!google.Connected&&!File.Exists(Path.Combine(directory,"tokens.bin")),"断开后迟到的刷新响应不能恢复登录");
   google.RevokeAsync(refresh).GetAwaiter().GetResult();Check(delayed.Revoked&&!google.Connected,"撤销授权仅调用撤销接口并保持断开状态");
  }
  UpdateTests.Run(Check,Path.Combine(directory,"updates"));
  TasksChecks(Path.Combine(directory,"tasks"));CreateChecks(Path.Combine(directory,"create"));EditChecks(Path.Combine(directory,"edit"));
  WidgetChecks(Path.Combine(directory,"widget"));
  File.WriteAllText(Path.Combine(directory,"self-test.txt"),count+" checks passed\r\n"+DateTimeOffset.Now.ToString("o"));
 }
 static IEnumerable<DependencyObject> VisualNodes(DependencyObject root){yield return root;for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)foreach(var node in VisualNodes(System.Windows.Media.VisualTreeHelper.GetChild(root,i)))yield return node;}
 static void Pump(Window w){w.UpdateLayout();var frame=new DispatcherFrame();w.Dispatcher.BeginInvoke(new Action(()=>frame.Continue=false),DispatcherPriority.ApplicationIdle);Dispatcher.PushFrame(frame);w.UpdateLayout();}
 static IEnumerable<DependencyObject> Descendants(DependencyObject root){
  yield return root;foreach(object child in LogicalTreeHelper.GetChildren(root)){var d=child as DependencyObject;if(d!=null)foreach(var item in Descendants(d))yield return item;}
 }
 static void WidgetChecks(string directory){
  var app=Application.Current??new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var store=new Store(directory);
  store.Write("tokens.bin",new TokenData{Access="test-token",Expiry=DateTime.UtcNow.AddHours(1).Ticks},true);
  store.Write("calendar.bin",CalendarMath.Demo(),true);
  store.Write("settings.json",new Preferences{Pinned=true},false);
  var window=new CalendarWindow(store,false,true){ShowActivated=false};
  var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
  try{
   window.StartInTray();Check(!window.IsVisible,"启动后仅驻留托盘，不显示日历");
   window.ShowCalendar();Check(window.IsVisible,"点击托盘展开日历");CreateWidgetChecks(window);
   Check(!window.Topmost&&!((Preferences)typeof(CalendarWindow).GetField("prefs",flags).GetValue(window)).Pinned,"升级时清除旧置顶设置");
   window.TogglePopup();Check(!window.IsVisible,"再次点击托盘收起日历");window.TogglePopup();Check(window.IsVisible&&!window.Topmost,"收起后可重新展开，仍不置顶");
   Check(!Descendants(window).OfType<System.Windows.Controls.Button>().Any(b=>Convert.ToString(b.ToolTip).Contains("置顶")),"主界面不再提供置顶入口");
   Check(!window.ShowInTaskbar,"桌面组件不显示任务栏按钮");
   int styles=Native.GetWindowLong(new System.Windows.Interop.WindowInteropHelper(window).Handle,-20);
   Check((styles&0x80)!=0&&(styles&0x40000)==0,"原生工具窗口样式不进入应用切换列表");
   window.Close();Check(!window.IsVisible,"关闭请求收起弹窗但保留程序");window.ShowCalendar();
   window.WindowState=WindowState.Minimized;Check(!window.IsVisible&&window.WindowState==WindowState.Normal,"最小化请求收起弹窗");window.ShowCalendar();
   var bounds=PopupPlacement.Bounds(new System.Drawing.Rectangle(-1920,0,1920,1040),760,650,new System.Drawing.Point(-30,1060),8);
   Check(bounds.Left>=-1912&&bounds.Right<=-8&&bounds.Bottom==1032,"弹窗锚定任务栏上方并限制在负坐标显示器内");
   var small=PopupPlacement.Bounds(new System.Drawing.Rectangle(0,0,800,600),1200,900,new System.Drawing.Point(799,610),8);
   Check(small.Width==784&&small.Height==584&&small.Left==8&&small.Top==8,"小屏幕弹窗尺寸不会超出工作区");
   IntPtr hwnd=new System.Windows.Interop.WindowInteropHelper(window).Handle;
   bool glass=(bool)typeof(CalendarWindow).GetMethod("GlassEnabled",flags).Invoke(window,null);
   Check((Native.BackdropType(hwnd)==1||(glass&&Native.BackdropType(hwnd)==3)),"DWM 实际应用预期 Acrylic 或实色回退");
   var surface=(System.Windows.Controls.Border)typeof(CalendarWindow).GetField("surface",flags).GetValue(window);var before=((System.Windows.Media.SolidColorBrush)surface.Background).Color;CheckWindowCorners(window,surface,"主页");
   typeof(Window).GetMethod("OnDeactivated",flags).Invoke(window,new object[]{EventArgs.Empty});var after=((System.Windows.Media.SolidColorBrush)surface.Background).Color;
   Check(before==after,"失去焦点不改变背景透明度和颜色");
   Check(Native.SuppressBorder(hwnd),"系统接受主窗口额外边框禁用设置");
   int materialUpdates=window.MaterialUpdates;
   for(int focus=0;focus<12;focus++){typeof(Window).GetMethod("OnActivated",flags).Invoke(window,new object[]{EventArgs.Empty});typeof(Window).GetMethod("OnDeactivated",flags).Invoke(window,new object[]{EventArgs.Empty});}
   Check(window.MaterialUpdates==materialUpdates,"反复激活失焦不重新设置原生背景材质");
   typeof(CalendarWindow).GetMethod("ApplyMaterial",flags).Invoke(window,null);
   Check(window.MaterialUpdates==materialUpdates,"材质状态未变时不触发 DWM 重配置");
   int nativeStyle=Native.GetWindowLong(hwnd,-16),backdrop=Native.BackdropType(hwnd);window.SetLocked(true);Pump(window);
   Check(window.ResizeMode==ResizeMode.CanResize&&Native.GetWindowLong(hwnd,-16)==nativeStyle,"锁定不改变原生窗口样式");
   Check(Native.BackdropType(hwnd)==backdrop,"锁定后毛玻璃保持原状态");
   Check(System.Windows.Shell.WindowChrome.GetWindowChrome(window).ResizeBorderThickness==new Thickness(0),"锁定禁用边缘缩放");
   var lockIcon=Descendants(window).OfType<System.Windows.Controls.Button>().Single(b=>b.Name=="LockShortcut");
   Check(Convert.ToString(lockIcon.Content)=="\uE72E"&&System.Windows.Automation.AutomationProperties.GetItemStatus(lockIcon)=="已锁定","锁定快捷图标显示已锁定状态");
   Check(store.Read<Preferences>("settings.json",false).Locked,"锁定偏好已保存");
   window.SetLocked(false);Pump(window);Check(System.Windows.Shell.WindowChrome.GetWindowChrome(window).ResizeBorderThickness==new Thickness(5)&&Native.BackdropType(hwnd)==backdrop,"解锁恢复缩放且保持毛玻璃");
   Window detail=null;
   try{
    typeof(CalendarWindow).GetMethod("OpenEvent",flags).Invoke(window,new object[]{CalendarMath.Demo().Events.First()});
    detail=window.OwnedWindows.Cast<Window>().Single(w=>w.Title=="日程详情");
    var detailFrame=(System.Windows.Controls.Border)detail.Tag;
    CheckWindowCorners(detail,detailFrame,"小窗");
    Check(Native.SuppressBorder(new System.Windows.Interop.WindowInteropHelper(detail).Handle),"详情窗口禁用系统额外边框");
    object initialDetailContent=detail.Content;Pump(window);Check(ReferenceEquals(initialDetailContent,detail.Content),"详情首次显示后不清空或替换外框");
    Check(((System.Windows.Media.SolidColorBrush)detailFrame.Background).Color==((System.Windows.Media.SolidColorBrush)surface.Background).Color,"事件详情与主窗口使用相同背景颜色和透明度");
    Check(detail.WindowStyle==WindowStyle.None&&detail.Background==System.Windows.Media.Brushes.Transparent,"事件详情移除系统浅色标题栏和实色底板");
    Check(Native.BackdropType(new System.Windows.Interop.WindowInteropHelper(detail).Handle)==Native.BackdropType(hwnd),"主窗口与事件详情的 DWM 材质一致");
    Check(window.IsEnabled,"事件详情不阻塞主日历交互");
    window.Activate();Pump(window);
    Check(!detail.IsVisible&&typeof(CalendarWindow).GetField("eventDetail",flags).GetValue(window)==null,"事件详情失焦后关闭并释放引用");
    typeof(CalendarWindow).GetMethod("DismissIfOutside",flags).Invoke(window,null);Check(window.IsVisible&&window.IsEnabled,"关闭事件详情后主日历仍保持展开");
    Check(((System.Windows.Media.SolidColorBrush)surface.Background).Color==after,"关闭详情后主窗口背景保持一致");
   }finally{if(detail!=null&&detail.IsVisible)detail.Close();}
   typeof(CalendarWindow).GetMethod("OpenDay",flags).Invoke(window,new object[]{DateTime.Today});
   var dayWindow=(Window)typeof(CalendarWindow).GetField("dayDetail",flags).GetValue(window);
   Check(dayWindow.IsVisible&&window.IsEnabled,"当天详情使用非模态窗口，主日历可点击");
   window.Activate();Pump(window);typeof(CalendarWindow).GetMethod("DismissIfOutside",flags).Invoke(window,null);
   Check(!dayWindow.IsVisible&&typeof(CalendarWindow).GetField("dayDetail",flags).GetValue(window)==null,"点击主日历后当天详情自动关闭");
   Check(window.IsVisible&&window.IsEnabled,"关闭当天详情不收起主日历");
   typeof(CalendarWindow).GetMethod("OpenDay",flags).Invoke(window,new object[]{DateTime.Today});
   typeof(CalendarWindow).GetMethod("OpenEvent",flags).Invoke(window,new object[]{CalendarMath.Demo().Events.First()});Pump(window);
   var selectedDetail=(Window)typeof(CalendarWindow).GetField("eventDetail",flags).GetValue(window);
   Check(selectedDetail!=null&&selectedDetail.IsVisible&&typeof(CalendarWindow).GetField("dayDetail",flags).GetValue(window)==null,"从当天列表进入事件时只保留事件详情");
   window.Activate();Pump(window);Check(window.IsVisible&&typeof(CalendarWindow).GetField("eventDetail",flags).GetValue(window)==null,"返回主日历后不残留嵌套详情");
   Check(!Descendants(window).OfType<System.Windows.Controls.Button>().Any(b=>(string)b.ToolTip=="关闭窗口"||(string)b.ToolTip=="隐藏到托盘"),"桌面组件无关闭或隐藏按钮");
   Check(PopupPlacement.Bounds(new System.Drawing.Rectangle(0,0,1920,1040),760,650,new System.Drawing.Point(10,1060),8)==PopupPlacement.Bounds(new System.Drawing.Rectangle(0,0,1920,1040),760,650,new System.Drawing.Point(1800,1060),8),"托盘图标位置不影响弹窗位置");
   Check(bounds.Right==-8,"弹窗始终贴靠所在显示器右侧");
   var prefs=(Preferences)typeof(CalendarWindow).GetField("prefs",flags).GetValue(window);
   Check(new Preferences().TimelineZoom==1&&TimelineScale.Normalize(double.NaN)==1,"旧设置和无效缩放使用默认比例");
   Check(TimelineScale.Zoom(3,120)==3&&TimelineScale.Zoom(.5,-120)==.5,"缩放范围限定在 50% 至 300%");
   prefs.View="week";typeof(CalendarWindow).GetMethod("Render",flags,null,Type.EmptyTypes,null).Invoke(window,null);Pump(window);
   var timeline=(System.Windows.Controls.ScrollViewer)typeof(CalendarWindow).GetField("timelineViewer",flags).GetValue(window);
   Check(Math.Abs(timeline.VerticalOffset-540)<1,"时间轴默认从上午 9 点显示");
   timeline.ScrollToVerticalOffset(0);Pump(window);Check(timeline.VerticalOffset==0,"向上可滚动到凌晨零点");
   timeline.ScrollToVerticalOffset(540);Pump(window);double pointedMinute=timeline.VerticalOffset+100;
   window.ZoomTimeline(120,100);Pump(window);timeline=(System.Windows.Controls.ScrollViewer)typeof(CalendarWindow).GetField("timelineViewer",flags).GetValue(window);
   Check(Math.Abs(prefs.TimelineZoom-1.1)<.0001&&Math.Abs((timeline.VerticalOffset+100)/prefs.TimelineZoom-pointedMinute)<1,"纵向缩放保持鼠标所在时间锚点");
   Check(Math.Abs(store.Read<Preferences>("settings.json",false).TimelineZoom-1.1)<.0001,"纵向缩放比例保存到设置");
   Check(Math.Abs(((System.Windows.Controls.Canvas)typeof(CalendarWindow).GetField("weekCanvas",flags).GetValue(window)).Height-1584)<1,"缩放同步改变 24 小时时间轴高度");
   var mini=(System.Windows.Controls.Grid)typeof(CalendarWindow).GetMethod("MonthView",flags).Invoke(window,new object[]{new DateTime(2026,10,1),true});
   Check(Descendants(mini).OfType<System.Windows.Controls.Button>().Count()==31,"季度小月历只包含本月日期按钮");
   mini.Resources=window.Resources;mini.Measure(new Size(110,260));mini.Arrange(new Rect(0,0,110,260));mini.UpdateLayout();
   Check(Descendants(mini).OfType<System.Windows.Controls.Button>().All(b=>b.ActualWidth<=((FrameworkElement)b.Parent).ActualWidth+.01),"窄季度小月历日期背景不会超出单元格");
   var marker=Descendants(mini).OfType<System.Windows.Controls.Button>().First(b=>Convert.ToString(b.Content)=="25");
   Check(VisualNodes(marker).OfType<System.Windows.Controls.Border>().Any(b=>b.CornerRadius.TopRight>0&&b.CornerRadius.BottomRight>0),"季度日期标记保留完整右侧圆角");
   var dates=mini.Children.OfType<System.Windows.Controls.Primitives.UniformGrid>().Last();
   Check(dates.Children.Cast<System.Windows.Controls.Border>().Take(3).All(b=>b.Child==null&&!b.IsHitTestVisible),"季度上月补位格保留为空且不可点击");
   Check(window.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] is Style,"毛玻璃界面统一应用自定义滚动条样式");
   var verticalBar=VisualNodes(timeline).OfType<System.Windows.Controls.Primitives.ScrollBar>().First(b=>b.Orientation==System.Windows.Controls.Orientation.Vertical);Check(Math.Abs(verticalBar.ActualWidth-10)<=1.0/System.Windows.Media.VisualTreeHelper.GetDpi(verticalBar).DpiScaleX&&((System.Windows.Media.SolidColorBrush)verticalBar.Background).Color.A==0&&verticalBar.Template.FindName("PART_Track",verticalBar) is System.Windows.Controls.Primitives.Track,"实际时间轴滚动条为透明轨道和细滑块 width="+verticalBar.ActualWidth+" background="+verticalBar.Background+" track="+verticalBar.Template.FindName("PART_Track",verticalBar));
   var updateFixture=UpdateService.ParseRelease("{\"tag_name\":\"v99.0.0\",\"assets\":[{\"name\":\"DeskCalendar-Setup-99.0.0-x64.exe\",\"size\":4,\"digest\":\"sha256:"+new string('a',64)+"\",\"browser_download_url\":\"https://github.com/Acerocloud/Google-Calendar-Desktop/releases/download/v99.0.0/DeskCalendar-Setup-99.0.0-x64.exe\"}]}",UpdateService.Current);
   typeof(CalendarWindow).GetMethod("ShowUpdatePrompt",flags).Invoke(window,new object[]{updateFixture});Pump(window);
   var updateWindow=(Window)typeof(CalendarWindow).GetField("updatePrompt",flags).GetValue(window);
   Check(updateWindow!=null&&Descendants(updateWindow).OfType<System.Windows.Controls.Button>().Any(b=>b.Name=="InstallUpdate"),"发现新版本时应用内提供一键更新弹窗");
   Descendants(updateWindow).OfType<System.Windows.Controls.Button>().Single(b=>Convert.ToString(b.Content)=="稍后").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
   Check(typeof(CalendarWindow).GetField("updatePrompt",flags).GetValue(window)==null,"稍后关闭更新弹窗，不自动安装");
   Exception error=null;
   window.Dispatcher.BeginInvoke(new Action(()=>{
    Window settings=null;
    try{
     settings=window.OwnedWindows.Cast<Window>().Single(w=>w.Title=="设置");
     Check(Descendants(settings).OfType<System.Windows.Controls.Button>().Any(b=>b.Name=="CheckForUpdates"),"设置中提供检查更新按钮");
     var settingsLogin=Descendants(settings).OfType<System.Windows.Controls.Button>().Single(b=>b.Name=="SettingsGoogleLogin");
     Check(settingsLogin.Visibility==Visibility.Collapsed&&!Descendants(settings).OfType<System.Windows.Controls.Button>().Any(b=>Convert.ToString(b.Content)=="重新登录 Google"),"登录成功后不显示登录或重新登录按钮");
     var checks=Descendants(settings).OfType<System.Windows.Controls.CheckBox>().ToList();
     Check(checks.All(cb=>VisualNodes(cb).OfType<System.Windows.Controls.Border>().Any(b=>b.CornerRadius.TopLeft==4)),"设置勾选框使用统一圆角模板");
     var lockCheck=checks.Single(cb=>Convert.ToString(cb.Content)=="锁定位置与尺寸");lockCheck.IsChecked=true;Pump(window);
     Check(Convert.ToString(lockIcon.Content)=="\uE72E"&&Native.BackdropType(hwnd)==backdrop,"设置锁定开关同步快捷图标且不影响毛玻璃");lockCheck.IsChecked=false;Pump(window);
     var combo=Descendants(settings).OfType<System.Windows.Controls.ComboBox>().Single();combo.IsDropDownOpen=true;Pump(window);
     var popup=(System.Windows.Controls.Primitives.Popup)combo.Template.FindName("PART_Popup",combo);
     Check(popup.IsOpen&&((System.Windows.Controls.Border)popup.Child).CornerRadius.TopLeft==6&&((System.Windows.Media.SolidColorBrush)((System.Windows.Controls.Border)popup.Child).Background).Color!=System.Windows.Media.Colors.White,"下拉菜单为统一深色圆角面板");combo.IsDropDownOpen=false;
     var slider=Descendants(settings).OfType<System.Windows.Controls.Slider>().Single();
     Check(VisualNodes(slider).OfType<System.Windows.Shapes.Ellipse>().Any(),"设置滑块使用圆形滑块和细轨道");
     Check(Descendants(settings).OfType<System.Windows.Controls.Button>().Where(b=>Convert.ToString(b.Content)=="完成"||Convert.ToString(b.Content)=="断开账号").All(b=>b.BorderThickness==new Thickness(1)&&((System.Windows.Media.SolidColorBrush)b.BorderBrush).Color.A>0),"完成与断开账号按钮具有可见细线边框");
     Check(Native.SuppressBorder(new System.Windows.Interop.WindowInteropHelper(settings).Handle),"设置窗口禁用系统额外边框");
     Check(((System.Windows.Media.SolidColorBrush)((System.Windows.Controls.Border)settings.Tag).Background).Color==((System.Windows.Media.SolidColorBrush)surface.Background).Color,"设置与主窗口保持一致背景");
     Check(!Descendants(settings).OfType<System.Windows.Controls.CheckBox>().Any(b=>Convert.ToString(b.Content).Contains("置顶")),"设置不再提供置顶开关");
     var logout=Descendants(settings).OfType<System.Windows.Controls.Button>().Single(b=>Convert.ToString(b.Content)=="断开账号");
     logout.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
     var google=(GoogleService)typeof(CalendarWindow).GetField("google",flags).GetValue(window);
     Check(!google.Connected&&!File.Exists(Path.Combine(directory,"tokens.bin"))&&!File.Exists(Path.Combine(directory,"calendar.bin")),"设置中断开账号删除令牌和日程缓存");
     Check(settings.IsVisible&&!window.IsBusy&&logout.Visibility==Visibility.Collapsed,"断开后保留设置页，不启动登录流程");
     var caption=Descendants(window).OfType<System.Windows.Controls.TextBlock>().Single(t=>t.Name=="ReadOnlyCaption");
     Check(caption.TextAlignment==TextAlignment.Center&&caption.HorizontalAlignment==HorizontalAlignment.Stretch,"只读提示在首页水平居中");
     var loginButton=Descendants(window).OfType<System.Windows.Controls.Button>().Single(b=>b.Name=="GoogleLoginButton");
     Check(((System.Windows.Media.SolidColorBrush)loginButton.Background).Color==System.Windows.Media.Colors.White&&loginButton.Width==204&&loginButton.Height==40,"Google 登录按钮使用白底固定尺寸");
     Check(Descendants(loginButton).OfType<System.Windows.Controls.TextBlock>().Any(t=>t.Text=="Google 登录")&&Descendants(loginButton).OfType<System.Windows.Controls.Image>().Any(i=>i.Source!=null),"Google 登录按钮包含正确文案和嵌入图标");
     Check(Descendants(settings).OfType<System.Windows.Controls.Button>().Any(b=>b.Name=="SettingsGoogleLogin"&&b.IsEnabled&&b.Visibility==Visibility.Visible),"断开后仅提供手动重新登录入口");
     Check(Descendants(settingsLogin).OfType<System.Windows.Controls.Image>().Any(i=>i.Source!=null)&&Descendants(settingsLogin).OfType<System.Windows.Controls.TextBlock>().Any(t=>t.Text=="Google 登录"),"设置未登录按钮包含 Google 官方图标与文案");
    }catch(Exception ex){error=ex;}finally{if(settings!=null)settings.Close();}
   }),DispatcherPriority.ApplicationIdle);
   typeof(CalendarWindow).GetMethod("OpenSettings",flags).Invoke(window,null);if(error!=null)throw error;
   Check(ReferenceEquals(window.Content,surface),"关闭设置不重建主页外框");
   typeof(CalendarWindow).GetMethod("Build",flags).Invoke(window,null);typeof(CalendarWindow).GetMethod("Render",flags,null,Type.EmptyTypes,null).Invoke(window,null);Pump(window);CheckWindowCorners(window,(System.Windows.Controls.Border)window.Content,"主页重建后");Check(Native.BackdropType(hwnd)==backdrop,"主页重建后仍保持原有毛玻璃材质");MotionChecks(window);PreparedViewChecks(window);YearChecks(window);FocusFeedbackChecks(window);TasksWidgetChecks(window);
  }finally{typeof(CalendarWindow).GetField("exiting",flags).SetValue(window,true);window.Close();}
 }
}
public class MockCalendarHandler:HttpMessageHandler {
 public bool HoldToken,Revoked;public readonly TaskCompletionSource<HttpResponseMessage> PendingToken=new TaskCompletionSource<HttpResponseMessage>();
 public string LastTokenClientId;
 public int NotModified,TokenRequests;public bool FailSecondPage,Throttled,UnauthorizedOnce,InvalidGrant;public bool Authenticated=true;
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){
  if(r.RequestUri.AbsolutePath.EndsWith("/revoke")){Revoked=true;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{}")});}
  if(HoldToken&&r.RequestUri.AbsolutePath.EndsWith("/token"))return PendingToken.Task;
  if(r.RequestUri.AbsolutePath.EndsWith("/token")){TokenRequests++;LastTokenClientId=r.Content.ReadAsStringAsync().GetAwaiter().GetResult().Split('&').Where(x=>x.StartsWith("client_id=")).Select(x=>Uri.UnescapeDataString(x.Substring(10))).FirstOrDefault();return Task.FromResult(new HttpResponseMessage(InvalidGrant?HttpStatusCode.BadRequest:HttpStatusCode.OK){Content=new StringContent(InvalidGrant?"{\"error\":\"invalid_grant\"}":"{\"access_token\":\"fresh-token\",\"expires_in\":3600}")});}
  if(UnauthorizedOnce){UnauthorizedOnce=false;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized){Content=new StringContent("{}")});}
  Authenticated=Authenticated&&r.Headers.Authorization!=null&&(r.Headers.Authorization.Parameter=="test-token"||r.Headers.Authorization.Parameter=="fresh-token");
  if(Throttled){var limited=new HttpResponseMessage((HttpStatusCode)429);limited.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(10));limited.Content=new StringContent("{}");return Task.FromResult(limited);}
  string body;
  if(r.RequestUri.AbsolutePath.EndsWith("calendarList"))body="{\"items\":[{\"id\":\"primary\",\"summary\":\"Test\",\"backgroundColor\":\"#112233\",\"primary\":true}]}";
  else if(r.RequestUri.AbsolutePath.EndsWith("colors")){
   if(r.Headers.Contains("If-None-Match")){NotModified++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));}
   var colors=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"event\":{\"1\":{\"background\":\"#AABBCC\"}}}")};colors.Headers.ETag=new System.Net.Http.Headers.EntityTagHeaderValue("\"colors1\"");return Task.FromResult(colors);
  } else {
   string ev="{\"id\":\"e1\",\"summary\":\"Meeting\",\"colorId\":\"1\",\"start\":{\"dateTime\":\"2026-10-24T10:00:00+08:00\"},\"end\":{\"dateTime\":\"2026-10-24T11:00:00+08:00\"}}";
   if(r.RequestUri.Query.Contains("pageToken=")){if(FailSecondPage)throw new HttpRequestException("test second page failure");body="{\"items\":["+ev+",{\"id\":\"all\",\"summary\":\"All day\",\"start\":{\"date\":\"2026-10-24\"},\"end\":{\"date\":\"2026-10-26\"}},{\"id\":\"point\",\"summary\":\"Point event\",\"start\":{\"dateTime\":\"2026-10-24T14:00:00+08:00\"},\"end\":{\"dateTime\":\"2026-10-24T14:00:00+08:00\"}},{\"id\":\"cancelled\",\"status\":\"cancelled\"}]}";}
   else body="{\"items\":["+ev+"],\"nextPageToken\":\"page2\"}";
  }
  return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)});
 }
}
}

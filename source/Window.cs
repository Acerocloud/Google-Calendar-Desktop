using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace DeskCalendar {
public class Segment {
 public CalendarEvent Event;public double Start,End;public int Lane,Count;
}
public static class Overlaps {
 public static List<Segment> Arrange(IEnumerable<CalendarEvent> items,DateTime day){
  var segments=items.Where(e=>!e.AllDay&&e.OnDay(day)).Select(e=>new Segment{Event=e,Start=Math.Max(0,(e.StartLocal-day.Date).TotalMinutes),End=Math.Min(1440,(e.EndLocal-day.Date).TotalMinutes)}).OrderBy(e=>e.Start).ThenByDescending(e=>e.End).ToList();
  var group=new List<Segment>();var laneEnds=new List<double>();double groupEnd=-1;
  foreach(var s in segments){
   if(s.Event.IsPoint)s.End=Math.Min(1440,s.Start+22);
   else if(s.End<=s.Start)s.End=Math.Min(1440,s.Start+s.Event.DurationMinutes);
   if(s.Start>=groupEnd){foreach(var old in group)old.Count=laneEnds.Count;group.Clear();laneEnds.Clear();}
   int lane=laneEnds.FindIndex(end=>end<=s.Start);if(lane<0){lane=laneEnds.Count;laneEnds.Add(s.End);}else laneEnds[lane]=s.End;
   s.Lane=lane;group.Add(s);groupEnd=Math.Max(groupEnd,s.End);
  }
  foreach(var s in group)s.Count=laneEnds.Count;return segments;
 }
}

public partial class CalendarWindow:Window {
 readonly Store store;readonly GoogleService google;readonly bool demo,rendering;
 Preferences prefs;CacheData cache;
 readonly CancellationTokenSource lifetime=new CancellationTokenSource();CancellationTokenSource operation;
 readonly SemaphoreSlim syncGate=new SemaphoreSlim(1,1);
 readonly DispatcherTimer poll=new DispatcherTimer(),clock=new DispatcherTimer(),midnight=new DispatcherTimer(),resize=new DispatcherTimer(),saveTimer=new DispatcherTimer();
 DateTime anchor=DateTime.Today;DateTime retryAfter=DateTime.MinValue;DateTime lastSyncRequest=DateTime.MinValue,metadataRead=DateTime.MinValue;
 int failures;bool exiting,busy,disconnecting,suspended,sessionLocked,authExpired,dirtySize,pendingNavigation;string status="未连接 Google 日历";
 Border surface;Grid shell;ContentControl content;TextBlock period,statusLabel;Button syncButton;StackPanel viewButtons;
 Line nowLine;Canvas weekCanvas;double weekWidth;DateTime weekStart;
 Forms.NotifyIcon tray;Brush foreground,muted,line,hover,dialogBg;
 bool started;DateTime lastAutoDismiss=DateTime.MinValue;
 readonly Dictionary<Window,string> materialStates=new Dictionary<Window,string>();internal int MaterialUpdates;
 Button lockButton;
 Window eventDetail,dayDetail;ScrollViewer timelineViewer;double timelineMinute=540;
 public bool IsBusy {get{return busy;}}
 public bool DemoMode {get{return demo;}}
 public CalendarWindow(Store s,bool demoMode,bool renderMode){
  store=s;demo=demoMode;rendering=renderMode;try{prefs=store.Read<Preferences>("settings.json",false);}catch{prefs=new Preferences();}
  try{cache=demo?CalendarMath.Demo():store.Read<CacheData>("calendar.bin",true);}catch{cache=new CacheData();status="本地缓存无法读取，请重新同步。";}
  try{google=new GoogleService(store);}catch{google=null;status="登录信息无法读取，请在设置中重置本地登录信息。";}
  prefs.TimelineZoom=TimelineScale.Normalize(prefs.TimelineZoom);
  if(prefs.Selected==null)prefs.Selected=new List<string>();
  if(!new[]{"month","week","quarter"}.Contains(prefs.View))prefs.View="month";
  Title="日历";FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");FontSize=12;Width=CalendarMath.Clip(prefs.Width,480,1600);Height=CalendarMath.Clip(prefs.Height,480,1300);MinWidth=480;MinHeight=480;
  prefs.Pinned=false;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.CanResize;Background=Brushes.Transparent;ShowInTaskbar=false;Topmost=false;ShowActivated=false;
#if VISUAL_QA
  ShowInTaskbar=true;ShowActivated=true;
#endif
  WindowStartupLocation=WindowStartupLocation.CenterScreen;
  if(!double.IsNaN(prefs.Left)&&!double.IsNaN(prefs.Top)&&prefs.Left>=SystemParameters.VirtualScreenLeft&&prefs.Left+Width<=SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth&&prefs.Top>=SystemParameters.VirtualScreenTop&&prefs.Top+80<SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight){Left=prefs.Left;Top=prefs.Top;WindowStartupLocation=WindowStartupLocation.Manual;}
  WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(prefs.Locked?0:5),GlassFrameThickness=new Thickness(-1),UseAeroCaptionButtons=false});
  SourceInitialized+=delegate{IntPtr hwnd=new WindowInteropHelper(this).Handle;Native.WidgetStyle(hwnd);AttachMaterialHook(this);ApplyMaterial();};
  Loaded+=delegate{Render();if(!renderMode&&!started)StartInTray();};
  Deactivated+=delegate{if(!renderMode)Dispatcher.BeginInvoke(new Action(DismissIfOutside),DispatcherPriority.Background);};
  SizeChanged+=delegate{if(!IsLoaded)return;dirtySize=true;resize.Stop();resize.Start();saveTimer.Stop();saveTimer.Start();};
  PreviewMouseDown+=delegate{popupRise.Stop(true);};
  LocationChanged+=delegate{if(!IsLoaded||popupRise.Running)return;saveTimer.Stop();saveTimer.Start();};
  Closing+=OnClosing;
  StateChanged+=delegate{if(!exiting&&WindowState==WindowState.Minimized){HidePopup();WindowState=WindowState.Normal;}};
  IsVisibleChanged+=delegate{if(!IsLoaded||renderMode)return;Schedule();if(IsVisible&&!busy&&DateTime.UtcNow-lastSyncRequest>TimeSpan.FromMinutes(2))Dispatcher.BeginInvoke(new Action(async()=>await SyncAsync(false)));};
  KeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e){if(e.Key==System.Windows.Input.Key.Escape){HidePopup();e.Handled=true;}if(e.Key==System.Windows.Input.Key.F5){SyncFromButton();e.Handled=true;}};
  poll.Tick+=async delegate{poll.Stop();await SyncAsync(false);};
  clock.Tick+=delegate{UpdateNow();clock.Interval=TimeSpan.FromMinutes(1);};
  midnight.Tick+=delegate{midnight.Stop();Render();Schedule();};
  resize.Interval=TimeSpan.FromMilliseconds(180);resize.Tick+=delegate{resize.Stop();if(dirtySize){dirtySize=false;RenderContent();}};
  saveTimer.Interval=TimeSpan.FromMilliseconds(700);saveTimer.Tick+=delegate{saveTimer.Stop();Save();};
  if(!renderMode){CreateTray();SystemEvents.PowerModeChanged+=OnPower;SystemEvents.SessionSwitch+=OnSession;SystemEvents.DisplaySettingsChanged+=OnDisplays;SystemEvents.UserPreferenceChanged+=OnPreferences;NetworkChange.NetworkAvailabilityChanged+=OnNetwork;}
  try{using(var icon=System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location)){base.Icon=Imaging.CreateBitmapSourceFromHIcon(icon.Handle,Int32Rect.Empty,System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());}}catch{}
  Build();InitUpdates();popupRise.Completed+=delegate{QueuePreparedViews();};
 }
 SolidColorBrush BrushColor(string color){try{return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));}catch{return new SolidColorBrush(Color.FromRgb(132,165,190));}}
 Brush Alpha(byte alpha){return new SolidColorBrush(Color.FromArgb(alpha,255,255,255));}
 void Theme(){bool light=prefs.Theme=="light";foreground=BrushColor(light?"#202020":"#F0F0ED");muted=BrushColor(light?"#626260":"#B3B3AF");line=BrushColor(light?"#16000000":"#19FFFFFF");hover=BrushColor(light?"#10000000":"#13FFFFFF");dialogBg=BrushColor(light?"#F1F1EE":"#242526");if(SystemParameters.HighContrast){foreground=muted=SystemColors.WindowTextBrush;line=SystemColors.WindowFrameBrush;hover=SystemColors.HighlightBrush;dialogBg=SystemColors.WindowBrush;}Foreground=foreground;}
 TextBlock Text(string text,double size,Brush color){var t=new TextBlock{Text=text,FontSize=size,Foreground=color,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};if(ReferenceEquals(color,foreground))t.SetResourceReference(TextBlock.ForegroundProperty,"ControlText");else if(ReferenceEquals(color,muted))t.SetResourceReference(TextBlock.ForegroundProperty,"ControlMuted");return t;}
 Button Button(string text,string label,Action action){var b=new Button{Content=text,Padding=new Thickness(10,5,10,5),Margin=new Thickness(2,0,2,0),Foreground=foreground,Background=Brushes.Transparent,BorderThickness=new Thickness(0),ToolTip=label,MinHeight=28};AutomationProperties.SetName(b,label);b.Click+=delegate{action();};return b;}
 Button GoogleLoginButton(Action action=null){
  var button=Button("","Google 登录",action??new Action(BeginLogin));button.Name="GoogleLoginButton";button.Width=204;button.Height=40;button.Padding=new Thickness(12,0,12,0);button.HorizontalAlignment=HorizontalAlignment.Center;button.Foreground=BrushColor("#1F1F1F");button.Background=Brushes.White;button.BorderBrush=BrushColor("#747775");button.BorderThickness=new Thickness(1);button.IsEnabled=google!=null&&!busy&&!disconnecting;
  var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
  using(var stream=typeof(CalendarWindow).Assembly.GetManifestResourceStream("DeskCalendar.GoogleIcon")){var icon=new System.Windows.Media.Imaging.BitmapImage();icon.BeginInit();icon.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;icon.StreamSource=stream;icon.EndInit();icon.Freeze();row.Children.Add(new Image{Source=icon,Width=20,Height=20,Margin=new Thickness(0,0,10,0)});}
  row.Children.Add(new TextBlock{Text="Google 登录",FontSize=14,FontWeight=FontWeights.Medium,Foreground=BrushColor("#1F1F1F"),VerticalAlignment=VerticalAlignment.Center});button.Content=row;
  button.Template=(ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='surface' CornerRadius='6' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Background' Value='#FFF7F8F8'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='surface' Property='Background' Value='#FFE8EAED'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='BorderBrush' Value='#FF1A73E8'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");return button;
 }
 new Button Icon(string glyph,string label,Action action){var b=Button(glyph,label,action);b.FontFamily=new FontFamily("Segoe MDL2 Assets");b.FontSize=14;b.Width=30;b.Padding=new Thickness(0);return b;}
 void Styles(){
  const string x="<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><Style TargetType='Button'><Setter Property='Cursor' Value='Hand'/><Setter Property='HorizontalContentAlignment' Value='Center'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='bg' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='4' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center' RecognizesAccessKey='True'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bg' Property='Background' Value='#20A0A0A0'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='bg' Property='Background' Value='#35A0A0A0'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='bg' Property='BorderBrush' Value='#FFAAAAAA'/><Setter TargetName='bg' Property='BorderThickness' Value='1'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style><Style TargetType='ToolTip'><Setter Property='Background' Value='#FF292A2B'/><Setter Property='Foreground' Value='#FFF0F0ED'/><Setter Property='BorderBrush' Value='#FF555555'/><Setter Property='Padding' Value='10'/><Setter Property='MaxWidth' Value='360'/></Style></ResourceDictionary>";
  Resources=(ResourceDictionary)XamlReader.Parse(x.Replace("</ResourceDictionary>",@"<Style TargetType='ScrollBar'><Setter Property='MinWidth' Value='0'/><Setter Property='MinHeight' Value='0'/><Setter Property='Width' Value='10'/><Setter Property='Background' Value='Transparent'/><Setter Property='Focusable' Value='False'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Grid Background='Transparent'><Track x:Name='PART_Track' Orientation='{TemplateBinding Orientation}' IsDirectionReversed='True' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}' ViewportSize='{TemplateBinding ViewportSize}'><Track.DecreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageUpCommand}' Opacity='0' Focusable='False'/></Track.DecreaseRepeatButton><Track.Thumb><Thumb MinHeight='28' Margin='2,0' Background='{DynamicResource GlassScrollThumb}'><Thumb.Template><ControlTemplate TargetType='Thumb'><Border x:Name='grip' Background='{TemplateBinding Background}' CornerRadius='3'/><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='grip' Property='Background' Value='{DynamicResource GlassScrollHover}'/></Trigger><Trigger Property='IsDragging' Value='True'><Setter TargetName='grip' Property='Background' Value='{DynamicResource GlassScrollHover}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageDownCommand}' Opacity='0' Focusable='False'/></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate></Setter.Value></Setter></Style><Style TargetType='ScrollViewer'><Setter Property='Background' Value='Transparent'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollViewer'><Grid Background='{TemplateBinding Background}'><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><Grid.RowDefinitions><RowDefinition Height='*'/><RowDefinition Height='Auto'/></Grid.RowDefinitions><ScrollContentPresenter x:Name='PART_ScrollContentPresenter' Content='{TemplateBinding Content}' ContentTemplate='{TemplateBinding ContentTemplate}' CanContentScroll='{TemplateBinding CanContentScroll}' Margin='{TemplateBinding Padding}'/><ScrollBar x:Name='PART_VerticalScrollBar' Grid.Column='1' Orientation='Vertical' Width='10' MinWidth='0' Background='Transparent' Visibility='{TemplateBinding ComputedVerticalScrollBarVisibility}' Maximum='{TemplateBinding ScrollableHeight}' ViewportSize='{TemplateBinding ViewportHeight}' Value='{Binding VerticalOffset, Mode=OneWay, RelativeSource={RelativeSource TemplatedParent}}'/><ScrollBar x:Name='PART_HorizontalScrollBar' Grid.Row='1' Orientation='Horizontal' Height='10' Width='Auto' Background='Transparent' Visibility='{TemplateBinding ComputedHorizontalScrollBarVisibility}' Maximum='{TemplateBinding ScrollableWidth}' ViewportSize='{TemplateBinding ViewportWidth}' Value='{Binding HorizontalOffset, Mode=OneWay, RelativeSource={RelativeSource TemplatedParent}}'/></Grid></ControlTemplate></Setter.Value></Setter></Style></ResourceDictionary>"));Resources["GlassScrollThumb"]=SystemParameters.HighContrast?SystemColors.WindowTextBrush:BrushColor(prefs.Theme=="light"?"#66000000":"#66FFFFFF");Resources["GlassScrollHover"]=SystemParameters.HighContrast?SystemColors.HighlightBrush:BrushColor(prefs.Theme=="light"?"#99000000":"#AAFFFFFF");
 }
 void UpdateLockIcon(){if(lockButton==null)return;lockButton.Content=prefs.Locked?"\uE72E":"\uE785";lockButton.ToolTip=prefs.Locked?"解锁位置与尺寸":"锁定位置与尺寸";lockButton.Background=prefs.Locked?hover:Brushes.Transparent;AutomationProperties.SetName(lockButton,prefs.Locked?"解锁位置与尺寸":"锁定位置与尺寸");AutomationProperties.SetItemStatus(lockButton,prefs.Locked?"已锁定":"未锁定");}
 internal void SetLocked(bool value){prefs.Locked=value;var chrome=WindowChrome.GetWindowChrome(this);if(chrome!=null)chrome.ResizeBorderThickness=new Thickness(value?0:5);UpdateLockIcon();Save();}
 Button SettingsButton(string text,string label,Action action){var b=Button(text,label,action);b.BorderThickness=new Thickness(1);b.SetResourceReference(Control.BorderBrushProperty,"ControlBorder");b.SetResourceReference(Control.ForegroundProperty,"ControlText");b.Padding=new Thickness(12,8,12,8);b.MinHeight=36;b.Margin=new Thickness(0,4,0,4);return b;}
 Button SettingsGoogleLoginButton(Action action){
  var button=SettingsButton("","Google 登录",action);var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
  using(var stream=typeof(CalendarWindow).Assembly.GetManifestResourceStream("DeskCalendar.GoogleIcon")){var icon=new System.Windows.Media.Imaging.BitmapImage();icon.BeginInit();icon.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;icon.StreamSource=stream;icon.EndInit();icon.Freeze();row.Children.Add(new Image{Source=icon,Width=20,Height=20,Margin=new Thickness(0,0,10,0)});}
  row.Children.Add(Text("Google 登录",12,foreground));button.Content=row;return button;
 }
 void SettingsResources(){
  bool light=prefs.Theme=="light";Resources["ControlText"]=foreground;Resources["ControlMuted"]=muted;Resources["ControlInverse"]=SystemParameters.HighContrast?SystemColors.HighlightTextBrush:BrushColor(light?"#FFFFFF":"#202223");Resources["ControlBorder"]=SystemParameters.HighContrast?SystemColors.WindowTextBrush:BrushColor(light?"#66000000":"#99FFFFFF");Resources["ControlFill"]=SystemParameters.HighContrast?SystemColors.WindowBrush:BrushColor(light?"#0C000000":"#0CFFFFFF");Resources["ControlChecked"]=SystemParameters.HighContrast?SystemColors.HighlightBrush:foreground;Resources["ControlHover"]=hover;Resources["ControlTrack"]=SystemParameters.HighContrast?SystemColors.WindowTextBrush:BrushColor(light?"#33000000":"#33FFFFFF");Resources["ControlPopup"]=SystemParameters.HighContrast?SystemColors.WindowBrush:BrushColor(light?"#FFF1F1EE":"#FF252728");
  using(var stream=typeof(CalendarWindow).Assembly.GetManifestResourceStream("DeskCalendar.Controls"))Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
 }
 void Build(){
  StopMotion();ClearPreparedViews();
  Theme();Styles();SettingsResources();foreach(Window owned in OwnedWindows)owned.Resources=Resources;shell=new Grid();shell.RowDefinitions.Add(new RowDefinition{Height=new GridLength(42)});shell.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});shell.RowDefinitions.Add(new RowDefinition());shell.RowDefinitions.Add(new RowDefinition{Height=new GridLength(34)});
  surface=new Border{CornerRadius=new CornerRadius(0),BorderThickness=new Thickness(0),BorderBrush=Brushes.Transparent,Child=shell,SnapsToDevicePixels=true,UseLayoutRounding=true};Content=surface;
  var title=new Grid{Margin=new Thickness(14,0,8,0)};title.ColumnDefinitions.Add(new ColumnDefinition());title.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
  var drag=Text("日历",12,muted);drag.HorizontalAlignment=HorizontalAlignment.Stretch;drag.Background=Brushes.Transparent;drag.MouseLeftButtonDown+=delegate{if(!prefs.Locked)try{DragMove();}catch(InvalidOperationException){} };title.Children.Add(drag);
  var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(actions,1);title.Children.Add(actions);
  lockButton=Icon("", "锁定位置与尺寸",()=>SetLocked(!prefs.Locked));lockButton.Name="LockShortcut";UpdateLockIcon();actions.Children.Add(lockButton);
  var add=Icon("\uE710","新增日程",()=>OpenCreateEvent(DateTime.Today));add.Name="AddEvent";actions.Children.Add(add);
  actions.Children.Add(Icon("\uE713","设置",OpenSettings));shell.Children.Add(title);
  var header=new StackPanel{Margin=new Thickness(18,15,18,12)};Grid.SetRow(header,1);shell.Children.Add(header);
  var periodGrid=new Grid();periodGrid.ColumnDefinitions.Add(new ColumnDefinition());periodGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});period=Text("",24,foreground);periodGrid.Children.Add(period);
  var navigation=new StackPanel{Orientation=Orientation.Horizontal};Grid.SetColumn(navigation,1);periodGrid.Children.Add(navigation);
  navigation.Children.Add(Button("今天","返回今天",()=>{int direction=DateTime.Today.CompareTo(anchor);anchor=DateTime.Today;Navigate(direction,true);}));navigation.Children.Add(Icon("\uE76B","上一周期",()=>Move(-1)));navigation.Children.Add(Icon("\uE76C","下一周期",()=>Move(1)));header.Children.Add(periodGrid);
  var views=new Grid{Margin=new Thickness(0,14,0,0)};views.ColumnDefinitions.Add(new ColumnDefinition());views.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});viewButtons=new StackPanel{Orientation=Orientation.Horizontal};views.Children.Add(viewButtons);
  foreach(string kind in new[]{"week","month","quarter"}){string key=kind;var b=Button(kind=="week"?"周":kind=="month"?"月":"季","切换日历视图",()=>SwitchView(key));b.Tag=key;b.Width=44;viewButtons.Children.Add(b);}
  var timezone=Text(TimeZoneInfo.Local.BaseUtcOffset==TimeSpan.FromHours(8)?"本地时间 · UTC+08":"本地时间 · "+TimeZoneInfo.Local.StandardName,11,muted);Grid.SetColumn(timezone,1);views.Children.Add(timezone);header.Children.Add(views);
  content=new ContentControl{Margin=new Thickness(18,0,18,8),HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};calendarViewport=new Grid{ClipToBounds=true};calendarViewport.Children.Add(content);Grid.SetRow(calendarViewport,2);shell.Children.Add(calendarViewport);
  var footer=new Grid{Margin=new Thickness(14,0,8,0)};footer.ColumnDefinitions.Add(new ColumnDefinition());footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});Grid.SetRow(footer,3);shell.Children.Add(footer);statusLabel=Text(status,11,muted);statusLabel.ToolTip=status;footer.Children.Add(statusLabel);
  syncButton=Icon("\uE72C","刷新（F5）",SyncFromButton);Grid.SetColumn(syncButton,1);footer.Children.Add(syncButton);ApplySurface();
 }
 void Move(int amount){anchor=CalendarMath.Navigation(anchor,prefs.View,amount);Navigate(amount,true);}
 void QueueSync(){Dispatcher.BeginInvoke(new Action(async()=>await SyncAsync(false)));}
 void Navigate(int direction=0,bool paging=false){StopViewSlide();var previous=content.Content as UIElement;var previousTimeline=timelineViewer;double previousOffset=previousTimeline==null?0:previousTimeline.VerticalOffset;timelineMinute=540;navigationRendering=true;try{Render();}finally{navigationRendering=false;}if(direction!=0&&MotionAllowed)StartViewSlide(previous,direction,previousTimeline,previousOffset,paging);resize.Stop();resize.Interval=TimeSpan.FromMilliseconds(200);resize.Start();dirtySize=false;if(busy){pendingNavigation=true;return;}if(!demo&&google!=null&&google.Connected)QueueSync();}
 IEnumerable<CalendarEvent> VisibleEvents(){return cache.Events.Where(e=>demo||prefs.Selected.Contains(e.CalendarId));}
 void Render(){if(period==null)return;DateTime d=anchor;
  period.Text=prefs.View=="week"?CalendarMath.Monday(d).ToString("M 月 d 日")+" – "+CalendarMath.Monday(d).AddDays(6).ToString("M 月 d 日"):prefs.View=="quarter"?d.Year+" 年 · 第 "+(((d.Month-1)/3)+1)+" 季度":d.ToString("yyyy 年 M 月");
  foreach(Button b in viewButtons.Children)b.Background=(string)b.Tag==prefs.View?hover:Brushes.Transparent;
  if(demo)SetStatus("示例日程 · 未连接真实账号");else UpdateStatus();RenderContent();ScheduleClock();
 }
 void SetStatus(string text){status=text;if(statusLabel!=null){statusLabel.Text=text;statusLabel.ToolTip=text;}if(syncButton!=null)syncButton.IsEnabled=!busy&&google!=null&&google.Connected&&!demo;}
 void UpdateStatus(){
  if(busy){SetStatus("正在同步…");return;}
  if(authExpired){SetStatus("登录已过期，请在设置中重新登录");return;}
  if(google==null||!google.Connected){SetStatus("未连接 Google 日历 · 点击 Google 登录");return;}
  if(prefs.Selected.Count==0){SetStatus("未选择日历 · 请在设置中勾选");return;}
  string latest=cache.Updated.Values.OrderByDescending(x=>x).FirstOrDefault();DateTime t;
  string last=DateTime.TryParse(latest,null,DateTimeStyles.RoundtripKind,out t)?t.ToLocalTime().ToString(t.ToLocalTime().Date==DateTime.Today?"HH:mm":"M/d HH:mm"):"尚未同步";
  string mode=BatterySaving()?" · 节电模式":"";
  if(!NetworkInterface.GetIsNetworkAvailable())SetStatus("未联网 · 上次更新 "+last);
  else if(failures>0)SetStatus("同步未完成 · 上次更新 "+last+mode);
  else SetStatus(last+" 更新"+mode);
 }
 void RenderContent(){
  if(!navigationRendering)ClearPreparedViews();if(pageSliding){redrawAfterSlide=true;return;}StopViewSlide();nowLine=null;weekCanvas=null;if(content==null)return;
  if(!demo&&(google==null||!google.Connected)&&cache.Events.Count==0){var box=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};var title=Text("连接 Google 日历",20,foreground);title.TextAlignment=TextAlignment.Center;box.Children.Add(title);var info=Text("登录后，选择要显示的日历。",12,muted);info.TextAlignment=TextAlignment.Center;info.Margin=new Thickness(0,12,0,20);box.Children.Add(info);box.Children.Add(GoogleLoginButton());var caption=Text("登录后查看、新增和删除日程",11,muted);caption.Name="ReadOnlyCaption";caption.TextAlignment=TextAlignment.Center;caption.HorizontalAlignment=HorizontalAlignment.Stretch;caption.Margin=new Thickness(0,12,0,0);box.Children.Add(caption);content.Content=box;return;}
  if(!UsePreparedView()){if(prefs.View=="week")content.Content=WeekView();else if(prefs.View=="quarter")content.Content=QuarterView();else content.Content=MonthView(anchor,false);}QueuePreparedViews();
 }
 UIElement MonthView(DateTime month,bool mini){
  var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(mini?26:32)});root.RowDefinitions.Add(new RowDefinition());
  var labels=new UniformGrid{Columns=7};foreach(string s in new[]{"一","二","三","四","五","六","日"}){var t=Text(s,11,muted);t.HorizontalAlignment=HorizontalAlignment.Center;labels.Children.Add(t);}root.Children.Add(labels);
  DateTime first=CalendarMath.Month(month),start=CalendarMath.Monday(first);int rows=(int)Math.Ceiling(((first-start).Days+DateTime.DaysInMonth(first.Year,first.Month))/7.0);
  var dates=new UniformGrid{Columns=7,Rows=mini?6:rows};Grid.SetRow(dates,1);root.Children.Add(dates);
  int count=(mini?6:rows)*7;var all=VisibleEvents().ToList();
  double available=Math.Max(240,ActualHeight-198);int cap=(int)CalendarMath.Clip((available/rows-38)/22,1,3);
  for(int i=0;i<count;i++){
   DateTime day=start.AddDays(i);bool outside=day.Month!=first.Month;
   if(mini&&outside){dates.Children.Add(new Border{IsHitTestVisible=false});continue;}var dayEvents=CalendarMath.ForDay(all,day);
   var panel=new StackPanel{Margin=new Thickness(mini?1:4,mini?2:5,mini?1:3,2)};
   var date=Button(day.Day.ToString(),day.ToString("yyyy年M月d日")+"，"+dayEvents.Count+" 项日程",()=>OpenDay(day));date.Width=27;date.Height=27;date.MinHeight=0;date.Margin=new Thickness(0,0,0,mini?0:4);date.Padding=new Thickness(0);date.HorizontalAlignment=HorizontalAlignment.Left;date.Foreground=outside?muted:foreground;
   if(day==DateTime.Today){date.Background=foreground;date.Foreground=prefs.Theme=="light"?Brushes.White:BrushColor("#202122");}
   if(mini){date.Width=double.NaN;date.MaxWidth=27;date.MinWidth=0;date.HorizontalAlignment=HorizontalAlignment.Stretch;}
   if(outside)date.Opacity=.4;
   panel.Children.Add(date);
   if(mini){if(dayEvents.Count>0){var mark=new Ellipse{Width=3,Height=3,Fill=foreground,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0)};panel.Children.Add(mark);}}
   else {
    foreach(var ev in dayEvents.Take(cap)){
     var current=ev;var row=new Grid{Height=18};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(9)});row.ColumnDefinitions.Add(new ColumnDefinition());var dot=new Ellipse{Name="MonthEventDot",Width=3,Height=3,Fill=BrushColor(ev.Color),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};row.Children.Add(dot);var label=Text((ev.AllDay?"全天 ":ev.StartLocal.ToString("HH:mm "))+ev.Title,11,foreground);label.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(label,1);row.Children.Add(label);
     var b=Button("",Tooltip(ev,day),()=>OpenEvent(current));b.Content=row;b.Padding=new Thickness(0,2,0,2);b.MinHeight=20;b.Margin=new Thickness(0);b.HorizontalContentAlignment=HorizontalAlignment.Stretch;panel.Children.Add(b);
    }
    if(dayEvents.Count>cap){var more=Button("还有 "+(dayEvents.Count-cap)+" 项","查看当天全部日程",()=>OpenDay(day));more.HorizontalContentAlignment=HorizontalAlignment.Left;more.Foreground=muted;more.FontSize=11;more.Padding=new Thickness(7,1,0,1);more.MinHeight=18;panel.Children.Add(more);}
   }
   var cell=new Border{BorderBrush=line,BorderThickness=new Thickness(0,mini?0:1,0,0),Child=panel,ClipToBounds=true};dates.Children.Add(cell);
  }
  return root;
 }
 UIElement QuarterView(){
  var grid=new Grid();for(int i=0;i<3;i++)grid.ColumnDefinitions.Add(new ColumnDefinition());DateTime first=CalendarMath.Quarter(anchor);
  for(int i=0;i<3;i++){DateTime month=first.AddMonths(i);var panel=new Grid{Margin=new Thickness(i==0?0:9,4,i==2?0:9,4)};panel.RowDefinitions.Add(new RowDefinition{Height=new GridLength(36)});panel.RowDefinitions.Add(new RowDefinition());var title=Button(month.ToString("M 月"),"打开该月",()=>{int direction=month.CompareTo(anchor);anchor=month;prefs.View="month";QueuePreferencesSave();Navigate(direction==0?-1:direction);});title.HorizontalAlignment=HorizontalAlignment.Left;title.Padding=new Thickness(2);panel.Children.Add(title);var mini=MonthView(month,true);Grid.SetRow(mini,1);panel.Children.Add(mini);Grid.SetColumn(panel,i);grid.Children.Add(panel);}
  return grid;
 }
 UIElement WeekView(){
  weekStart=CalendarMath.Monday(anchor);var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(50)});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());
  var header=new Grid{Margin=new Thickness(0,0,17,0)};header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(38)});for(int i=0;i<7;i++)header.ColumnDefinitions.Add(new ColumnDefinition());
  for(int i=0;i<7;i++){DateTime d=weekStart.AddDays(i);var panel=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center};panel.Children.Add(Text("周"+"日一二三四五六"[(int)d.DayOfWeek],11,muted));var num=Text(d.Day.ToString(),16,d==DateTime.Today?foreground:muted);num.HorizontalAlignment=HorizontalAlignment.Center;panel.Children.Add(num);Grid.SetColumn(panel,i+1);header.Children.Add(panel);}root.Children.Add(header);
  var allDay=new Grid{Margin=new Thickness(0,0,17,4)};allDay.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(38)});for(int i=0;i<7;i++)allDay.ColumnDefinitions.Add(new ColumnDefinition());var caption=Text("全天",10,muted);caption.VerticalAlignment=VerticalAlignment.Top;allDay.Children.Add(caption);
  var events=VisibleEvents().ToList();
  for(int i=0;i<7;i++){DateTime d=weekStart.AddDays(i);var panel=new StackPanel();foreach(var ev in events.Where(e=>e.AllDay&&e.OnDay(d))){var current=ev;var b=Button(ev.Title,Tooltip(ev,d),()=>OpenEvent(current));b.Padding=new Thickness(3,2,3,2);b.FontSize=10;b.HorizontalContentAlignment=HorizontalAlignment.Left;b.MinHeight=23;b.Foreground=BrushColor(ev.Color);panel.Children.Add(b);}var scroll=new ScrollViewer{Content=panel,MaxHeight=100,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(scroll,i+1);allDay.Children.Add(scroll);}Grid.SetRow(allDay,1);root.Children.Add(allDay);
  weekWidth=Math.Max(400,ActualWidth-56);double col=(weekWidth-38)/7;
  double zoom=prefs.TimelineZoom;weekCanvas=new Canvas{Width=weekWidth,Height=1440*zoom};
  for(int h=0;h<=24;h++){double y=h*60*zoom;var label=Text(h.ToString("00")+":00",10,muted);Canvas.SetTop(label,y+2);Canvas.SetLeft(label,0);weekCanvas.Children.Add(label);var l=new Line{X1=38,X2=weekWidth,Y1=y,Y2=y,Stroke=line,StrokeThickness=1};weekCanvas.Children.Add(l);}
  for(int i=0;i<=7;i++)weekCanvas.Children.Add(new Line{X1=38+i*col,X2=38+i*col,Y1=0,Y2=1440*zoom,Stroke=line,StrokeThickness=1});
  for(int i=0;i<7;i++){DateTime day=weekStart.AddDays(i);foreach(var seg in Overlaps.Arrange(events,day)){
   var ev=seg.Event;var block=new StackPanel();var title=Text(ev.Title,11,foreground);title.TextWrapping=TextWrapping.Wrap;block.Children.Add(title);if(seg.End-seg.Start>35)block.Children.Add(Text(CalendarMath.TimeText(ev,day),10,muted));
   var b=Button("",Tooltip(ev,day),()=>OpenEvent(ev));b.Content=block;b.HorizontalContentAlignment=HorizontalAlignment.Stretch;b.Padding=new Thickness(4,2,3,2);b.Margin=new Thickness(0);b.MinHeight=0;b.Width=Math.Max(8,col/seg.Count-5);b.Height=ev.IsPoint?22:Math.Max(8,(seg.End-seg.Start)*zoom-2);b.Background=new SolidColorBrush(Color.FromArgb(30,128,160,185));b.BorderBrush=BrushColor(ev.Color);b.BorderThickness=new Thickness(2,0,0,0);b.ClipToBounds=true;
   Canvas.SetLeft(b,38+i*col+seg.Lane*col/seg.Count+2);Canvas.SetTop(b,seg.Start*zoom);weekCanvas.Children.Add(b);
  }}
  nowLine=new Line{Stroke=foreground,StrokeThickness=1,IsHitTestVisible=false};weekCanvas.Children.Add(nowLine);UpdateNow();
  var viewer=new ScrollViewer{Name="TimelineViewer",Content=weekCanvas,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};timelineViewer=viewer;double minute=timelineMinute;bool ready=false;viewer.Loaded+=delegate{if(!ready){viewer.ScrollToVerticalOffset(minute*zoom);ready=true;}};viewer.ScrollChanged+=delegate{if(ready&&viewer.IsLoaded&&timelineViewer==viewer)timelineMinute=viewer.VerticalOffset/zoom;};viewer.PreviewMouseWheel+=delegate(object sender,System.Windows.Input.MouseWheelEventArgs e){if((System.Windows.Input.Keyboard.Modifiers&System.Windows.Input.ModifierKeys.Control)==0)return;e.Handled=true;ZoomTimeline(e.Delta,e.GetPosition(viewer).Y);};Grid.SetRow(viewer,2);root.Children.Add(viewer);return root;
 }
 internal void ZoomTimeline(int delta,double pointerY){if(timelineViewer==null)return;double old=prefs.TimelineZoom,next=TimelineScale.Zoom(old,delta);if(next==old)return;double minute=(timelineViewer.VerticalOffset+pointerY)/old;timelineMinute=Math.Max(0,minute-pointerY/next);prefs.TimelineZoom=next;RenderContent();Save();}
 void UpdateNow(){if(nowLine==null)return;int index=(DateTime.Today-weekStart).Days;nowLine.Visibility=index>=0&&index<7?Visibility.Visible:Visibility.Collapsed;if(index<0||index>=7)return;double col=(weekWidth-38)/7;nowLine.X1=38+index*col;nowLine.X2=nowLine.X1+col;nowLine.Y1=nowLine.Y2=DateTime.Now.TimeOfDay.TotalMinutes*prefs.TimelineZoom;}
 string Tooltip(CalendarEvent e,DateTime day){return e.Title+"\n"+CalendarMath.TimeText(e,day)+(string.IsNullOrEmpty(e.Location)?"":"\n"+e.Location)+"\n"+e.CalendarName;}
 Window Dialog(string title,double width,double height){
  var w=new Window{Title=title,Owner=this,Width=width,Height=height,MinWidth=340,MinHeight=260,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brushes.Transparent,Foreground=foreground,FontFamily=FontFamily,FontSize=12,Resources=Resources,ShowInTaskbar=false};
  WindowChrome.SetWindowChrome(w,new WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(5),GlassFrameThickness=new Thickness(-1),UseAeroCaptionButtons=false});
  w.SourceInitialized+=delegate{Native.WidgetStyle(new WindowInteropHelper(w).Handle);AttachMaterialHook(w);ApplyDialogMaterial(w);};
  var layout=new DockPanel();var caption=new Grid{Height=40,Margin=new Thickness(14,0,6,0)};caption.ColumnDefinitions.Add(new ColumnDefinition());caption.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var label=Text(title,12,foreground);label.VerticalAlignment=VerticalAlignment.Center;label.Background=Brushes.Transparent;label.MouseLeftButtonDown+=delegate{try{w.DragMove();}catch(InvalidOperationException){}};caption.Children.Add(label);var close=Icon("\uE8BB","关闭",w.Close);Grid.SetColumn(close,1);caption.Children.Add(close);DockPanel.SetDock(caption,Dock.Top);layout.Children.Add(caption);layout.Children.Add(new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch});var frame=new Border{CornerRadius=new CornerRadius(0),BorderThickness=new Thickness(0),BorderBrush=Brushes.Transparent,Background=MaterialBrush(),Child=layout,SnapsToDevicePixels=true,UseLayoutRounding=true};w.Tag=frame;w.Content=frame;
  return w;
 }
 void SetDialogContent(Window w,UIElement body){var layout=(DockPanel)((Border)w.Tag).Child;((ContentControl)layout.Children[layout.Children.Count-1]).Content=body;}
 TextBlock Wrap(string text,double size,Brush color){var t=Text(text,size,color);t.TextWrapping=TextWrapping.Wrap;t.TextTrimming=TextTrimming.None;return t;}
 void OpenEvent(CalendarEvent ev){
  if(dayDetail!=null)dayDetail.Close();
  if(eventDetail!=null)eventDetail.Close();
  var w=Dialog("日程详情",430,430);var panel=new StackPanel{Margin=new Thickness(22)};panel.Children.Add(Text(ev.CalendarName,12,muted));var title=Wrap(ev.Title,22,foreground);title.Margin=new Thickness(0,14,0,20);panel.Children.Add(title);
  panel.Children.Add(Wrap(ev.AllDay?ev.StartLocal.ToString("yyyy年M月d日")+" – "+ev.EndLocal.AddDays(-1).ToString("M月d日")+" · 全天":ev.StartLocal.ToString("yyyy年M月d日 HH:mm")+" – "+ev.EndLocal.ToString("M月d日 HH:mm"),13,foreground));
  panel.Children.Add(Wrap("时间按 Windows 本地时区显示",11,muted));if(!string.IsNullOrEmpty(ev.Location)){var location=Wrap(ev.Location,13,foreground);location.Margin=new Thickness(0,14,0,0);panel.Children.Add(location);}
  if(!string.IsNullOrEmpty(ev.Description)){var description=Wrap(ev.Description,12,muted);description.Margin=new Thickness(0,18,0,18);panel.Children.Add(description);}
  var open=Button("在 Google 日历中打开","打开原始日程",()=>{if(demo){MessageBox.Show(w,"此事件为示例。","日历");return;}OpenUrl(ev.Link);});open.HorizontalAlignment=HorizontalAlignment.Left;open.Margin=new Thickness(0,18,0,0);panel.Children.Add(open);AddDeleteAction(panel,w,ev);SetDialogContent(w,new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});eventDetail=w;
  CloseDetailOnFocusLoss(w);
  w.KeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e){if(e.Key==System.Windows.Input.Key.Escape){e.Handled=true;w.Close();}};
  w.Closed+=delegate{if(eventDetail==w)eventDetail=null;};w.Show();w.Activate();
 }
 void OpenDay(DateTime day){
  if(eventDetail!=null)eventDetail.Close();
  if(dayDetail!=null)dayDetail.Close();
  var w=Dialog(day.ToString("M月d日 dddd",CultureInfo.GetCultureInfo("zh-CN")),420,470);var panel=new StackPanel{Margin=new Thickness(18)};var events=CalendarMath.ForDay(VisibleEvents(),day);
  var add=Icon("\uE710","新增当天日程",()=>{w.Close();OpenCreateEvent(day);});add.Name="AddDayEvent";var caption=(Grid)((DockPanel)((Border)w.Tag).Child).Children[0];caption.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var close=caption.Children.OfType<Button>().Single();Grid.SetColumn(close,2);Grid.SetColumn(add,1);caption.Children.Add(add);
  FillDayEvents(panel,w,day);
  SetDialogContent(w,new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});dayDetail=w;CloseDetailOnFocusLoss(w);w.Closed+=delegate{if(dayDetail==w)dayDetail=null;};w.Show();w.Activate();
 }
 void CloseDetailOnFocusLoss(Window detail){
  detail.Deactivated+=delegate{Dispatcher.BeginInvoke(new Action(()=>{
   if(!detail.IsVisible||detail.IsActive||detail.OwnedWindows.Cast<Window>().Any(w=>w.IsVisible))return;
   detail.Close();if(!rendering)Dispatcher.BeginInvoke(new Action(DismissIfOutside),DispatcherPriority.ContextIdle);
  }),DispatcherPriority.Background);};
 }
 void OpenUrl(string url){Uri u;if(Uri.TryCreate(url,UriKind.Absolute,out u)&&u.Scheme=="https"&&(u.Host=="calendar.google.com"||u.Host=="www.google.com"))Process.Start(new ProcessStartInfo(u.AbsoluteUri){UseShellExecute=true});}
 void Section(StackPanel p,string title){var t=Text(title,14,foreground);t.Margin=new Thickness(0,22,0,10);p.Children.Add(t);}
 CheckBox Check(string label,bool value,Action<bool> changed){var c=new CheckBox{Content=label,IsChecked=value,Foreground=foreground,Margin=new Thickness(0,6,0,6)};c.SetResourceReference(Control.ForegroundProperty,"ControlText");c.Checked+=delegate{changed(true);};c.Unchecked+=delegate{changed(false);};return c;}
 void OpenSettings(){
  var w=Dialog("设置",460,680);var p=new StackPanel{Margin=new Thickness(22)};
  var calendarChoices=new StackPanel();
  Section(p,"Google 日历");var accountStatus=Wrap(demo?"示例模式，不会连接真实账号。":authExpired?"登录已过期 · 请重新连接":google!=null&&google.Connected?(google.CanWrite?"已连接 · 可新增和删除日程":"已连接 · 请更新 Google 授权"):"未连接 · 在浏览器中登录 Google",12,muted);p.Children.Add(accountStatus);
  if(google!=null){
   var login=SettingsGoogleLoginButton(()=>{if(disconnecting||busy)return;w.Close();Dispatcher.BeginInvoke(new Action(BeginLogin));});login.Name="SettingsGoogleLogin";login.IsEnabled=!demo&&!disconnecting&&!busy;login.Visibility=!google.Connected||!google.CanWrite||authExpired?Visibility.Visible:Visibility.Collapsed;p.Children.Add(login);
   if(busy)p.Children.Add(SettingsButton("取消登录 / 同步","取消当前操作",()=>{if(operation!=null)operation.Cancel();w.Close();}));
   if(google.Connected){Button logout=null;logout=SettingsButton("断开账号","清除本机的令牌及日程缓存",async()=>{if(disconnecting)return;login.IsEnabled=false;logout.IsEnabled=false;accountStatus.Text="正在断开…";try{await DisconnectAsync();accountStatus.Text="已断开 · 日程缓存已清除";login.Visibility=Visibility.Visible;logout.Visibility=Visibility.Collapsed;calendarChoices.Children.Clear();calendarChoices.Children.Add(Text("登录后可选择日历",12,muted));}catch(Exception ex){accountStatus.Text="断开未完成";MessageBox.Show(w,Friendly(ex),"断开账号");logout.IsEnabled=true;}finally{login.IsEnabled=!demo&&!busy;}});logout.Margin=new Thickness(0,8,0,0);logout.IsEnabled=!demo&&!disconnecting;p.Children.Add(logout);}
  }else {p.Children.Add(SettingsButton("重置本地登录信息","删除无法读取的本地凭据",()=>{store.Delete("tokens.bin");store.Delete("client.bin");MessageBox.Show(w,"已清除。请退出并重新打开应用。","日历");}));}
  p.Children.Add(SettingsButton("登录帮助","打开随附的 Google 登录说明",()=>{string path=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"Google登录步骤.html");if(File.Exists(path))Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}));
  Section(p,"显示的日历");bool selectionChanged=false;
  if(cache.Calendars.Count==0)calendarChoices.Children.Add(Text("登录后可选择日历",12,muted));
  foreach(var c in cache.Calendars){var cal=c;var check=Check(c.Name,demo||prefs.Selected.Contains(c.Id),selected=>{selectionChanged=true;prefs.SelectionSaved=true;if(selected&&!prefs.Selected.Contains(cal.Id))prefs.Selected.Add(cal.Id);if(!selected)prefs.Selected.Remove(cal.Id);Save();});check.IsEnabled=!demo;check.ToolTip=c.Id;calendarChoices.Children.Add(check);}p.Children.Add(calendarChoices);
  Section(p,"窗口");
  p.Children.Add(Wrap("点击托盘图标展开；点击外部或按 Esc 收起。",12,muted));
  p.Children.Add(Check("锁定位置与尺寸",prefs.Locked,SetLocked));
  p.Children.Add(Check("开机启动",prefs.Startup,v=>{if(demo)return;try{SetStartup(v);prefs.Startup=v;Save();}catch(Exception ex){MessageBox.Show(w,ex.Message,"无法设置开机启动");}}));
  var theme=new ComboBox{Margin=new Thickness(0,8,0,8),MinHeight=27};theme.Items.Add("黑色");theme.Items.Add("浅色");theme.SelectedIndex=prefs.Theme=="light"?1:0;theme.SelectionChanged+=delegate{prefs.Theme=theme.SelectedIndex==1?"light":"dark";Build();Render();ApplyMaterial();Save();};p.Children.Add(Text("主题",12,muted));p.Children.Add(theme);
  p.Children.Add(Check("毛玻璃背景",prefs.Glass,v=>{prefs.Glass=v;ApplyMaterial();Save();}));
  var tintLabel=Text("背景不透明度："+prefs.Tint+"%",12,muted);p.Children.Add(tintLabel);var slider=new Slider{Minimum=35,Maximum=85,Value=prefs.Tint,TickFrequency=1,IsSnapToTickEnabled=true,Margin=new Thickness(0,8,0,8)};slider.ValueChanged+=delegate{prefs.Tint=(int)slider.Value;tintLabel.Text="背景不透明度："+prefs.Tint+"%";ApplySurface();saveTimer.Stop();saveTimer.Start();};p.Children.Add(slider);
  Section(p,"节电策略");p.Children.Add(Wrap("显示时每 5 分钟同步；使用电池时每 15 分钟；隐藏时每 30 分钟。锁屏、休眠时暂停同步。周视图时间线每分钟更新，不持续重绘。系统省电模式下使用实色背景。",12,muted));
  p.Children.Add(Wrap("平时只显示右下角托盘图标。托盘菜单中选择「退出」可结束程序。",11,muted));
  AddUpdateSettings(p,w);
  var done=SettingsButton("完成","保存并关闭设置",()=>w.Close());done.Margin=new Thickness(0,20,0,0);p.Children.Add(done);SetDialogContent(w,new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});w.ShowDialog();if(selectionChanged){Render();if(!demo)Dispatcher.BeginInvoke(new Action(async()=>await SyncAsync(true)));}
 }
 async void BeginLogin(){
  if(demo||google==null||busy||disconnecting)return;if(!google.Configured){MessageBox.Show(this,"应用缺少 Google 登录配置，请使用完整安装包。","无法登录");return;}
  busy=true;operation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);SetStatus("请在浏览器中完成登录 · 设置中可取消");poll.Stop();
  try{await google.LoginAsync(operation.Token);authExpired=false;failures=0;metadataRead=DateTime.MinValue;cache=new CacheData();prefs.Selected.Clear();prefs.SelectionSaved=false;store.Delete("calendar.bin");Save();SetStatus("已登录，正在读取日历…");}
  catch(OperationCanceledException){SetStatus("登录已取消或超时");}
  catch(Exception ex){SetStatus("登录未完成");MessageBox.Show(this,Friendly(ex),"Google 登录");}
  finally{busy=false;operation.Dispose();operation=null;}
  if(google.Connected&&!authExpired)await SyncAsync(true);else Schedule();
 }
 async Task DisconnectAsync(){
  if(google==null||disconnecting)return;disconnecting=true;
  try{poll.Stop();pendingNavigation=false;if(operation!=null)operation.Cancel();string refresh=google.RefreshToken;google.Clear();cache=new CacheData();prefs.Selected.Clear();prefs.SelectionSaved=false;authExpired=false;failures=0;retryAfter=DateTime.MinValue;metadataRead=DateTime.MinValue;store.Delete("calendar.bin");Save();Render();await google.RevokeAsync(refresh);}
  finally{disconnecting=false;Render();Schedule();}
 }
 async void SyncFromButton(){await SyncAsync(true);}
 Tuple<DateTime,DateTime> Range(){DateTime start=prefs.View=="week"?CalendarMath.Monday(anchor):prefs.View=="quarter"?CalendarMath.Quarter(anchor):CalendarMath.Month(anchor);DateTime end=prefs.View=="week"?start.AddDays(7):prefs.View=="quarter"?start.AddMonths(3):start.AddMonths(1);return Tuple.Create(start.AddDays(-7),end.AddDays(7));}
 async Task SyncAsync(bool manual){
  if(demo||google==null||!google.Connected||authExpired||suspended||sessionLocked||lifetime.IsCancellationRequested)return;
  if(!manual&&DateTime.UtcNow<retryAfter){Schedule();return;}
  if(DateTime.UtcNow<retryAfter){SetStatus("请稍后再试 · "+retryAfter.ToLocalTime().ToString("HH:mm")+" 后重试");return;}
  if(!await syncGate.WaitAsync(0))return;
  if(busy){syncGate.Release();return;}
  busy=true;pendingNavigation=false;poll.Stop();lastSyncRequest=DateTime.UtcNow;var range=Range();operation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var ct=operation.Token;
  bool succeeded=false;int completed=0,failed=0;string error="";
  SetStatus("正在同步…");
  try{
   if(!NetworkInterface.GetIsNetworkAvailable())throw new Exception("未联网，保留上次同步的日程。 ");
   bool reloadMetadata=manual||cache.Calendars.Count==0||DateTime.UtcNow-metadataRead>TimeSpan.FromHours(1);
   var calendars=reloadMetadata?await google.CalendarsAsync(ct):cache.Calendars;ct.ThrowIfCancellationRequested();
   if(!prefs.SelectionSaved){prefs.Selected=calendars.Where(c=>c.Primary).Select(c=>c.Id).ToList();if(prefs.Selected.Count==0&&calendars.Count>0)prefs.Selected.Add(calendars[0].Id);prefs.SelectionSaved=true;Save();}
   prefs.Selected=prefs.Selected.Where(id=>calendars.Any(c=>c.Id==id)).ToList();cache.Calendars=calendars;
   if(reloadMetadata){try{await google.ColorsAsync(ct);metadataRead=DateTime.UtcNow;}catch(LoginRequiredException){throw;}catch(RetryException){throw;}catch(OperationCanceledException){throw;}catch(HttpRequestException){}}
   foreach(var cal in calendars.Where(c=>prefs.Selected.Contains(c.Id))){
    try{
     var fresh=await google.EventsAsync(cal,range.Item1,range.Item2,ct);ct.ThrowIfCancellationRequested();
     cache.Events=cache.Events.Where(e=>e.CalendarId!=cal.Id||!e.InRange(range.Item1,range.Item2)).Concat(fresh).GroupBy(e=>e.CalendarId+"\n"+e.Id).Select(g=>g.Last()).ToList();cache.Updated[cal.Id]=DateTime.UtcNow.ToString("o");completed++;
    }catch(LoginRequiredException){throw;}catch(RetryException){throw;}catch(OperationCanceledException){throw;}catch(Exception ex){failed++;error=Friendly(ex);}
   }
   cache.Events=cache.Events.Where(e=>calendars.Any(c=>c.Id==e.CalendarId)).ToList();ct.ThrowIfCancellationRequested();store.Write("calendar.bin",cache,true);succeeded=failed==0;
   if(failed>0)SetStatus("部分日历同步失败（"+failed+" 个），保留旧日程");
  }catch(LoginRequiredException){authExpired=true;error="登录已过期，请在设置中重新登录。";}
   catch(RetryException ex){retryAfter=DateTime.UtcNow.Add(ex.Delay);error=ex.Message;}
   catch(OperationCanceledException){error="同步已取消";}
   catch(Exception ex){error=Friendly(ex);}
  finally{
   busy=false;if(operation!=null){operation.Dispose();operation=null;}syncGate.Release();failures=succeeded?0:failures+1;Render();if(google.Connected&&!succeeded&&error!="")SetStatus((failed>0?"部分日历同步失败 · ":"")+error);Schedule();if(pendingNavigation){pendingNavigation=false;QueueSync();}
  }
 }
 string Friendly(Exception ex){if(ex is System.Net.Http.HttpRequestException)return "无法连接 Google，请检查网络或系统代理。";if(ex is TaskCanceledException)return "请求超时，请稍后重试。";return ex.Message;}
 static bool Battery(){return Forms.SystemInformation.PowerStatus.PowerLineStatus!=Forms.PowerLineStatus.Online;}
 static bool BatterySaving(){Native.PowerStatus p;return Native.GetSystemPowerStatus(out p)&&p.Saver!=0;}
 void Schedule(){
  poll.Stop();midnight.Stop();ScheduleClock();
  if(exiting||lifetime.IsCancellationRequested||suspended||sessionLocked)return;
  midnight.Interval=DateTime.Today.AddDays(1)-DateTime.Now+TimeSpan.FromSeconds(1);midnight.Start();
  if(demo||google==null||!google.Connected||authExpired||busy||(prefs.SelectionSaved&&prefs.Selected.Count==0))return;
  var delay=TimeSpan.FromMinutes(CalendarMath.PollMinutes(IsVisible,Battery()||BatterySaving(),failures));if(retryAfter>DateTime.UtcNow&&retryAfter-DateTime.UtcNow>delay)delay=retryAfter-DateTime.UtcNow;poll.Interval=delay;poll.Start();
 }
 void ScheduleClock(){clock.Stop();if(IsVisible&&prefs.View=="week"&&!suspended&&!sessionLocked){clock.Interval=TimeSpan.FromSeconds(60-DateTime.Now.Second);clock.Start();}}
 void OnPower(object sender,PowerModeChangedEventArgs e){Dispatcher.BeginInvoke(new Action(()=>{if(e.Mode==PowerModes.Suspend){StopMotion();ClearPreparedViews();suspended=true;poll.Stop();clock.Stop();midnight.Stop();if(operation!=null)operation.Cancel();}else{bool was=suspended;suspended=false;ApplyMaterial();Schedule();ResumeUpdateChecks();if(was&&IsVisible&&!busy)SyncFromButton();}}));}
 void OnSession(object sender,SessionSwitchEventArgs e){Dispatcher.BeginInvoke(new Action(()=>{sessionLocked=e.Reason==SessionSwitchReason.SessionLock;if(sessionLocked){StopMotion();ClearPreparedViews();poll.Stop();clock.Stop();midnight.Stop();if(operation!=null)operation.Cancel();}else{Schedule();ResumeUpdateChecks();if(IsVisible&&!busy)SyncFromButton();}}));}
 void OnNetwork(object sender,NetworkAvailabilityEventArgs e){if(e.IsAvailable)Dispatcher.BeginInvoke(new Action(()=>{if(!busy&&!suspended&&!sessionLocked&&IsVisible&&DateTime.UtcNow-lastSyncRequest>TimeSpan.FromSeconds(30))SyncFromButton();}));}
 void OnDisplays(object sender,EventArgs e){Dispatcher.BeginInvoke(new Action(()=>{StopMotion();if(Left+80>SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth||Top+80>SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight||Left+Width<SystemParameters.VirtualScreenLeft||Top+Height<SystemParameters.VirtualScreenTop){Left=SystemParameters.WorkArea.Left+30;Top=SystemParameters.WorkArea.Top+30;}Save();}));}
 void OnPreferences(object sender,UserPreferenceChangedEventArgs e){Dispatcher.BeginInvoke(new Action(()=>{Build();Render();ApplyMaterial();}));}
 void CreateTray(){
  tray=new Forms.NotifyIcon{Text="日历",Icon=System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location),Visible=true};
  var menu=new Forms.ContextMenuStrip();menu.Items.Add("打开日历",null,delegate{Dispatcher.BeginInvoke(new Action(ShowCalendar));});menu.Items.Add("刷新",null,delegate{Dispatcher.BeginInvoke(new Action(SyncFromButton));});menu.Items.Add("设置",null,delegate{Dispatcher.BeginInvoke(new Action(()=>{ShowCalendar();OpenSettings();}));});menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("退出",null,delegate{Dispatcher.BeginInvoke(new Action(Exit));});tray.ContextMenuStrip=menu;
  tray.MouseClick+=delegate(object sender,Forms.MouseEventArgs e){if(e.Button==Forms.MouseButtons.Left)Dispatcher.BeginInvoke(new Action(TogglePopup));};
 }
 public void StartInTray(){
  if(started)return;started=true;new WindowInteropHelper(this).EnsureHandle();Render();Schedule();BeginUpdateChecks();
  if(!rendering&&!demo&&google!=null&&google.Connected)Dispatcher.BeginInvoke(new Action(async()=>await SyncAsync(false)));
 }
 internal void TogglePopup(){if(IsVisible){HidePopup();return;}if(DateTime.UtcNow-lastAutoDismiss<TimeSpan.FromMilliseconds(250))return;ShowCalendar();}
 public void ShowCalendar(){
  if(exiting)return;IntPtr hwnd=new WindowInteropHelper(this).EnsureHandle();var cursor=Forms.Cursor.Position;var screen=Forms.Screen.FromPoint(cursor);double scale=Native.MonitorScale(cursor);
  var work=screen.WorkingArea;int gap=(int)(8*scale);MinWidth=Math.Min(480,Math.Max(100,(work.Width-gap*2)/scale));MinHeight=Math.Min(480,Math.Max(100,(work.Height-gap*2)/scale));
  Width=Math.Min(Width,(work.Width-gap*2)/scale);Height=Math.Min(Height,(work.Height-gap*2)/scale);
  var bounds=PopupPlacement.Bounds(work,(int)Math.Round(Width*scale),(int)Math.Round(Height*scale),cursor,gap);
  WindowStartupLocation=WindowStartupLocation.Manual;Topmost=false;WindowState=WindowState.Normal;
  bool opening=!IsVisible;popupRise.Stop(false);bool animate=opening&&MotionAllowed;if(animate)popupRise.Start(bounds,Math.Max(12,(int)Math.Round(20*scale)),r=>Native.Position(hwnd,r));else Native.Position(hwnd,bounds);if(opening)Show();if(!animate)Native.Position(hwnd,bounds);Activate();ApplyMaterial();if(!animate)QueuePreparedViews();
 }
 internal void HidePopup(){if(!IsVisible)return;StopMotion();ClearPreparedViews();Save();Hide();Schedule();}
 void DismissIfOutside(){
  if(exiting||!IsVisible||IsActive||IsKeyboardFocusWithin||OwnedWindows.Cast<Window>().Any(w=>w.IsVisible))return;
  IntPtr foreground=Native.GetForegroundWindow();if(foreground==IntPtr.Zero||foreground==new WindowInteropHelper(this).Handle)return;
  uint process;Native.GetWindowThreadProcessId(foreground,out process);if(process==0||process==(uint)Process.GetCurrentProcess().Id)return;
  lastAutoDismiss=DateTime.UtcNow;HidePopup();
 }
 void SetStartup(bool enabled){using(var key=Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")){if(enabled)key.SetValue("DeskCalendar","\""+Assembly.GetExecutingAssembly().Location+"\"");else key.DeleteValue("DeskCalendar",false);}}
 void Save(){if(!IsLoaded)return;prefs.Left=Left;prefs.Top=Top;prefs.Width=ActualWidth;prefs.Height=ActualHeight;try{store.Write("settings.json",prefs,false);}catch(IOException){SetStatus("窗口设置未保存，检查目录权限");}catch(UnauthorizedAccessException){SetStatus("窗口设置未保存，检查目录权限");}}
 void OnClosing(object sender,CancelEventArgs e){if(!exiting){e.Cancel=true;HidePopup();return;}StopMotion();ClearPreparedViews();Save();poll.Stop();clock.Stop();midnight.Stop();resize.Stop();saveTimer.Stop();updateTimer.Stop();lifetime.Cancel();updater.Dispose();if(operation!=null)operation.Cancel();if(tray!=null){tray.Visible=false;var icon=tray.Icon;tray.Dispose();if(icon!=null)icon.Dispose();}SystemEvents.PowerModeChanged-=OnPower;SystemEvents.SessionSwitch-=OnSession;SystemEvents.DisplaySettingsChanged-=OnDisplays;SystemEvents.UserPreferenceChanged-=OnPreferences;NetworkChange.NetworkAvailabilityChanged-=OnNetwork;}
 public void Exit(){exiting=true;Close();Application.Current.Shutdown();}
 bool SystemTransparency(){try{using(var key=Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize"))return key==null||Convert.ToInt32(key.GetValue("EnableTransparency",1))!=0;}catch{return true;}}
 bool GlassEnabled(){return prefs.Glass&&!BatterySaving()&&!SystemParameters.HighContrast&&SystemTransparency();}
 void AttachMaterialHook(Window w){var source=HwndSource.FromHwnd(new WindowInteropHelper(w).Handle);if(source!=null)source.AddHook(delegate(IntPtr hwnd,int message,IntPtr param,IntPtr data,ref bool handled){if(w==this&&prefs.Locked&&message==0x112&&(((long)param&0xFFF0)==0xF000||((long)param&0xFFF0)==0xF010)){handled=true;return IntPtr.Zero;}if(message==0x86&&GlassEnabled()){handled=true;return Native.DefWindowProc(hwnd,message,new IntPtr(1),new IntPtr(-1));}return IntPtr.Zero;});}
 Brush MaterialBrush(){if(SystemParameters.HighContrast)return SystemColors.WindowBrush;byte alpha=GlassEnabled()?(byte)(CalendarMath.Clip(prefs.Tint,35,85)*255/100):(byte)255;return new SolidColorBrush(prefs.Theme=="light"?Color.FromArgb(alpha,240,240,237):Color.FromArgb(alpha,8,10,12));}
 void PaintWindowSurface(Border frame){if(frame==null)return;frame.Background=MaterialBrush();frame.BorderBrush=Brushes.Transparent;frame.BorderThickness=new Thickness(0);frame.CornerRadius=new CornerRadius(0);frame.SnapsToDevicePixels=true;frame.UseLayoutRounding=true;}
 void ApplySurface(){PaintWindowSurface(surface);foreach(Window w in OwnedWindows)ApplyDialogMaterial(w);}
 void ApplyWindowMaterial(Window w){IntPtr hwnd=new WindowInteropHelper(w).Handle;if(hwnd==IntPtr.Zero)return;string state=(prefs.Theme=="light"?"light":"dark")+(GlassEnabled()?":glass":":solid");string previous;if(materialStates.TryGetValue(w,out previous)&&previous==state)return;if(!materialStates.ContainsKey(w))w.Closed+=delegate{materialStates.Remove(w);};materialStates[w]=state;MaterialUpdates++;Native.Dark(hwnd,prefs.Theme!="light");var source=HwndSource.FromHwnd(hwnd);if(source!=null&&source.CompositionTarget!=null)source.CompositionTarget.BackgroundColor=Colors.Transparent;Native.Backdrop(hwnd,GlassEnabled());}
 void ApplyDialogMaterial(Window w){var frame=w.Tag as Border;PaintWindowSurface(frame);w.Foreground=foreground;ApplyWindowMaterial(w);}
 void ApplyMaterial(){ApplySurface();ApplyWindowMaterial(this);}
 public void RenderTo(string directory){Directory.CreateDirectory(directory);foreach(string view in new[]{"month","week","quarter"}){prefs.View=view;Build();Render();UpdateLayout();var frame=new DispatcherFrame();Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);UpdateLayout();var bmp=new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(surface);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));using(var file=File.Create(System.IO.Path.Combine(directory,view+".png")))encoder.Save(file);} }
}

public static class TimelineScale {
 public static double Normalize(double value){return double.IsNaN(value)||double.IsInfinity(value)?1:Math.Max(.5,Math.Min(3,value));}
 public static double Zoom(double current,int wheel){return Normalize(current*Math.Pow(1.1,wheel/120.0));}
}
public static class PopupPlacement {
 public static System.Drawing.Rectangle Bounds(System.Drawing.Rectangle work,int width,int height,System.Drawing.Point anchor,int gap){
  width=Math.Max(1,Math.Min(width,work.Width-gap*2));height=Math.Max(1,Math.Min(height,work.Height-gap*2));
  int x=work.Right-gap-width;return new System.Drawing.Rectangle(x,work.Bottom-gap-height,width,height);
 }
}
public static class Native {
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
 [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
 [StructLayout(LayoutKind.Sequential)] struct Point{public int X,Y;}
 [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point point,uint flags);
 [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor,int type,out uint x,out uint y);
 public static double MonitorScale(System.Drawing.Point point){try{uint x,y;if(GetDpiForMonitor(MonitorFromPoint(new Point{X=point.X,Y=point.Y},2),0,out x,out y)==0)return x/96.0;}catch(EntryPointNotFoundException){}catch(DllNotFoundException){}return 1;}
 public static void Position(IntPtr hwnd,System.Drawing.Rectangle bounds){SetWindowPos(hwnd,IntPtr.Zero,bounds.X,bounds.Y,bounds.Width,bounds.Height,0x14);}
 [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd,int index);
 [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd,int index,int value);
 public static void WidgetStyle(IntPtr hwnd){
#if !VISUAL_QA
 SetWindowLong(hwnd,-20,(GetWindowLong(hwnd,-20)|0x80)&~0x40000);
#endif
 }
 [StructLayout(LayoutKind.Sequential)] public struct Margins{public int Left,Right,Top,Bottom;}
 [StructLayout(LayoutKind.Sequential)] public struct PowerStatus{public byte AC,BatteryFlag,BatteryPercent,Saver;public int Life,FullLife;}
 [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
 [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd,ref Margins margins);
 [DllImport("kernel32.dll")] public static extern bool GetSystemPowerStatus(out PowerStatus status);
 public static void Dark(IntPtr hwnd,bool dark){int value=dark?1:0;DwmSetWindowAttribute(hwnd,20,ref value,4);int corner=2;DwmSetWindowAttribute(hwnd,33,ref corner,4);SuppressBorder(hwnd);}
 [DllImport("user32.dll")] public static extern IntPtr DefWindowProc(IntPtr hwnd,int message,IntPtr param,IntPtr data);
 [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd,int attribute,out int value,int size);
 public static bool SuppressBorder(IntPtr hwnd){int border=unchecked((int)0xFFFFFFFE);return DwmSetWindowAttribute(hwnd,34,ref border,4)==0;}
 public static int BackdropType(IntPtr hwnd){int value;return DwmGetWindowAttribute(hwnd,38,out value,4)==0?value:-1;}
 public static void Backdrop(IntPtr hwnd,bool glass){int backdrop=glass?3:1;DwmSetWindowAttribute(hwnd,38,ref backdrop,4);var m=new Margins{Left=-1,Right=-1,Top=-1,Bottom=-1};DwmExtendFrameIntoClientArea(hwnd,ref m);}
}
}

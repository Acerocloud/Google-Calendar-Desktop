using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Runtime.InteropServices;
using Forms=System.Windows.Forms;
namespace DeskCalendar {
public partial class CalendarWindow {
 Window trayMenu;
 void AttachDialogRise(Window window){
  var rise=new PopupRise{DurationMilliseconds=160};window.Loaded+=delegate{if(!MotionAllowed)return;NativeRect rect;if(!GetWindowRect(new WindowInteropHelper(window).Handle,out rect))return;var bounds=new System.Drawing.Rectangle(rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top);var source=PresentationSource.FromVisual(window);double scale=source==null?1:source.CompositionTarget.TransformToDevice.M22;rise.Start(bounds,(int)Math.Round(8*scale),r=>Native.Position(new WindowInteropHelper(window).Handle,r));};
  window.PreviewMouseDown+=delegate{rise.Stop(true);};window.Closing+=delegate{rise.Stop(true);};window.Closed+=delegate{rise.Stop(false);};
 }
 [StructLayout(LayoutKind.Sequential)] struct NativeRect {public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
 void ShowTrayMenu(){
  if(exiting)return;if(trayMenu!=null){trayMenu.Close();return;}var w=new Window{Title="日历菜单",Owner=this,Width=216,SizeToContent=SizeToContent.Height,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Topmost=true,Background=Brushes.Transparent,Foreground=foreground,FontFamily=FontFamily,Resources=Resources,WindowStartupLocation=WindowStartupLocation.Manual};trayMenu=w;
  WindowChrome.SetWindowChrome(w,new WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(0),GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false});var rows=new StackPanel{Margin=new Thickness(6)};
  Action<string,string,Action,bool> add=(glyph,label,action,enabled)=>{var b=Button("",label,()=>{w.Close();action();});b.MinHeight=36;b.Margin=new Thickness(0,1,0,1);b.Padding=new Thickness(10,6,10,6);b.HorizontalContentAlignment=HorizontalAlignment.Stretch;b.IsEnabled=enabled;var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(28)});row.ColumnDefinitions.Add(new ColumnDefinition());row.Children.Add(new TextBlock{Text=glyph,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=14,VerticalAlignment=VerticalAlignment.Center,Foreground=foreground});var text=Text(label,13,foreground);Grid.SetColumn(text,1);row.Children.Add(text);b.Content=row;rows.Children.Add(b);};
  add("\uE787","打开",ShowCalendar,true);add("\uE72C","刷新",SyncFromButton,!busy&&!demo&&google!=null&&google.Connected);add("\uE713","设置",()=>{ShowCalendar();OpenSettings();},true);rows.Children.Add(new Border{Height=1,Background=line,Margin=new Thickness(10,5,10,5)});add("\uE7E8","退出",Exit,true);
  var frame=new Border{Child=rows,SnapsToDevicePixels=true,UseLayoutRounding=true};w.Content=frame;w.Tag=frame;w.SourceInitialized+=delegate{Native.WidgetStyle(new WindowInteropHelper(w).Handle);AttachMaterialHook(w);ApplyDialogMaterial(w);};bool closing=false;w.Closing+=delegate{closing=true;};w.Deactivated+=delegate{if(!closing)w.Close();};w.Closed+=delegate{if(trayMenu==w)trayMenu=null;};w.KeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e){if(e.Key==System.Windows.Input.Key.Escape){w.Close();e.Handled=true;}};InputFeedback.Attach(w);AttachDialogRise(w);
  frame.Measure(new Size(w.Width,double.PositiveInfinity));double height=frame.DesiredSize.Height;w.SizeToContent=SizeToContent.Manual;w.Height=height;var cursor=Forms.Cursor.Position;var screen=Forms.Screen.FromPoint(cursor);double scale=Native.MonitorScale(cursor);var work=screen.WorkingArea;w.Left=Math.Max(work.Left/scale+8,Math.Min(cursor.X/scale-w.Width+12,work.Right/scale-w.Width-8));w.Top=Math.Max(work.Top/scale+8,work.Bottom/scale-height-12);w.Show();w.Activate();
 }
}
}

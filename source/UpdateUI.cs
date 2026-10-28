using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DeskCalendar {
public partial class CalendarWindow {
 readonly UpdateService updater=new UpdateService();
 readonly DispatcherTimer updateTimer=new DispatcherTimer(DispatcherPriority.Background);
 bool checkingUpdates;Window updatePrompt;TextBlock updateStatus;string updateMessage="尚未检查",notifiedUpdate;DateTime updateCheckedAt=DateTime.MinValue;
 ReleaseUpdate availableUpdate;
 Button updateCheckIcon;
 void UpdateCheckIcon(){
  if(updateCheckIcon==null)return;updateCheckIcon.IsEnabled=!checkingUpdates&&!demo&&!rendering;
  var glyph=updateCheckIcon.Content as TextBlock;if(glyph==null)return;
  var rotation=glyph.RenderTransform as RotateTransform;if(rotation==null)return;
  rotation.BeginAnimation(RotateTransform.AngleProperty,null);rotation.Angle=0;
  if(checkingUpdates&&MotionAllowed)rotation.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(0,360,TimeSpan.FromMilliseconds(900)){RepeatBehavior=RepeatBehavior.Forever});
 }
 void InitUpdates(){if(demo||rendering)return;updateTimer.Interval=UpdateService.Interval;updateTimer.Tick+=async delegate{await CheckForUpdatesAsync(false);};}
 void BeginUpdateChecks(){if(demo||rendering)return;updateTimer.Start();Dispatcher.BeginInvoke(new Action(async()=>await CheckForUpdatesAsync(false)),DispatcherPriority.ApplicationIdle);}
 void ResumeUpdateChecks(){if(demo||rendering||exiting||suspended||sessionLocked)return;if(availableUpdate!=null&&notifiedUpdate!=availableUpdate.Tag)ShowUpdatePrompt(availableUpdate);if(DateTime.UtcNow-updateCheckedAt>=UpdateService.Interval)Dispatcher.BeginInvoke(new Action(async()=>await CheckForUpdatesAsync(false)),DispatcherPriority.Background);}
 void UpdateMessage(string message){updateMessage=message;if(updateStatus!=null)updateStatus.Text=message;}
 void AddUpdateSettings(StackPanel panel,Window owner){
  Section(panel,"软件更新");var versionRow=new StackPanel{Orientation=Orientation.Horizontal};var version=Text("当前版本 "+UpdateService.Current.ToString(3),12,muted);version.VerticalAlignment=VerticalAlignment.Center;versionRow.Children.Add(version);
  var check=Icon("\uE72C","检查更新",async()=>await CheckForUpdatesAsync(true));check.Name="CheckForUpdates";check.Width=28;check.Height=28;check.Margin=new Thickness(6,0,0,0);check.Cursor=System.Windows.Input.Cursors.Hand;check.Content=new TextBlock{Text="\uE72C",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=13,Foreground=foreground,RenderTransformOrigin=new Point(.5,.5),RenderTransform=new RotateTransform()};updateCheckIcon=check;UpdateCheckIcon();versionRow.Children.Add(check);panel.Children.Add(versionRow);
  var label=Wrap(updateMessage,12,muted);label.Margin=new Thickness(0,6,0,8);panel.Children.Add(label);updateStatus=label;
  owner.Closed+=delegate{if(updateStatus==label)updateStatus=null;if(updateCheckIcon==check){((RotateTransform)((TextBlock)check.Content).RenderTransform).BeginAnimation(RotateTransform.AngleProperty,null);updateCheckIcon=null;}};
 }
 async Task CheckForUpdatesAsync(bool manual){
  if(exiting||(!manual&&(suspended||sessionLocked)))return;
  if(checkingUpdates){if(manual)UpdateMessage("正在检查更新…");return;}
  checkingUpdates=true;UpdateCheckIcon();updateCheckedAt=DateTime.UtcNow;UpdateMessage("正在检查更新…");
  try{
   using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)){
    timeout.CancelAfter(TimeSpan.FromSeconds(30));availableUpdate=await updater.CheckAsync(timeout.Token);
   }
   if(exiting)return;
   if(availableUpdate==null){UpdateMessage("已是最新版本 · "+DateTime.Now.ToString("HH:mm")+" 检查");return;}
   UpdateMessage("可更新至 "+availableUpdate.Version.ToString(3));
   if((manual||notifiedUpdate!=availableUpdate.Tag)&&!suspended&&!sessionLocked)ShowUpdatePrompt(availableUpdate);
  }catch(OperationCanceledException){if(!exiting)UpdateMessage("检查超时，请稍后重试");}
   catch(Exception ex){if(!exiting)UpdateMessage(ex is System.Net.Http.HttpRequestException?"无法连接 GitHub，请检查网络后重试":ex.Message);}
  finally{checkingUpdates=false;UpdateCheckIcon();}
 }
 void ShowUpdatePrompt(ReleaseUpdate release){
  if(updatePrompt!=null){updatePrompt.Activate();return;}
  var activeOwner=OwnedWindows;Window parent=null;foreach(Window owned in activeOwner)if(owned.IsActive)parent=owned;
  var window=Dialog("发现新版本",420,390);if(parent!=null)window.Owner=parent;
  updatePrompt=window;notifiedUpdate=release.Tag;var panel=new StackPanel{Margin=new Thickness(22)};
  panel.Children.Add(Text("Google Calendar Desktop "+release.Version.ToString(3),18,foreground));
  var caption=Wrap("当前版本 "+UpdateService.Current.ToString(3)+"。更新会保留账号和设置，安装完成后自动重新启动。",12,muted);caption.Margin=new Thickness(0,14,0,12);panel.Children.Add(caption);
  if(!string.IsNullOrWhiteSpace(release.Notes)){var notes=Wrap(release.Notes.Substring(0,Math.Min(1600,release.Notes.Length)),12,muted);panel.Children.Add(new ScrollViewer{Content=notes,MaxHeight=120,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});}
  var status=Wrap("安装包 "+(release.Size/1048576.0).ToString("0.0")+" MB",12,muted);status.Margin=new Thickness(0,12,0,8);panel.Children.Add(status);
  CancellationTokenSource download=null;bool downloading=false;Button update=null,later=null;
  update=SettingsButton("一键更新","下载、校验并安装新版本",async()=>{
   if(downloading)return;downloading=true;update.IsEnabled=false;later.Content="取消下载";download=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);download.CancelAfter(TimeSpan.FromMinutes(30));
   try{
    string directory=Path.Combine(Path.GetTempPath(),"DeskCalendar-Update-"+Guid.NewGuid().ToString("N"));
    var progress=new Progress<int>(value=>status.Text="正在下载更新 · "+value+"%");
    string installer=await updater.DownloadAsync(release,directory,progress,download.Token);download.Token.ThrowIfCancellationRequested();
    status.Text="校验通过，正在安装…";UpdateService.LaunchHelper(installer,release.Hash);Exit();
   }catch(OperationCanceledException){if(!exiting)status.Text="下载已取消";}
    catch(Exception ex){status.Text=ex is System.Net.Http.HttpRequestException?"下载失败，请检查网络后重试":ex.Message;}
   finally{downloading=false;update.IsEnabled=true;later.Content="稍后";if(download!=null){download.Dispose();download=null;}}
  });update.Name="InstallUpdate";panel.Children.Add(update);
  later=SettingsButton("稍后","暂不安装更新",()=>{if(downloading){if(download!=null)download.Cancel();return;}window.Close();});panel.Children.Add(later);
  window.Closed+=delegate{if(download!=null)download.Cancel();if(updatePrompt==window)updatePrompt=null;};
  SetDialogContent(window,new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});window.Show();window.Activate();
 }
}
}

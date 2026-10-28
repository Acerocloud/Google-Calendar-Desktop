using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace DeskCalendar {
public partial class CalendarWindow {
 Button taskAddButton; Window taskEditor; Window tasksWindow; TodoCache todos=new TodoCache(); bool linkingTasks,taskMutation,completedTasksOpen,historyTasksOpen; string tasksError=""; Grid tasksRoot;
 void InitTasks(){
  try{todos=store.Read<TodoCache>("tasks.bin",true);}catch{todos=new TodoCache();}
  if(demo){todos=new TodoCache();todos.Lists.Add(new TaskListInfo{Id="demo",Title="我的任务"});foreach(var name in new[]{"整理报销单","确认下周会议时间","预约体检","准备分享会资料","整理照片"})todos.Items.Add(new TodoItem{Id=name,ListId="demo",Title=name,Due=name=="整理照片"?null:DateTime.Today.AddDays(name=="准备分享会资料"?3:0).ToString("yyyy-MM-dd")});}
  SizeChanged+=delegate{AlignTasks();};LocationChanged+=delegate{AlignTasks();};IsVisibleChanged+=delegate{if(IsVisible&&!rendering)ShowTasks();else {StopTasksSlide();if(tasksWindow!=null)tasksWindow.Hide();}UpdateTasksReveal();};
 }
 internal void ShowTasks(){if(prefs.TasksCollapsed&&!coldPreparing){UpdateTasksReveal();return;}PrepareTasksWindow(true);}
 void PrepareTasksWindow(bool show){
  if(tasksWindow==null){
   var w=new Window{Title="待办",Owner=this,Width=CalendarMath.Clip(prefs.TasksWidth,240,520),Height=Height,MinWidth=240,MaxWidth=520,MinHeight=MinHeight,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,ShowActivated=true,WindowStartupLocation=WindowStartupLocation.Manual,Background=Brushes.Transparent,FontFamily=FontFamily,FontSize=FontSize,Resources=Resources};tasksWindow=w;
#if VISUAL_QA
   w.Owner=null;w.ShowInTaskbar=true;
#endif
   WindowChrome.SetWindowChrome(w,new WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(prefs.Locked?0:5, prefs.Locked?0:5,0,prefs.Locked?0:5),GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false});
   w.SourceInitialized+=delegate{Native.WidgetStyle(new WindowInteropHelper(w).Handle);AttachMaterialHook(w);ApplyDialogMaterial(w);};
   w.SizeChanged+=delegate(object sender,SizeChangedEventArgs e){if(linkingTasks||coldPreparing||!w.IsLoaded)return;linkingTasks=true;try{if(e.HeightChanged)Height=Math.Max(MinHeight,w.ActualHeight);Top=w.Top;prefs.TasksWidth=w.Width;}finally{linkingTasks=false;}AlignTasks();QueuePreferencesSave();};
   w.LocationChanged+=delegate{if(linkingTasks||coldPreparing||!w.IsLoaded)return;linkingTasks=true;try{Top=w.Top;}finally{linkingTasks=false;}Dispatcher.BeginInvoke(new Action(AlignTasks),DispatcherPriority.Background);};
   w.Deactivated+=delegate{if(!coldPreparing)Dispatcher.BeginInvoke(new Action(DismissIfOutside),DispatcherPriority.Background);};
   w.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;HidePopup();}};
   InputFeedback.Attach(w);RenderTasks();
  }
  if(!show){new WindowInteropHelper(tasksWindow).EnsureHandle();var size=new Size(tasksWindow.Width,tasksWindow.Height);tasksRoot.Measure(size);tasksRoot.Arrange(new Rect(size));tasksRoot.UpdateLayout();ApplyDialogMaterial(tasksWindow);return;}AlignTasks();linkingTasks=true;try{if(!tasksWindow.IsVisible){bool rebuild=historyTasksOpen;historyTasksOpen=false;if(rebuild)RenderTasks();tasksWindow.ShowActivated=false;tasksWindow.Show();}tasksWindow.UpdateLayout();}finally{linkingTasks=false;}AlignTasks();ApplyDialogMaterial(tasksWindow);
 }
 void AlignTasks(){if(tasksWindow==null||linkingTasks||tasksSliding)return;linkingTasks=true;try{tasksWindow.MinHeight=MinHeight;tasksWindow.Height=Math.Max(MinHeight,Height);tasksWindow.Top=Top;tasksWindow.Left=Left-tasksWindow.Width-10;var c=WindowChrome.GetWindowChrome(tasksWindow);if(c!=null)c.ResizeBorderThickness=new Thickness(prefs.Locked?0:5,prefs.Locked?0:5,0,prefs.Locked?0:5);}finally{linkingTasks=false;}UpdateTasksReveal();}
 void ClearTasks(){todos=new TodoCache();tasksError="";prefs.TaskListId="";store.Delete("tasks.bin");RenderTasks();}
 async Task SyncTasksAsync(System.Threading.CancellationToken ct){if(!google.CanTasks)return;try{var fresh=await google.ReadTasksAsync(ct);ct.ThrowIfCancellationRequested();todos=fresh;store.Write("tasks.bin",todos,true);tasksError="";}catch(OperationCanceledException){throw;}catch(LoginRequiredException){throw;}catch(Exception ex){tasksError=Friendly(ex);}RenderTasks();}
 void RenderTasks(){
  if(tasksSliding){deferredCollapseTasks=true;return;}if(popupRise.Running){deferredRiseTasks=true;return;}if(tasksWindow==null)return;tasksWindow.Resources=Resources;tasksRoot=new Grid();tasksRoot.RowDefinitions.Add(new RowDefinition{Height=new GridLength(42)});tasksRoot.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});tasksRoot.RowDefinitions.Add(new RowDefinition());tasksRoot.RowDefinitions.Add(new RowDefinition{Height=new GridLength(34)});
  var caption=new Grid{Margin=new Thickness(14,0,8,0)};caption.ColumnDefinitions.Add(new ColumnDefinition());caption.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var label=Text("待办",12,muted);label.Background=Brushes.Transparent;label.MouseLeftButtonDown+=delegate{if(!prefs.Locked)try{DragMove();}catch(InvalidOperationException){}};caption.Children.Add(label);var plus=Icon("\uE710","添加待办",()=>EditTask(null));taskAddButton=plus;plus.IsEnabled=taskEditor==null&&!taskMutation&&(demo||(google!=null&&google.Connected&&google.CanTasks&&todos.Lists.Count>0));Grid.SetColumn(plus,1);caption.Children.Add(plus);tasksRoot.Children.Add(caption);
  var header=new StackPanel{Margin=new Thickness(20,15,20,12)};Grid.SetRow(header,1);tasksRoot.Children.Add(header);
  var body=new StackPanel{Margin=new Thickness(20,0,20,24)};var scroll=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,2);tasksRoot.Children.Add(scroll);
  var collapse=Icon("\uE76C","收起待办",()=>SetTasksCollapsed(true));collapse.Name="CollapseTasks";collapse.HorizontalAlignment=HorizontalAlignment.Right;collapse.Margin=new Thickness(0,0,12,4);collapse.FontSize=12;Grid.SetRow(collapse,3);tasksRoot.Children.Add(collapse);
  if(!demo&&(google==null||!google.Connected)){header.Children.Add(Text("我的任务",24,foreground));body.Children.Add(Wrap("登录 Google 后查看待办。",13,muted));body.Children.Add(SettingsGoogleLoginButton(BeginLogin));}
  else if(!demo&&!google.CanTasks){header.Children.Add(Text("我的任务",24,foreground));body.Children.Add(Wrap("连接 Google Tasks 后，在这里添加和完成待办。",13,muted));body.Children.Add(SettingsButton("连接 Google Tasks","授权 Google Tasks",BeginLogin));}
  else {
   var lists=new ComboBox{ItemsSource=todos.Lists,FontSize=20,MinHeight=36,Margin=new Thickness(0,0,0,14),IsEnabled=!taskMutation};lists.Name="TaskListSelector";ConfigureTaskListSelector(lists);var selected=todos.Lists.FirstOrDefault(l=>l.Id==prefs.TaskListId)??todos.Lists.FirstOrDefault();lists.SelectedItem=selected;if(selected!=null)prefs.TaskListId=selected.Id;lists.SelectionChanged+=delegate{var value=lists.SelectedItem as TaskListInfo;if(value!=null){prefs.TaskListId=value.Id;QueuePreferencesSave();RenderTasks();}};header.Children.Add(lists);

   var items=todos.Items.Where(t=>t.ListId==prefs.TaskListId).ToList();
   foreach(var item in items.Where(t=>!t.Completed).OrderBy(t=>t.Date??DateTime.MaxValue))body.Children.Add(TaskRow(item));
   if(items.All(t=>t.Completed))body.Children.Add(Text("暂无待办",13,muted));
   AddCompletedTasks(body,items);
  }
  if(tasksError!=""){var error=Wrap(tasksError+"\n可使用日历右下角的刷新重试。",12,muted);error.Margin=new Thickness(0,16,0,0);body.Children.Add(error);}
  var frame=new Border{Child=tasksRoot,SnapsToDevicePixels=true,UseLayoutRounding=true};tasksWindow.Tag=frame;tasksWindow.Content=frame;ApplyDialogMaterial(tasksWindow);
 }
 void AddTaskSection(StackPanel body,string title,IEnumerable<TodoItem> source){var items=source.OrderBy(t=>t.Date??DateTime.MaxValue).ToList();if(items.Count==0)return;var label=Text(title,13,muted);label.Margin=new Thickness(0,20,0,12);body.Children.Add(label);foreach(var item in items)body.Children.Add(TaskRow(item));}
 UIElement TaskRow(TodoItem item){
  var row=new Grid{Margin=new Thickness(0,0,0,12)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(30)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
  var complete=Button("","完成或恢复待办",async()=>await MutateTask(async()=>{var updated=await google.SaveTaskAsync(item.ListId,item,TaskStatusFields(item.Completed),lifetime.Token);ReplaceTask(updated);}));complete.Width=26;complete.Height=30;complete.Padding=new Thickness(0);complete.Margin=new Thickness(0);complete.IsEnabled=!taskMutation;
  var circle=new Border{Width=17,Height=17,CornerRadius=new CornerRadius(9),BorderThickness=new Thickness(1),BorderBrush=muted,Background=item.Completed?foreground:Brushes.Transparent};if(item.Completed)circle.Child=new TextBlock{Text="✓",FontSize=12,Foreground=BrushColor("#252728"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};complete.Content=circle;row.Children.Add(complete);
  var details=new StackPanel();var text=Wrap(item.Title,14,item.Completed?muted:foreground);if(item.Completed)text.TextDecorations=TextDecorations.Strikethrough;details.Children.Add(text);{var due=Text(item.DateCaption(DateTime.Today),10,muted);due.Name="TaskDateLabel";due.Margin=new Thickness(0,5,0,0);details.Children.Add(due);}var edit=Button("","编辑待办",()=>EditTask(item));edit.HorizontalContentAlignment=HorizontalAlignment.Stretch;edit.Content=details;edit.Padding=new Thickness(4,4,0,4);edit.IsEnabled=!taskMutation;Grid.SetColumn(edit,1);row.Children.Add(edit);var remove=Icon("\uE74D","删除待办",()=>ConfirmDeleteTask(tasksWindow,item,null));remove.Name="DeleteTask";remove.Tag=item;remove.IsEnabled=!taskMutation;Grid.SetColumn(remove,2);row.Children.Add(remove);return row;
 }
 static Dictionary<string,object> TaskStatusFields(bool completed){var fields=new Dictionary<string,object>{{"status",completed?"needsAction":"completed"}};if(completed)fields["completed"]=null;return fields;}
 void ReplaceTask(TodoItem item){todos.Items.RemoveAll(t=>t.ListId==item.ListId&&t.Id==item.Id);todos.Items.Add(item);}
 async Task<bool> MutateTask(Func<Task> action){
  if(taskMutation)return false;if(demo){MessageBox.Show(tasksWindow,"示例模式不会修改 Google 任务。","待办");return false;}taskMutation=true;RenderTasks();await syncGate.WaitAsync();
  try{await action();store.Write("tasks.bin",todos,true);tasksError="";return true;}catch(Exception ex){tasksError=Friendly(ex);return false;}finally{syncGate.Release();taskMutation=false;RenderTasks();}
 }
 void EditTask(TodoItem item){
  if(taskEditor!=null){taskEditor.Activate();return;}if(taskMutation||todos.Lists.Count==0)return;string listId=item==null?prefs.TaskListId:item.ListId;var w=Dialog(item==null?"添加待办":"编辑待办",380,500);w.Owner=tasksWindow??this;var body=new StackPanel{Margin=new Thickness(22,12,22,20)};
  var title=EventInput("TaskTitle",item==null?"":item.Title);EventField(body,"标题",title);
  var date=new EventDatePicker(item!=null&&item.Date.HasValue?item.Date.Value:DateTime.Today,Resources,foreground,muted,line,hover){Margin=new Thickness(0,8,0,6)};var shortcuts=new System.Windows.Controls.Primitives.UniformGrid{Columns=3,Margin=new Thickness(0,0,0,4)};var dated=Check("安排日期",item!=null&&item.Date.HasValue,v=>{date.IsEnabled=v;});dated.Name="TaskHasDate";date.IsEnabled=dated.IsChecked==true;body.Children.Add(dated);body.Children.Add(date);for(int i=0;i<3;i++){int offset=i;var quick=SettingsButton(i==0?"今天":i==1?"明天":"后天","选择快捷日期",()=>{dated.IsChecked=true;date.SetDate(DateTime.Today.AddDays(offset));});quick.Name="TaskQuickDate"+i;quick.Margin=new Thickness(i==0?0:4,0,i==2?0:4,0);shortcuts.Children.Add(quick);}body.Children.Add(shortcuts);
  var notes=EventInput("TaskNotes",item==null?"":item.Notes,true);notes.Height=90;EventField(body,"备注",notes);var error=Wrap("",12,muted);body.Children.Add(error);
  bool saving=false;w.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(saving)e.Cancel=true;};
  var save=SettingsButton("保存","保存待办",async()=>{Dictionary<string,object> fields;try{fields=TodoItem.Fields(title.Text,notes.Text,dated.IsChecked==true?date.SelectedDate:(DateTime?)null);}catch(Exception ex){error.Text=ex.Message;return;}saving=true;body.IsEnabled=false;bool ok=await MutateTask(async()=>{ReplaceTask(await google.SaveTaskAsync(listId,item,fields,lifetime.Token));});saving=false;body.IsEnabled=true;if(ok)w.Close();else error.Text=tasksError;});save.Name="SaveTask";var cancel=SettingsButton("取消","放弃修改待办",w.Close);cancel.Name="CancelTask";var buttons=new Grid{Margin=new Thickness(0,12,0,0)};buttons.ColumnDefinitions.Add(new ColumnDefinition());buttons.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(10)});buttons.ColumnDefinitions.Add(new ColumnDefinition());buttons.Children.Add(save);Grid.SetColumn(cancel,2);buttons.Children.Add(cancel);body.Children.Add(buttons);
  if(item!=null){AddCaptionBrowser(w,item.WebViewLink,"在浏览器中打开待办");AddCaptionRight(w,Icon("\uE74D","删除待办",()=>ConfirmDeleteTask(w,item,w.Close)),"DeleteTaskHeader");}
  SetDialogContent(w,new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});taskEditor=w;w.Closed+=delegate{if(taskEditor==w){taskEditor=null;if(taskAddButton!=null)taskAddButton.IsEnabled=!taskMutation&&(demo||(google!=null&&google.Connected&&google.CanTasks&&todos.Lists.Count>0));}};if(taskAddButton!=null)taskAddButton.IsEnabled=false;w.Show();w.Activate();
 }
}
}

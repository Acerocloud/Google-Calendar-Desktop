using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace DeskCalendar {
public partial class CalendarWindow {
 bool recentTasksOpen=true;
 void ConfirmDeleteTask(Window owner,TodoItem item,Action completed){
  if(taskMutation)return;var w=Dialog("删除待办",370,270);w.Owner=owner??this;var body=new StackPanel{Margin=new Thickness(22,10,22,18)};body.Children.Add(Wrap("删除“"+item.Title+"”？",16,foreground));body.Children.Add(Wrap("这条待办也会从 Google Tasks 中删除。",12,muted));var error=Wrap("",12,muted);error.Margin=new Thickness(0,10,0,6);body.Children.Add(error);bool working=false,deleted=false;
  var yes=SettingsButton("删除","确认删除待办",()=>{});yes.Name="ConfirmDeleteTask";var no=SettingsButton("取消","保留待办",w.Close);no.Name="CancelDeleteTask";var buttons=new Grid();buttons.ColumnDefinitions.Add(new ColumnDefinition());buttons.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(10)});buttons.ColumnDefinitions.Add(new ColumnDefinition());buttons.Children.Add(yes);Grid.SetColumn(no,2);buttons.Children.Add(no);body.Children.Add(buttons);
  w.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(working)e.Cancel=true;};yes.Click+=async delegate{if(working)return;working=true;yes.IsEnabled=no.IsEnabled=false;deleted=await MutateTask(async()=>{await google.DeleteTaskAsync(item,lifetime.Token);todos.Items.RemoveAll(t=>t.ListId==item.ListId&&t.Id==item.Id);});working=false;yes.IsEnabled=no.IsEnabled=true;if(deleted)w.Close();else error.Text=tasksError;};
  SetDialogContent(w,body);w.ShowDialog();if(deleted&&completed!=null)completed();
 }
}
}

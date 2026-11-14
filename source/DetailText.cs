using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace DeskCalendar {
public partial class CalendarWindow {
 TextBox CopyableDetailText(string value,double size,Brush color){
  var box=EventInput("",value??"",true);
  box.ClearValue(HeightProperty);
  box.MinHeight=0;
  box.Padding=new Thickness(0);
  box.IsReadOnly=true;
  box.FontSize=size;
  box.Foreground=color;
  box.SelectionBrush=SystemParameters.HighContrast?SystemColors.HighlightBrush:BrushColor("#4278B8");
  box.SelectionOpacity=SystemParameters.HighContrast?1:0.75;
  box.VerticalScrollBarVisibility=ScrollBarVisibility.Disabled;
  box.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){
   if(e.ClickCount<2||box.Text.Length==0)return;
   int index=box.GetCharacterIndexFromPoint(e.GetPosition(box),true);
   if(index<0)return;
   int first,length;
   DetailSelection(box.Text,index,e.ClickCount>=3,out first,out length);
   box.Focus();
   box.Select(first,length);
   e.Handled=true;
  };
  return box;
 }

 RichTextBox LinkableDetailText(Window owner,string value,double size){
  var paragraph=new Paragraph{Margin=new Thickness(0)};
  int offset=0;
  foreach(Match match in Regex.Matches(value,@"https?://[^\s<>""“”‘’。，；！？、）】]+",RegexOptions.IgnoreCase)){
   string label=match.Value.TrimEnd('.',',',';',':','!','?');
   while(label.EndsWith(")")&&CountCharacter(label,')')>CountCharacter(label,'('))
    label=label.Substring(0,label.Length-1);
   Uri url;
   if(!Uri.TryCreate(label,UriKind.Absolute,out url)||string.IsNullOrEmpty(url.Host))continue;
   paragraph.Inlines.Add(new Run(value.Substring(offset,match.Index-offset)));
   var link=new Hyperlink(new Run(label)){NavigateUri=url,Foreground=BrushColor("#91C8FF"),Cursor=Cursors.Hand};
   link.RequestNavigate+=delegate(object sender,System.Windows.Navigation.RequestNavigateEventArgs args){
    args.Handled=true;
    if(args.Uri.Scheme!=Uri.UriSchemeHttps&&args.Uri.Scheme!=Uri.UriSchemeHttp)return;
    try{Process.Start(new ProcessStartInfo(args.Uri.AbsoluteUri){UseShellExecute=true});}
    catch(System.ComponentModel.Win32Exception){MessageBox.Show(owner,"无法打开链接。你可以复制链接后在浏览器中打开。","打开链接");}
   };
   paragraph.Inlines.Add(link);
   offset=match.Index+label.Length;
  }
  paragraph.Inlines.Add(new Run(value.Substring(offset)));
  var document=new FlowDocument(paragraph){PagePadding=new Thickness(0)};
  var box=new RichTextBox(document){
   IsReadOnly=true,IsDocumentEnabled=true,FontSize=size,Foreground=foreground,
   Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),
   SelectionBrush=SystemParameters.HighContrast?SystemColors.HighlightBrush:BrushColor("#4278B8"),
   SelectionOpacity=SystemParameters.HighContrast?1:0.75,
   VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled
  };
  box.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){
   if(e.ClickCount<2)return;
   var position=box.GetPositionFromPoint(e.GetPosition(box),true);
   if(position==null)return;
   var run=position.Parent as Run;
   if(e.ClickCount==2&&run==null)return;
   box.Focus();
   if(e.ClickCount>=3)box.Selection.Select(paragraph.ContentStart,paragraph.ContentEnd);
   else{
    int first,length;
    DetailSelection(run.Text,run.ContentStart.GetOffsetToPosition(position),false,out first,out length);
    box.Selection.Select(run.ContentStart.GetPositionAtOffset(first),run.ContentStart.GetPositionAtOffset(first+length));
   }
   e.Handled=true;
  };
  return box;
 }

 static void DetailSelection(string text,int index,bool paragraph,out int first,out int length){
  if(text.Length==0){first=length=0;return;}
  index=Math.Min(index,text.Length-1);
  first=index;
  int end=index+1;
  if(paragraph){
   while(first>0&&text[first-1]!='\n'&&text[first-1]!='\r')first--;
   while(end<text.Length&&text[end]!='\n'&&text[end]!='\r')end++;
  }else if(char.IsLetterOrDigit(text[index])||text[index]=='_'){
   while(first>0&&(char.IsLetterOrDigit(text[first-1])||text[first-1]=='_'))first--;
   while(end<text.Length&&(char.IsLetterOrDigit(text[end])||text[end]=='_'))end++;
  }
  length=end-first;
 }

 static int CountCharacter(string text,char character){
  int count=0;
  foreach(char current in text)if(current==character)count++;
  return count;
 }
}
}

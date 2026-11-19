using Avalonia;
using Avalonia.Controls;
using System.ComponentModel;
using CyRevision.Desktop.ViewModels;

namespace CyRevision.Desktop;

public partial class MainWindow
{
    private Window? _automationApprovalWindow;
    private void OnAutomationApprovalChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(args.PropertyName!=nameof(MainWindowViewModel.AiSdkHasApproval)||_viewModel is null)return;
        if(!_viewModel.AiSdkHasApproval){_automationApprovalWindow?.Close();return;}
        if(_automationApprovalWindow is not null)return;
        var viewModel=_viewModel;
        var window=new Window{Title=Translate("CyMCPSDK · Confirm application operation"),Width=820,Height=510,MinWidth=520,MinHeight=300,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        _automationApprovalWindow=window;
        var approve=new Button{Content=Translate("Approve exact operation"),Margin=new Thickness(0,0,8,0)};
        var reject=new Button{Content=Translate("Reject")};
        approve.Click+=(_,_)=>{viewModel.ApproveAiSdkOperation();window.Close();};
        reject.Click+=(_,_)=>{viewModel.RejectAiSdkOperation();window.Close();};
        var actions=new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Children={approve,reject}};
        var text=new TextBox{Text=viewModel.AiSdkPendingApproval,IsReadOnly=true,AcceptsReturn=true,TextWrapping=Avalonia.Media.TextWrapping.NoWrap};
        ScrollViewer.SetHorizontalScrollBarVisibility(text,Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(text,Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        var grid=new Grid{Margin=new Thickness(14),RowDefinitions=new RowDefinitions("*,Auto"),RowSpacing=10};
        grid.Children.Add(text);Grid.SetRow(actions,1);grid.Children.Add(actions);window.Content=grid;
        window.Closed+=(_,_)=>{_automationApprovalWindow=null;if(viewModel.AiSdkHasApproval)viewModel.RejectAiSdkOperation();};
        window.Show(this);
    }
}

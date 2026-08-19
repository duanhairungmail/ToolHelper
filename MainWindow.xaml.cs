using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ToolHelper.ViewModels;

namespace ToolHelper;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.DisposeAllViews();
    }

    private void ToolItem_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is ToolItem tool)
        {
            var vm = DataContext as MainViewModel;
            if (vm != null)
            {
                vm.SelectedTool = tool;
                // CurrentView 由 OnSelectedToolChanged 自动通过缓存设置
            }
        }
    }
}
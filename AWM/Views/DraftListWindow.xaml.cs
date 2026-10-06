using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AWM.ViewModels;

namespace AWM.Views;

public partial class DraftListWindow : Window
{
    private readonly DraftListViewModel _viewModel;

    public DraftListWindow(DraftListViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += () => DialogResult = true;
        Loaded += async (_, _) => await viewModel.LoadAsync();
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 머리글·빈 곳 더블클릭은 무시한다 — 항목 위에서만 연다
        if (ItemsControl.ContainerFromElement(DraftList, (DependencyObject)e.OriginalSource) is ListViewItem
            && _viewModel.OpenCommand.CanExecute(null))
            _viewModel.OpenCommand.Execute(null);
    }
}

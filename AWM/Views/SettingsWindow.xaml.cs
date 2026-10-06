using System.Windows;
using AWM.ViewModels;

namespace AWM.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += () => DialogResult = true;
    }
}

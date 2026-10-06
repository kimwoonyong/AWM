using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using AWM.ViewModels;

namespace AWM;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _closeConfirmed;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // 처음 크기는 모니터 작업 영역에 비례 — 노트북·큰 모니터 모두 알맞게 (사용자 결정 2026-10-06)
        var work = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Round(work.Width * 0.5));
        Height = Math.Max(MinHeight, Math.Round(work.Height * 0.6));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || e.Cancel || !_viewModel.HasUnsavedChanges)
            return;

        // Closing 안에서 Close() 를 부르면 WPF 가 막아 창이 영영 안 닫힌다 (LL-080).
        // 일단 막고, 묻기·저장은 이 이벤트가 끝난 뒤 다음 메시지에서 한다
        e.Cancel = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
        {
            if (!await _viewModel.ConfirmLeaveAsync())
                return;
            _closeConfirmed = true;
            Close();
        }));
    }
}

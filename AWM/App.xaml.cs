using System.IO;
using System.Windows;
using System.Windows.Threading;
using AWM.Services;
using AWM.ViewModels;
using AWM.Views.Platform;

namespace AWM;

public partial class App : Application
{
    private MainViewModel? _viewModel;

    /// <summary>
    /// 조립 루트. DI 컨테이너를 쓰지 않는다(D-003) — 새 서비스는 여기서 만든다.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var drafts = new DraftService(new ClaudeCli());
        // 문서 폴더가 OneDrive 등으로 옮겨져 있어도 따라가도록 알려진 폴더로 찾는다
        var store = new DraftStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AWM", "drafts"));
        var dialogs = new DialogService(store);
        _viewModel = new MainViewModel(drafts, new ClipboardService(), store, dialogs, new ChromeLauncher());

        var window = new MainWindow(_viewModel);
        dialogs.Owner = window;
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 생성 중에 창을 닫으면 claude.exe 가 혼자 남는다 — 취소 콜백이 그 자리에서 종료시킨다
        _viewModel?.CancelGeneration();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.ToString(), "AWM — 예기치 않은 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}

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
    private GlobalHotkey? _hotkey;

    /// <summary>
    /// 조립 루트. DI 컨테이너를 쓰지 않는다(D-003) — 새 서비스는 여기서 만든다.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // 설정을 먼저 읽는다 — 저장 위치가 설정에 있다
        var settings = new SettingsStore(SettingsStore.DefaultFilePath);
        var notice = settings.Load();

        var drafts = new DraftService(new ClaudeCli(), settings);
        // 문서 폴더가 OneDrive 등으로 옮겨져 있어도 따라가도록 알려진 폴더로 찾는다
        var defaultDraftsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AWM", "drafts");
        var store = new DraftStore(settings.Current.DraftsFolder ?? defaultDraftsFolder);
        var dialogs = new DialogService(store);
        var clipboard = new ClipboardService();
        var sequencer = new PasteSequencer(clipboard, new KeyboardSender(), PasteTimings.Default);
        _viewModel = new MainViewModel(drafts, clipboard, store, dialogs, new ChromeLauncher(), new ImageShrinker(), sequencer,
            settings, defaultDraftsFolder);
        _viewModel.ShowNotice(notice);

        var window = new MainWindow(_viewModel);
        dialogs.Owner = window;
        MainWindow = window;
        window.Show();

        // 전역 단축키는 창 핸들로 등록한다 — 창을 띄운 뒤에 만든다
        _hotkey = new GlobalHotkey(window);
        _viewModel.AttachHotkey(_hotkey);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 생성 중에 창을 닫으면 claude.exe 가 혼자 남는다 — 취소 콜백이 그 자리에서 종료시킨다
        _viewModel?.CancelGeneration();
        // 켜 둔 단축키를 풀어 다른 프로그램이 같은 키를 쓸 수 있게
        _hotkey?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.ToString(), "AWM — 예기치 않은 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}

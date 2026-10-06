using System.ComponentModel;
using TextBean.Services.Interfaces;
using TextBean.ViewModels;

namespace TextBean.Views.Platform;

/// <summary>
/// 트레이 아이콘 ↔ 주 창 · 셸. 아이콘 자체(ITrayIcon)와 떼어 둔다 — 시험은 가짜 아이콘으로 이 연결을 본다 (D-103).
/// 다 이은 뒤에야 ✕ 가 숨기기가 된다(CanHideToTray, D-106).
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly ITrayIcon _tray;
    private readonly MainWindow _window;
    private readonly ShellViewModel _shell;
    private bool _disposed;

    public TrayController(ITrayIcon tray, MainWindow window, ShellViewModel shell)
    {
        _tray = tray;
        _window = window;
        _shell = shell;

        tray.OpenRequested += OnOpen;
        tray.LockRequested += OnLock;
        tray.EnterKeyRequested += OnEnterKey;
        tray.ExitRequested += OnExit;
        shell.PropertyChanged += OnShellChanged;

        Refresh();
        tray.Show();
        window.CanHideToTray = true;
    }

    private void OnOpen(object? sender, EventArgs e) => _window.ShowFromTray();

    private void OnLock(object? sender, EventArgs e) => Gate(() => _shell.LockCommand.Execute(null));

    private void OnEnterKey(object? sender, EventArgs e) => Gate(() =>
    {
        // 키 입력창은 주 창을 주인으로 뜬다 — 숨은 창 위에 뜨지 않게 먼저 보인다
        _window.ShowFromTray();
        _shell.ChangeKeyCommand.Execute(null);
    });

    // 대화상자 검사는 RequestExit 안에 있다 — 도구 모음 「종료」와 같은 자리다
    private void OnExit(object? sender, EventArgs e) => _window.RequestExit();

    /// <summary>
    /// 트레이 메뉴는 WPF 모달이 끄지 못한다. 대화상자가 떠 있으면 창만 보인다 — 그 밑에서 탭을 닫거나(잠그기),
    /// 키 입력창을 한 겹 더 띄우지(시작 때 키 입력창은 바쁨 표시보다 먼저 뜬다) 않는다.
    /// Windows 종료 · 절전 대기 중에는 아무것도 하지 않는다.
    /// </summary>
    private void Gate(Action action)
    {
        if (!_window.AcceptsRequests) return;

        if (_window.IsModal())
        {
            _window.ShowFromTray();
            return;
        }

        action();
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.TrayToolTip) or nameof(ShellViewModel.HasKey)) Refresh();
    }

    private void Refresh() => _tray.Update(_shell.TrayToolTip, _shell.HasKey);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 트레이가 사라지면 숨은 창을 다시 열 길이 없다 — ✕ 를 먼저 종료로 되돌린다
        _window.CanHideToTray = false;

        _tray.OpenRequested -= OnOpen;
        _tray.LockRequested -= OnLock;
        _tray.EnterKeyRequested -= OnEnterKey;
        _tray.ExitRequested -= OnExit;
        _shell.PropertyChanged -= OnShellChanged;

        _tray.Dispose();
    }
}

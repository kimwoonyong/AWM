using TextBean.Services;
using TextBean.Services.Interfaces;
using TextBean.ViewModels;

namespace TextBean.Views.Platform;

/// <summary>
/// 절전 · 최대 절전에 들어갈 때 키를 지운다 (D-100). 트레이 상주로 "끄면 키가 지워진다"는 전제가 없어졌고,
/// 최대 절전은 메모리를 디스크(hiberfil)에 쓴다 [문서]. 대화상자 없이 잠그고, 못 하면 잠그지 않는다(fail-open).
/// </summary>
public sealed class SuspendLock : IDisposable
{
    private readonly IPowerEvents _power;
    private readonly MainWindow _window;
    private readonly Func<Task> _lockQuietly;
    private readonly Func<IDisposable> _suppressDialogs;
    private readonly TimeSpan _timeout;
    private bool _disposed;

    public SuspendLock(IPowerEvents power, MainWindow window, ShellViewModel shell, TimeSpan timeout)
        : this(power, window, shell.LockQuietlyAsync, shell.SuppressDialogs, timeout)
    {
    }

    /// 시험이 잠그기를 붙잡아 "기다리는 중"을 결정적으로 만든다 — 실제 잠그기는 탭 상태에 따라 동기로 끝나기도 한다 (LL-079).
    public SuspendLock(IPowerEvents power, MainWindow window, Func<Task> lockQuietly, Func<IDisposable> suppressDialogs,
                       TimeSpan timeout)
    {
        _power = power;
        _window = window;
        _lockQuietly = lockQuietly;
        _suppressDialogs = suppressDialogs;
        _timeout = timeout;

        power.Suspending += OnSuspending;
    }

    private void OnSuspending(object? sender, EventArgs e)
    {
        // 대화상자가 떠 있으면 그 밑에서 탭을 닫지 않는다. 다른 대기(Windows 종료) 중이면 그쪽이 마무리한다
        if (!_window.AcceptsRequests || _window.IsModal())
        {
            AppLog.Warn("suspend-lock-skipped", null, null);
            return;
        }

        _window.AcceptsRequests = false;
        try
        {
            using (_suppressDialogs())
            {
                var locking = _lockQuietly();
                _ = locking.ContinueWith(t => AppLog.Error("suspend-lock", null, t.Exception!.InnerException),
                                         TaskContinuationOptions.OnlyOnFaulted);

                // Windows 는 약 2초만 기다린다 [문서]. 넘기면 잠그기는 뒤에서 이어지고 절전이 먼저 될 수 있다
                if (!DispatcherWait.Until(locking, _timeout)) AppLog.Warn("suspend-lock-timeout", null, null);
            }
        }
        finally
        {
            _window.AcceptsRequests = true;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _power.Suspending -= OnSuspending;
        _power.Dispose();
    }
}

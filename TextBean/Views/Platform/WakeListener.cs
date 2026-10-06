using System.Windows.Threading;

namespace TextBean.Views.Platform;

/// <summary>
/// 두 번째 실행이 보낸 "깨워라" 신호(이름 있는 이벤트)를 기다렸다가 UI 스레드에서 <paramref name="onWake"/> 를 부른다 (D-099).
/// 대기는 스레드 풀이 한다 — UI 스레드를 막지 않는다. 이벤트가 자동 리셋이라 등록 전에 온 신호도 등록하자마자 한 번 돈다.
/// 핸들은 만든 쪽(App)이 해제한다.
/// </summary>
public sealed class WakeListener : IDisposable
{
    private readonly RegisteredWaitHandle _registration;
    private bool _disposed;

    public WakeListener(WaitHandle signal, Action onWake)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;

        _registration = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) =>
        {
            // 해제 뒤 늦게 온 신호는 버린다 — 닫힌 창을 다시 띄우면 안 된다
            dispatcher.BeginInvoke(() => { if (!_disposed) onWake(); });
        }, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _registration.Unregister(null);
    }
}

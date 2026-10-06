using System.Windows.Threading;
using TextBean.Services.Interfaces;

namespace TextBean.Views.Platform;

public sealed class AutoSaveTimer : IAutoSaveTimer
{
    /// 입력이 멈추고 이만큼 지나면 저장한다 (D-015).
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(1500);

    private readonly DispatcherTimer _timer;

    public AutoSaveTimer()
    {
        // 저장은 UI 스레드에서 시작해야 ViewModel 상태 갱신이 안전하다
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Debounce };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Elapsed?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? Elapsed;

    public void Restart()
    {
        _timer.Stop();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void Dispose() => _timer.Stop();
}

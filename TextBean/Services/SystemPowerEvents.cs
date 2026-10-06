using Microsoft.Win32;
using TextBean.Services.Interfaces;

namespace TextBean.Services;

/// <summary>
/// <see cref="SystemEvents.PowerModeChanged"/> 래퍼. UI 스레드에서 만든다 — SystemEvents 는 구독한 스레드의
/// 동기화 문맥으로 동기(Send) 호출한다 [실측 — 리서치]. 정적 이벤트라 Dispose 에서 구독을 풀지 않으면 끝까지 붙어 있다.
/// </summary>
public sealed class SystemPowerEvents : IPowerEvents
{
    public event EventHandler? Suspending;

    public SystemPowerEvents() => SystemEvents.PowerModeChanged += OnPowerModeChanged;

    /// 절전 · 최대 절전 진입만 고른다. 깨어남(Resume) · 배터리 상태 변화(StatusChange)는 잠그지 않는다.
    public static bool IsSuspend(PowerModeChangedEventArgs e) => e.Mode == PowerModes.Suspend;

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (IsSuspend(e)) Suspending?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => SystemEvents.PowerModeChanged -= OnPowerModeChanged;
}

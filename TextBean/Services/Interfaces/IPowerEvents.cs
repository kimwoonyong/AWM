namespace TextBean.Services.Interfaces;

/// <summary>
/// 절전 · 최대 절전 진입 알림. 실제 구현(SystemPowerEvents)은 조립 루트에서만 만든다 —
/// 시험이 실제 시스템 이벤트를 구독하지 않게 한다 (D-103).
/// </summary>
public interface IPowerEvents : IDisposable
{
    /// <summary>
    /// 절전에 들어가기 직전. UI 스레드에서 동기로 올라온다 — Windows 는 처리기가 끝나기를 약 2초만 기다린다 [문서] (D-100).
    /// </summary>
    event EventHandler? Suspending;
}

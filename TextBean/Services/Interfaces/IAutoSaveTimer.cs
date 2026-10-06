namespace TextBean.Services.Interfaces;

/// <summary>
/// 입력이 멈춘 뒤 한 번 발생하는 디바운스 타이머 (D-015).
/// WPF DispatcherTimer 에 의존하므로 구현은 Views/Platform 에 둔다 (D-013).
/// </summary>
public interface IAutoSaveTimer : IDisposable
{
    /// 입력이 있을 때마다 호출한다. 이미 돌고 있으면 처음부터 다시 센다.
    void Restart();

    void Stop();

    event EventHandler? Elapsed;
}

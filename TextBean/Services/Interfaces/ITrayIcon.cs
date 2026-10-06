namespace TextBean.Services.Interfaces;

/// <summary>
/// 알림 영역(트레이) 아이콘. 실제 구현(Views/Platform/TrayIcon)은 조립 루트에서만 만든다 —
/// 화면 시험이 MainWindow 를 실제로 만들므로, 창이 만들면 시험이 사용자 트레이에 아이콘을 올린다 (D-103).
/// 이벤트는 UI 스레드에서 올라온다.
/// </summary>
public interface ITrayIcon : IDisposable
{
    /// 왼쪽 클릭 · 두 번 클릭 · 메뉴 「TextBean 열기」.
    event EventHandler? OpenRequested;

    /// 메뉴 「잠그기」 — 키가 있을 때만 보인다.
    event EventHandler? LockRequested;

    /// 메뉴 「키 입력…」 — 키가 없을 때만 보인다.
    event EventHandler? EnterKeyRequested;

    /// 메뉴 「종료」.
    event EventHandler? ExitRequested;

    void Show();

    /// <summary>
    /// 툴팁과 메뉴(잠그기 / 키 입력…)를 바꾼다. 툴팁에 문서 이름 · 경로 · 개수를 넣지 않는다 —
    /// Windows 가 사용자 레지스트리에 남긴다 (D-102).
    /// </summary>
    void Update(string toolTip, bool hasKey);
}

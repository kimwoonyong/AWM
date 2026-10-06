namespace TextBean.Models;

public sealed class AppSettings
{
    public string? RootPath { get; set; }

    public double WindowWidth { get; set; } = 1000;

    public double WindowHeight { get; set; } = 640;

    /// 처음 ✕ 로 트레이에 숨길 때의 안내를 봤는가 (D-096). 키 · 잠금 상태는 여기 두지 않는다 (D-053).
    public bool TrayNoticeShown { get; set; }
}

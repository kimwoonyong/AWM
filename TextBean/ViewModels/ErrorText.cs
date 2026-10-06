using TextBean.Services;

namespace TextBean.ViewModels;

/// <summary>
/// 실패 알림 본문. 링크 거부는 이유를 그대로 보인다 — "(UnauthorizedAccessException)" 만으로는
/// 사용자가 원인도, 금고 밖이 무사한지도 알 수 없다 (D-039).
/// </summary>
internal static class ErrorText
{
    public static string For(Exception ex, string fallback)
        => ex is LinkedPathException or KeyUnavailableException ? ex.Message : $"{fallback} ({ex.GetType().Name})";
}

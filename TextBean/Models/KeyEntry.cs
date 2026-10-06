namespace TextBean.Models;

/// <summary>
/// 키 입력창이 돌려주는 값. 두 번 입력을 요구했으면 <paramref name="Confirmation"/> 이 있다.
/// 같은지는 ViewModel 이 판정한다 — 비교를 창에 두면 규칙이 화면에 흩어진다 (PROHIBITED-UI-03).
/// </summary>
public sealed record KeyEntry(string Key, string? Confirmation);

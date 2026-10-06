namespace AWM.Models;

/// <summary>
/// 「사진 배치」 결과. AfterBlock 은 번호 붙인 문단 중 몇 번 뒤인지(0 = 맨 앞).
/// </summary>
public sealed record PhotoPlacement(string File, string Description, int AfterBlock);

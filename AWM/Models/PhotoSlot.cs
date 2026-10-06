namespace AWM.Models;

/// <summary>
/// 본문의 사진 줄 하나. File 은 초안 폴더 images\ 안의 파일 이름, 아직 사진이 없으면 null.
/// </summary>
public sealed record PhotoSlot(int Line, string Description, string? File);

namespace TextBean.Models;

/// <summary>
/// 평문 파일을 읽은 형식 — 저장할 때 이대로 다시 쓴다 (D-118).
/// Encoding 은 "UTF-8" · "UTF-16LE" · "UTF-16BE" · "CP949" 중 하나, NewLine 은 "\r\n" 또는 "\n".
/// </summary>
public sealed record PlainTextFormat(string Encoding, bool HasBom, string NewLine);

namespace AWM.Models;

/// <summary>
/// 본문 하나를 붙여넣을 곳에 맞게 바꾼 결과. Html 은 네이버 스마트에디터용 조각, PlainText 는 표시 기호를 뺀 글.
/// </summary>
public sealed record FormattedBody(string Html, string PlainText);

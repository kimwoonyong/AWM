namespace AWM.Models;

/// <summary>
/// 초안 요청. 주제는 필수, 키워드·추가 요청은 빈 문자열일 수 있다.
/// </summary>
public sealed record DraftRequest(string Topic, string Keywords, string Extra);

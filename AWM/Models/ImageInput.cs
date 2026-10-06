namespace AWM.Models;

/// <summary>
/// Claude 에게 보여 줄 그림. Data 는 줄인 사본이다 — 원본은 images\ 에 그대로 있다 (D-013).
/// </summary>
public sealed record ImageInput(string FileName, byte[] Data, string MediaType);

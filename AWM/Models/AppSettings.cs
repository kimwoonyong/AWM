namespace AWM.Models;

public enum DraftLength
{
    Short,
    Normal,
    Long,
}

public enum ToneKind
{
    Friendly,
    Informative,
    Diary,
    Custom,
}

/// <summary>
/// %APPDATA%\AWM\settings.json 형식 v1. 모든 글에 적용된다(add-settings D-005).
/// </summary>
public sealed record AppSettings
{
    public const int CurrentVersion = 1;
    public const int TagLimit = 30;

    /// <summary>
    /// 직접 쓴 말투 지침의 최대 글자 수. 지침은 claude.exe 명령 줄 인자로 가서 Windows 한도(32,767자) 안에 들어야 한다 (add-settings D-013).
    /// </summary>
    public const int CustomToneLimit = 2000;

    public int Version { get; init; } = CurrentVersion;
    public DraftLength Length { get; init; } = DraftLength.Normal;
    public ToneKind Tone { get; init; } = ToneKind.Friendly;
    public string CustomTone { get; init; } = "";
    public int TagMin { get; init; } = 5;
    public int TagMax { get; init; } = 10;

    /// <summary>
    /// 초안 저장 위치. null 이면 기본 위치(문서\AWM\drafts).
    /// </summary>
    public string? DraftsFolder { get; init; }

    public static bool IsValidTagRange(int min, int max) => min >= 1 && max <= TagLimit && min <= max;
}

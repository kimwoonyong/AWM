namespace TextBean.Models;

public enum MoveRejection
{
    None,
    SameLocation,
    IntoItself,
    OutsideRoot,
    ReservedFolder,
    NameTaken,
    PathTooLong,
    SourceMissing,
    DestinationNotFolder
}

/// <summary>
/// 이동 가부 판정. 디스크를 바꾸지 않는다.
/// OS도 대부분의 잘못된 이동을 막아 주지만 이유를 설명하지 않는다 — 사용자는
/// "왜 안 되는지 모르는 실패"를 반복하게 된다. design.md 8.6의 "만들기 전에 막는다"와 같은 원칙.
/// </summary>
public sealed record MoveCheck(MoveRejection Reason)
{
    public static readonly MoveCheck Ok = new(MoveRejection.None);

    public bool CanMove => Reason == MoveRejection.None;

    /// 값이나 문서 내용은 넣지 않는다 (PROHIBITED-CUSTOM-04).
    public string Message => Reason switch
    {
        MoveRejection.None => "",
        MoveRejection.SameLocation => "이미 그 폴더에 있습니다.",
        MoveRejection.IntoItself => "폴더를 자기 안으로 옮길 수 없습니다.",
        MoveRejection.OutsideRoot => "금고 밖으로는 옮길 수 없습니다.",
        MoveRejection.ReservedFolder => "앱이 쓰는 폴더로는 옮길 수 없습니다.",
        MoveRejection.NameTaken => "옮길 위치에 같은 이름이 이미 있습니다.",
        MoveRejection.PathTooLong => "옮기면 경로가 너무 길어집니다. 더 얕은 폴더를 골라주세요.",
        MoveRejection.SourceMissing => "옮기려는 항목을 찾을 수 없습니다. 도구 모음 [새로고침](기본 F5)으로 새로고침해주세요.",
        MoveRejection.DestinationNotFolder => "폴더 위에만 놓을 수 있습니다.",
        _ => "옮길 수 없습니다."
    };
}

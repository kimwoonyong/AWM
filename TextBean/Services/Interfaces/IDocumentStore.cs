using TextBean.Models;

namespace TextBean.Services.Interfaces;

public interface IDocumentStore
{
    /// 유효한 빈 문서를 기록한다. 아무것도 안 만들면 F5에 노드가 사라지고,
    /// 0바이트로 만들면 "문서가 아님"으로 영구 잠긴다 (D-009).
    Task CreateAsync(string fullPath);

    Task<DocumentReadResult> LoadAsync(string fullPath);

    /// 실패 시 예외를 던진다. 원본은 보존되고 호출자는 수정 상태를 유지한 채 전환을 취소한다 (D-011).
    /// 결속 없이 부르면 지금 세대로 본다.
    Task SaveAsync(string fullPath, string text);

    /// <summary>
    /// 문서를 잠근 키로만 저장한다. 결속의 세대가 지금 세대가 아니면 <see cref="Services.KeyUnavailableException"/>.
    /// 편집기는 열 때 받은 결속(<see cref="DocumentReadResult.Binding"/>)을 넘긴다.
    /// </summary>
    Task SaveAsync(string fullPath, DocumentBody body, DocumentKeyBinding? binding);

    /// 평문(.txt)을 읽은 형식 그대로 저장한다 (D-117 · D-118). 못 담는 글자면 PlainTextEncodeException.
    Task SavePlainAsync(string fullPath, string text, PlainTextFormat format);

    /// 문서마다 헤더만 읽는다. 읽지 못한 항목은 null — 하나가 실패해도 나머지는 계속한다.
    Task<IReadOnlyList<DocumentHeader?>> ReadHeadersAsync(IReadOnlyList<string> fullPaths);

    /// 지금 키에 대한 문서마다의 상태. 읽지 못한 항목은 Unreadable.
    Task<IReadOnlyList<DocumentKeyState>> ClassifyAsync(IReadOnlyList<string> fullPaths);

    /// <summary>
    /// 문서를 열 때 그 시점 내용을 한 벌 남긴다 (D-016). 이미 있으면 이번에 연 시점으로 덮는다.
    /// 자동 저장은 실수를 1.5초 만에 확정하는데, 문서를 바꾸면 실행취소 스택도 비워진다(D-010).
    /// 이 한 벌이 "이번에 연 뒤로 내가 망친 것"을 되돌릴 유일한 수단이다.
    /// </summary>
    void CaptureOpenSnapshot(string fullPath);

    /// 스냅샷이 있으면 그 경로, 없으면 null.
    string? SnapshotPathIfExists(string fullPath);
}

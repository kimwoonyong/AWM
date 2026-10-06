using TextBean.Models;

namespace TextBean.Services.Interfaces;

/// <summary>
/// 지금 쓰는 키. 메모리에만 두고 디스크·로그에는 남기지 않는다 (D-053 · PROHIBITED-CUSTOM-04).
///
/// 키를 넣을 때마다 <b>세대</b>가 바뀐다. 한 세대는 불변이다 — 키 바이트와, 그 키로 이미 만든 키들.
/// 전환 중에도 옛 세대를 빌려 쓰던 저장·검색은 끝까지 옛 세대로 돌고, 새 세대에 섞이지 않는다.
/// 옛 세대의 키는 그 세대를 빌린 작업이 모두 끝난 뒤에 0 으로 지운다 — 제자리에서 지우면
/// 진행 중인 저장이 0 키로 암호화해 문서가 어느 키로도 열리지 않게 된다.
/// </summary>
public interface IVaultKeyService
{
    /// 지금 세대. 키가 없을 때도 번호는 있다 — 잠그기 전에 연 탭의 결속이 잠근 뒤 세대와 맞지 않게 하려는 것이다.
    long Generation { get; }

    bool HasKey { get; }

    /// 세대가 바뀌었다 (키 넣기 · 전환 · 잠그기).
    event EventHandler? Changed;

    /// <summary>
    /// 후보 키로 헤더들을 판정한다. 지금 세대는 건드리지 않는다.
    /// 서로 다른 salt 마다 키를 한 번씩 만들어 오래 걸린다 — 스레드 풀에서 부른다.
    /// 쓰지 않을 결과는 <see cref="Discard"/> 로 버린다 (오타 키일 수 있다).
    /// </summary>
    KeyEvaluation Evaluate(string key, IReadOnlyList<DocumentHeader?> headers, CancellationToken ct = default);

    /// <summary>
    /// 지금 키로 다른 헤더들(금고 폴더를 바꾼 뒤)을 다시 판정한다. 키가 없으면 null.
    /// 세대를 새로 세워야 한다 — 옛 금고의 판정으로 "맞는 문서 3개"가 남으면 새 금고에서 첫 문서 재입력(오타 방지)이 빠진다.
    /// </summary>
    KeyEvaluation? EvaluateCurrent(IReadOnlyList<DocumentHeader?> headers, CancellationToken ct = default);

    /// 이 키가 지금 키와 같은가 (첫 문서 재입력 확인).
    bool Matches(string key);

    void Activate(KeyEvaluation evaluation);

    void Discard(KeyEvaluation evaluation);

    /// 잠그기. 키를 버리고 세대를 바꾼다.
    void Clear();

    /// <summary>
    /// 지금 세대를 빌린다. 빌린 동안 그 세대의 키는 지워지지 않는다. 키가 없으면 null.
    /// <paramref name="expectedGeneration"/> 을 주면 그 세대가 지금 세대일 때만 빌려준다.
    /// </summary>
    KeyLease? Acquire(long? expectedGeneration = null);
}

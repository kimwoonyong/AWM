using TextBean.Models;

namespace TextBean.Services.Interfaces;

/// <summary>
/// .tbx 암복호. 어느 키로 쓰는지를 메서드가 드러낸다 — "지금 키로 암호화"를 기본값으로 두면,
/// 옛 키로 연 탭의 저장이 문서를 조용히 새 키로 다시 잠근다 [문서 — 계획 검토].
/// </summary>
public interface IDocumentCodec
{
    /// 헤더 길이. 트리 판정·저장 규칙은 파일 앞 이만큼만 읽는다.
    int HeaderLength { get; }

    DocumentReadResult Decrypt(byte[] fileBytes);

    /// 새 문서. 지금 세대의 새 문서용 키로 잠근다.
    byte[] EncryptForNew(string plainText);

    /// <summary>
    /// 디스크에 있는 파일을 덮어쓴다. 그 파일을 잠근 키로만 — 헤더의 KDF 종류 · 반복 횟수 · salt · 키 확인값이
    /// 지금 세대(결속이 있으면 그 세대)의 키와 모두 맞아야 한다. 아니면 <see cref="Services.KeyUnavailableException"/>.
    /// </summary>
    byte[] EncryptReplacing(string plainText, ReadOnlySpan<byte> existingHeader, DocumentKeyBinding? binding);

    /// 결속의 키로 잠근다. 파일이 밖에서 없어진 뒤의 저장 — 지금 키로 새로 만들면 다른 키로 되살아난다.
    byte[] EncryptFor(string plainText, DocumentKeyBinding binding);

    /// 복호하지 않고 헤더만 판정한다.
    DocumentHeader ParseHeader(ReadOnlySpan<byte> bytes);

    /// 헤더만으로 지금 키에 대한 상태를 판정한다 (트리 표시).
    DocumentKeyState Classify(DocumentHeader header);
}

using TextBean.Models;

namespace TextBean.Services.Interfaces;

/// <summary>
/// 키 바이트와 헤더 값(KDF 종류 · 반복 횟수 · salt)으로 암호화 키와 키 확인값을 만든다.
/// 생성자로 주입한다 — 테스트가 60만 회를 돌지 않게 하되, 헤더의 반복 횟수 범위 검사는 약하게 하지 않는다.
/// </summary>
public interface IKeyDerivation
{
    /// 결과를 0 으로 지울 책임은 받은 쪽에 있다.
    DerivedKey Derive(ReadOnlySpan<byte> keyBytes, DocumentKeyId id);
}

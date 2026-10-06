using System.Security.Cryptography;
using TextBean.Models;
using TextBean.Services.Interfaces;

namespace TextBean.Services;

/// <summary>
/// 키에서 만든 암호화 키(32B)와 키 확인값(8B). 둘은 HKDF 로 따로 뽑아 확인값이 암호화 키를 드러내지 않는다.
/// </summary>
public sealed class DerivedKey(byte[] encryptionKey, byte[] check)
{
    public byte[] EncryptionKey { get; } = encryptionKey;

    public byte[] Check { get; } = check;

    public void Clear()
    {
        CryptographicOperations.ZeroMemory(EncryptionKey);
        CryptographicOperations.ZeroMemory(Check);
    }
}

/// <summary>
/// PBKDF2-HMAC-SHA256 → 32B → HKDF-Expand 로 두 값을 나눈다.
/// PBKDF2 에서 64B 를 바로 뽑으면 두 배 느리다 [실측 — 60만 회 70ms → 140ms].
/// AES·PBKDF2·HKDF 호출은 이 파일과 KeyDocumentCodec 밖에서 금지한다 (PROHIBITED-CUSTOM-01).
/// </summary>
public sealed class Pbkdf2KeyDerivation : IKeyDerivation
{
    public const byte KdfPbkdf2Sha256 = 1;

    private static ReadOnlySpan<byte> EncryptionInfo => "TextBean/v2/encryption"u8;
    private static ReadOnlySpan<byte> CheckInfo => "TextBean/v2/key-check"u8;

    public DerivedKey Derive(ReadOnlySpan<byte> keyBytes, DocumentKeyId id)
    {
        if (id.KdfId != KdfPbkdf2Sha256) throw new NotSupportedException("알 수 없는 키 만들기 방식입니다.");

        Span<byte> master = stackalloc byte[32];
        try
        {
            Rfc2898DeriveBytes.Pbkdf2(keyBytes, Convert.FromHexString(id.SaltHex), master, id.Iterations,
                                      HashAlgorithmName.SHA256);
            return FromMaster(master);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(master);
        }
    }

    /// 테스트의 빠른 키 만들기도 같은 나눔 규칙을 쓰게 연다. 규칙이 둘로 갈리면 테스트가 다른 형식을 시험한다.
    public static DerivedKey FromMaster(ReadOnlySpan<byte> master)
    {
        // 고정 배열: GC 가 옮기며 옛 위치에 사본을 남기면 잠그기·전환 때 지워도 그 사본은 남는다
        var encryption = GC.AllocateArray<byte>(32, pinned: true);
        var check = GC.AllocateArray<byte>(8, pinned: true);
        HKDF.Expand(HashAlgorithmName.SHA256, master, encryption, EncryptionInfo);
        HKDF.Expand(HashAlgorithmName.SHA256, master, check, CheckInfo);
        return new DerivedKey(encryption, check);
    }
}

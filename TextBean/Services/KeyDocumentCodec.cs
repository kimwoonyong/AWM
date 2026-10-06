using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using TextBean.Models;
using TextBean.Services.Interfaces;

namespace TextBean.Services;

/// <summary>
/// TBX1 버전 2 — 사용자가 넣은 키로 잠근다 (D-047). 다른 PC 에서도 exe · 파일 · 키만 있으면 열린다.
///
/// <code>
/// 0   TBX1          매직 4B
/// 4   02            버전
/// 5   KDF 종류       1 = PBKDF2-HMAC-SHA256 + HKDF
/// 6   반복 횟수      int32 LE
/// 10  salt          16B   같은 키 문서끼리 공유 (DocumentKeyId)
/// 26  키 확인값      8B    "다른 키"와 "손상"을 가른다 — AES-GCM 만으로는 둘 다 태그 불일치다 [실측]
/// 34  nonce         12B   암호화할 때마다 새로
/// 46  체크섬         4B    앞 46B 의 SHA-256 앞 4B. 헤더의 우연한 손상을 "다른 키"로 오판하지 않게 한다
/// 50  암호문 · 태그 16B   헤더 50B 전체가 AAD
/// </code>
///
/// 옛 exe 는 버전이 1보다 크면 "더 최신 버전"으로 잠가 이 파일을 덮어쓰지 않는다 [실측].
/// AES·PBKDF2·HKDF 는 이 파일과 KeyDerivation 밖에서 부르지 않는다 (PROHIBITED-CUSTOM-01).
/// </summary>
public sealed class KeyDocumentCodec(IVaultKeyService keys) : IDocumentCodec
{
    public const int FixedHeaderLength = 50;
    public const byte LegacyVersion = 1;
    public const byte CurrentVersion = 2;
    public const int SaltLength = 16;
    public const int CheckLength = 8;
    public const int NonceLength = 12;
    public const int TagLength = 16;

    /// OWASP 권고치. 키 입력 1회 약 0.1초 [실측 60~87ms]. 헤더에 적으므로 나중에 올릴 수 있다.
    public const int DefaultIterations = 600_000;

    // 범위 밖은 손상으로 본다. 상한이 없으면 조작된 파일 하나로 앱이 수 분 멈추고(int.MaxValue ≈ 265초),
    // 하한이 없으면 공격자가 약한 반복 횟수 헤더를 들이밀 수 있다.
    public const int MinIterations = 100_000;
    public const int MaxIterations = 10_000_000;

    private const int VersionOffset = 4;
    private const int KdfOffset = 5;
    private const int IterationsOffset = 6;
    private const int SaltOffset = 10;
    private const int CheckOffset = 26;
    private const int NonceOffset = 34;
    private const int ChecksumOffset = 46;

    private static ReadOnlySpan<byte> Magic => "TBX1"u8;

    // BOM 없는 UTF-8. BOM이 붙으면 왕복 시 평문 앞에 ﻿ 가 남는다.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public int HeaderLength => FixedHeaderLength;

    public DocumentHeader ParseHeader(ReadOnlySpan<byte> bytes) => Parse(bytes);

    public static DocumentHeader Parse(ReadOnlySpan<byte> bytes)
    {
        // 헤더를 두는 이유: 엉뚱한 파일을 열었을 때 "복호화 실패"가 아니라
        // "TextBean 문서가 아님"이라고 정확히 말할 수 있다.
        if (bytes.Length <= VersionOffset || !bytes[..4].SequenceEqual(Magic)) return new(DocumentHeaderKind.NotTextBean);

        var version = bytes[VersionOffset];
        if (version == LegacyVersion) return new(DocumentHeaderKind.Legacy);
        if (version > CurrentVersion) return new(DocumentHeaderKind.Newer);
        if (version != CurrentVersion || bytes.Length < FixedHeaderLength) return new(DocumentHeaderKind.Corrupted);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes[..ChecksumOffset], hash);
        if (!hash[..4].SequenceEqual(bytes.Slice(ChecksumOffset, 4))) return new(DocumentHeaderKind.Corrupted);

        var kdf = bytes[KdfOffset];
        var iterations = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(IterationsOffset, 4));
        if (kdf != Pbkdf2KeyDerivation.KdfPbkdf2Sha256 || iterations is < MinIterations or > MaxIterations)
            return new(DocumentHeaderKind.Corrupted);

        var id = new DocumentKeyId(kdf, iterations, Convert.ToHexString(bytes.Slice(SaltOffset, SaltLength)));
        return new(DocumentHeaderKind.Valid, id, bytes.Slice(CheckOffset, CheckLength).ToArray());
    }

    public DocumentReadResult Decrypt(byte[] fileBytes)
    {
        var header = Parse(fileBytes);
        switch (header.Kind)
        {
            case DocumentHeaderKind.NotTextBean: return DocumentReadResult.Fail(DocumentReadStatus.NotTextBeanDocument);
            case DocumentHeaderKind.Newer: return DocumentReadResult.Fail(DocumentReadStatus.NewerVersion);
            case DocumentHeaderKind.Legacy: return DocumentReadResult.Fail(DocumentReadStatus.LegacyDpapi);
            case DocumentHeaderKind.Corrupted: return DocumentReadResult.Fail(DocumentReadStatus.Corrupted);
        }

        if (fileBytes.Length < FixedHeaderLength + TagLength) return DocumentReadResult.Fail(DocumentReadStatus.Corrupted);

        using var lease = keys.Acquire();
        if (lease is null) return DocumentReadResult.Fail(DocumentReadStatus.NoKey);

        var key = lease.Find(header.Key, header.Check);
        if (key is null) return DocumentReadResult.Fail(DocumentReadStatus.DifferentKey);

        var cipherLength = fileBytes.Length - FixedHeaderLength - TagLength;
        var plain = new byte[cipherLength];
        try
        {
            using var aes = new AesGcm(key.EncryptionKey, TagLength);
            aes.Decrypt(fileBytes.AsSpan(NonceOffset, NonceLength),
                        fileBytes.AsSpan(FixedHeaderLength, cipherLength),
                        fileBytes.AsSpan(FixedHeaderLength + cipherLength, TagLength),
                        plain,
                        fileBytes.AsSpan(0, FixedHeaderLength));

            return DocumentReadResult.Success(Utf8NoBom.GetString(plain), new DocumentKeyBinding(lease.Generation, header.Key));
        }
        catch (AuthenticationTagMismatchException)
        {
            // 키 확인값은 맞았는데 태그가 틀렸다 — 본문이나 nonce 가 손상됐다
            return DocumentReadResult.Fail(DocumentReadStatus.Corrupted);
        }
        catch (Exception ex)
        {
            // 예외 메시지를 그대로 싣지 않는다. 타입 이름만 (PROHIBITED-CUSTOM-04)
            return DocumentReadResult.Fail(DocumentReadStatus.ReadFailed, ex.GetType().Name);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public byte[] EncryptForNew(string plainText)
    {
        using var lease = keys.Acquire() ?? throw new KeyUnavailableException(KeyUnavailableReason.NoKey);
        var (id, key) = lease.NewDocumentKey();
        return Seal(plainText, id, key);
    }

    public byte[] EncryptReplacing(string plainText, ReadOnlySpan<byte> existingHeader, DocumentKeyBinding? binding)
    {
        var header = Parse(existingHeader);
        if (header.Kind != DocumentHeaderKind.Valid)
            throw new KeyUnavailableException(KeyUnavailableReason.FileLockedWithOtherKey);

        using var lease = AcquireFor(binding);

        // 헤더의 네 값(KDF · 반복 · salt · 확인값)이 이 세대의 키와 모두 맞아야 쓴다
        var key = lease.Find(header.Key, header.Check)
                  ?? throw new KeyUnavailableException(KeyUnavailableReason.FileLockedWithOtherKey);
        return Seal(plainText, header.Key, key);
    }

    public byte[] EncryptFor(string plainText, DocumentKeyBinding binding)
    {
        using var lease = AcquireFor(binding);
        var key = lease.FindMatched(binding.Key) ?? throw new KeyUnavailableException(KeyUnavailableReason.KeyChanged);
        return Seal(plainText, binding.Key, key);
    }

    public DocumentKeyState Classify(DocumentHeader header)
    {
        switch (header.Kind)
        {
            case DocumentHeaderKind.Legacy: return DocumentKeyState.Legacy;
            case DocumentHeaderKind.Valid: break;
            default: return DocumentKeyState.Corrupted;
        }

        using var lease = keys.Acquire();
        if (lease is null) return DocumentKeyState.NoKey;

        return lease.Find(header.Key, header.Check) is null ? DocumentKeyState.DifferentKey : DocumentKeyState.Matches;
    }

    private KeyLease AcquireFor(DocumentKeyBinding? binding)
    {
        if (binding is null)
            return keys.Acquire() ?? throw new KeyUnavailableException(KeyUnavailableReason.NoKey);

        // 결속의 세대가 지금 세대가 아니면 쓰지 않는다 — 전환 뒤에 살아남은 탭이 새 키로 쓰는 길
        return keys.Acquire(binding.Generation) ?? throw new KeyUnavailableException(KeyUnavailableReason.KeyChanged);
    }

    private static byte[] Seal(string plainText, DocumentKeyId id, DerivedKey key)
    {
        var plain = Utf8NoBom.GetBytes(plainText);
        try
        {
            var output = new byte[FixedHeaderLength + plain.Length + TagLength];
            var header = output.AsSpan(0, FixedHeaderLength);

            Magic.CopyTo(header);
            header[VersionOffset] = CurrentVersion;
            header[KdfOffset] = id.KdfId;
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(IterationsOffset, 4), id.Iterations);
            Convert.FromHexString(id.SaltHex).CopyTo(header.Slice(SaltOffset, SaltLength));
            key.Check.CopyTo(header.Slice(CheckOffset, CheckLength));

            // nonce 는 언제나 새로 뽑는다. 기존 헤더에서 가져오는 것은 KDF · 반복 · salt · 확인값뿐이다 —
            // 같은 키에 nonce 가 되풀이되면 두 판의 평문이 드러나고 위조가 가능해진다.
            RandomNumberGenerator.Fill(header.Slice(NonceOffset, NonceLength));

            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(header[..ChecksumOffset], hash);
            hash[..4].CopyTo(header.Slice(ChecksumOffset, 4));

            using var aes = new AesGcm(key.EncryptionKey, TagLength);
            aes.Encrypt(header.Slice(NonceOffset, NonceLength),
                        plain,
                        output.AsSpan(FixedHeaderLength, plain.Length),
                        output.AsSpan(FixedHeaderLength + plain.Length, TagLength),
                        header);
            return output;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }
}

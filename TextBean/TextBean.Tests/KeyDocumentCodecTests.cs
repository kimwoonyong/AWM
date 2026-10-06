using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using TextBean.Models;
using TextBean.Services;

namespace TextBean.Tests;

/// <summary>
/// TBX1 버전 2 형식과 키 판정. 사용자 요구(2026-09-30)를 그대로 옮긴 것이 K-1 · K-2 다 —
/// "test1(1234)·test2(5678) 에서 키를 바꾸면 열리는 문서가 뒤바뀐다", "다른 PC 에서 파일 하나와 키만으로 열린다".
/// </summary>
public class KeyDocumentCodecTests
{
    private const string KeyA = "first-key-1234";
    private const string KeyB = "second-key-5678";

    private readonly KeyDocumentCodec _codec = TestKeys.Codec(KeyA);

    [Theory]
    [InlineData("")]
    [InlineData("API_KEY=sk-live-123")]
    [InlineData("한글 비밀번호 🔐 줄바꿈\r\n둘째 줄\n셋째 줄")]
    public void 왕복하면_원문이_그대로_나온다(string plain)
    {
        var result = _codec.Decrypt(_codec.EncryptForNew(plain));

        Assert.Equal(DocumentReadStatus.Ok, result.Status);
        Assert.Equal(plain, result.Text);
        Assert.NotNull(result.Binding);
    }

    [Fact]
    public void 매우_긴_문자열도_왕복한다()
    {
        var plain = string.Concat(Enumerable.Repeat("가나다ABC123\n", 20_000));

        Assert.Equal(plain, _codec.Decrypt(_codec.EncryptForNew(plain)).Text);
    }

    [Fact]
    public void 암호문에_평문_바이트가_들어있지_않다()
    {
        const string secret = "SUPER_SECRET_VALUE_8Qm";

        Assert.False(ContainsSequence(_codec.EncryptForNew(secret), Encoding.UTF8.GetBytes(secret)));
    }

    [Fact]
    public void 헤더는_TBX1_버전2_PBKDF2_60만회다()
    {
        var bytes = _codec.EncryptForNew("x");

        Assert.Equal("TBX1"u8.ToArray(), bytes[..4]);
        Assert.Equal(2, bytes[4]);
        Assert.Equal(Pbkdf2KeyDerivation.KdfPbkdf2Sha256, bytes[5]);
        Assert.Equal(KeyDocumentCodec.DefaultIterations, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(6, 4)));
        Assert.Equal(KeyDocumentCodec.FixedHeaderLength + 1 + KeyDocumentCodec.TagLength, bytes.Length);
    }

    [Fact]
    public void 평문_UTF8에_BOM이_없다()
        => Assert.False(_codec.Decrypt(_codec.EncryptForNew("A")).Text!.StartsWith('﻿'));

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3 })]
    [InlineData(new byte[] { (byte)'X', (byte)'X', (byte)'X', (byte)'X', 2, 9, 9 })]
    public void 문서가_아니면_NotTextBeanDocument(byte[] bytes)
        => Assert.Equal(DocumentReadStatus.NotTextBeanDocument, _codec.Decrypt(bytes).Status);

    [Fact]
    public void 상위_버전이면_NewerVersion()
    {
        var bytes = _codec.EncryptForNew("x");
        bytes[4] = 3;

        Assert.Equal(DocumentReadStatus.NewerVersion, _codec.Decrypt(bytes).Status);
    }

    /// 옛 exe 는 버전이 1보다 크면 NewerVersion 으로 잠가 새 문서를 덮어쓰지 않는다 (K-11).
    /// 그 옛 검사를 그대로 옮겨 새 형식에 대 본다 — 옛 코덱은 지웠으므로 규칙만 고정한다.
    [Fact]
    public void 옛_버전의_헤더_검사는_새_문서를_더_최신_버전으로_본다()
    {
        var bytes = _codec.EncryptForNew("x");
        const byte oldCodecCurrentVersion = 1;

        Assert.True(bytes[..4].SequenceEqual("TBX1"u8.ToArray()) && bytes[4] > oldCodecCurrentVersion);
    }

    [Fact]
    public void 버전1은_옛_방식_문서다()
    {
        byte[] legacy = [(byte)'T', (byte)'B', (byte)'X', (byte)'1', 1, 0x01, 0x00, 0x00, 0x00, 0xD0, 0x8C];

        Assert.Equal(DocumentReadStatus.LegacyDpapi, _codec.Decrypt(legacy).Status);
    }

    // ── 사용자 예시 (K-1 · K-2) ────────────────────────────────────────────────

    [Fact]
    public void 키를_바꿔_끼우면_열리는_문서가_뒤바뀌고_파일은_그대로다()
    {
        var test1 = TestKeys.Codec(KeyA).EncryptForNew("test1 내용");
        var test2 = TestKeys.Codec(KeyB).EncryptForNew("test2 내용");
        var before = (Hash(test1), Hash(test2));

        var keys = TestKeys.Service(KeyA);
        var codec = new KeyDocumentCodec(keys);
        Assert.Equal("test1 내용", codec.Decrypt(test1).Text);
        Assert.Equal(DocumentReadStatus.DifferentKey, codec.Decrypt(test2).Status);

        keys.Activate(keys.Evaluate(KeyB, []));

        Assert.Equal(DocumentReadStatus.DifferentKey, codec.Decrypt(test1).Status);
        Assert.Equal("test2 내용", codec.Decrypt(test2).Text);
        Assert.Equal(before, (Hash(test1), Hash(test2)));
    }

    [Fact]
    public void 다른_PC에서_파일_하나와_같은_키만으로_열린다()
    {
        var file = TestKeys.Codec(KeyA).EncryptForNew("다른 PC 로 가져간 값");

        // 캐시가 전혀 없는 새 서비스 — 다른 PC 의 첫 실행과 같다
        var elsewhere = TestKeys.Codec(KeyA);

        Assert.Equal("다른 PC 로 가져간 값", elsewhere.Decrypt(file).Text);
    }

    [Fact]
    public void 키가_없으면_NoKey다()
    {
        var file = _codec.EncryptForNew("x");
        var noKey = new KeyDocumentCodec(new VaultKeyService(new FastKeyDerivation()));

        Assert.Equal(DocumentReadStatus.NoKey, noKey.Decrypt(file).Status);
        Assert.Throws<KeyUnavailableException>(() => noKey.EncryptForNew("x"));
    }

    // ── 손상은 "다른 키"가 아니다 (K-3) ────────────────────────────────────────

    [Fact]
    public void 본문_한_바이트가_바뀌면_Corrupted다()
    {
        var bytes = _codec.EncryptForNew("some content here");
        bytes[KeyDocumentCodec.FixedHeaderLength + 2] ^= 0x01;

        Assert.Equal(DocumentReadStatus.Corrupted, _codec.Decrypt(bytes).Status);
    }

    [Fact]
    public void 헤더_salt_한_바이트가_바뀌면_Corrupted다()
    {
        var bytes = _codec.EncryptForNew("x");
        bytes[12] ^= 0x01;

        Assert.Equal(DocumentReadStatus.Corrupted, _codec.Decrypt(bytes).Status);
    }

    [Fact]
    public void 끝이_잘리면_Corrupted다()
    {
        var bytes = _codec.EncryptForNew("some content here");

        Assert.Equal(DocumentReadStatus.Corrupted, _codec.Decrypt(bytes[..^10]).Status);
        Assert.Equal(DocumentReadStatus.Corrupted, _codec.Decrypt(bytes[..30]).Status);
    }

    [Theory]
    [InlineData(99_999)]
    [InlineData(10_000_001)]
    [InlineData(int.MaxValue)]
    public void 반복_횟수가_범위_밖이면_키를_만들기_전에_Corrupted다(int iterations)
    {
        var derivation = new FastKeyDerivation();
        var codec = new KeyDocumentCodec(TestKeys.Service(KeyA, derivation));
        var bytes = codec.EncryptForNew("x");
        var before = derivation.Calls;

        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(6, 4), iterations);
        RewriteChecksum(bytes);

        Assert.Equal(DocumentReadStatus.Corrupted, codec.Decrypt(bytes).Status);
        Assert.Equal(before, derivation.Calls);
    }

    [Fact]
    public void 손상_메시지는_열었을_때_상태_보기를_안내한다()
    {
        // 없는 버튼 이름("이전 세대 열기")을 안내하던 것을 고쳤다
        var message = DocumentReadResult.Fail(DocumentReadStatus.Corrupted).UserMessage;

        Assert.Contains("열었을 때 상태 보기", message);
        Assert.DoesNotContain("이전 세대", message);
    }

    // ── nonce · salt ─────────────────────────────────────────────────────────

    [Fact]
    public void 같은_본문을_두_번_암호화하면_nonce와_암호문이_다르다()
    {
        var first = _codec.EncryptForNew("같은 값");
        var second = _codec.EncryptForNew("같은 값");

        Assert.NotEqual(first.AsSpan(34, 12).ToArray(), second.AsSpan(34, 12).ToArray());
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void 같은_키로_만든_문서는_salt를_공유하고_키는_한_번만_만든다()
    {
        var derivation = new FastKeyDerivation();
        var codec = new KeyDocumentCodec(TestKeys.Service(KeyA, derivation));

        var docs = Enumerable.Range(0, 3).Select(i => codec.EncryptForNew($"문서{i}")).ToList();

        Assert.Single(docs.Select(d => Convert.ToHexString(d, 10, 16)).Distinct());
        Assert.Equal(1, derivation.Calls);

        // 다른 PC: 세 문서를 판정해도 salt 가 하나라 키 만들기는 한 번
        var elsewhere = new FastKeyDerivation();
        var service = new VaultKeyService(elsewhere);
        var evaluation = service.Evaluate(KeyA, docs.Select(d => (DocumentHeader?)KeyDocumentCodec.Parse(d)).ToList());

        Assert.Equal(3, evaluation.MatchCount);
        Assert.Equal(1, elsewhere.Calls);
    }

    [Fact]
    public void 판정은_맞음_다른키_옛방식_손상_읽지못함을_가른다()
    {
        var mine = _codec.EncryptForNew("a");
        var other = TestKeys.Codec(KeyB).EncryptForNew("b");
        var corrupt = _codec.EncryptForNew("c");
        corrupt[12] ^= 0x01;
        byte[] legacy = [(byte)'T', (byte)'B', (byte)'X', (byte)'1', 1, 9, 9];

        var service = new VaultKeyService(new FastKeyDerivation());
        var headers = new List<DocumentHeader?>
        {
            KeyDocumentCodec.Parse(mine), KeyDocumentCodec.Parse(other), KeyDocumentCodec.Parse(corrupt),
            KeyDocumentCodec.Parse(legacy), null
        };

        var evaluation = service.Evaluate(KeyA, headers);

        Assert.Equal((1, 1, 1, 1, 1, 5),
            (evaluation.MatchCount, evaluation.OtherKeyCount, evaluation.CorruptedCount,
             evaluation.LegacyCount, evaluation.UnreadableCount, evaluation.Total));
    }

    // ── 덮어쓰기는 그 파일을 잠근 키로만 ──────────────────────────────────────

    [Fact]
    public void 다른_키로_잠긴_파일은_덮어쓰지_않는다()
    {
        var other = TestKeys.Codec(KeyB).EncryptForNew("b");

        var ex = Assert.Throws<KeyUnavailableException>(() => _codec.EncryptReplacing("x", other, binding: null));
        Assert.Equal(KeyUnavailableReason.FileLockedWithOtherKey, ex.Reason);
    }

    [Fact]
    public void salt가_같아도_반복_횟수가_다르면_같은_키로_보지_않는다()
    {
        var bytes = _codec.EncryptForNew("x");
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(6, 4), 700_000);
        RewriteChecksum(bytes);

        // 60만 회로 만든 키로 쓰면서 70만 회를 적으면 어느 키로도 열리지 않는 문서가 된다
        Assert.Throws<KeyUnavailableException>(() => _codec.EncryptReplacing("y", bytes, binding: null));
        Assert.Equal(DocumentReadStatus.DifferentKey, _codec.Decrypt(bytes).Status);
    }

    [Fact]
    public void 키를_바꾼_뒤_옛_세대_결속으로는_쓰지_않는다()
    {
        var keys = TestKeys.Service(KeyA);
        var codec = new KeyDocumentCodec(keys);
        var file = codec.EncryptForNew("A 문서");
        var binding = codec.Decrypt(file).Binding!;

        keys.Activate(keys.Evaluate(KeyA, [KeyDocumentCodec.Parse(file)]));     // 같은 키라도 세대가 바뀐다

        Assert.Equal(KeyUnavailableReason.KeyChanged,
            Assert.Throws<KeyUnavailableException>(() => codec.EncryptReplacing("x", file, binding)).Reason);
        Assert.Equal(KeyUnavailableReason.KeyChanged,
            Assert.Throws<KeyUnavailableException>(() => codec.EncryptFor("x", binding)).Reason);
    }

    [Fact]
    public void 잠그면_키가_없어진다()
    {
        var keys = TestKeys.Service(KeyA);
        var codec = new KeyDocumentCodec(keys);
        var file = codec.EncryptForNew("x");

        keys.Clear();

        Assert.False(keys.HasKey);
        Assert.Equal(DocumentReadStatus.NoKey, codec.Decrypt(file).Status);
    }

    /// 전환하는 사이 진행 중인 작업이 옛 키를 쓰고 있을 수 있다. 제자리에서 지우면 그 작업이 0 키로
    /// 암호화해 문서가 어느 키로도 열리지 않는다. 빌린 작업이 끝난 뒤에 지워야 한다.
    [Fact]
    public void 옛_세대의_키는_빌린_작업이_끝난_뒤에_지운다()
    {
        var keys = TestKeys.Service(KeyA);
        var lease = keys.Acquire()!;
        var (_, key) = lease.NewDocumentKey();

        keys.Activate(keys.Evaluate(KeyB, []));

        Assert.Contains(key.EncryptionKey, b => b != 0);      // 아직 쓰는 중 — 지우지 않았다
        lease.Dispose();
        Assert.All(key.EncryptionKey, b => Assert.Equal(0, b));
    }

    [Fact]
    public void 쓰지_않기로_한_판정의_키는_지운다()
    {
        var file = _codec.EncryptForNew("x");
        var keys = new VaultKeyService(new FastKeyDerivation());
        var evaluation = keys.Evaluate(KeyA, [KeyDocumentCodec.Parse(file)]);

        keys.Discard(evaluation);

        Assert.False(keys.HasKey);
        Assert.Throws<InvalidOperationException>(() => keys.Activate(evaluation));
    }

    [Fact]
    public void 새_키_규칙을_못_넘는_키로는_새_문서를_만들지_않지만_열기는_한다()
    {
        var file = TestKeys.Codec(KeyA).EncryptForNew("x");
        var weak = new KeyDocumentCodec(TestKeys.Service("short"));

        Assert.Equal(KeyUnavailableReason.WeakKeyForNewDocument,
            Assert.Throws<KeyUnavailableException>(() => weak.EncryptForNew("y")).Reason);
        Assert.Equal(DocumentReadStatus.DifferentKey, weak.Decrypt(file).Status);
    }

    // ── 키 규칙 (K-8) ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", KeyRuleResult.Empty)]
    [InlineData("1234567", KeyRuleResult.TooShort)]
    [InlineData("12345678", KeyRuleResult.Ok)]
    [InlineData(" 12345678", KeyRuleResult.EdgeWhitespace)]
    [InlineData("12345678 ", KeyRuleResult.EdgeWhitespace)]
    [InlineData("비밀번호1234", KeyRuleResult.ContainsHangul)]
    [InlineData("abcdㄱ1234", KeyRuleResult.ContainsHangul)]
    public void 새_키_규칙(string key, KeyRuleResult expected)
        => Assert.Equal(expected, KeyRules.CheckNew(KeyRules.Normalize(key)));

    [Fact]
    public void 길이는_NFC로_정규화한_뒤_센다()
    {
        // 조합형 é(e + U+0301)는 8글자처럼 보이는 입력을 7자로 만든다
        var composed = "abcdef" + "é";

        Assert.Equal(8, composed.Length);
        Assert.Equal(KeyRuleResult.TooShort, KeyRules.CheckNew(KeyRules.Normalize(composed)));
    }

    [Fact]
    public void NFC와_NFD는_같은_키가_된다()
    {
        var nfc = "café-key-123";
        var nfd = "café-key-123";
        var file = TestKeys.Codec(nfc).EncryptForNew("x");

        Assert.Equal("x", TestKeys.Codec(nfd).Decrypt(file).Text);
    }

    // ── 도우미 ───────────────────────────────────────────────────────────────

    private static void RewriteChecksum(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes.AsSpan(0, 46));
        hash.AsSpan(0, 4).CopyTo(bytes.AsSpan(46, 4));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static bool ContainsSequence(byte[] haystack, byte[] needle)
        => haystack.AsSpan().IndexOf(needle) >= 0;
}

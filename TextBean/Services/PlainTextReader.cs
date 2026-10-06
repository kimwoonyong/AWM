using System.Text;
using TextBean.Models;

namespace TextBean.Services;

/// <summary>
/// 평문 바이트를 문자열로 바꾼다. KeyDocumentCodec 이 암복호 경계인 것처럼
/// 인코딩 판별·디코딩은 전부 이 클래스 안에 가둔다. 디스크에는 닿지 않는다 —
/// 파일을 읽는 것은 DocumentStore 의 일이다 (PROHIBITED-CUSTOM-03).
///
/// 판별이 틀려도 예외는 나지 않는다. CP949 로 잘못 읽으면 '?' 가, UTF-8 로 잘못 읽으면
/// U+FFFD 가 나올 뿐이고 WPF 는 그대로 그린다. 그래서 무엇으로 읽었는지와
/// 그것이 확정인지 추정인지를 결과에 함께 실어 화면까지 나른다.
/// </summary>
public static class PlainTextReader
{
    private const int Cp949CodePage = 949;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static PlainTextReader()
    {
        // CP949(한국어 ANSI)는 .NET 기본 제공자에 없어 GetEncoding(949) 가 NotSupportedException 을 던진다.
        // 제공자 타입 자체는 공유 프레임워크에 들어 있어 PackageReference 가 필요 없다 —
        // 패키지를 추가하면 NU1510 경고 2개가 나서 "경고 0" 게이트가 깨진다 [실측].
        //
        // 등록을 조립 루트(App)가 아니라 여기에 두는 이유: 이 클래스가 유일한 사용처이고,
        // 정적 생성자는 첫 사용 직전에 정확히 한 번만 돈다. 조립 루트에 두면 테스트와 프로브가
        // 그 경로를 타지 않아 등록이 빠지고, 빌드는 통과한 채 실행 시점에만 터진다.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// 판별 순서는 고정이다 — ① BOM ② NUL ③ UTF-8 엄격 ④ CP949.
    /// CP949 를 먼저 시도하면 거의 모든 바이트를 받아들여서 UTF-8 파일이 전부 깨진다 [실측].
    /// </summary>
    public static DocumentReadResult Decode(byte[] bytes)
    {
        var byBom = DecodeByBom(bytes);
        if (byBom is not null) return byBom;

        // NUL 이 섞여 있으면 이진 파일이거나 BOM 없는 UTF-16 이다. 둘 다 지원 범위 밖이다.
        // 깨진 채로 보여주면 사용자는 앱이 아니라 자기 파일이 깨졌다고 읽는다.
        if (Array.IndexOf(bytes, (byte)0) >= 0) return Undecodable;

        return TryDecode(StrictUtf8, bytes, "UTF-8", isGuess: true)
               ?? TryDecode(StrictCp949(), bytes, "CP949", isGuess: true)
               ?? Undecodable;
    }

    private static DocumentReadResult Undecodable => DocumentReadResult.Fail(DocumentReadStatus.UndecodableText);

    private static DocumentReadResult? DecodeByBom(byte[] bytes)
    {
        // UTF-32 는 UTF-16 과 같은 두 바이트로 시작한다. 먼저 걸러내지 않으면
        // NUL 이 가득한 UTF-16 문자열로 잘못 읽힌다. 지원 범위 밖이라 판정 불가로 보낸다.
        if (StartsWith(bytes, [0xFF, 0xFE, 0x00, 0x00]) || StartsWith(bytes, [0x00, 0x00, 0xFE, 0xFF]))
            return Undecodable;

        // BOM 은 디코딩 후 문자열에 U+FEFF 로 남는다. 길이만큼 건너뛰지 않으면 본문 맨 앞에
        // 보이지 않는 문자가 생겨 검색 강조 사각형이 한 칸씩 밀린다.
        if (StartsWith(bytes, [0xEF, 0xBB, 0xBF]))
            return TryDecode(StrictUtf8, bytes, "UTF-8 (BOM)", isGuess: false, offset: 3) ?? Undecodable;

        if (StartsWith(bytes, [0xFF, 0xFE])) return DecodeUtf16(bytes, Encoding.Unicode, "UTF-16 LE (BOM)");
        if (StartsWith(bytes, [0xFE, 0xFF])) return DecodeUtf16(bytes, Encoding.BigEndianUnicode, "UTF-16 BE (BOM)");

        return null;
    }

    private static DocumentReadResult DecodeUtf16(byte[] bytes, Encoding encoding, string label)
        // 홀수 길이면 마지막 코드 단위가 잘려 있다. 그냥 디코딩하면 끝 한 글자가 조용히 사라진다.
        => (bytes.Length - 2) % 2 != 0
            ? Undecodable
            : TryDecode(encoding, bytes, label, isGuess: false, offset: 2) ?? Undecodable;

    /// <summary>
    /// 엄격 CP949. 완벽한 관문은 아니다 — 0xFF·0x80 단독은 예외 없이 통과한다 [실측].
    /// 그래서 성공해도 결과는 "추정"으로 표시한다.
    /// </summary>
    private static Encoding? StrictCp949()
    {
        try
        {
            return Encoding.GetEncoding(Cp949CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        catch (NotSupportedException)
        {
            return null;          // 제공자 등록이 없는 환경. 조용히 깨뜨리지 않고 판정 불가로 떨어뜨린다.
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static DocumentReadResult? TryDecode(Encoding? encoding, byte[] bytes, string label, bool isGuess, int offset = 0)
    {
        if (encoding is null) return null;

        try
        {
            return DocumentReadResult.PlainText(encoding.GetString(bytes, offset, bytes.Length - offset), label, isGuess);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static bool StartsWith(byte[] bytes, ReadOnlySpan<byte> prefix)
        => bytes.Length >= prefix.Length && bytes.AsSpan(0, prefix.Length).SequenceEqual(prefix);
}

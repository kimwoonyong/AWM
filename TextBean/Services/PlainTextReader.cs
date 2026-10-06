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

        return TryDecode(StrictUtf8, bytes, "UTF-8", isGuess: true, "UTF-8", hasBom: false)
               ?? TryDecode(StrictCp949(), bytes, "CP949", isGuess: true, "CP949", hasBom: false)
               ?? Undecodable;
    }

    /// <summary>
    /// 읽은 형식 그대로 다시 쓴다 (D-118) — 인코딩 · BOM · 줄바꿈. 줄바꿈은 파일이 쓰던 하나로 맞춘다
    /// (Enter 는 CRLF 를 넣는다). 담을 수 없는 글자가 있으면 PlainTextEncodeException — 인코딩을 바꾸지 않는다 (D-119).
    /// CP949 는 읽었다 다시 쓰면 바이트가 같다 [실측 — 0x80 · 0xFF 단독 포함].
    /// </summary>
    public static byte[] Encode(string text, PlainTextFormat format)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (format.NewLine == "\r\n") normalized = normalized.Replace("\n", "\r\n");

        var encoding = StrictEncoder(format.Encoding)
                       ?? throw new InvalidOperationException($"{format.Encoding} 로 쓸 수 없습니다.");
        byte[] body;
        try
        {
            body = encoding.GetBytes(normalized);
        }
        catch (EncoderFallbackException)
        {
            throw new PlainTextEncodeException(FirstUnencodableLine(normalized, encoding), format.Encoding);
        }

        if (!format.HasBom) return body;
        byte[] bom = format.Encoding switch
        {
            "UTF-8" => [0xEF, 0xBB, 0xBF],
            "UTF-16LE" => [0xFF, 0xFE],
            "UTF-16BE" => [0xFE, 0xFF],
            _ => []
        };
        return [.. bom, .. body];
    }

    private static Encoding? StrictEncoder(string name) => name switch
    {
        "UTF-8" => StrictUtf8,
        "UTF-16LE" => new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true),
        "UTF-16BE" => new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true),
        "CP949" => StrictCp949(),
        _ => null
    };

    private static int FirstUnencodableLine(string text, Encoding encoding)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            try { encoding.GetBytes(lines[i]); }
            catch (EncoderFallbackException) { return i + 1; }
        }
        return 1;
    }

    /// 파일에서 많이 쓴 줄바꿈. 줄바꿈이 없으면 Windows 기본(CRLF).
    private static string DominantNewLine(string text)
    {
        var crlf = 0;
        var lf = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            if (i > 0 && text[i - 1] == '\r') crlf++;
            else lf++;
        }
        return lf > crlf ? "\n" : "\r\n";
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
            return TryDecode(StrictUtf8, bytes, "UTF-8 (BOM)", isGuess: false, "UTF-8", hasBom: true, offset: 3) ?? Undecodable;

        if (StartsWith(bytes, [0xFF, 0xFE])) return DecodeUtf16(bytes, Encoding.Unicode, "UTF-16 LE (BOM)", "UTF-16LE");
        if (StartsWith(bytes, [0xFE, 0xFF])) return DecodeUtf16(bytes, Encoding.BigEndianUnicode, "UTF-16 BE (BOM)", "UTF-16BE");

        return null;
    }

    private static DocumentReadResult DecodeUtf16(byte[] bytes, Encoding encoding, string label, string name)
        // 홀수 길이면 마지막 코드 단위가 잘려 있다. 그냥 디코딩하면 끝 한 글자가 조용히 사라진다.
        => (bytes.Length - 2) % 2 != 0
            ? Undecodable
            : TryDecode(encoding, bytes, label, isGuess: false, name, hasBom: true, offset: 2) ?? Undecodable;

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

    private static DocumentReadResult? TryDecode(Encoding? encoding, byte[] bytes, string label, bool isGuess,
                                                 string name, bool hasBom, int offset = 0)
    {
        if (encoding is null) return null;

        try
        {
            var text = encoding.GetString(bytes, offset, bytes.Length - offset);
            return DocumentReadResult.PlainText(text, label, isGuess, new PlainTextFormat(name, hasBom, DominantNewLine(text)));
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static bool StartsWith(byte[] bytes, ReadOnlySpan<byte> prefix)
        => bytes.Length >= prefix.Length && bytes.AsSpan(0, prefix.Length).SequenceEqual(prefix);
}

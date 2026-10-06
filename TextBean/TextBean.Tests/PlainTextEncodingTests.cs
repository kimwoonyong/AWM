using System.Text;
using TextBean.Models;
using TextBean.Services;

namespace TextBean.Tests;

/// <summary>
/// 인코딩 판별을 고정한다. 오판은 예외를 내지 않고 조용히 깨진 글자를 만들기 때문에
/// 빌드도 통과하고 화면도 뜬다 — 자동 테스트 말고는 잡을 수단이 없다 (LL-073).
/// </summary>
public class PlainTextEncodingTests
{
    private const string Korean = "서버 접속 정보 abc 123";

    private static Encoding Cp949
    {
        get
        {
            // PlainTextReader 의 정적 생성자가 등록하지만, 이 테스트는 그보다 먼저 돌 수 있다.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(949);
        }
    }

    private static byte[] Utf8(string s, bool bom)
        => bom
            ? [.. new byte[] { 0xEF, 0xBB, 0xBF }, .. Encoding.UTF8.GetBytes(s)]
            : new UTF8Encoding(false).GetBytes(s);

    [Fact]
    public void BOM_없는_UTF8은_추정으로_읽는다()
    {
        var result = PlainTextReader.Decode(Utf8(Korean, bom: false));

        Assert.True(result.IsOk);
        Assert.Equal(DocumentKind.PlainText, result.Kind);
        Assert.Equal(Korean, result.Text);
        Assert.Equal("UTF-8", result.EncodingLabel);
        Assert.True(result.EncodingIsGuess);           // BOM 이 없으면 확정이라고 말하지 않는다
    }

    /// <summary>
    /// BOM 을 건너뛰지 않으면 본문 맨 앞에 보이지 않는 U+FEFF 가 남고, 검색 일치 인덱스가
    /// 1씩 밀려 강조 사각형이 엉뚱한 자리에 칠해진다. 비밀 보관함에서는 표시 오류가 아니다.
    /// </summary>
    [Fact]
    public void UTF8_BOM은_확정이고_본문에_BOM이_안_남는다()
    {
        var result = PlainTextReader.Decode(Utf8(Korean, bom: true));

        Assert.Equal(Korean, result.Text);
        Assert.DoesNotContain('﻿', result.Text!);
        Assert.Equal("UTF-8 (BOM)", result.EncodingLabel);
        Assert.False(result.EncodingIsGuess);
    }

    [Fact]
    public void UTF16_LE_BOM을_읽는다()
    {
        var result = PlainTextReader.Decode([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Korean)]);

        Assert.Equal(Korean, result.Text);
        Assert.DoesNotContain('﻿', result.Text!);
        Assert.Equal("UTF-16 LE (BOM)", result.EncodingLabel);
        Assert.False(result.EncodingIsGuess);
    }

    [Fact]
    public void UTF16_BE_BOM을_읽는다()
    {
        var result = PlainTextReader.Decode(
            [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(Korean)]);

        Assert.Equal(Korean, result.Text);
        Assert.Equal("UTF-16 BE (BOM)", result.EncodingLabel);
    }

    [Fact]
    public void CP949_한글을_패키지_없이_읽는다()
    {
        var result = PlainTextReader.Decode(Cp949.GetBytes(Korean));

        Assert.True(result.IsOk);
        Assert.Equal(Korean, result.Text);
        Assert.Equal("CP949", result.EncodingLabel);
        Assert.True(result.EncodingIsGuess);
    }

    /// <summary>
    /// 순서가 뒤집히면 CP949 가 거의 모든 바이트를 받아들여 UTF-8 한글이 전부 깨진다.
    /// 이 테스트가 바로 그 순서를 고정한다.
    /// </summary>
    [Fact]
    public void UTF8을_CP949보다_먼저_시도한다()
    {
        var result = PlainTextReader.Decode(Utf8("한글만 있는 메모", bom: false));

        Assert.Equal("UTF-8", result.EncodingLabel);
        Assert.Equal("한글만 있는 메모", result.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("password=hunter2 abc 123")]
    public void 빈_파일과_영문_파일은_UTF8로_읽는다(string content)
    {
        var result = PlainTextReader.Decode(Utf8(content, bom: false));

        Assert.True(result.IsOk);
        Assert.Equal(content, result.Text);
        Assert.Equal("UTF-8", result.EncodingLabel);
    }

    [Fact]
    public void NUL이_있으면_판정_불가다()
    {
        var result = PlainTextReader.Decode([.. "앞"u8.ToArray(), 0x00, .. "뒤"u8.ToArray()]);

        Assert.False(result.IsOk);
        Assert.Equal(DocumentReadStatus.UndecodableText, result.Status);
        Assert.Null(result.Text);
    }

    /// BOM 없는 UTF-16 은 지원 범위 밖이다. NUL 검사에 걸려 판정 불가가 된다.
    [Fact]
    public void BOM_없는_UTF16은_판정_불가다()
    {
        var result = PlainTextReader.Decode(Encoding.Unicode.GetBytes(Korean));

        Assert.Equal(DocumentReadStatus.UndecodableText, result.Status);
    }

    /// UTF-32 는 UTF-16 과 같은 두 바이트로 시작한다. 먼저 걸러내지 않으면 NUL 투성이로 읽힌다.
    [Fact]
    public void UTF32_BOM은_UTF16으로_오인하지_않는다()
    {
        var result = PlainTextReader.Decode([0xFF, 0xFE, 0x00, 0x00, 0x41, 0x00, 0x00, 0x00]);

        Assert.Equal(DocumentReadStatus.UndecodableText, result.Status);
    }

    [Fact]
    public void UTF16_BOM인데_길이가_홀수면_판정_불가다()
    {
        var result = PlainTextReader.Decode([.. Encoding.Unicode.GetPreamble(), 0x41, 0x00, 0x42]);

        Assert.Equal(DocumentReadStatus.UndecodableText, result.Status);
    }

    [Fact]
    public void 이진_파일은_판정_불가다()
    {
        // PNG 머리말
        var result = PlainTextReader.Decode([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D]);

        Assert.Equal(DocumentReadStatus.UndecodableText, result.Status);
    }

    /// <summary>
    /// 한글 한가운데에서 잘린 UTF-8 은 엄격 검사를 통과하지 못한다.
    /// 무엇으로 읽든 "UTF-8 이다"라고 단정해서는 안 된다 — 단정하면 사용자가 깨진 글자를 사실로 믿는다.
    /// </summary>
    [Fact]
    public void 잘린_UTF8을_UTF8이라고_단정하지_않는다()
    {
        var full = Utf8("한글메모", bom: false);
        var result = PlainTextReader.Decode(full[..^1]);

        Assert.NotEqual("UTF-8", result.EncodingLabel);
        if (result.IsOk) Assert.True(result.EncodingIsGuess);
    }

    /// 어떤 입력에도 예외를 던지지 않는다 — 던지면 트리 클릭이 앱을 죽인다.
    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0xFF })]
    [InlineData(new byte[] { 0x80 })]
    [InlineData(new byte[] { 0xFE })]
    [InlineData(new byte[] { 0xEF, 0xBB })]
    [InlineData(new byte[] { 0xFF, 0xFE })]
    [InlineData(new byte[] { 0xB0 })]
    public void 어떤_바이트에도_예외를_던지지_않는다(byte[] bytes)
    {
        var result = PlainTextReader.Decode(bytes);

        Assert.True(result.IsOk || result.Status == DocumentReadStatus.UndecodableText);
    }
}

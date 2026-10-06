using TextBean.Services;

namespace TextBean.Tests;

public class PathRulesTests
{
    [Theory]
    [InlineData(@"C:\Vault", @"C:\Vault\a\b.tbx", true)]
    [InlineData(@"C:\Vault", @"C:\Vault\..\Other\b.tbx", false)]
    [InlineData(@"C:\Vault", @"C:\VaultOther\b.tbx", false)]   // 접두사만 같은 형제 폴더
    [InlineData(@"C:\Vault", @"C:\Vault", true)]
    public void IsInsideRoot_판정(string root, string candidate, bool expected)
        => Assert.Equal(expected, PathRules.IsInsideRoot(root, candidate));

    [Theory]
    [InlineData("정상이름", NameCheckResult.Ok)]
    [InlineData("", NameCheckResult.Empty)]
    [InlineData("  ", NameCheckResult.Empty)]
    [InlineData("a/b", NameCheckResult.InvalidChars)]
    [InlineData("a:b", NameCheckResult.InvalidChars)]
    [InlineData("CON", NameCheckResult.ReservedDeviceName)]
    [InlineData("com1", NameCheckResult.ReservedDeviceName)]
    [InlineData("이름.", NameCheckResult.TrailingDotOrSpace)]
    [InlineData("이름 ", NameCheckResult.TrailingDotOrSpace)]
    public void CheckName_판정(string name, NameCheckResult expected)
        => Assert.Equal(expected, PathRules.CheckName(name, directlyUnderRoot: false));

    /// "열었을 때 상태" 탭의 원본 폴더를 ▾ 목록에 보일 때 쓴다 (make-tab-strip-single-row).
    [Theory]
    [InlineData(@"C:\Vault\문서.tbx")]
    [InlineData(@"C:\Vault\폴더1\하위\문서.tbx")]
    public void 열었을_때_상태_경로에서_원본_문서를_되찾는다(string document)
    {
        const string root = @"C:\Vault";
        var snapshot = PathRules.SnapshotPathFor(root, document);

        Assert.Equal(document, PathRules.DocumentPathForSnapshot(root, snapshot));
    }

    [Theory]
    [InlineData(@"C:\Vault\문서.tbx")]                              // .history 밖
    [InlineData(@"C:\Vault\.history\문서.tbx\20260101.tbx")]        // 옛 이력 — 열었을 때 상태가 아님
    [InlineData(@"C:\Vault\.history\opened.tbx")]                   // 문서 자리가 없음
    [InlineData(@"C:\Other\.history\문서.tbx\opened.tbx")]          // 다른 금고
    public void 열었을_때_상태가_아니면_원본을_돌려주지_않는다(string path)
        => Assert.Null(PathRules.DocumentPathForSnapshot(@"C:\Vault", path));

    [Fact]
    public void 루트_바로_아래_trash는_예약어()
    {
        Assert.Equal(NameCheckResult.ReservedTrashName, PathRules.CheckName(".trash", directlyUnderRoot: true));
        Assert.Equal(NameCheckResult.Ok, PathRules.CheckName(".trash", directlyUnderRoot: false));
    }

    [Fact]
    public void 파생경로_길이는_이력과_휴지통_중_긴_쪽()
    {
        const string root = @"C:\Vault";
        const string doc = @"C:\Vault\a\b\문서.tbx";
        const string stamp = "20260921-143000-000";

        var length = PathRules.LongestDerivedLength(root, doc, stamp);

        // 원본 경로만 재면 "만들 수는 있는데 저장도 삭제도 못 하는 문서"가 생긴다
        Assert.True(length > doc.Length);
        Assert.Equal(
            Math.Max(PathRules.SnapshotPathFor(root, doc).Length, PathRules.TrashPathFor(root, doc, stamp).Length),
            length);
    }

    [Fact]
    public void 이력_경로는_문서의_상대경로를_따라간다()
    {
        const string root = @"C:\Vault";

        var folder = PathRules.HistoryFolderFor(root, @"C:\Vault\a\b\문서.tbx");

        Assert.Equal(@"C:\Vault\.history\a\b\문서.tbx", folder);
    }

    [Fact]
    public void 루트_바로_아래_history도_예약어()
        => Assert.Equal(NameCheckResult.ReservedTrashName, PathRules.CheckName(".history", directlyUnderRoot: true));

    [Theory]
    [InlineData(@"C:\Vault\메모.txt", true)]
    [InlineData(@"C:\Vault\메모.TXT", true)]          // 대소문자를 무시하지 않으면 트리에서 조용히 빠진다
    [InlineData(@"C:\Vault\메모.tbx", false)]
    [InlineData(@"C:\Vault\메모.txt.tbx", false)]     // 마지막 확장자만 본다
    [InlineData(@"C:\Vault\메모.txtx", false)]
    [InlineData(@"C:\Vault\메모.txt.tmp", false)]
    [InlineData(@"C:\Vault\메모", false)]
    public void 평문_판정은_마지막_확장자만_대소문자_무시로_본다(string path, bool expected)
        => Assert.Equal(expected, PathRules.IsPlainText(path));

    [Theory]
    [InlineData(@"C:\Vault\메모.tbx", true)]
    [InlineData(@"C:\Vault\메모.TBX", true)]
    [InlineData(@"C:\Vault\메모.tbx.bak", false)]
    [InlineData(@"C:\Vault\메모.txt", false)]
    public void 금고문서_판정도_같은_규칙이다(string path, bool expected)
        => Assert.Equal(expected, PathRules.IsDocument(path));

    [Fact]
    public void 트리에_싣는_확장자는_허용목록_두_개다()
    {
        Assert.Equal([".tbx", ".txt"], PathRules.ListedExtensions);
        Assert.True(PathRules.IsListed(@"C:\Vault\a.tbx"));
        Assert.True(PathRules.IsListed(@"C:\Vault\a.txt"));

        // 제외 목록으로 가면 이 둘이 전부 문서로 뜬다
        Assert.False(PathRules.IsListed(@"C:\Vault\a.tbx.bak"));
        Assert.False(PathRules.IsListed(@"C:\Vault\a.env"));
    }

    /// <summary>
    /// 평문은 스냅샷을 만들지 않지만, 파생 경로 계산식 자체는 두 확장자에 같은 값을 준다
    /// (둘 다 4글자). 계획에서 "이 계산은 변경 대상이 아니다"로 못 박은 근거를 고정한다.
    /// </summary>
    [Fact]
    public void 파생_경로_길이는_평문과_금고문서가_같다()
    {
        const string root = @"C:\Vault";
        const string stamp = "20260923-143000-000";

        Assert.Equal(
            PathRules.LongestDerivedLength(root, @"C:\Vault\a\메모.tbx", stamp),
            PathRules.LongestDerivedLength(root, @"C:\Vault\a\메모.txt", stamp));
    }
}

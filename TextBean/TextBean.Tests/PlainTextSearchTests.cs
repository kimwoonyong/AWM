using System.Text;
using TextBean.Models;
using TextBean.Services;

namespace TextBean.Tests;

/// <summary>
/// 평문 파일이 검색에서 어떻게 다뤄지는지 고정한다.
///
/// 가장 위험한 실패는 "못 찾았다"가 아니라 <b>상시 거짓 경보</b>다.
/// 읽기 경로를 고치지 않은 채 트리 열거만 넓히면 .txt 한 건마다 UnreadableCount 가 올라
/// 금고에 평문이 하나만 있어도 모든 검색에 "확인하지 못한 곳이 있습니다" 띠가 켜진다.
/// 상시로 뜨면 진짜 사각지대가 있을 때 그 경고가 무시된다.
/// </summary>
public class PlainTextSearchTests
{
    private static readonly Dictionary<string, string> NoOverrides = [];

    private static (SearchService Search, DocumentStore Store) Build(TempVault vault)
    {
        var tree = new TreeService(vault.Root);
        var store = new DocumentStore(tree, TestKeys.Codec());
        return (new SearchService(tree, store), store);
    }

    private static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);

    [Fact]
    public async Task 평문_본문이_검색된다()
    {
        using var vault = new TempVault();
        var (search, _) = Build(vault);
        vault.WriteRaw("메모.txt", Utf8("AKIA3QF7 운영키"));

        var outcome = await search.SearchAsync("AKIA", null, NoOverrides, default);

        var hit = Assert.Single(outcome.Hits);
        Assert.Equal("메모.txt", hit.Name);
        Assert.True(hit.IsPlainText);
        Assert.Equal(1, hit.MatchCount);
    }

    /// <summary>
    /// 이 태스크가 닫는 가장 중요한 결함. 평문이 금고에 있어도 결과를 "없다"로 읽어도 된다.
    /// </summary>
    [Fact]
    public async Task 평문이_있어도_사각지대_경고가_켜지지_않는다()
    {
        using var vault = new TempVault();
        var (search, store) = Build(vault);
        await store.CreateAsync(Path.Combine(vault.Root, "문서.tbx"));
        await store.SaveAsync(Path.Combine(vault.Root, "문서.tbx"), "관계없는 내용");
        vault.WriteRaw("메모.txt", Utf8("관계없는 평문"));

        var outcome = await search.SearchAsync("AKIA", null, NoOverrides, default);

        Assert.Empty(outcome.Hits);
        Assert.Equal(2, outcome.ScannedCount);
        Assert.Equal(0, outcome.UnreadableCount);
        Assert.True(outcome.IsComplete);
    }

    /// <summary>
    /// 반대편. 정말 읽지 못한 파일은 정직하게 세야 한다 — 여기까지 0으로 만들면
    /// "뒤져봤고 없었다"는 거짓 답이 된다.
    /// </summary>
    [Fact]
    public async Task 판정_불가_평문은_정직하게_사각지대로_센다()
    {
        using var vault = new TempVault();
        var (search, _) = Build(vault);
        vault.WriteRaw("이진.txt", [0x89, 0x50, 0x4E, 0x47, 0x00, 0x01]);

        var outcome = await search.SearchAsync("AKIA", null, NoOverrides, default);

        Assert.Equal(1, outcome.UnreadableCount);
        Assert.False(outcome.IsComplete);
    }

    [Fact]
    public async Task CP949_평문도_검색된다()
    {
        using var vault = new TempVault();
        var (search, _) = Build(vault);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        vault.WriteRaw("메모.txt", Encoding.GetEncoding(949).GetBytes("은행 계좌 비밀번호"));

        var outcome = await search.SearchAsync("계좌", null, NoOverrides, default);

        Assert.Single(outcome.Hits);
        Assert.True(outcome.IsComplete);
    }

    /// 표시 상한을 넘는 파일도 찾을 수는 있어야 한다 — 상한은 화면에만 건다.
    [Fact]
    public async Task 표시_상한을_넘는_평문도_검색된다()
    {
        using var vault = new TempVault();
        var (search, _) = Build(vault);
        vault.WriteRaw("큰파일.txt", Utf8(new string('가', 1_000_001) + " AKIA3QF7"));

        var outcome = await search.SearchAsync("AKIA", null, NoOverrides, default);

        Assert.Single(outcome.Hits);
        Assert.True(outcome.IsComplete);
    }

    [Fact]
    public async Task 이름_검색에도_평문이_구분되어_나온다()
    {
        using var vault = new TempVault();
        var (search, store) = Build(vault);
        await store.CreateAsync(Path.Combine(vault.Root, "메모.tbx"));
        vault.WriteRaw("메모.txt", Utf8("내용"));

        var hits = await search.SearchNamesAsync("메모", null, default);

        Assert.Equal(["메모", "메모.txt"], hits.Select(h => h.Name).Order().ToArray());
        Assert.True(Assert.Single(hits, h => h.Name == "메모.txt").IsPlainText);
        Assert.False(Assert.Single(hits, h => h.Name == "메모").IsPlainText);
    }

    [Fact]
    public async Task 문서_범위_검색도_평문을_읽는다()
    {
        using var vault = new TempVault();
        var (search, _) = Build(vault);
        var path = vault.WriteRaw("메모.txt", Utf8("AKIA3QF7 운영키"));

        var outcome = await search.SearchDocumentAsync(path, "AKIA", null, default);

        var hit = Assert.Single(outcome.Hits);
        Assert.Equal("메모.txt", hit.Name);          // 평문은 확장자를 드러낸다
        Assert.True(hit.IsPlainText);
        Assert.Equal(0, outcome.UnreadableCount);
    }

    /// 예약 영역은 어떤 경로로도 검색하지 않는다 — 평문이 들어와도 그대로다.
    [Fact]
    public async Task 예약_영역의_평문은_검색되지_않는다()
    {
        using var vault = new TempVault();
        var (search, _) = Build(vault);
        vault.WriteRaw(@".trash\20260101-000000\지운메모.txt", Utf8("AKIA3QF7"));
        vault.WriteRaw(@".history\메모.txt\opened.txt", Utf8("AKIA3QF7"));

        var outcome = await search.SearchAsync("AKIA", null, NoOverrides, default);

        Assert.Empty(outcome.Hits);
        Assert.Equal(0, outcome.ScannedCount);
        Assert.True(outcome.IsComplete);
    }
}

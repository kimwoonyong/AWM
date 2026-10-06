using TextBean.Models;
using TextBean.Services;

namespace TextBean.Tests;

/// <summary>
/// 금고를 뒤지는 검색. 위험은 속도가 아니라 **"없다"는 거짓 답**이다 —
/// 비밀 보관함에서 그 답은 사용자가 키를 새로 발급받게 만든다.
/// 그래서 여기서는 "찾는가"만큼 "못 찾은 것을 정직하게 말하는가"를 본다.
/// </summary>
public class SearchServiceTests
{
    private static (SearchService search, DocumentStore store, TreeService tree) Build(TempVault vault)
    {
        var tree = new TreeService(vault.Root);
        var store = new DocumentStore(tree, TestKeys.Codec());
        return (new SearchService(tree, store), store, tree);
    }

    private static readonly Dictionary<string, string> NoOverrides = [];

    private static string[] Names(SearchOutcome outcome)
        => [.. outcome.Hits.Select(h => h.Name).Order()];

    [Fact]
    public async Task 일치하는_문서만_돌려준다()
    {
        using var vault = new TempVault();
        var (search, store, _) = Build(vault);
        await store.CreateAsync(Path.Combine(vault.Root, "A.tbx"));
        await store.SaveAsync(Path.Combine(vault.Root, "A.tbx"), "AKIA3QF7 운영키");
        await store.CreateAsync(Path.Combine(vault.Root, "B.tbx"));
        await store.SaveAsync(Path.Combine(vault.Root, "B.tbx"), "관계없는 내용");

        var outcome = await search.SearchAsync("AKIA", null, NoOverrides, default);

        Assert.Equal(["A"], Names(outcome));
        Assert.Equal(2, outcome.ScannedCount);
        Assert.Equal(0, outcome.UnreadableCount);
        Assert.False(outcome.Canceled);
    }

    [Fact]
    public async Task 일치_개수와_첫_위치를_센다()
    {
        using var vault = new TempVault();
        var (search, store, _) = Build(vault);
        var p = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(p);
        await store.SaveAsync(p, "xxAKIAyyAKIAzz");

        var outcome = await search.SearchAsync("AKIA", null, NoOverrides, default);

        var hit = Assert.Single(outcome.Hits);
        Assert.Equal(2, hit.MatchCount);
        Assert.Equal(2, hit.FirstIndex);
        Assert.Equal(2, outcome.TotalMatches);
    }

    [Fact]
    public async Task 겹치는_일치는_한_번만_센다()
    {
        using var vault = new TempVault();
        var (search, store, _) = Build(vault);
        var p = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(p);
        await store.SaveAsync(p, "aaaa");

        var outcome = await search.SearchAsync("aa", null, NoOverrides, default);

        // 겹쳐 세면 3, 안 겹치면 2. 사용자가 다음/이전으로 돌 수 있는 자리 수와 같아야 한다
        Assert.Equal(2, Assert.Single(outcome.Hits).MatchCount);
    }

    [Fact]
    public async Task 휴지통_문서는_검색되지_않는다()
    {
        using var vault = new TempVault();
        var (search, store, tree) = Build(vault);
        var doomed = Path.Combine(vault.Root, "지운문서.tbx");
        await store.CreateAsync(doomed);
        await store.SaveAsync(doomed, "SECRETWORD 지운 비밀");
        tree.MoveToTrash(doomed, "20260922-000000");

        var outcome = await search.SearchAsync("SECRETWORD", null, NoOverrides, default);

        // Directory.EnumerateFiles 로 열거하면 휴지통 문서가 그대로 복호화돼 뜬다 [실측]
        Assert.Empty(outcome.Hits);
        Assert.Equal(0, outcome.UnreadableCount);
    }

    [Fact]
    public async Task 이전_세대_스냅샷은_검색되지_않는다()
    {
        using var vault = new TempVault();
        var (search, store, _) = Build(vault);
        var p = Path.Combine(vault.Root, "비밀.tbx");
        await store.CreateAsync(p);
        await store.SaveAsync(p, "OLDVALUE 옛 비밀번호");
        store.CaptureOpenSnapshot(p);                    // .history 에 옛 값이 남는다
        await store.SaveAsync(p, "새 비밀번호");

        var outcome = await search.SearchAsync("OLDVALUE", null, NoOverrides, default);

        // D-016 이 세대를 1개로 줄인 이유가 '바꾼 옛 비밀값이 남는 것'이었다.
        // 검색이 그걸 다시 꺼내오면 그 결정이 무효가 된다.
        Assert.Empty(outcome.Hits);
    }

    [Fact]
    public async Task 폴더_범위는_그_아래만_본다()
    {
        using var vault = new TempVault();
        var (search, store, _) = Build(vault);
        var inner = vault.Dir(@"aws\하위");
        await store.CreateAsync(Path.Combine(vault.Root, "밖.tbx"));
        await store.SaveAsync(Path.Combine(vault.Root, "밖.tbx"), "AKIA 밖");
        await store.CreateAsync(Path.Combine(vault.Root, "aws", "안.tbx"));
        await store.SaveAsync(Path.Combine(vault.Root, "aws", "안.tbx"), "AKIA 안");
        await store.CreateAsync(Path.Combine(inner, "더안.tbx"));
        await store.SaveAsync(Path.Combine(inner, "더안.tbx"), "AKIA 더안");

        var outcome = await search.SearchAsync("AKIA", Path.Combine(vault.Root, "aws"), NoOverrides, default);

        // 하위 폴더까지 포함한다
        Assert.Equal(["더안", "안"], Names(outcome));
    }

    [Fact]
    public async Task 읽지_못한_문서는_개수로_집계되고_나머지는_검색된다()
    {
        using var vault = new TempVault();
        var (search, store, _) = Build(vault);
        await store.CreateAsync(Path.Combine(vault.Root, "정상.tbx"));
        await store.SaveAsync(Path.Combine(vault.Root, "정상.tbx"), "AKIA 정상");
        vault.WriteRaw("깨진문서.tbx", "이건 TextBean 문서가 아니다"u8.ToArray());

        var outcome = await search.SearchAsync("AKIA", null, NoOverrides, default);

        // 조용히 삼키면 '일치 없음'과 구분되지 않는다 — 사용자는 금고에 없다고 믿는다
        Assert.Equal(["정상"], Names(outcome));
        Assert.Equal(1, outcome.UnreadableCount);
        Assert.Equal(2, outcome.ScannedCount);
    }

    [Fact]
    public async Task 열린_탭_본문이_디스크보다_우선한다()
    {
        using var vault = new TempVault();
        var (search, store, _) = Build(vault);
        var p = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(p);
        await store.SaveAsync(p, "디스크에 저장된 옛 값");

        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [PathRules.NormalizeFull(p)] = "JUSTTYPED 방금 친 값",
        };

        var outcome = await search.SearchAsync("JUSTTYPED", null, overrides, default);

        // 자동 저장은 1.5초 디바운스다. 디스크만 보면 방금 친 값이 '없음'이 된다 [실측]
        Assert.Equal(["A"], Names(outcome));

        var stale = await search.SearchAsync("디스크에 저장된", null, overrides, default);
        Assert.Empty(stale.Hits);          // 탭이 있으면 디스크는 읽지 않는다
    }

    [Fact]
    public async Task 대소문자를_무시한다()
    {
        using var vault = new TempVault();
        var (search, store, _) = Build(vault);
        var p = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(p);
        await store.SaveAsync(p, "Akia3QF7");

        Assert.Single((await search.SearchAsync("AKIA", null, NoOverrides, default)).Hits);
        Assert.Single((await search.SearchAsync("akia", null, NoOverrides, default)).Hits);
    }

    [Fact]
    public async Task 빈_검색어는_아무것도_찾지_않는다()
    {
        using var vault = new TempVault();
        var (search, store, _) = Build(vault);
        var p = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(p);
        await store.SaveAsync(p, "내용");

        foreach (var q in new[] { "", "   " })
        {
            var outcome = await search.SearchAsync(q, null, NoOverrides, default);
            Assert.Empty(outcome.Hits);
            Assert.Equal(0, outcome.ScannedCount);      // 복호화도 하지 않는다
        }
    }

    [Fact]
    public async Task 취소하면_취소로_표시되고_예외가_새지_않는다()
    {
        using var vault = new TempVault();
        var (search, store, _) = Build(vault);
        for (var i = 0; i < 20; i++)
        {
            var p = Path.Combine(vault.Root, $"문서{i:00}.tbx");
            await store.CreateAsync(p);
            await store.SaveAsync(p, "AKIA " + new string('x', 2000));
        }

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var outcome = await search.SearchAsync("AKIA", null, NoOverrides, cts.Token);

        // 취소는 오류가 아니다 — 호출자가 대화상자를 띄우면 안 된다
        Assert.True(outcome.Canceled);
    }

    // ── 이름 검색 — 복호화 0회라 입력 즉시 돌릴 수 있다 ──────────────────────

    [Fact]
    public async Task 이름_검색은_본문을_읽지_않는다()
    {
        using var vault = new TempVault();
        var (search, store, _) = Build(vault);
        var named = Path.Combine(vault.Root, "AKIA운영계정.tbx");
        await store.CreateAsync(named);
        await store.SaveAsync(named, "본문에는 없음");

        // 본문에만 있는 문서는 이름 검색에 걸리지 않아야 한다
        var other = Path.Combine(vault.Root, "다른문서.tbx");
        await store.CreateAsync(other);
        await store.SaveAsync(other, "AKIA 본문에만");

        var names = await search.SearchNamesAsync("AKIA", null, default);

        // 파일명은 평문이라 복호화 없이 찾힌다 (D-002 가 받아들인 대가)
        Assert.Equal(["AKIA운영계정"], names.Select(h => h.Name).ToArray());
    }

    [Fact]
    public async Task 이름_검색도_예약_영역과_범위를_지킨다()
    {
        using var vault = new TempVault();
        var (search, store, tree) = Build(vault);
        var doomed = Path.Combine(vault.Root, "AKIA지운문서.tbx");
        await store.CreateAsync(doomed);
        tree.MoveToTrash(doomed, "20260922-000000");

        vault.Dir("aws");
        await store.CreateAsync(Path.Combine(vault.Root, "aws", "AKIA안.tbx"));
        await store.CreateAsync(Path.Combine(vault.Root, "AKIA밖.tbx"));

        Assert.Equal(["AKIA밖", "AKIA안"],
            (await search.SearchNamesAsync("AKIA", null, default)).Select(h => h.Name).Order().ToArray());

        Assert.Equal(["AKIA안"],
            (await search.SearchNamesAsync("AKIA", Path.Combine(vault.Root, "aws"), default))
                .Select(h => h.Name).ToArray());
    }

    [Fact]
    public async Task 이름_검색은_폴더도_찾는다()
    {
        using var vault = new TempVault();
        var (search, _, _) = Build(vault);
        vault.Dir("AKIA보관함");

        var names = await search.SearchNamesAsync("AKIA", null, default);

        Assert.Equal(["AKIA보관함"], names.Select(h => h.Name).ToArray());
    }
}

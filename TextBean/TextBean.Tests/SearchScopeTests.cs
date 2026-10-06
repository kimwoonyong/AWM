using TextBean.Models;
using TextBean.Services;
using TextBean.ViewModels;
using static TextBean.Tests.ShellFixture;

namespace TextBean.Tests;

/// <summary>
/// 범위 세 가지와, 검색이 "없다"고 거짓말하지 않게 막는 장치들.
/// </summary>
public class SearchScopeTests
{
    private static async Task<(ShellViewModel shell, FakeDialogs dlg, DocumentStore store)>
        BuildVaultAsync(TempVault vault)
    {
        var (shell, dlg, store) = await BuildAsync(vault);
        vault.Dir("aws");

        foreach (var (rel, body) in new[]
                 {
                     ("밖.tbx", "AKIA 밖"),
                     (@"aws\운영키.tbx", "AKIA 운영"),
                     (@"aws\백업키.tbx", "AKIA 백업"),
                 })
        {
            var p = Path.Combine(vault.Root, rel);
            await store.CreateAsync(p);
            await store.SaveAsync(p, body);
        }
        await shell.RefreshAsync();
        return (shell, dlg, store);
    }

    private static string[] Names(SearchOutcome o) => [.. o.Hits.Select(h => h.Name).Order()];

    [Fact]
    public async Task 전체_범위는_금고_전부를_본다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await BuildVaultAsync(vault);

        var outcome = await shell.RunSearchAsync("AKIA", SearchScope.All);

        Assert.Equal(["밖", "백업키", "운영키"], Names(outcome));
    }

    [Fact]
    public async Task 폴더_범위는_고른_폴더_아래만_본다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await BuildVaultAsync(vault);
        shell.Selected = FindNode(shell.Roots, "aws");
        await Settle(shell);

        var outcome = await shell.RunSearchAsync("AKIA", SearchScope.Folder);

        Assert.Equal(["백업키", "운영키"], Names(outcome));
    }

    [Fact]
    public async Task 폴더_범위_대상은_문서를_고르면_그_문서가_든_폴더다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await BuildVaultAsync(vault);
        shell.Selected = FindNode(shell.Roots, "운영키");
        await Settle(shell);

        // "새 폴더를 만들면 어디 생기나" 와 같은 규칙이라 사용자가 새로 배울 게 없다
        Assert.EndsWith("aws", shell.ScopeFolderFor(SearchScope.Folder));

        var outcome = await shell.RunSearchAsync("AKIA", SearchScope.Folder);
        Assert.Equal(["백업키", "운영키"], Names(outcome));
    }

    [Fact]
    public async Task 아무것도_안_고르면_폴더_범위를_쓸_수_없다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await BuildVaultAsync(vault);
        shell.Selected = null;

        Assert.False(shell.CanUseFolderScope);
        Assert.Null(shell.ScopeFolderFor(SearchScope.Folder));    // 루트면 전체와 같다
    }

    [Fact]
    public async Task 범위는_실행_시점에_고정된다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await BuildVaultAsync(vault);
        shell.Selected = FindNode(shell.Roots, "aws");
        await Settle(shell);

        var outcome = await shell.RunSearchAsync("AKIA", SearchScope.Folder);
        Assert.Equal(["백업키", "운영키"], Names(outcome));

        // 결과를 보는 중 트리 선택이 바뀌어도 이미 낸 결과가 갈아끼워지지 않는다
        shell.Selected = null;
        Assert.Equal(["백업키", "운영키"], Names(shell.LastSearch!));
        Assert.Equal(SearchScope.Folder, shell.LastSearchScope);
    }

    [Fact]
    public async Task 문서_범위는_활성_탭만_본다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await BuildVaultAsync(vault);
        await shell.OpenAsync(Path.Combine(vault.Root, "aws", "운영키.tbx"));

        var outcome = await shell.RunSearchAsync("AKIA", SearchScope.Document);

        Assert.Equal(["운영키"], Names(outcome));
        Assert.Equal(1, outcome.ScannedCount);
    }

    [Fact]
    public async Task 문서_범위인데_탭이_없으면_아무것도_안_찾는다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await BuildVaultAsync(vault);

        var outcome = await shell.RunSearchAsync("AKIA", SearchScope.Document);

        Assert.Empty(outcome.Hits);
    }

    [Fact]
    public async Task 저장되지_않은_탭의_내용도_검색된다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await BuildVaultAsync(vault);
        await shell.OpenAsync(Path.Combine(vault.Root, "밖.tbx"));
        shell.ActiveTab!.Text = "JUSTTYPED 방금 친 값";

        // 자동 저장은 1.5초 디바운스다. 디스크만 보면 '없음'으로 보고된다 [실측]
        var outcome = await shell.RunSearchAsync("JUSTTYPED", SearchScope.All);

        Assert.Equal(["밖"], Names(outcome));
    }

    [Fact]
    public async Task 읽기_전용_탭은_메모리를_덮어쓰지_않는다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildVaultAsync(vault);
        var doc = Path.Combine(vault.Root, "밖.tbx");
        await shell.OpenAsync(doc);
        shell.OpenBackupCommand.Execute(null);        // 스냅샷 탭 — 읽기 전용, 본문이 다르다
        await Settle(shell);

        var outcome = await shell.RunSearchAsync("AKIA", SearchScope.All);

        // 스냅샷·읽기 실패 탭의 본문으로 원본 문서를 덮으면 엉뚱한 답이 나온다
        Assert.Contains("밖", Names(outcome));
    }

    [Fact]
    public async Task 읽지_못한_문서_수가_결과에_실린다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await BuildVaultAsync(vault);
        vault.WriteRaw("깨진문서.tbx", "TextBean 문서가 아님"u8.ToArray());
        await shell.RefreshAsync();

        var outcome = await shell.RunSearchAsync("AKIA", SearchScope.All);

        // 조용히 빠지면 '일치 없음'과 구분되지 않는다 — 사용자는 금고에 없다고 믿는다
        Assert.Equal(1, outcome.UnreadableCount);
        Assert.Equal(3, outcome.Hits.Count);
    }

    [Fact]
    public async Task 새_검색을_시작하면_이전_검색이_취소된다()
    {
        using var vault = new TempVault();
        var tree = new TreeService(vault.Root);
        var gated = new GatedDocumentStore(new DocumentStore(tree, TestKeys.Codec()));
        var dlg = new FakeDialogs();
        var settings = new AppSettingsService(Path.Combine(vault.Root, "settings.json"));
        await settings.LoadAsync();
        var shell = new ShellViewModel(settings, dlg, gated, tree,
                                       new FakeEditorFactory(gated, dlg), new SearchService(tree, gated),
                                       new FakeExplorerLauncher(), TestKeys.Service(), new FakeClipboard());

        for (var i = 0; i < 5; i++)
        {
            var p = Path.Combine(vault.Root, $"문서{i}.tbx");
            await gated.CreateAsync(p);
            await gated.SaveAsync(p, "AKIA");
        }

        gated.CloseReads();
        var first = shell.RunSearchAsync("AKIA", SearchScope.All);
        var second = shell.RunSearchAsync("AKIA", SearchScope.All);
        gated.Open();

        var a = await first;
        var b = await second;

        // 겹쳐 돌면 늦게 끝난 쪽이 먼저 낸 결과를 덮어써 엉뚱한 목록이 남는다
        Assert.True(a.Canceled);
        Assert.False(b.Canceled);
        Assert.Same(b, shell.LastSearch);
    }

    [Fact]
    public async Task 이름_검색은_본문을_읽지_않고도_찾는다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildVaultAsync(vault);
        await store.CreateAsync(Path.Combine(vault.Root, "AKIA이름.tbx"));
        await shell.RefreshAsync();

        var names = await shell.SearchNamesAsync("AKIA", SearchScope.All);

        Assert.Equal(["AKIA이름"], names.Select(h => h.Name).ToArray());
    }

    // ── 결과에서 문서 열기 ───────────────────────────────────────────────────

    [Fact]
    public async Task 결과를_열면_탭이_생기고_활성화되고_트리도_따라간다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await BuildVaultAsync(vault);
        var outcome = await shell.RunSearchAsync("AKIA", SearchScope.All);
        var hit = outcome.Hits.Single(h => h.Name == "운영키");

        await shell.OpenHitAsync(hit, "AKIA");

        Assert.Single(shell.Tabs);
        Assert.Equal(hit.FullPath, shell.ActiveTab?.CurrentPath);
        Assert.Equal(hit.FullPath, shell.Selected?.FullPath);
    }

    [Fact]
    public async Task 결과를_열면_그_문서에_검색어가_걸린다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await BuildVaultAsync(vault);
        var outcome = await shell.RunSearchAsync("AKIA", SearchScope.All);

        await shell.OpenHitAsync(outcome.Hits.First(), "AKIA");

        // 열기만 하고 강조를 안 걸면 사용자가 그 긴 문서에서 다시 찾아야 한다
        Assert.Equal("AKIA", shell.ActiveTab!.SearchQuery);
        Assert.NotEmpty(shell.ActiveTab.Matches);
    }

    [Fact]
    public async Task 이미_열린_문서를_결과에서_열어도_탭이_늘지_않는다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await BuildVaultAsync(vault);
        var doc = Path.Combine(vault.Root, "aws", "운영키.tbx");
        await shell.OpenAsync(doc);

        var outcome = await shell.RunSearchAsync("AKIA", SearchScope.All);
        await shell.OpenHitAsync(outcome.Hits.Single(h => h.Name == "운영키"), "AKIA");

        // 다시 로드하면 opened.tbx(유일한 되돌릴 지점)가 덮인다 (C-05, D-016)
        Assert.Single(shell.Tabs);
    }
}

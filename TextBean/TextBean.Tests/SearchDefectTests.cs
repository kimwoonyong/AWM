using TextBean.Models;
using TextBean.Services;
using TextBean.Services.Interfaces;
using TextBean.ViewModels;
using static TextBean.Tests.ShellFixture;

namespace TextBean.Tests;

/// <summary>
/// 적대적 검토(2026-09-22)가 반증에 실패한 결함들을 고정한다.
/// 전부 "없다고 거짓말하는" 축이거나 그 직전 단계다 —
/// 비밀 보관함에서 거짓 없음은 사용자가 키를 새로 발급받게 만든다.
/// </summary>
public class SearchDefectTests
{
    // ── 취소 경쟁 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task 취소된_검색은_마지막_문서에서_끝나도_결과를_덮지_않는다()
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

        // 문서 하나뿐 = 첫 문서가 곧 마지막 문서. 취소 검사가 루프 머리에만 있으면 이 경계가 샌다.
        var p = Path.Combine(vault.Root, "운영키.tbx");
        await gated.CreateAsync(p);
        await gated.SaveAsync(p, "AKIA 운영키");

        // 첫 읽기만 막는다. 전부 막으면 뒤 검색도 함께 멈춰 이 경계를 만들 수 없다.
        gated.CloseReads(count: 1);
        var stale = shell.RunSearchAsync("AKIA", SearchScope.All);     // 마지막 문서 읽는 중 멈춘다
        await gated.ReadBlocked;                                        // 슬롯을 먼저 문 것이 stale 임을 확정
        var fresh = await shell.RunSearchAsync("존재하지않는값", SearchScope.All);
        gated.Open();
        var staleResult = await stale;

        // 늦게 끝난 옛 검색이 새 결과를 덮으면 '존재하지않는값' 을 찾았다며 '운영키' 가 뜬다
        Assert.Empty(fresh.Hits);
        Assert.Same(fresh, shell.LastSearch);
        Assert.Empty(shell.LastSearch!.Hits);
        Assert.NotSame(staleResult, shell.LastSearch);
    }

    [Fact]
    public async Task 취소한_검색은_옛_결과를_새_검색어의_결과처럼_남기지_않는다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var p = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(p);
        await store.SaveAsync(p, "AKIA");

        var first = await shell.RunSearchAsync("AKIA", SearchScope.All);
        Assert.Single(first.Hits);

        // 취소된 검색은 LastSearch 를 건드리지 않는다 — 그런데 그 자리에 옛 결과가 남으면
        // 사용자는 새 검색어가 그걸 찾았다고 읽는다
        shell.CancelSearch();
        var second = await shell.RunSearchAsync("없는값", SearchScope.All);

        Assert.Empty(second.Hits);
        Assert.Same(second, shell.LastSearch);
    }

    // ── 문서 범위의 정직함 ───────────────────────────────────────────────────

    [Fact]
    public async Task 문서_범위도_읽지_못한_탭을_읽지_못함으로_센다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await BuildAsync(vault);
        var broken = vault.WriteRaw("깨진문서.tbx", "TextBean 문서가 아님"u8.ToArray());
        await shell.RefreshAsync();
        await shell.OpenAsync(broken);

        Assert.True(shell.ActiveTab!.IsReadOnly);

        var outcome = await shell.RunSearchAsync("AKIA", SearchScope.Document);

        // 전체 범위는 이걸 1건으로 센다. 범위에 따라 정직함이 갈리면 안 된다 (C-07)
        Assert.Equal(1, outcome.UnreadableCount);
        Assert.Empty(outcome.Hits);
    }

    [Fact]
    public async Task 문서_범위에서_검색어를_비우면_강조도_지워진다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var p = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(p);
        await store.SaveAsync(p, "AKIA");
        await shell.OpenAsync(p);

        await shell.RunSearchAsync("AKIA", SearchScope.Document);
        Assert.NotEmpty(shell.ActiveTab!.Matches);

        await shell.RunSearchAsync("", SearchScope.Document);

        // 강조가 남으면 화면에 어느 값이 민감한지 표시해 두는 셈이 된다
        Assert.Empty(shell.ActiveTab!.Matches);
        Assert.Equal("", shell.ActiveTab.SearchQuery);
    }

    // ── 범위 표시가 거짓말하지 않기 ──────────────────────────────────────────

    [Fact]
    public async Task 폴더를_못_고른_채_폴더_범위로_찾으면_전체로_기록된다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        await store.CreateAsync(Path.Combine(vault.Root, "A.tbx"));
        await store.SaveAsync(Path.Combine(vault.Root, "A.tbx"), "AKIA");
        await shell.RefreshAsync();
        shell.Selected = null;

        await shell.RunSearchAsync("AKIA", SearchScope.Folder);

        // 실제로 전체를 훑었는데 '폴더에서 찾았다'고 적으면 범위 착각이 그대로 난다
        Assert.Equal(SearchScope.All, shell.LastSearchScope);
    }

    [Fact]
    public async Task 범위와_폴더_사용_가능_여부가_바뀌면_알림이_나간다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        vault.Dir("aws");
        await store.CreateAsync(Path.Combine(vault.Root, "aws", "A.tbx"));
        await store.SaveAsync(Path.Combine(vault.Root, "aws", "A.tbx"), "AKIA");
        await shell.RefreshAsync();

        var notified = new List<string?>();
        shell.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        shell.Selected = FindNode(shell.Roots, "aws");
        await Settle(shell);
        Assert.Contains(nameof(ShellViewModel.CanUseFolderScope), notified);

        notified.Clear();
        await shell.RunSearchAsync("AKIA", SearchScope.Folder);

        // 알림이 없으면 결과 헤더의 범위 표시가 옛 값에 굳는다
        Assert.Contains(nameof(ShellViewModel.LastSearchScope), notified);
    }

    // ── 문서 내 검색 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task 검색어를_바꾸면_첫_일치부터_시작한다()
    {
        using var vault = new TempVault();
        var store = new DocumentStore(new TreeService(vault.Root), TestKeys.Codec());
        var p = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(p);
        await store.SaveAsync(p, "AAA BBB AAA BBB AAA");

        var vm = new EditorViewModel(store, new FakeClipboard(), new FakeDialogs(), new FakeAutoSaveTimer());
        await vm.LoadAsync(p);

        vm.SetSearch("AAA");
        vm.MoveToNextMatch();
        vm.MoveToNextMatch();
        Assert.Equal(2, vm.CurrentMatch);

        vm.SetSearch("BBB");

        // 직전 검색의 순번을 물려받으면 새 검색어가 첫 일치가 아닌 자리에서 시작하고
        // '2 / 2' 같은 숫자를 보여준다
        Assert.Equal(0, vm.CurrentMatch);
        Assert.Equal("1 / 2", vm.MatchPositionText);
    }

    [Fact]
    public async Task 같은_검색어를_다시_치면_자리를_잃지_않는다()
    {
        using var vault = new TempVault();
        var store = new DocumentStore(new TreeService(vault.Root), TestKeys.Codec());
        var p = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(p);
        await store.SaveAsync(p, "AAA AAA AAA");

        var vm = new EditorViewModel(store, new FakeClipboard(), new FakeDialogs(), new FakeAutoSaveTimer());
        await vm.LoadAsync(p);
        vm.SetSearch("AAA");
        vm.MoveToNextMatch();
        Assert.Equal(1, vm.CurrentMatch);

        // 타이핑 중 본문이 바뀌는 경우와 구분해야 한다 — 같은 검색어면 보던 자리를 지킨다
        vm.Text = "AAA AAA AAA!";

        Assert.Equal(1, vm.CurrentMatch);
    }

    // ── 이름 검색 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task 이름_검색_결과는_폴더와_문서를_구분한다()
    {
        using var vault = new TempVault();
        var tree = new TreeService(vault.Root);
        var store = new DocumentStore(tree, TestKeys.Codec());
        var search = new SearchService(tree, store);
        vault.Dir("AKIA보관함");
        await store.CreateAsync(Path.Combine(vault.Root, "AKIA문서.tbx"));

        var names = await search.SearchNamesAsync("AKIA", null, default);

        // 구분이 없으면 폴더를 열었을 때 '읽을 수 없는 문서' 유령 탭이 생긴다
        var folder = names.Single(h => h.Name == "AKIA보관함");
        var doc = names.Single(h => h.Name == "AKIA문서");
        Assert.True(folder.IsFolder);
        Assert.False(doc.IsFolder);
    }

    [Fact]
    public async Task 이름_검색은_금고_루트_자신을_돌려주지_않는다()
    {
        using var vault = new TempVault();
        var tree = new TreeService(vault.Root);
        var store = new DocumentStore(tree, TestKeys.Codec());
        var search = new SearchService(tree, store);

        // 루트 폴더명이 검색어를 포함하면 루트가 결과에 뜬다 — 열 수 없는 항목이다
        var rootName = Path.GetFileName(PathRules.NormalizeFull(vault.Root));
        var names = await search.SearchNamesAsync(rootName, null, default);

        Assert.DoesNotContain(names, h => string.Equals(
            PathRules.NormalizeFull(h.FullPath), PathRules.NormalizeFull(vault.Root),
            StringComparison.OrdinalIgnoreCase));
    }

    // ── "이 문서" 범위 — 금고 안 문서 아무거나 고른다 ────────────────────────

    [Fact]
    public async Task 열려_있지_않은_문서도_고르면_검색된다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var closed = Path.Combine(vault.Root, "안열린문서.tbx");
        await store.CreateAsync(closed);
        await store.SaveAsync(closed, "AKIA 여기 있다");
        await shell.RefreshAsync();

        Assert.Empty(shell.Tabs);

        var outcome = await shell.RunSearchAsync("AKIA", SearchScope.Document, scopeTarget: closed);

        // 열린 탭만 뒤지면 '금고에 없다'는 거짓 답이 된다
        Assert.Single(outcome.Hits);
        Assert.Equal("안열린문서", outcome.Hits[0].Name);
        Assert.Equal(1, outcome.ScannedCount);
    }

    [Fact]
    public async Task 고른_문서가_열려_있으면_저장_전_내용까지_찾는다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var p = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(p);
        await store.SaveAsync(p, "디스크 값");
        await shell.OpenAsync(p);
        shell.ActiveTab!.Text = "JUSTTYPED 방금 친 값";

        var outcome = await shell.RunSearchAsync("JUSTTYPED", SearchScope.Document, scopeTarget: p);

        // 자동 저장은 1.5초 디바운스다. 디스크만 보면 '없음'이 된다
        Assert.Single(outcome.Hits);
    }

    [Fact]
    public async Task 고른_문서를_못_읽으면_읽지_못함으로_센다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await BuildAsync(vault);
        var broken = vault.WriteRaw("깨진문서.tbx", "TextBean 문서가 아님"u8.ToArray());
        await shell.RefreshAsync();

        var outcome = await shell.RunSearchAsync("AKIA", SearchScope.Document, scopeTarget: broken);

        Assert.Equal(1, outcome.UnreadableCount);
        Assert.False(outcome.IsComplete);
    }

    [Fact]
    public async Task 고르지_않으면_지금_보는_문서를_뒤진다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var p = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(p);
        await store.SaveAsync(p, "AKIA");
        await shell.OpenAsync(p);

        var outcome = await shell.RunSearchAsync("AKIA", SearchScope.Document);

        Assert.Single(outcome.Hits);
    }

    [Fact]
    public async Task 금고_밖_문서는_고를_수_없다()
    {
        using var vault = new TempVault();
        var (shell, dlg, _) = await BuildAsync(vault);

        var outside = Path.Combine(Path.GetTempPath(), "금고밖.tbx");
        File.WriteAllBytes(outside, "x"u8.ToArray());
        try
        {
            dlg.PickDocumentResult = outside;

            Assert.Null(shell.PickVaultDocument(null));
            Assert.Equal(1, dlg.ErrorCount);
        }
        finally { File.Delete(outside); }
    }

    [Fact]
    public async Task 휴지통과_이전_세대_문서는_고를_수_없다()
    {
        using var vault = new TempVault();
        var (shell, dlg, store) = await BuildAsync(vault);
        var p = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(p);
        store.CaptureOpenSnapshot(p);

        // .history 안의 스냅샷 — 바꾸기 전 비밀값이 들어 있는 곳이다
        dlg.PickDocumentResult = store.SnapshotPathIfExists(p);

        Assert.Null(shell.PickVaultDocument(null));
        Assert.Equal(1, dlg.ErrorCount);
    }

    [Fact]
    public async Task 금고_안_문서를_고르면_정규화된_경로를_돌려준다()
    {
        using var vault = new TempVault();
        var (shell, dlg, store) = await BuildAsync(vault);
        var p = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(p);
        dlg.PickDocumentResult = Path.Combine(vault.Root, ".", "A.tbx");

        var picked = shell.PickVaultDocument(null);

        Assert.Equal(PathRules.NormalizeFull(p), picked);
        Assert.Equal(0, dlg.ErrorCount);
    }

    // ── 목록조차 못 읽은 폴더 ────────────────────────────────────────────────

    /// 권한이 없어 목록을 못 읽는 폴더를 테스트에서 결정적으로 만들 수 없어(ACL 조작은
    /// 실행 계정에 달려 있다) 스캔 결과를 직접 물린다. TreeService 가 그 표시를 세우는지는
    /// 별도 실측으로 확인됐다 — icacls /deny 를 건 폴더가 검색에서 통째로 사라졌다.
    private sealed class BlindFolderTree(string root, TreeNode scan) : ITreeService
    {
        public string Root => root;
        public Task<TreeNode> ScanAsync(CancellationToken ct = default) => Task.FromResult(scan);

        public void SetRoot(string r) => throw new NotSupportedException();
        public string CreateFolder(string p, string n) => throw new NotSupportedException();
        public string Rename(string p, string n) => throw new NotSupportedException();
        public void MoveToTrash(string p, string s) => throw new NotSupportedException();
        public MoveCheck CheckMove(string s, string d) => throw new NotSupportedException();
        public string Move(string s, string d) => throw new NotSupportedException();
        public int DeepestDerivedLength(string p, string s) => throw new NotSupportedException();
        public void EmptyTrash() => throw new NotSupportedException();
        public int CountTrashItems() => throw new NotSupportedException();
        public bool NameTaken(string p, string n) => throw new NotSupportedException();
        public bool FileExists(string p) => true;
        public bool RootExists() => true;
        public void EnsureInsideRoot(string p) { }
    }

    [Fact]
    public async Task 목록을_못_읽은_폴더가_결과에_집계된다()
    {
        using var vault = new TempVault();
        var real = new TreeService(vault.Root);
        var store = new DocumentStore(real, TestKeys.Codec());
        var doc = Path.Combine(vault.Root, "보이는문서.tbx");
        await store.CreateAsync(doc);
        await store.SaveAsync(doc, "AKIA 보인다");

        var scan = new TreeNode(vault.Root, "금고", true,
        [
            new TreeNode(doc, "보이는문서", false, []),
            new TreeNode(Path.Combine(vault.Root, "잠긴폴더"), "잠긴폴더", true, [], Unreadable: true),
        ]);

        var search = new SearchService(new BlindFolderTree(vault.Root, scan), store);
        var outcome = await search.SearchAsync("AKIA", null, new Dictionary<string, string>(), default);

        // 안 세면 '다 뒤졌고 1건 있다'가 되지만, 실제로는 통째로 안 본 영역이 있다
        Assert.Single(outcome.Hits);
        Assert.Equal(1, outcome.UnreadableFolderCount);
        Assert.False(outcome.IsComplete);
    }

    [Fact]
    public async Task 아무_문제가_없으면_결과가_완전하다고_말한다()
    {
        using var vault = new TempVault();
        var tree = new TreeService(vault.Root);
        var store = new DocumentStore(tree, TestKeys.Codec());
        var p = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(p);
        await store.SaveAsync(p, "AKIA");

        var outcome = await new SearchService(tree, store)
            .SearchAsync("AKIA", null, new Dictionary<string, string>(), default);

        Assert.True(outcome.IsComplete);
    }

    [Fact]
    public async Task 탭을_닫고_같은_결과를_다시_열_수_있다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var p = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(p);
        await store.SaveAsync(p, "AKIA 값");
        await shell.RefreshAsync();

        var outcome = await shell.RunSearchAsync("AKIA", SearchScope.All);
        var hit = outcome.Hits.Single();

        await shell.OpenHitAsync(hit, "AKIA");
        Assert.Single(shell.Tabs);

        await shell.CloseTabAsync(shell.ActiveTab!);
        Assert.Empty(shell.Tabs);

        // 같은 결과를 다시 눌렀을 때 아무 일도 안 일어나면 사용자는 검색이 고장났다고 본다
        await shell.OpenHitAsync(hit, "AKIA");

        Assert.Single(shell.Tabs);
        Assert.Equal(hit.FullPath, shell.ActiveTab?.CurrentPath);
        Assert.NotEmpty(shell.ActiveTab!.Matches);
    }

    // ── 검색 결과로 연 탭 정리 ───────────────────────────────────────────────

    [Fact]
    public async Task 검색_강조를_모든_탭에서_한_번에_지울_수_있다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        foreach (var n in new[] { "A", "B" })
        {
            var p = Path.Combine(vault.Root, n + ".tbx");
            await store.CreateAsync(p);
            await store.SaveAsync(p, "AKIA");
            await shell.OpenAsync(p);
        }
        var outcome = await shell.RunSearchAsync("AKIA", SearchScope.All);
        foreach (var hit in outcome.Hits) await shell.OpenHitAsync(hit, "AKIA");

        Assert.All(shell.Tabs, t => Assert.NotEmpty(t.Matches));

        shell.ClearSearchHighlights();

        // 강조가 무기한 남으면 화면에 어느 값이 민감한지 표시해 두는 셈이 된다
        Assert.All(shell.Tabs, t => Assert.Empty(t.Matches));
    }
}

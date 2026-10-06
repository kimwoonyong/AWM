using TextBean.Services;
using TextBean.ViewModels;
using static TextBean.Tests.ShellFixture;

namespace TextBean.Tests;

/// <summary>
/// 여러 탭을 한 번에 닫는다. 이 앱에서 처음 생기는 "N개를 연속으로 닫는" 동작이라
/// 저장 실패 계약(D-011)·알림 1회 원칙·J-5 트리 선택 비움이 한꺼번에 걸린다.
/// </summary>
public class MultiCloseTests
{
    private static async Task<(ShellViewModel shell, FakeDialogs dlg, DocumentStore store, FakeEditorFactory factory, string[] paths)>
        OpenThreeAsync(TempVault vault)
    {
        var (shell, dlg, store, factory) = await BuildWithFactoryAsync(vault);
        var paths = new[] { "A", "B", "C" }.Select(n => Path.Combine(vault.Root, n + ".tbx")).ToArray();

        foreach (var p in paths)
        {
            await store.CreateAsync(p);
            await shell.OpenAsync(p);
        }
        await shell.RefreshAsync();
        return (shell, dlg, store, factory, paths);
    }

    private static string[] Order(ShellViewModel shell)
        => [.. shell.Tabs.Select(t => Path.GetFileNameWithoutExtension(t.CurrentPath!))];

    [Fact]
    public async Task 다른_탭_닫기는_지정한_탭만_남기고_그것을_활성으로_만든다()
    {
        using var vault = new TempVault();
        var (shell, dlg, _, _, _) = await OpenThreeAsync(vault);
        var keep = shell.Tabs[1];                      // 활성(C)이 아닌 배경 탭을 남긴다

        Assert.True(await shell.CloseOtherTabsAsync(keep));

        // 기준은 활성 탭이 아니라 우클릭한 탭이다 — 활성 기준이면 남길 탭이 통째로 뒤바뀐다
        Assert.Single(shell.Tabs);
        Assert.Same(keep, shell.Tabs[0]);
        Assert.Same(keep, shell.ActiveTab);
        Assert.True(keep.IsActive);
        Assert.Equal(0, dlg.ErrorCount);
    }

    [Fact]
    public async Task 다른_탭_닫기_후_트리_선택은_남은_탭을_가리킨다()
    {
        using var vault = new TempVault();
        var (shell, _, _, _, _) = await OpenThreeAsync(vault);
        var keep = shell.Tabs[0];

        await shell.CloseOtherTabsAsync(keep);

        // 닫힌 문서의 노드가 선택된 채 남으면 그 문서를 다시 클릭해도
        // SelectedItemChanged 가 안 나서 안 열린다 (J-5 의 새 형태)
        Assert.Equal(keep.CurrentPath, shell.Selected?.FullPath);
    }

    [Fact]
    public async Task 모든_탭_닫기는_전부_닫고_트리_선택까지_비운다()
    {
        using var vault = new TempVault();
        var (shell, _, _, _, _) = await OpenThreeAsync(vault);
        shell.Selected = FindNode(shell.Roots, "A");
        await Settle(shell);

        Assert.True(await shell.CloseAllTabsRequestedAsync());

        Assert.Empty(shell.Tabs);
        Assert.Null(shell.ActiveTab);
        Assert.True(shell.HasNoTabs);

        // 기존 CloseAllTabs() 를 재사용하면 여기가 비지 않는다 — 재사용 금지의 근거 ②
        Assert.Null(shell.Selected);
    }

    [Fact]
    public async Task 다중_닫기는_닫기_전에_전부_저장한다()
    {
        using var vault = new TempVault();
        var (shell, _, store, _, paths) = await OpenThreeAsync(vault);
        for (var i = 0; i < 3; i++) shell.Tabs[i].Text = $"값{i}";

        Assert.True(await shell.CloseAllTabsRequestedAsync());

        // 기존 CloseAllTabs() 는 저장을 하지 않는다 — 1.5초 디바운스 안의 편집이 사라진다. 근거 ①
        for (var i = 0; i < 3; i++)
            Assert.Equal($"값{i}", (await store.LoadAsync(paths[i])).Text);
    }

    [Fact]
    public async Task 저장에_실패한_탭만_남고_나머지는_닫히며_알림은_한_번이다()
    {
        using var vault = new TempVault();
        var (shell, dlg, store, _, paths) = await OpenThreeAsync(vault);
        foreach (var t in shell.Tabs) t.Text = "저장할 값";

        var stuck = shell.Tabs.Single(t => t.CurrentPath == PathRules.NormalizeFull(paths[1]));
        File.SetAttributes(paths[1], FileAttributes.ReadOnly);
        try
        {
            Assert.False(await shell.CloseAllTabsRequestedAsync());

            Assert.Single(shell.Tabs);
            Assert.Same(stuck, shell.Tabs[0]);
            Assert.Same(stuck, shell.ActiveTab);       // 문제 있는 탭으로 이동한다

            // 편집기와 셸이 각자 띄우면 모달이 N개 겹쳐 뜨고 사용자는 마지막 것만 읽는다
            Assert.Equal(1, dlg.ErrorCount);

            Assert.Equal("저장할 값", (await store.LoadAsync(paths[0])).Text);
            Assert.Equal("저장할 값", (await store.LoadAsync(paths[2])).Text);
        }
        finally { File.SetAttributes(paths[1], FileAttributes.Normal); }
    }

    [Fact]
    public async Task 닫힌_탭은_전부_해제되어_나중에_디스크에_안_쓴다()
    {
        using var vault = new TempVault();
        var (shell, _, store, factory, paths) = await OpenThreeAsync(vault);
        var timers = factory.Timers.ToList();
        var tabs = shell.Tabs.ToList();
        foreach (var t in tabs) t.Text = "닫기 전 값";

        await shell.CloseAllTabsRequestedAsync();

        for (var i = 0; i < tabs.Count; i++)
        {
            tabs[i].Text = "닫은 뒤 값";
            timers[i].Restart();
            timers[i].Fire();
        }
        await Task.Delay(50);

        foreach (var p in paths)
            Assert.Equal("닫기 전 값", (await store.LoadAsync(p)).Text);
    }

    [Fact]
    public async Task 이전_세대_탭도_함께_닫힌다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var doc = Path.Combine(vault.Root, "정보.tbx");
        await store.CreateAsync(doc);
        await store.SaveAsync(doc, "연 시점 값");
        await shell.RefreshAsync();

        shell.Selected = FindNode(shell.Roots, "정보");
        await Settle(shell);
        shell.OpenBackupCommand.Execute(null);
        await Settle(shell);
        Assert.Equal(2, shell.Tabs.Count);

        // 스냅샷은 읽기 전용이라 저장 게이트를 항상 통과한다.
        // 제외하면 "모든 탭 닫기"인데 탭이 남는 예측 불가 상태가 된다.
        Assert.True(await shell.CloseAllTabsRequestedAsync());
        Assert.Empty(shell.Tabs);
    }

    [Fact]
    public async Task 다중_닫기가_도는_중에_다시_부르면_무시된다()
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

        foreach (var n in new[] { "A", "B" })
        {
            var p = Path.Combine(vault.Root, n + ".tbx");
            await gated.CreateAsync(p);
            await shell.OpenAsync(p);
        }
        foreach (var t in shell.Tabs) t.Text = "값";

        gated.Close();
        var first = shell.CloseAllTabsRequestedAsync();     // 첫 저장에서 멈춘다
        var second = await shell.CloseAllTabsRequestedAsync();

        // 겹쳐 돌면 같은 탭을 두 번 제거하거나 해제된 편집기를 다시 만진다
        Assert.False(second);
        Assert.Equal(2, shell.Tabs.Count);

        gated.Open();
        Assert.True(await first);
        Assert.Empty(shell.Tabs);
    }

    [Fact]
    public async Task 탭이_하나뿐이면_다른_탭_닫기는_실행할_수_없다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);

        Assert.False(shell.CloseOtherTabsCommand.CanExecute(null));
        Assert.False(shell.CloseAllTabsCommand.CanExecute(null));
        Assert.False(shell.CloseActiveTabCommand.CanExecute(null));

        var a = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(a);
        await shell.OpenAsync(a);

        Assert.False(shell.CloseOtherTabsCommand.CanExecute(null));   // 남길 탭 하나뿐
        Assert.True(shell.CloseAllTabsCommand.CanExecute(null));
        Assert.True(shell.CloseActiveTabCommand.CanExecute(null));

        var b = Path.Combine(vault.Root, "B.tbx");
        await store.CreateAsync(b);
        await shell.OpenAsync(b);

        Assert.True(shell.CloseOtherTabsCommand.CanExecute(null));
    }

    [Fact]
    public async Task 현재_탭_닫기_명령은_활성_탭을_닫는다()
    {
        using var vault = new TempVault();
        var (shell, _, _, _, _) = await OpenThreeAsync(vault);
        var active = shell.ActiveTab!;

        shell.CloseActiveTabCommand.Execute(null);
        await Settle(shell);

        Assert.Equal(2, shell.Tabs.Count);
        Assert.DoesNotContain(active, shell.Tabs);
    }

    [Fact]
    public async Task 메뉴_명령은_넘겨받은_탭을_기준으로_닫는다()
    {
        using var vault = new TempVault();
        var (shell, _, _, _, _) = await OpenThreeAsync(vault);
        var target = shell.Tabs[0];                    // 활성이 아닌 탭을 우클릭한 상황

        shell.CloseOtherTabsCommand.Execute(target);
        await Settle(shell);

        Assert.Single(shell.Tabs);
        Assert.Same(target, shell.Tabs[0]);
    }

    [Fact]
    public async Task 메뉴로_닫기는_넘겨받은_탭_하나만_닫는다()
    {
        using var vault = new TempVault();
        var (shell, _, _, _, _) = await OpenThreeAsync(vault);
        var target = shell.Tabs[0];

        shell.CloseTabCommand.Execute(target);
        await Settle(shell);

        Assert.Equal(["B", "C"], Order(shell));
    }
}

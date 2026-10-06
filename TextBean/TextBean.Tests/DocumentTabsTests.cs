using TextBean.Services;
using TextBean.ViewModels;
using static TextBean.Tests.ShellFixture;

namespace TextBean.Tests;

/// <summary>
/// 탭이 새로 만드는 위험은 "문서가 여러 개 열려 있다"는 것 자체다 —
/// 같은 문서가 두 탭이 되면 나중 탭이 조용히 이기고, 다시 로드하면 유일한 되돌릴 지점이 덮인다.
/// </summary>
public class DocumentTabsTests
{
    [Fact]
    public async Task 문서를_열면_탭이_생기고_활성화된다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var a = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(a);

        await shell.OpenAsync(a);

        Assert.Single(shell.Tabs);
        Assert.Equal(PathRules.NormalizeFull(a), shell.ActiveTab?.CurrentPath);
    }

    [Fact]
    public async Task 문서_두_개를_열면_탭이_둘이고_각자_본문을_유지한다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var a = Path.Combine(vault.Root, "A.tbx");
        var b = Path.Combine(vault.Root, "B.tbx");
        await store.CreateAsync(a);
        await store.CreateAsync(b);
        await store.SaveAsync(a, "A의 내용");
        await store.SaveAsync(b, "B의 내용");

        await shell.OpenAsync(a);
        await shell.OpenAsync(b);

        Assert.Equal(2, shell.Tabs.Count);
        Assert.Equal("A의 내용", shell.Tabs[0].Text);   // 전환해도 배경 탭 본문은 그대로다
        Assert.Equal("B의 내용", shell.Tabs[1].Text);
        Assert.Same(shell.Tabs[1], shell.ActiveTab);
    }

    [Fact]
    public async Task 활성_탭_하나만_활성_표시가_켜진다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var a = Path.Combine(vault.Root, "A.tbx");
        var b = Path.Combine(vault.Root, "B.tbx");
        await store.CreateAsync(a);
        await store.CreateAsync(b);

        // 탭 본문은 탭마다 하나씩 만들어 두고 활성 탭만 보인다 —
        // TabControl 의 ContentTemplate 은 TextBox 를 하나만 만들어 돌려 쓴다 [실측].
        await shell.OpenAsync(a);
        var tabA = shell.ActiveTab!;
        Assert.True(tabA.IsActive);
        Assert.False(shell.HasNoTabs);

        await shell.OpenAsync(b);
        Assert.False(tabA.IsActive);
        Assert.True(shell.ActiveTab!.IsActive);

        shell.ActiveTab = tabA;
        Assert.True(tabA.IsActive);
        Assert.False(shell.Tabs[1].IsActive);

        await shell.CloseTabAsync(tabA);
        Assert.True(shell.ActiveTab!.IsActive);      // 이어받은 탭이 안 켜지면 화면이 빈다

        await shell.CloseTabAsync(shell.ActiveTab);
        Assert.True(shell.HasNoTabs);
    }

    [Fact]
    public async Task 이미_열린_문서를_다시_열면_탭이_늘지_않고_복구지점도_안_덮인다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var a = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(a);
        await store.SaveAsync(a, "연 시점");

        await shell.OpenAsync(a);
        shell.ActiveTab!.Text = "그 뒤에 고친 값";
        await shell.ActiveTab.TrySaveAsync();

        await shell.OpenAsync(a);                       // 다시 열기

        Assert.Single(shell.Tabs);

        // 로드 경로를 타면 opened.tbx 가 현재 내용으로 덮여 되돌릴 지점이 사라진다 (D-016, F-3)
        var snapshot = store.SnapshotPathIfExists(a)!;
        Assert.Equal("연 시점", TestKeys.Codec().Decrypt(File.ReadAllBytes(snapshot)).Text);
    }

    [Fact]
    public async Task 대소문자만_다른_경로도_같은_탭이다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var a = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(a);

        await shell.OpenAsync(a);
        await shell.OpenAsync(a.ToUpperInvariant());

        // 두 탭이 각자 저장하면 나중 탭이 조용히 이긴다 [실측] — Mutex(D-008)가 막아둔 조건이다
        Assert.Single(shell.Tabs);
    }

    [Fact]
    public async Task 탭을_닫으면_저장되고_편집기가_해제된다()
    {
        using var vault = new TempVault();
        var (shell, _, store, factory) = await BuildWithFactoryAsync(vault);
        var a = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(a);
        await shell.OpenAsync(a);
        var tab = shell.ActiveTab!;
        var timer = factory.Timers[0];
        tab.Text = "닫기 전 값";

        Assert.True(await shell.CloseTabAsync(tab));

        Assert.Empty(shell.Tabs);
        Assert.Equal("닫기 전 값", (await store.LoadAsync(a)).Text);

        // 해제를 빠뜨리면 닫힌 탭이 1.5초 뒤 디스크에 쓴다 (F-7)
        tab.Text = "닫은 뒤 값";
        timer.Restart();
        timer.Fire();
        await Task.Delay(50);
        Assert.Equal("닫기 전 값", (await store.LoadAsync(a)).Text);
    }

    [Fact]
    public async Task 저장에_실패하면_탭이_닫히지_않고_그_탭으로_이동한다()
    {
        using var vault = new TempVault();
        var (shell, dlg, store) = await BuildAsync(vault);
        var a = Path.Combine(vault.Root, "A.tbx");
        var b = Path.Combine(vault.Root, "B.tbx");
        await store.CreateAsync(a);
        await store.CreateAsync(b);
        await shell.OpenAsync(a);
        var tabA = shell.ActiveTab!;
        tabA.Text = "저장 실패할 값";
        await shell.OpenAsync(b);                       // A는 배경 탭이 된다

        File.SetAttributes(a, FileAttributes.ReadOnly);
        try
        {
            Assert.False(await shell.CloseTabAsync(tabA));
            Assert.Equal(2, shell.Tabs.Count);

            // 안 보이는 문서의 실패 대화상자를 받으면 무엇을 고쳐야 할지 알 수 없다
            Assert.Same(tabA, shell.ActiveTab);
            Assert.Equal("저장 실패할 값", tabA.Text);

            // 편집기와 셸이 각자 띄우면 두 개가 겹쳐 떠 사용자는 뒤엣것만 읽는다
            Assert.Equal(1, dlg.ErrorCount);
        }
        finally { File.SetAttributes(a, FileAttributes.Normal); }
    }

    [Fact]
    public async Task 배경_탭을_닫으면_활성_탭은_그대로다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var a = Path.Combine(vault.Root, "A.tbx");
        var b = Path.Combine(vault.Root, "B.tbx");
        await store.CreateAsync(a);
        await store.CreateAsync(b);
        await shell.OpenAsync(a);
        var tabA = shell.ActiveTab!;
        await shell.OpenAsync(b);
        var tabB = shell.ActiveTab!;

        Assert.True(await shell.CloseTabAsync(tabA));

        Assert.Same(tabB, shell.ActiveTab);   // 배경 탭을 닫았다고 보던 문서가 바뀌면 안 된다
        Assert.Single(shell.Tabs);
    }

    [Fact]
    public async Task 마지막_탭을_닫으면_트리_선택도_비워진다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var a = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(a);
        await shell.RefreshAsync();
        shell.Selected = FindNode(shell.Roots, "A");
        await Settle(shell);

        await shell.CloseTabAsync(shell.ActiveTab!);

        // 안 비우면 같은 문서를 다시 클릭해도 SelectedItemChanged 가 안 나서 안 열린다 [실측]
        Assert.Null(shell.Selected);
        Assert.Null(shell.ActiveTab);
        Assert.Empty(shell.Tabs);
    }

    [Fact]
    public async Task 닫은_문서는_트리에서_다시_열_수_있다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var a = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(a);
        await shell.RefreshAsync();
        shell.Selected = FindNode(shell.Roots, "A");
        await Settle(shell);

        await shell.CloseTabAsync(shell.ActiveTab!);

        shell.Selected = FindNode(shell.Roots, "A");
        await Settle(shell);

        Assert.Single(shell.Tabs);
    }

    [Fact]
    public async Task 폴더를_고르면_탭은_그대로다()
    {
        using var vault = new TempVault();
        var (shell, dlg, store) = await BuildAsync(vault);
        vault.Dir("폴더1");
        var a = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(a);
        await shell.RefreshAsync();
        shell.Selected = FindNode(shell.Roots, "A");
        await Settle(shell);

        shell.Selected = FindNode(shell.Roots, "폴더1");
        await Settle(shell);

        Assert.Single(shell.Tabs);
        Assert.Equal(PathRules.NormalizeFull(a), shell.ActiveTab?.CurrentPath);
        Assert.Equal(0, dlg.ErrorCount);
    }

    [Fact]
    public async Task 창을_닫을_때_모든_탭이_저장된다()
    {
        using var vault = new TempVault();
        var (shell, dlg, store) = await BuildAsync(vault);
        var a = Path.Combine(vault.Root, "A.tbx");
        var b = Path.Combine(vault.Root, "B.tbx");
        await store.CreateAsync(a);
        await store.CreateAsync(b);

        await shell.OpenAsync(a);
        shell.ActiveTab!.Text = "A 값";
        await shell.OpenAsync(b);
        shell.ActiveTab!.Text = "B 값";

        Assert.True(await shell.SaveAllForExitAsync());

        // 활성 탭만 저장하면 배경 탭의 편집 내용이 종료와 함께 사라진다 (J-3)
        Assert.Equal("A 값", (await store.LoadAsync(a)).Text);
        Assert.Equal("B 값", (await store.LoadAsync(b)).Text);
        Assert.Equal(0, dlg.ErrorCount);
    }

    [Fact]
    public async Task 한_탭이라도_저장에_실패하면_종료가_취소되고_그_탭으로_이동한다()
    {
        using var vault = new TempVault();
        var (shell, dlg, store) = await BuildAsync(vault);
        var a = Path.Combine(vault.Root, "A.tbx");
        var b = Path.Combine(vault.Root, "B.tbx");
        await store.CreateAsync(a);
        await store.CreateAsync(b);

        await shell.OpenAsync(a);
        var tabA = shell.ActiveTab!;
        tabA.Text = "저장 실패할 값";
        await shell.OpenAsync(b);
        shell.ActiveTab!.Text = "B 값";

        File.SetAttributes(a, FileAttributes.ReadOnly);
        try
        {
            Assert.False(await shell.SaveAllForExitAsync());

            // 어느 문서 때문에 못 닫는지 보이지 않으면 사용자는 창이 안 닫힌다고만 느낀다
            Assert.Same(tabA, shell.ActiveTab);
            Assert.Equal(1, dlg.ErrorCount);
            Assert.Equal(2, shell.Tabs.Count);       // 종료 취소지 탭 정리가 아니다
        }
        finally { File.SetAttributes(a, FileAttributes.Normal); }
    }

    [Fact]
    public async Task 이전_세대_보기는_별도_탭으로_열리고_원본_탭이_남는다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var a = Path.Combine(vault.Root, "비밀번호.tbx");
        await store.CreateAsync(a);
        await store.SaveAsync(a, "연 시점 값");
        await shell.RefreshAsync();

        shell.Selected = FindNode(shell.Roots, "비밀번호");
        await Settle(shell);
        shell.ActiveTab!.Text = "그 뒤에 고친 값";

        shell.OpenBackupCommand.Execute(null);
        await Settle(shell);

        // 편집기를 점유하면 원본으로 돌아갈 방법이 없다 (J-2). 나란히 두고 복사하게 한다.
        Assert.Equal(2, shell.Tabs.Count);
        Assert.True(shell.ActiveTab!.IsSnapshot);
        Assert.True(shell.ActiveTab.IsReadOnly);
        Assert.Equal("연 시점 값", shell.ActiveTab.Text);
        Assert.Contains("비밀번호", shell.ActiveTab.TabTitle);
        Assert.Equal("그 뒤에 고친 값", shell.Tabs[0].Text);
    }

    [Fact]
    public async Task 이전_세대_탭은_두_번_열어도_하나다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var a = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(a);
        await store.SaveAsync(a, "연 시점 값");
        await shell.RefreshAsync();

        shell.Selected = FindNode(shell.Roots, "A");
        await Settle(shell);

        shell.OpenBackupCommand.Execute(null);
        await Settle(shell);
        shell.OpenBackupCommand.Execute(null);
        await Settle(shell);

        Assert.Equal(2, shell.Tabs.Count);
    }

    // ── 탭 줄 신호 · ▾ 목록 (make-tab-strip-single-row) ──────────────────────

    /// 이미 활성인 탭을 다시 가리키면 선택 변화가 없어 탭 줄이 넘길 길이 없다 — 셸이 신호를 낸다 (D-077).
    [Fact]
    public async Task 활성_탭에_같은_값을_다시_넣어도_보이게_하라는_신호를_낸다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var a = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(a);
        await shell.OpenAsync(a);
        var signals = 0;
        shell.ActiveTabRevealRequested += (_, _) => signals++;

        await shell.OpenAsync(a);            // 이미 열린 문서를 트리·검색에서 다시 연다
        shell.ActiveTab = null;              // 마지막 탭을 닫은 경우 — 처리기는 null 을 견뎌야 한다

        Assert.Equal(2, signals);
    }

    /// "문서" 두 개를 목록에서 가를 수 없다. 같은 제목일 때만 금고 안 폴더를 붙인다 (D-078).
    [Fact]
    public async Task 열린_탭_목록은_탭_순서이고_같은_제목에만_폴더를_붙인다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var top = Path.Combine(vault.Root, "문서.tbx");
        var unique = Path.Combine(vault.Root, "고유.tbx");
        var nested = Path.Combine(vault.Dir("폴더1"), "문서.tbx");
        foreach (var path in new[] { top, unique, nested })
        {
            await store.CreateAsync(path);
            await shell.OpenAsync(path);
        }

        var entries = shell.TabListEntries();

        Assert.Equal(shell.Tabs, entries.Select(e => e.Tab));
        Assert.Equal(new string?[] { "금고 맨 위", null, "폴더1" }, entries.Select(e => e.Folder));
    }

    /// "열었을 때 상태" 탭의 실제 자리는 .history 안이다 — 그 자리를 보이면 어느 문서 것인지 알 수 없다.
    [Fact]
    public async Task 열었을_때_상태_탭은_원본_문서의_폴더를_보인다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var first = Path.Combine(vault.Dir("폴더1"), "문서.tbx");
        var second = Path.Combine(vault.Dir("폴더2"), "문서.tbx");
        foreach (var path in new[] { first, second })
        {
            await store.CreateAsync(path);
            await store.SaveAsync(path, "연 시점 값");
        }
        await shell.RefreshAsync();

        foreach (var path in new[] { first, second })
        {
            shell.Selected = FindByPath(shell.Roots, path);
            await Settle(shell);
            shell.OpenBackupCommand.Execute(null);
            await Settle(shell);
        }

        var snapshots = shell.TabListEntries().Where(e => e.Tab.IsSnapshot).Select(e => e.Folder);
        Assert.Equal(new string?[] { "폴더1", "폴더2" }, snapshots);
    }

    /// <summary>
    /// 탭 아이콘의 그림은 파일 종류(경로)로 고른다 — 트리와 같은 판정이다(D-091). 읽지 못한 .txt 도 평문이다.
    /// 경로가 바뀌면 그림도 다시 골라야 하므로 바뀌었다고 알린다.
    /// </summary>
    [Fact]
    public async Task 파일_종류는_읽었는지와_상관없이_경로로_정하고_경로가_바뀌면_알린다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var doc = Path.Combine(vault.Root, "A.tbx");
        var binary = Path.Combine(vault.Root, "이진.txt");
        await store.CreateAsync(doc);
        await File.WriteAllBytesAsync(binary, [0x00, 0x01, 0x02, 0xFF, 0xFE, 0x00, 0x10, 0x00, 0x80, 0x00]);

        await shell.OpenAsync(binary);
        var unreadable = shell.ActiveTab!;
        await shell.OpenAsync(doc);
        var tab = shell.ActiveTab!;

        Assert.False(unreadable.IsPlainText);          // 읽지 못했다 — 배너 · 흐림은 이 값
        Assert.True(unreadable.IsPlainTextFile);       // 그래도 평문 파일이다 — 그림은 이 값
        Assert.False(tab.IsPlainTextFile);

        var raised = new List<string?>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        tab.UpdatePath(Path.Combine(vault.Root, "A.txt"));

        Assert.True(tab.IsPlainTextFile);
        Assert.Contains(nameof(EditorViewModel.IsPlainTextFile), raised);
    }

    private static TreeNodeViewModel FindByPath(IEnumerable<TreeNodeViewModel> nodes, string path)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.FullPath, PathRules.NormalizeFull(path), StringComparison.OrdinalIgnoreCase)) return node;
            if (TryFindByPath(node.Children, path) is { } hit) return hit;
        }
        throw new InvalidOperationException($"노드를 찾지 못함: {path}");
    }

    private static TreeNodeViewModel? TryFindByPath(IEnumerable<TreeNodeViewModel> nodes, string path)
    {
        try { return FindByPath(nodes, path); }
        catch (InvalidOperationException) { return null; }
    }
}

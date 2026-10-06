using TextBean.Services;
using TextBean.Services.Interfaces;
using TextBean.ViewModels;

namespace TextBean.Tests;

public class ShellViewModelTests
{
    // 조립·탐색은 ShellFixture 가 맡는다 — 탭 테스트와 같은 것을 쓴다
    private static Task<(ShellViewModel shell, FakeDialogs dlg, DocumentStore store)> BuildAsync(TempVault vault)
        => ShellFixture.BuildAsync(vault);

    private static Task Settle(ShellViewModel shell) => ShellFixture.Settle(shell);

    private static TreeNodeViewModel FindNode(IEnumerable<TreeNodeViewModel> nodes, string name)
        => ShellFixture.FindNode(nodes, name);

    [Fact]
    public async Task 문서를_고르면_열린다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        await store.CreateAsync(Path.Combine(vault.Root, "A.tbx"));
        await store.SaveAsync(Path.Combine(vault.Root, "A.tbx"), "A의 내용");
        await shell.RefreshAsync();

        shell.Selected = FindNode(shell.Roots, "A");

        await Settle(shell);
        Assert.Equal("A의 내용", shell.Editor!.Text);
    }

    /// <summary>
    /// 탭 도입으로 계약이 바뀌었다. 전과 달리 B를 고른다고 A가 저장되지는 않는다 —
    /// A는 자기 탭에 수정 상태 그대로 남고, 예약된 자동 저장이 1.5초 뒤 저장한다 (D-015).
    /// 전에는 편집기가 하나뿐이라 떠나는 순간 저장하지 않으면 편집 내용이 사라졌다.
    /// </summary>
    [Fact]
    public async Task A편집후_B선택하면_B가_열리고_A는_자기_탭에_남는다()
    {
        using var vault = new TempVault();
        var (shell, dlg, store, factory) = await ShellFixture.BuildWithFactoryAsync(vault);
        var pathA = Path.Combine(vault.Root, "A.tbx");
        var pathB = Path.Combine(vault.Root, "B.tbx");
        await store.CreateAsync(pathA);
        await store.CreateAsync(pathB);
        await store.SaveAsync(pathB, "B의 내용");
        await shell.RefreshAsync();

        shell.Selected = FindNode(shell.Roots, "A");
        await Settle(shell);

        shell.Editor!.Text = "A에 입력한 값";
        Assert.True(shell.Editor!.IsDirty);

        shell.Selected = FindNode(shell.Roots, "B");
        await Settle(shell);

        Assert.Equal("B의 내용", shell.Editor!.Text);          // 화면이 B로 바뀌어야 한다
        Assert.False(shell.Editor!.IsDirty);                   // B는 방금 읽었으니 깨끗하다

        // A의 편집 내용은 탭에 그대로 있고, 저장 예약도 살아 있어야 한다
        var tabA = shell.Tabs[0];
        Assert.Equal("A에 입력한 값", tabA.Text);
        Assert.True(tabA.IsDirty);
        Assert.True(factory.Timers[0].IsRunning);

        factory.Timers[0].Fire();
        await Task.Delay(50);
        Assert.Equal("A에 입력한 값", (await store.LoadAsync(pathA)).Text);

        // 자동 저장이라 애초에 묻지 않는다 (D-015)
        Assert.Equal(0, dlg.ConfirmCount);
        Assert.Equal(0, dlg.ErrorCount);
    }

    [Fact]
    public async Task 문서를_옮기면_열린_문서의_경로가_따라간다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var doc = Path.Combine(vault.Root, "정보.tbx");
        await store.CreateAsync(doc);
        vault.Dir("폴더1");
        await shell.RefreshAsync();

        shell.Selected = FindNode(shell.Roots, "정보");
        await Settle(shell);

        await shell.TryMoveAsync(doc, Path.Combine(vault.Root, "폴더1"));

        // 경로를 갱신하지 않으면 1.5초 뒤 자동 저장이 옛 자리에 유령 문서를 만든다
        Assert.Equal(Path.Combine(vault.Root, "폴더1", "정보.tbx"), shell.Editor!.CurrentPath);
    }

    [Fact]
    public async Task 폴더를_옮기면_그_안에_열린_문서의_경로도_따라간다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        vault.Dir("폴더A");
        var doc = Path.Combine(vault.Root, "폴더A", "정보.tbx");
        await store.CreateAsync(doc);
        vault.Dir("폴더B");
        await shell.RefreshAsync();

        shell.Selected = FindNode(shell.Roots, "정보");
        await Settle(shell);

        await shell.TryMoveAsync(Path.Combine(vault.Root, "폴더A"), Path.Combine(vault.Root, "폴더B"));

        Assert.Equal(Path.Combine(vault.Root, "폴더B", "폴더A", "정보.tbx"), shell.Editor!.CurrentPath);
    }

    /// <summary>
    /// J-1로 계약이 바뀌었다. 전에는 수정 중이면 이동을 거부했다 —
    /// 자동 저장 앱에서 "저장 안 해서 못 옮김"은 사용자에게 이해가 되지 않는다.
    /// 이제는 먼저 저장하고 옮긴다. 저장에 실패할 때만 취소한다.
    /// </summary>
    [Fact]
    public async Task 수정_중인_문서는_저장한_뒤_옮긴다()
    {
        using var vault = new TempVault();
        var (shell, dlg, store) = await BuildAsync(vault);
        var doc = Path.Combine(vault.Root, "정보.tbx");
        await store.CreateAsync(doc);
        var folder = vault.Dir("폴더1");
        await shell.RefreshAsync();

        shell.Selected = FindNode(shell.Roots, "정보");
        await Settle(shell);
        shell.Editor!.Text = "저장 안 한 값";

        var moved = await shell.TryMoveAsync(doc, folder);

        Assert.True(moved);
        Assert.False(File.Exists(doc));
        Assert.Equal("저장 안 한 값", (await store.LoadAsync(Path.Combine(folder, "정보.tbx"))).Text);
        Assert.Equal(0, dlg.ErrorCount);
    }

    [Fact]
    public async Task 옮길_문서가_저장에_실패하면_옮기지_않는다()
    {
        using var vault = new TempVault();
        var (shell, dlg, store) = await BuildAsync(vault);
        var doc = Path.Combine(vault.Root, "정보.tbx");
        await store.CreateAsync(doc);
        var folder = vault.Dir("폴더1");
        await shell.RefreshAsync();

        shell.Selected = FindNode(shell.Roots, "정보");
        await Settle(shell);
        shell.Editor!.Text = "저장 실패할 값";

        File.SetAttributes(doc, FileAttributes.ReadOnly);
        try
        {
            var moved = await shell.TryMoveAsync(doc, folder);

            // 옮겨 두면 탭이 옛 경로를 쥔 채 남아 저장이 영영 실패한다
            Assert.False(moved);
            Assert.True(File.Exists(doc));
            Assert.Equal(1, dlg.ErrorCount);      // 이유를 말해야 한다
        }
        finally { File.SetAttributes(doc, FileAttributes.Normal); }
    }

    [Fact]
    public async Task 이동_후_선택이_새_경로로_옮겨간다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var doc = Path.Combine(vault.Root, "정보.tbx");
        await store.CreateAsync(doc);
        vault.Dir("폴더1");
        await shell.RefreshAsync();

        await shell.TryMoveAsync(doc, Path.Combine(vault.Root, "폴더1"));

        // 옛 경로를 쥐고 있으면 다음 "새 폴더"가 방금 비운 자리를 되살린다
        Assert.Equal(Path.Combine(vault.Root, "폴더1", "정보.tbx"), shell.Selected?.FullPath);
    }

    [Fact]
    public async Task 거부된_이동은_사유를_알리고_아무것도_바꾸지_않는다()
    {
        using var vault = new TempVault();
        var (shell, dlg, _) = await BuildAsync(vault);
        var folderA = vault.Dir("폴더A");
        var inner = vault.Dir(@"폴더A\안쪽");
        await shell.RefreshAsync();

        var moved = await shell.TryMoveAsync(folderA, inner);

        Assert.False(moved);
        Assert.True(Directory.Exists(folderA));
        Assert.Equal(1, dlg.ErrorCount);
    }

    [Fact]
    public async Task 같은_자리_드롭은_조용히_무시한다()
    {
        using var vault = new TempVault();
        var (shell, dlg, store) = await BuildAsync(vault);
        var doc = Path.Combine(vault.Root, "정보.tbx");
        await store.CreateAsync(doc);
        await shell.RefreshAsync();

        var moved = await shell.TryMoveAsync(doc, vault.Root);

        Assert.False(moved);
        Assert.Equal(0, dlg.ErrorCount);   // 헛손질마다 대화상자가 뜨면 못 쓴다
    }

    [Fact]
    public async Task 거부된_드래그는_사유를_상태표시줄에_내보낸다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var doc = Path.Combine(vault.Root, "정보.tbx");
        await store.CreateAsync(doc);
        var folder = vault.Dir("폴더1");
        await store.CreateAsync(Path.Combine(folder, "정보.tbx"));   // 같은 이름
        await shell.RefreshAsync();

        var canDrop = shell.EvaluateDrop(doc, folder);

        // 드래그오버에서 거부하면 Windows 가 드롭을 전달하지 않아 대화상자로는 사유가 영영 안 보인다
        Assert.False(canDrop);
        Assert.Contains("같은 이름", shell.DropHint);
    }

    [Fact]
    public async Task 놓을_수_있는_곳에서는_사유가_비워진다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var doc = Path.Combine(vault.Root, "정보.tbx");
        await store.CreateAsync(doc);
        var folder = vault.Dir("폴더1");
        await shell.RefreshAsync();

        shell.EvaluateDrop(doc, vault.Root);          // 거부 — 같은 자리
        Assert.NotEqual("", shell.DropHint);

        Assert.True(shell.EvaluateDrop(doc, folder));
        Assert.Equal("", shell.DropHint);

        shell.EvaluateDrop(doc, vault.Root);
        shell.ClearDropHint();
        Assert.Equal("", shell.DropHint);
    }

    [Fact]
    public async Task 루트_노드는_드래그_대상이_아니다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await BuildAsync(vault);
        await shell.RefreshAsync();

        Assert.True(shell.Roots[0].IsRoot);
        Assert.All(shell.Roots[0].Children, child => Assert.False(child.IsRoot));
    }

    [Fact]
    public async Task 폴더를_고른_상태에서_만들면_그_폴더_안에_생긴다()
    {
        using var vault = new TempVault();
        var (shell, dlg, _) = await BuildAsync(vault);
        vault.Dir(@"폴더1\폴더2");
        await shell.RefreshAsync();

        shell.Selected = FindNode(shell.Roots, "폴더2");
        dlg.PromptTextResult = "폴더3";
        shell.NewFolderCommand.Execute(null);
        await Settle(shell);

        // 최상위가 아니라 고른 폴더 안에 생겨야 한다
        Assert.True(Directory.Exists(Path.Combine(vault.Root, "폴더1", "폴더2", "폴더3")));
        Assert.False(Directory.Exists(Path.Combine(vault.Root, "폴더3")));
    }

    [Fact]
    public async Task 문서를_고른_상태에서_만들면_그_문서가_있는_폴더에_생긴다()
    {
        using var vault = new TempVault();
        var (shell, dlg, store) = await BuildAsync(vault);
        vault.Dir("폴더1");
        await store.CreateAsync(Path.Combine(vault.Root, "폴더1", "문서.tbx"));
        await shell.RefreshAsync();

        shell.Selected = FindNode(shell.Roots, "문서");
        await Settle(shell);
        dlg.PromptTextResult = "옆문서";
        shell.NewDocumentCommand.Execute(null);
        await Settle(shell);

        Assert.True(File.Exists(Path.Combine(vault.Root, "폴더1", "옆문서.tbx")));
    }

    [Fact]
    public async Task 폴더를_연달아_만들면_계속_같은_폴더_안에_생긴다()
    {
        using var vault = new TempVault();
        var (shell, dlg, _) = await BuildAsync(vault);
        vault.Dir(@"폴더1\폴더2");
        await shell.RefreshAsync();

        shell.Selected = FindNode(shell.Roots, "폴더2");

        dlg.PromptTextResult = "폴더3";
        shell.NewFolderCommand.Execute(null);
        await Settle(shell);

        // 트리를 다시 그리면서 선택이 풀리면, 두 번째부터 최상위로 간다
        dlg.PromptTextResult = "폴더4";
        shell.NewFolderCommand.Execute(null);
        await Settle(shell);

        Assert.True(Directory.Exists(Path.Combine(vault.Root, "폴더1", "폴더2", "폴더3")));
        Assert.True(Directory.Exists(Path.Combine(vault.Root, "폴더1", "폴더2", "폴더4")));
        Assert.False(Directory.Exists(Path.Combine(vault.Root, "폴더4")));
    }

    [Fact]
    public async Task 새로고침해도_선택이_유지된다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await BuildAsync(vault);
        vault.Dir(@"폴더1\폴더2");
        await shell.RefreshAsync();

        var target = FindNode(shell.Roots, "폴더2").FullPath;
        shell.Selected = FindNode(shell.Roots, "폴더2");

        await shell.RefreshAsync();

        Assert.Equal(target, shell.Selected?.FullPath);
        Assert.True(shell.Selected!.IsSelected);   // 화면에도 표시돼야 한다
    }

    [Fact]
    public async Task 아무것도_안_고른_상태에서_만들면_최상위에_생긴다()
    {
        using var vault = new TempVault();
        var (shell, dlg, _) = await BuildAsync(vault);
        await shell.RefreshAsync();

        shell.Selected = null;
        dlg.PromptTextResult = "최상위폴더";
        shell.NewFolderCommand.Execute(null);
        await Settle(shell);

        Assert.True(Directory.Exists(Path.Combine(vault.Root, "최상위폴더")));
    }

    [Fact]
    public async Task 폴더를_고르면_열린_문서가_그대로_남는다()
    {
        using var vault = new TempVault();
        var (shell, dlg, store) = await BuildAsync(vault);
        vault.Dir("폴더1");
        var pathA = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(pathA);
        await store.SaveAsync(pathA, "A의 내용");
        await shell.RefreshAsync();

        shell.Selected = FindNode(shell.Roots, "A");
        await Settle(shell);

        shell.Selected = FindNode(shell.Roots, "폴더1");
        await Settle(shell);

        Assert.Equal(pathA, shell.Editor!.CurrentPath);   // 폴더는 문서를 바꾸지 않는다
        Assert.Equal(0, dlg.ErrorCount);
    }

    [Fact]
    public async Task 수정한_채_폴더를_고르면_묻지_않고_문서도_그대로다()
    {
        using var vault = new TempVault();
        var (shell, dlg, store) = await BuildAsync(vault);
        vault.Dir("폴더1");
        var pathA = Path.Combine(vault.Root, "A.tbx");
        await store.CreateAsync(pathA);
        await shell.RefreshAsync();

        shell.Selected = FindNode(shell.Roots, "A");
        await Settle(shell);
        shell.Editor!.Text = "입력";

        shell.Selected = FindNode(shell.Roots, "폴더1");
        await Settle(shell);

        // 폴더 선택은 문서 전환이 아니므로 저장도 일어나지 않는다 (자동 저장 타이머는 따로 돈다)
        Assert.Equal(0, dlg.ErrorCount);
        Assert.True(shell.Editor!.IsDirty);
        Assert.Equal(pathA, shell.Editor!.CurrentPath);
    }
}

using TextBean.Services;
using TextBean.ViewModels;
using static TextBean.Tests.ShellFixture;

namespace TextBean.Tests;

/// <summary>
/// 탭 순서 바꾸기. 위험은 "순서가 안 바뀌는 것"이 아니라
/// 순서를 바꾸는 방식이 본문 TextBox 를 새로 만들어 실행취소 스택을 조용히 날리는 것이다 [실측].
/// 그래서 여기서는 순서뿐 아니라 "편집기가 살아 있는가"까지 본다.
/// </summary>
public class TabReorderTests
{
    private static async Task<(ShellViewModel shell, DocumentStore store, string[] paths)>
        OpenFourAsync(TempVault vault)
    {
        var (shell, _, store) = await BuildAsync(vault);
        var paths = new[] { "A", "B", "C", "D" }
            .Select(n => Path.Combine(vault.Root, n + ".tbx")).ToArray();

        foreach (var p in paths)
        {
            await store.CreateAsync(p);
            await shell.OpenAsync(p);
        }
        return (shell, store, paths);
    }

    private static string[] Order(ShellViewModel shell)
        => [.. shell.Tabs.Select(t => Path.GetFileNameWithoutExtension(t.CurrentPath!))];

    [Fact]
    public async Task 순서가_바뀐다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await OpenFourAsync(vault);

        shell.MoveTab(shell.Tabs[0], 2);

        Assert.Equal(["B", "C", "A", "D"], Order(shell));
    }

    [Fact]
    public async Task 뒤에서_앞으로도_옮겨진다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await OpenFourAsync(vault);

        shell.MoveTab(shell.Tabs[3], 0);

        Assert.Equal(["D", "A", "B", "C"], Order(shell));
    }

    [Fact]
    public async Task 제자리_이동은_활성_탭을_잃지_않는다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await OpenFourAsync(vault);
        var tab = shell.Tabs[1];
        shell.ActiveTab = tab;

        shell.MoveTab(tab, 1);

        // 그대로 흘리면 TabControl 이 SelectedIndex=-1 을 쓰고 양방향 바인딩이
        // ActiveTab=null 을 밀어 넣어 본문이 빈 화면이 된다 [실측].
        // 탭은 남아 있어 HasNoTabs 도 거짓이라 안내 문구조차 안 뜬다.
        Assert.Same(tab, shell.ActiveTab);
        Assert.True(tab.IsActive);
        Assert.Equal(["A", "B", "C", "D"], Order(shell));
    }

    [Fact]
    public async Task 활성_탭을_옮겨도_활성이_유지된다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await OpenFourAsync(vault);
        var tab = shell.Tabs[1];
        shell.ActiveTab = tab;

        shell.MoveTab(tab, 3);

        Assert.Same(tab, shell.ActiveTab);
        Assert.True(tab.IsActive);
        Assert.Equal(1, shell.Tabs.Count(t => t.IsActive));
        Assert.Equal(3, shell.Tabs.IndexOf(tab));
    }

    [Fact]
    public async Task 비활성_탭을_옮겨도_활성_탭은_그대로다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await OpenFourAsync(vault);
        var active = shell.Tabs[0];
        shell.ActiveTab = active;

        shell.MoveTab(shell.Tabs[3], 1);

        Assert.Same(active, shell.ActiveTab);
        Assert.Equal(["A", "D", "B", "C"], Order(shell));
    }

    [Fact]
    public async Task 범위_밖_인덱스는_양끝으로_붙는다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await OpenFourAsync(vault);

        shell.MoveTab(shell.Tabs[1], -5);
        Assert.Equal(["B", "A", "C", "D"], Order(shell));

        shell.MoveTab(shell.Tabs[0], 99);
        Assert.Equal(["A", "C", "D", "B"], Order(shell));
    }

    [Fact]
    public async Task 목록에_없는_탭은_무시한다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await OpenFourAsync(vault);
        var closed = shell.Tabs[0];
        await shell.CloseTabAsync(closed);

        // 드래그가 진행되는 동안 그 탭이 닫히면 Drop 이 사라진 탭을 짚는다.
        // 되살리면 해제된 편집기가 목록에 남아 이후 저장이 영영 실패한다.
        shell.MoveTab(closed, 0);

        Assert.Equal(["B", "C", "D"], Order(shell));
        Assert.DoesNotContain(closed, shell.Tabs);
    }

    /// <summary>
    /// 드롭 지점 → 순서 변환. "자기 자리가 먼저 빈다"는 성질 때문에 방향마다 한 칸씩 어긋나고,
    /// 이 계산이 틀리면 놓은 자리와 한 칸 다른 데로 간다 — 눈으로만 보면 "가끔 이상하다"로 남는다.
    /// </summary>
    [Theory]
    // 끌린 탭, 놓은 탭, 뒤에 놓는가, 기대 순서
    [InlineData("A", "C", false, "B,A,C,D")]   // 앞→뒤, C 앞에
    [InlineData("A", "C", true, "B,C,A,D")]    // 앞→뒤, C 뒤에
    [InlineData("D", "B", false, "A,D,B,C")]   // 뒤→앞, B 앞에
    [InlineData("D", "B", true, "A,B,D,C")]    // 뒤→앞, B 뒤에
    [InlineData("A", "D", true, "B,C,D,A")]    // 맨 뒤로
    [InlineData("D", "A", false, "D,A,B,C")]   // 맨 앞으로
    [InlineData("A", "A", false, "A,B,C,D")]   // 자기 위 — 아무 일도 없어야 한다
    [InlineData("A", "A", true, "A,B,C,D")]
    [InlineData("B", "A", true, "A,B,C,D")]    // A 뒤 = 이미 그 자리
    [InlineData("B", "C", false, "A,B,C,D")]   // C 앞 = 이미 그 자리
    public async Task 놓은_자리대로_옮겨진다(string dragged, string dropOn, bool after, string expected)
    {
        using var vault = new TempVault();
        var (shell, _, _) = await OpenFourAsync(vault);

        EditorViewModel Tab(string name)
            => shell.Tabs.Single(t => Path.GetFileNameWithoutExtension(t.CurrentPath!) == name);

        shell.MoveTabTo(Tab(dragged), Tab(dropOn), after);

        Assert.Equal(expected.Split(','), Order(shell));
    }

    [Fact]
    public async Task 놓은_자리가_같아도_활성_탭을_잃지_않는다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await OpenFourAsync(vault);
        var tab = shell.Tabs[1];
        shell.ActiveTab = tab;

        shell.MoveTabTo(tab, tab, false);
        shell.MoveTabTo(tab, tab, true);
        shell.MoveTabTo(tab, shell.Tabs[0], true);     // 이미 그 자리

        Assert.Same(tab, shell.ActiveTab);
        Assert.True(tab.IsActive);
    }

    [Fact]
    public async Task 재정렬은_편집기를_해제하지_않는다()
    {
        using var vault = new TempVault();
        var (shell, store, paths) = await OpenFourAsync(vault);
        var tab = shell.Tabs[0];

        shell.MoveTab(tab, 3);
        shell.MoveTab(tab, 0);
        shell.MoveTab(tab, 2);

        // RemoveTab 을 경유하면 Dispose 된 편집기가 목록에 남아
        // SaveCoreAsync 가 첫 줄에서 false 를 돌려주고 타이핑이 디스크에 한 글자도 안 남는다.
        tab.Text = "재정렬 뒤에 저장되는가";
        Assert.True(await tab.TrySaveAsync());
        Assert.Equal("재정렬 뒤에 저장되는가", (await store.LoadAsync(paths[0])).Text);
    }

    [Fact]
    public async Task 재정렬_후에도_이름변경_경로가_전파된다()
    {
        using var vault = new TempVault();
        var (shell, dlg, store) = await BuildAsync(vault);
        var doc = Path.Combine(vault.Root, "정보.tbx");
        var other = Path.Combine(vault.Root, "다른.tbx");
        await store.CreateAsync(doc);
        await store.CreateAsync(other);
        await shell.RefreshAsync();

        await shell.OpenAsync(doc);
        await shell.OpenAsync(other);
        shell.MoveTab(shell.Tabs[0], 1);              // 정보.tbx 를 뒤로 보낸다

        shell.Selected = FindNode(shell.Roots, "정보");
        await Settle(shell);
        dlg.PromptTextResult = "새이름";
        shell.RenameCommand.Execute(null);
        await Settle(shell);

        // TabsAffectedBy 는 순서와 무관해야 한다 — 인덱스에 기대면 여기서 깨진다
        var moved = shell.Tabs.Single(t => t.CurrentPath!.EndsWith("새이름.tbx"));
        Assert.Equal(Path.Combine(vault.Root, "새이름.tbx"), moved.CurrentPath);
        Assert.False(File.Exists(doc));
    }
}

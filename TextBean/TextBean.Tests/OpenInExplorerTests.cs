using System.Text;
using TextBean.Models;
using TextBean.Services;
using TextBean.ViewModels;

namespace TextBean.Tests;

/// <summary>
/// 트리 우클릭 "파일 탐색기에서 열기". 가짜 런처로 <b>무엇을 어떻게 열려고 했는지</b>만 본다.
/// 진짜 탐색기 동작(긴 경로·없는 경로·옆의 .cmd·가짜 shell32.dll)은 창을 띄우므로
/// 자동 테스트 밖에서 실측했다 — scratchpad/explorer-verify.
///
/// 사용자 확정(2026-09-29): 폴더는 안을 연다 · 파일(.tbx·.txt)은 상위 폴더를 열고 선택한다 ·
/// 찾지 못하면 알림만 띄우고 다른 곳을 열지 않는다.
/// </summary>
public class OpenInExplorerTests
{
    private static async Task<(ShellViewModel Shell, FakeDialogs Dlg, FakeExplorerLauncher Explorer)> Build(TempVault vault)
    {
        var (shell, dlg, explorer) = await ShellFixture.BuildWithLauncherAsync(vault);
        await shell.RefreshAsync();
        return (shell, dlg, explorer);
    }

    private static async Task Run(ShellViewModel shell)
    {
        // 선택만으로도 "문서 열기"가 Guarded 로 돈다. 그것이 끝난 뒤에 명령을 부른다 —
        // 안 기다리면 Pending 이 덮여 앞의 작업이 끝났는지 알 수 없다.
        await ShellFixture.Settle(shell);
        shell.OpenInExplorerCommand.Execute(null);
        await ShellFixture.Settle(shell);
    }

    private static async Task<string> CreateTbx(TempVault vault, string relative)
    {
        var path = Path.Combine(vault.Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await new DocumentStore(new TreeService(vault.Root), TestKeys.Codec()).CreateAsync(path);
        return path;
    }

    [Fact]
    public async Task 폴더는_그_폴더_안을_연다()
    {
        using var vault = new TempVault();
        vault.Dir("aws");
        var (shell, dlg, explorer) = await Build(vault);

        shell.Selected = ShellFixture.FindNode(shell.Roots, "aws");
        await Run(shell);

        Assert.Equal([("folder", Path.Combine(vault.Root, "aws"))], explorer.Calls);
        Assert.Equal(0, dlg.ErrorCount);
        Assert.Empty(shell.Tabs);              // 폴더를 연다고 탭이 생기지 않는다
    }

    [Fact]
    public async Task 금고문서는_상위_폴더를_열고_선택한다()
    {
        using var vault = new TempVault();
        var tbx = await CreateTbx(vault, @"aws\운영키.tbx");
        var (shell, dlg, explorer) = await Build(vault);

        shell.Selected = ShellFixture.FindNode(shell.Roots, "운영키");
        await Run(shell);

        Assert.Equal([("reveal", tbx)], explorer.Calls);
        Assert.Equal(0, dlg.ErrorCount);
    }

    /// 평문도 금고 문서와 똑같이 선택한다 (Q-3). 둘이 다르게 움직이면 사용자가 헷갈린다.
    [Fact]
    public async Task 평문도_똑같이_상위_폴더를_열고_선택한다()
    {
        using var vault = new TempVault();
        var txt = vault.WriteRaw(@"aws\운영노트.txt", new UTF8Encoding(false).GetBytes("메모"));
        var (shell, dlg, explorer) = await Build(vault);

        shell.Selected = ShellFixture.FindNode(shell.Roots, "운영노트.txt");
        await Run(shell);

        Assert.Equal([("reveal", txt)], explorer.Calls);
        Assert.Equal(0, dlg.ErrorCount);
    }

    /// <summary>
    /// 금고 루트를 "상위에서 선택"으로 열면 금고의 부모 폴더, 즉 금고 밖 형제 폴더들이 보인다.
    /// 루트는 반드시 안으로 연다.
    /// </summary>
    [Fact]
    public async Task 금고_루트는_안으로_연다()
    {
        using var vault = new TempVault();
        var (shell, _, explorer) = await Build(vault);

        shell.Selected = Assert.Single(shell.Roots);
        await Run(shell);

        Assert.Equal([("folder", PathRules.NormalizeFull(vault.Root))], explorer.Calls);
    }

    /// 빈 곳을 우클릭하면 선택이 없다 — 다른 트리 명령과 같이 금고 최상위로 본다.
    [Fact]
    public async Task 선택이_없으면_금고_루트를_연다()
    {
        using var vault = new TempVault();
        var (shell, _, explorer) = await Build(vault);

        shell.Selected = null;
        await Run(shell);

        Assert.Equal([("folder", PathRules.NormalizeFull(vault.Root))], explorer.Calls);
    }

    /// <summary>
    /// 트리를 새로 읽기 전에 밖에서 지워졌거나 이름이 바뀐 경우. 알림만 띄우고 다른 곳을 열지 않는다 (Q-2).
    /// 대신 상위 폴더를 열면 "요청한 것을 못 찾고 조용히 엉뚱한 곳을 연다" — explorer.exe 방식을 버린 이유다.
    /// </summary>
    [Fact]
    public async Task 찾지_못하면_알림만_띄우고_다른_곳을_열지_않는다()
    {
        using var vault = new TempVault();
        await CreateTbx(vault, @"aws\운영키.tbx");
        var (shell, dlg, explorer) = await Build(vault);
        shell.Selected = ShellFixture.FindNode(shell.Roots, "운영키");
        await ShellFixture.Settle(shell);
        var tabsBefore = shell.Tabs.Count;
        explorer.Result = false;

        await Run(shell);

        Assert.Single(explorer.Calls);         // 시도는 한 번뿐 — 상위 폴더로 다시 열지 않는다
        Assert.Equal(1, dlg.ErrorCount);
        Assert.Contains("찾을 수 없습니다", dlg.LastErrorMessage);
        Assert.Contains("F5", dlg.LastErrorMessage);
        Assert.Equal(tabsBefore, shell.Tabs.Count);
    }

    [Fact]
    public async Task 셸_호출이_던져도_앱은_죽지_않는다()
    {
        using var vault = new TempVault();
        vault.Dir("aws");
        var (shell, dlg, explorer) = await Build(vault);
        explorer.Throw = new DllNotFoundException();

        shell.Selected = ShellFixture.FindNode(shell.Roots, "aws");
        await Run(shell);

        Assert.Equal(1, dlg.ErrorCount);
        Assert.Equal("탐색기에서 열기 실패", dlg.LastErrorTitle);
        Assert.Contains(nameof(DllNotFoundException), dlg.LastErrorMessage);   // 예외는 타입만 보인다
    }

    /// 트리 불변식에 기대지 않는다. 루트 밖 노드가 들어와도 런처를 부르지 않는다 (PROHIBITED-CUSTOM-05).
    [Fact]
    public async Task 루트_밖_경로는_런처를_부르지_않는다()
    {
        using var vault = new TempVault();
        using var other = new TempVault();
        var (shell, dlg, explorer) = await Build(vault);

        shell.Selected = new TreeNodeViewModel(new TreeNode(other.Root, "밖", true, []));
        await Run(shell);

        Assert.Empty(explorer.Calls);
        Assert.Equal(1, dlg.ErrorCount);
        Assert.Contains("금고 밖이나 앱이 쓰는 영역은 열 수 없습니다", dlg.LastErrorMessage);
        Assert.DoesNotContain(other.Root, dlg.LastErrorMessage!);     // 막으면서 경로를 흘리지 않는다
    }

    /// <summary>
    /// 폴더 노드만 시험하면 "검사를 폴더일 때만 한다"로 망가뜨려도 전부 통과했다 [실측 — 리뷰가 코드를 일부러 바꿔 확인].
    /// 검사는 분기(폴더 열기 / 파일 선택) 앞에 있어야 한다.
    /// 파일 노드는 선택만으로 "문서 열기"가 돌아 그쪽 오류가 따로 셀 수 있으므로, 명령 전후의 차이만 본다.
    /// </summary>
    [Theory]
    [InlineData("outside")]
    [InlineData(@".trash\20260101-000000\aws\운영키.tbx")]
    [InlineData(@".history\aws\운영키.tbx\opened.tbx")]
    public async Task 파일_노드도_루트_밖과_예약_영역은_런처를_부르지_않는다(string where)
    {
        using var vault = new TempVault();
        using var other = new TempVault();
        var (shell, dlg, explorer) = await Build(vault);
        var path = where == "outside"
            ? Path.Combine(other.Root, "밖문서.tbx")
            : Path.Combine(vault.Root, where);

        shell.Selected = new TreeNodeViewModel(new TreeNode(path, "대상", false, []));
        await ShellFixture.Settle(shell);
        var errorsBefore = dlg.ErrorCount;

        shell.OpenInExplorerCommand.Execute(null);
        await ShellFixture.Settle(shell);

        Assert.Empty(explorer.Calls);
        Assert.Equal(errorsBefore + 1, dlg.ErrorCount);
        Assert.Contains("금고 밖이나 앱이 쓰는 영역은 열 수 없습니다", dlg.LastErrorMessage);
    }

    /// 지운 비밀(.trash)과 바꾸기 전 값(.history)이 들어 있는 곳은 어떤 경로로도 열지 않는다.
    [Theory]
    [InlineData(@".trash\20260101-000000\aws")]
    [InlineData(@".history\aws\운영키.tbx")]
    public async Task 예약_영역은_런처를_부르지_않는다(string relative)
    {
        using var vault = new TempVault();
        var reserved = vault.Dir(relative);
        var (shell, dlg, explorer) = await Build(vault);

        shell.Selected = new TreeNodeViewModel(new TreeNode(reserved, "예약", true, []));
        await Run(shell);

        Assert.Empty(explorer.Calls);
        Assert.Equal(1, dlg.ErrorCount);
        Assert.DoesNotContain("운영키", dlg.LastErrorMessage!);
        Assert.DoesNotContain(vault.Root, dlg.LastErrorMessage!);
    }

    /// 알림에 경로나 이름을 싣지 않는다 (PROHIBITED-CUSTOM-04 취지) — 화면 공유 중에도 새지 않게.
    [Fact]
    public async Task 알림_문구에_경로와_이름이_없다()
    {
        using var vault = new TempVault();
        await CreateTbx(vault, @"은행\비밀계좌.tbx");
        var (shell, dlg, explorer) = await Build(vault);
        shell.Selected = ShellFixture.FindNode(shell.Roots, "비밀계좌");
        explorer.Result = false;

        await Run(shell);

        // 알림이 아예 안 떠도 아래 단언은 통과한다 — 떴다는 것부터 고정한다
        Assert.NotNull(dlg.LastErrorMessage);
        Assert.DoesNotContain("비밀계좌", dlg.LastErrorMessage);
        Assert.DoesNotContain("은행", dlg.LastErrorMessage);
        Assert.DoesNotContain(vault.Root, dlg.LastErrorMessage);
    }

    /// <summary>
    /// CanExecute 를 두지 않았다 — 트리 메뉴에서 한 번 비활성으로 열리면 조건이 바뀌어도
    /// 다시 열 때 재평가되지 않아 굳는다 [실측 프로브]. 어떤 상태에서도 실행 가능해야 한다.
    /// </summary>
    [Fact]
    public async Task 어떤_선택_상태에서도_실행_가능하다()
    {
        using var vault = new TempVault();
        vault.Dir("aws");
        var (shell, _, _) = await Build(vault);

        shell.Selected = null;
        Assert.True(shell.OpenInExplorerCommand.CanExecute(null));

        shell.Selected = ShellFixture.FindNode(shell.Roots, "aws");
        Assert.True(shell.OpenInExplorerCommand.CanExecute(null));
    }
}

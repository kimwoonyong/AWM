using TextBean.Services;
using static TextBean.Tests.ShellFixture;

namespace TextBean.Tests;

/// <summary>
/// 탭이 여럿이면 이름변경·이동·삭제가 "보고 있지 않은 문서"를 건드린다.
/// 배경 탭은 예약된 자동 저장을 든 채 옛 경로를 가리키고 있어, 놓치면 지운 문서가 되살아난다 (F-4).
/// </summary>
public class TabPathPropagationTests
{
    [Fact]
    public async Task 배경_탭의_문서를_이름변경해도_옛_자리에_되살아나지_않는다()
    {
        using var vault = new TempVault();
        var (shell, dlg, store, factory) = await BuildWithFactoryAsync(vault);
        var secret = Path.Combine(vault.Root, "비밀.tbx");
        var other = Path.Combine(vault.Root, "다른.tbx");
        await store.CreateAsync(secret);
        await store.CreateAsync(other);
        await shell.RefreshAsync();

        await shell.OpenAsync(secret);
        shell.ActiveTab!.Text = "배경 탭에서 고친 값";
        await shell.OpenAsync(other);                      // 비밀.tbx 는 배경 탭이 된다

        shell.Selected = FindNode(shell.Roots, "비밀");
        await Settle(shell);
        dlg.PromptTextResult = "새이름";
        shell.RenameCommand.Execute(null);
        await Settle(shell);

        // 예약된 저장이 살아 있으면 1.5초 뒤 옛 경로로 나가 유령 문서를 만든다
        factory.Timers[0].Fire();
        await Task.Delay(50);

        Assert.False(File.Exists(secret));
        Assert.Equal("배경 탭에서 고친 값",
            (await store.LoadAsync(Path.Combine(vault.Root, "새이름.tbx"))).Text);
    }

    [Fact]
    public async Task 폴더를_옮기면_그_안_모든_탭의_경로가_따라간다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        vault.Dir("폴더A");
        vault.Dir("폴더B");
        var one = Path.Combine(vault.Root, "폴더A", "문서1.tbx");
        var two = Path.Combine(vault.Root, "폴더A", "문서2.tbx");
        await store.CreateAsync(one);
        await store.CreateAsync(two);
        await shell.RefreshAsync();

        await shell.OpenAsync(one);
        await shell.OpenAsync(two);

        Assert.True(await shell.TryMoveAsync(Path.Combine(vault.Root, "폴더A"), Path.Combine(vault.Root, "폴더B")));

        // 한 탭만 갱신하면 나머지는 옛 자리에 저장해 유령 문서를 만든다
        Assert.Equal(Path.Combine(vault.Root, "폴더B", "폴더A", "문서1.tbx"), shell.Tabs[0].CurrentPath);
        Assert.Equal(Path.Combine(vault.Root, "폴더B", "폴더A", "문서2.tbx"), shell.Tabs[1].CurrentPath);
    }

    [Fact]
    public async Task 배경_탭을_수정한_채_옮겨도_새_경로에_저장된다()
    {
        using var vault = new TempVault();
        var (shell, _, store, factory) = await BuildWithFactoryAsync(vault);
        var doc = Path.Combine(vault.Root, "정보.tbx");
        var other = Path.Combine(vault.Root, "다른.tbx");
        await store.CreateAsync(doc);
        await store.CreateAsync(other);
        var folder = vault.Dir("폴더1");
        await shell.RefreshAsync();

        await shell.OpenAsync(doc);
        shell.ActiveTab!.Text = "옮기기 직전 값";
        await shell.OpenAsync(other);

        // 자동 저장 앱에서 "저장 안 해서 못 옮김"은 이해가 안 된다 — 먼저 저장하고 옮긴다 (J-1)
        Assert.True(await shell.TryMoveAsync(doc, folder));

        factory.Timers[0].Fire();
        await Task.Delay(50);

        Assert.False(File.Exists(doc));
        Assert.Equal("옮기기 직전 값", (await store.LoadAsync(Path.Combine(folder, "정보.tbx"))).Text);
    }

    [Fact]
    public async Task 무관한_항목을_옮겨도_다른_탭의_예약_저장이_풀리지_않는다()
    {
        using var vault = new TempVault();
        var (shell, _, store, factory) = await BuildWithFactoryAsync(vault);
        var doc = Path.Combine(vault.Root, "정보.tbx");
        await store.CreateAsync(doc);
        var folder1 = vault.Dir("폴더1");
        var folder2 = vault.Dir("폴더2");
        await shell.RefreshAsync();

        await shell.OpenAsync(doc);
        shell.ActiveTab!.Text = "입력 중";
        Assert.True(factory.Timers[0].IsRunning);

        Assert.True(await shell.TryMoveAsync(folder1, folder2));

        // 무관한 이동이 예약을 죽이면 입력 내용이 저장되지 않는다 (탭 이전부터 있던 버그)
        Assert.True(factory.Timers[0].IsRunning);
    }

    [Fact]
    public async Task 문서를_지우면_그_탭이_닫힌다()
    {
        using var vault = new TempVault();
        var (shell, _, store) = await BuildAsync(vault);
        var a = Path.Combine(vault.Root, "A.tbx");
        var b = Path.Combine(vault.Root, "B.tbx");
        await store.CreateAsync(a);
        await store.CreateAsync(b);
        await shell.RefreshAsync();

        await shell.OpenAsync(b);
        await shell.OpenAsync(a);
        shell.Selected = FindNode(shell.Roots, "A");
        await Settle(shell);

        shell.DeleteCommand.Execute(null);
        await Settle(shell);

        Assert.Single(shell.Tabs);
        Assert.Equal(PathRules.NormalizeFull(b), shell.ActiveTab?.CurrentPath);
    }

    [Fact]
    public async Task 폴더를_지우면_그_안_탭이_모두_닫힌다()
    {
        using var vault = new TempVault();
        var (shell, _, store, factory) = await BuildWithFactoryAsync(vault);
        vault.Dir("폴더A");
        var one = Path.Combine(vault.Root, "폴더A", "문서1.tbx");
        var two = Path.Combine(vault.Root, "폴더A", "문서2.tbx");
        await store.CreateAsync(one);
        await store.CreateAsync(two);
        await shell.RefreshAsync();

        await shell.OpenAsync(one);
        shell.ActiveTab!.Text = "지우기 직전 값";
        await shell.OpenAsync(two);

        shell.Selected = FindNode(shell.Roots, "폴더A");
        await Settle(shell);
        shell.DeleteCommand.Execute(null);
        await Settle(shell);

        Assert.Empty(shell.Tabs);

        // 예약된 저장이 남으면 방금 휴지통으로 보낸 문서를 옛 자리에 되살린다
        factory.Timers[0].Fire();
        await Task.Delay(50);
        Assert.False(File.Exists(one));
    }

    [Fact]
    public async Task 이름변경_대상_탭이_저장에_실패하면_이름이_바뀌지_않는다()
    {
        using var vault = new TempVault();
        var (shell, dlg, store) = await BuildAsync(vault);
        var doc = Path.Combine(vault.Root, "정보.tbx");
        await store.CreateAsync(doc);
        await shell.RefreshAsync();

        await shell.OpenAsync(doc);
        var tab = shell.ActiveTab!;
        tab.Text = "저장 실패할 값";

        shell.Selected = FindNode(shell.Roots, "정보");
        await Settle(shell);

        File.SetAttributes(doc, FileAttributes.ReadOnly);
        try
        {
            dlg.PromptTextResult = "새이름";
            shell.RenameCommand.Execute(null);
            await Settle(shell);

            // 이름을 바꿔 두면 탭이 옛 경로를 쥔 채 남아 저장이 영영 실패한다 (J-1)
            Assert.True(File.Exists(doc));
            Assert.False(File.Exists(Path.Combine(vault.Root, "새이름.tbx")));
            Assert.Equal(1, dlg.ErrorCount);
            Assert.Same(tab, shell.ActiveTab);
        }
        finally { File.SetAttributes(doc, FileAttributes.Normal); }
    }

    [Fact]
    public async Task 원본을_이름변경하면_그_이전_세대_탭은_닫힌다()
    {
        using var vault = new TempVault();
        var (shell, dlg, store) = await BuildAsync(vault);
        var doc = Path.Combine(vault.Root, "정보.tbx");
        await store.CreateAsync(doc);
        await store.SaveAsync(doc, "연 시점 값");
        await shell.RefreshAsync();

        shell.Selected = FindNode(shell.Roots, "정보");
        await Settle(shell);
        shell.OpenBackupCommand.Execute(null);
        await Settle(shell);
        Assert.Equal(2, shell.Tabs.Count);

        dlg.PromptTextResult = "새이름";
        shell.RenameCommand.Execute(null);
        await Settle(shell);

        // 스냅샷 탭은 .history 밑이라 경로 갱신 대상이 아니다. 남겨두면 사라진 자리를 가리킨다 (J-2)
        Assert.Single(shell.Tabs);
        Assert.False(shell.Tabs[0].IsSnapshot);
        Assert.Equal(Path.Combine(vault.Root, "새이름.tbx"), shell.Tabs[0].CurrentPath);
    }
}

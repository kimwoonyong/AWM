using TextBean.Models;
using TextBean.Services;
using TextBean.ViewModels;

namespace TextBean.Tests;

/// <summary>
/// 탭마다 편집기가 생기고 사라지므로 수명이 처음으로 문제가 된다.
/// 해제를 빠뜨리면 닫힌 탭이 1.5초 뒤 디스크에 쓰고, 복호화된 평문이 종료까지 남는다 [실측].
/// </summary>
public class EditorLifetimeTests
{
    private static (EditorViewModel vm, DocumentStore store, FakeAutoSaveTimer timer) Build(TempVault vault)
    {
        var store = new DocumentStore(new TreeService(vault.Root), TestKeys.Codec());
        var timer = new FakeAutoSaveTimer();
        return (new EditorViewModel(store, new FakeClipboard(), new FakeDialogs(), timer), store, timer);
    }

    [Fact]
    public async Task 해제한_편집기는_더_이상_저장하지_않는다()
    {
        using var vault = new TempVault();
        var (vm, store, timer) = Build(vault);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);
        await vm.LoadAsync(path);
        vm.Text = "닫은 뒤에는 저장되면 안 된다";

        vm.Dispose();
        timer.Fire();
        await Task.Delay(50);

        // 닫힌 탭이 1.5초 뒤 디스크에 쓰면 사용자는 닫았다고 믿는다
        Assert.Equal("", (await store.LoadAsync(path)).Text);
    }

    [Fact]
    public async Task 해제_후_타이머가_되살아나도_저장하지_않는다()
    {
        using var vault = new TempVault();
        var (vm, store, timer) = Build(vault);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);
        await vm.LoadAsync(path);
        vm.Text = "값";

        vm.Dispose();

        // AutoSaveTimer.Dispose 는 Stop 한 줄이라 Restart 로 되살아난다 [실측].
        // 편집기 자신이 해제 상태를 알아야 한다.
        timer.Restart();
        timer.Fire();
        await Task.Delay(50);

        Assert.Equal("", (await store.LoadAsync(path)).Text);
    }

    [Fact]
    public async Task 해제한_편집기는_명시적_저장도_거부한다()
    {
        using var vault = new TempVault();
        var (vm, store, _) = Build(vault);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);
        await vm.LoadAsync(path);
        vm.Text = "값";

        vm.Dispose();

        Assert.False(await vm.TrySaveAsync());
        Assert.Equal("", (await store.LoadAsync(path)).Text);
    }

    [Fact]
    public void 두_번_해제해도_터지지_않는다()
    {
        using var vault = new TempVault();
        var (vm, _, _) = Build(vault);

        vm.Dispose();
        vm.Dispose();
    }

    [Theory]
    [InlineData(SaveState.Saved)]
    [InlineData(SaveState.Saving)]
    [InlineData(SaveState.Failed)]
    [InlineData(SaveState.ReadOnly)]
    public async Task 저장_상태가_네_가지로_구분된다(SaveState expected)
    {
        using var vault = new TempVault();
        var (vm, store, _) = Build(vault);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);

        switch (expected)
        {
            case SaveState.Saved:
                await vm.LoadAsync(path);
                break;

            case SaveState.Saving:
                await vm.LoadAsync(path);
                vm.Text = "입력";
                break;

            case SaveState.Failed:
                await vm.LoadAsync(path);
                vm.Text = "입력";
                File.SetAttributes(path, FileAttributes.ReadOnly);
                try { await vm.TrySaveAsync(); }
                finally { File.SetAttributes(path, FileAttributes.Normal); }
                break;

            case SaveState.ReadOnly:
                await vm.LoadAsync(vault.WriteRaw("가짜.tbx", "x"u8.ToArray()));
                break;
        }

        Assert.Equal(expected, vm.SaveState);
    }

    [Fact]
    public async Task 저장_상태가_바뀌면_알림이_나간다()
    {
        using var vault = new TempVault();
        var (vm, store, _) = Build(vault);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);
        await vm.LoadAsync(path);

        var notified = new List<string?>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        vm.Text = "입력";

        // 알림이 없으면 탭 마커가 안 바뀐다
        Assert.Contains(nameof(EditorViewModel.SaveState), notified);
    }

    [Fact]
    public async Task 경로는_정규화되어_들어온다()
    {
        using var vault = new TempVault();
        var (vm, store, _) = Build(vault);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);

        await vm.LoadAsync(Path.Combine(vault.Root, ".", "a.tbx"));

        // 정규화하지 않으면 같은 문서가 탭 두 개로 열린다
        Assert.Equal(PathRules.NormalizeFull(path), vm.CurrentPath);
    }

    [Fact]
    public async Task 탭_제목은_문서명이고_스냅샷은_구분된다()
    {
        using var vault = new TempVault();
        var (vm, store, _) = Build(vault);
        var path = Path.Combine(vault.Root, "비밀번호.tbx");
        await store.CreateAsync(path);
        await store.SaveAsync(path, "값");

        await vm.LoadAsync(path);
        Assert.Equal("비밀번호", vm.TabTitle);
        Assert.False(vm.IsSnapshot);

        await vm.LoadOpenSnapshotAsync(store.SnapshotPathIfExists(path)!, "비밀번호");

        // 스냅샷 탭 제목이 전부 'opened' 로 겹치면 어느 문서 것인지 알 수 없다 [실측]
        Assert.True(vm.IsSnapshot);
        Assert.Contains("비밀번호", vm.TabTitle);
        Assert.DoesNotContain("opened", vm.TabTitle);
    }
}

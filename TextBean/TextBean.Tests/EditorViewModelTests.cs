using TextBean.Models;
using TextBean.Services;
using TextBean.ViewModels;

namespace TextBean.Tests;

public class EditorViewModelTests
{
    private static (EditorViewModel vm, DocumentStore store, FakeClipboard clip, FakeDialogs dlg, FakeAutoSaveTimer timer)
        Build(TempVault vault)
    {
        var store = new DocumentStore(new TreeService(vault.Root), TestKeys.Codec());
        var clip = new FakeClipboard();
        var dlg = new FakeDialogs();
        var timer = new FakeAutoSaveTimer();
        return (new EditorViewModel(store, clip, dlg, timer), store, clip, dlg, timer);
    }

    [Fact]
    public async Task 문서를_열면_본문이_들어오고_수정상태가_아니다()
    {
        using var vault = new TempVault();
        var (vm, store, _, _, _) = Build(vault);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);
        await store.SaveAsync(path, "내용");

        await vm.LoadAsync(path);

        Assert.Equal("내용", vm.Text);
        Assert.False(vm.IsDirty);
        Assert.False(vm.IsReadOnly);
    }

    [Fact]
    public async Task 입력하면_자동_저장이_예약되고_발화하면_저장된다()
    {
        using var vault = new TempVault();
        var (vm, store, _, _, timer) = Build(vault);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);
        await vm.LoadAsync(path);

        vm.Text = "자동 저장될 값";
        Assert.True(timer.IsRunning);          // 입력이 저장을 예약한다

        timer.Fire();
        await Task.Delay(100);                 // 저장은 비동기다

        Assert.False(vm.IsDirty);
        Assert.Equal("자동 저장될 값", (await store.LoadAsync(path)).Text);
    }

    [Fact]
    public async Task 문서를_바꾸면_이전_문서의_예약_저장이_취소된다()
    {
        using var vault = new TempVault();
        var (vm, store, _, _, timer) = Build(vault);
        var pathA = Path.Combine(vault.Root, "a.tbx");
        var pathB = Path.Combine(vault.Root, "b.tbx");
        await store.CreateAsync(pathA);
        await store.CreateAsync(pathB);

        await vm.LoadAsync(pathA);
        vm.Text = "A 내용";
        await vm.ConfirmLeaveAsync();          // 떠나기 전 저장
        await vm.LoadAsync(pathB);

        // 예약이 남아 있으면 A를 겨냥한 저장이 B에 적용된다
        Assert.False(timer.IsRunning);
        timer.Fire();
        await Task.Delay(100);
        Assert.Equal("", (await store.LoadAsync(pathB)).Text);
    }

    [Fact]
    public async Task 읽기에_실패하면_잠기고_자동_저장도_예약되지_않는다()
    {
        using var vault = new TempVault();
        var (vm, _, _, _, timer) = Build(vault);
        var path = vault.WriteRaw("가짜.tbx", "문서가 아님"u8.ToArray());
        var before = await File.ReadAllBytesAsync(path);

        await vm.LoadAsync(path);
        Assert.True(vm.IsReadOnly);
        Assert.NotNull(vm.LockReason);

        vm.Text = "덮어쓰기 시도";

        Assert.False(timer.IsRunning);                             // 잠긴 문서는 저장을 예약하지 않는다
        Assert.False(await vm.TrySaveAsync());
        Assert.Equal(before, await File.ReadAllBytesAsync(path));   // 원본 그대로
    }

    [Fact]
    public async Task 저장에_실패하면_수정상태가_유지되고_상태표시가_알린다()
    {
        using var vault = new TempVault();
        var (vm, store, _, dlg, _) = Build(vault);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);
        await vm.LoadAsync(path);
        vm.Text = "새 내용";

        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Assert.False(await vm.TrySaveAsync());
            Assert.True(vm.IsDirty);                  // 저장됐다고 표시하면 사용자가 앱을 닫고 내용을 잃는다
            Assert.Contains("저장 실패", vm.StatusText);
            Assert.Equal(1, dlg.ErrorCount);          // 명시적 저장은 알린다
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task 자동_저장_실패는_팝업을_띄우지_않고_상태표시로만_알린다()
    {
        using var vault = new TempVault();
        var (vm, store, _, dlg, timer) = Build(vault);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);
        await vm.LoadAsync(path);
        vm.Text = "새 내용";

        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            timer.Fire();
            await Task.Delay(100);

            // 1.5초마다 팝업이 뜨면 앱을 쓸 수 없다. 조용히 실패하지도 않는다.
            Assert.Equal(0, dlg.ErrorCount);
            Assert.Contains("저장 실패", vm.StatusText);
            Assert.True(vm.IsDirty);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task 떠나기_전에_묻지_않고_저장한다()
    {
        using var vault = new TempVault();
        var (vm, store, _, dlg, _) = Build(vault);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);
        await vm.LoadAsync(path);
        vm.Text = "떠나기 전 값";

        Assert.True(await vm.ConfirmLeaveAsync());
        Assert.Equal(0, dlg.ConfirmCount);        // 확인 대화상자가 없다 (D-015)
        Assert.False(vm.IsDirty);
        Assert.Equal("떠나기 전 값", (await store.LoadAsync(path)).Text);
    }

    [Fact]
    public async Task 떠나기_전_저장에_실패하면_false를_돌려준다()
    {
        using var vault = new TempVault();
        var (vm, store, _, _, _) = Build(vault);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);
        await vm.LoadAsync(path);
        vm.Text = "새 내용";

        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            // 전환이 진행되면 편집기 본문이 교체되면서 편집 내용이 사라진다 (D-011)
            Assert.False(await vm.ConfirmLeaveAsync());
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task 선택_영역이_있으면_선택만_없으면_전체를_복사한다()
    {
        using var vault = new TempVault();
        var (vm, store, clip, _, _) = Build(vault);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);
        await store.SaveAsync(path, "첫줄\n둘째줄");
        await vm.LoadAsync(path);

        vm.Copy("둘째줄");
        Assert.Equal("둘째줄", clip.Copied);

        vm.Copy(null);
        Assert.Equal("첫줄\n둘째줄", clip.Copied);
    }

    [Fact]
    public async Task 읽지_못한_문서는_복사도_막는다()
    {
        using var vault = new TempVault();
        var (vm, _, clip, _, _) = Build(vault);

        await vm.LoadAsync(vault.WriteRaw("가짜.tbx", "x"u8.ToArray()));
        vm.Copy(null);

        Assert.Null(clip.Copied);   // 읽지도 못한 내용을 클립보드에 올릴 이유가 없다
    }

    [Fact]
    public async Task 빈_문서에_입력하면_CanCopy가_알림과_함께_켜진다()
    {
        using var vault = new TempVault();
        var (vm, store, _, _, _) = Build(vault);
        var path = Path.Combine(vault.Root, "새문서.tbx");
        await store.CreateAsync(path);
        await vm.LoadAsync(path);

        Assert.False(vm.CanCopy);
        var notified = new List<string?>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        vm.Text = "API_KEY=abc";

        Assert.True(vm.CanCopy);
        Assert.Contains(nameof(EditorViewModel.CanCopy), notified);
    }

    [Fact]
    public async Task UpdatePath는_본문과_수정상태를_건드리지_않는다()
    {
        using var vault = new TempVault();
        var (vm, store, _, _, _) = Build(vault);
        var path = Path.Combine(vault.Root, "정보1.tbx");
        await store.CreateAsync(path);
        await vm.LoadAsync(path);
        vm.Text = "편집 중";

        vm.UpdatePath(Path.Combine(vault.Root, "정보2.tbx"));

        Assert.Equal(Path.Combine(vault.Root, "정보2.tbx"), vm.CurrentPath);
        Assert.Equal("편집 중", vm.Text);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public async Task 문서를_열면_그_시점_스냅샷이_남는다()
    {
        using var vault = new TempVault();
        var (vm, store, _, _, _) = Build(vault);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);
        await store.SaveAsync(path, "연 시점의 값");

        await vm.LoadAsync(path);

        // 자동 저장은 실수를 1.5초 만에 확정하고, 문서를 바꾸면 실행취소 스택도 비워진다.
        // 이 한 벌이 유일하게 되돌릴 지점이다 (D-016).
        Assert.NotNull(store.SnapshotPathIfExists(path));
    }

    [Fact]
    public async Task 열었을_때_상태는_읽기_전용으로_열리되_복사는_된다()
    {
        using var vault = new TempVault();
        var (vm, store, clip, _, _) = Build(vault);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);
        await store.SaveAsync(path, "연 시점의 값");

        await vm.LoadAsync(path);
        vm.Text = "그 뒤에 망친 값";
        await vm.TrySaveAsync();

        await vm.LoadOpenSnapshotAsync(store.SnapshotPathIfExists(path)!, "a");

        Assert.Equal("연 시점의 값", vm.Text);
        Assert.True(vm.IsReadOnly);                        // 스냅샷에 저장하는 일은 없다
        Assert.False(await vm.TrySaveAsync());

        // 읽기 전용이어도 복사는 돼야 한다 — 복사해 쓰라고 여는 기능이다
        vm.Copy(null);
        Assert.Equal("연 시점의 값", clip.Copied);
    }

    [Fact]
    public async Task 열었을_때_상태를_봐도_원본은_그대로다()
    {
        using var vault = new TempVault();
        var (vm, store, _, _, _) = Build(vault);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);
        await store.SaveAsync(path, "연 시점");
        await vm.LoadAsync(path);
        vm.Text = "나중 값";
        await vm.TrySaveAsync();

        await vm.LoadOpenSnapshotAsync(store.SnapshotPathIfExists(path)!, "a");
        await vm.TrySaveAsync();          // 차단돼야 한다

        Assert.Equal("나중 값", (await store.LoadAsync(path)).Text);
    }
}

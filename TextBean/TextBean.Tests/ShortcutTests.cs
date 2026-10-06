using System.Windows.Input;
using TextBean.Services;
using TextBean.ViewModels;
using TextBean.Views.Dialogs;
using static TextBean.Tests.WpfTestHost;

namespace TextBean.Tests;

/// 단축키 보기 · 바꾸기 (add-shortcut-settings, D-110~D-114).
[Collection(WpfScreenCollection.Name)]
public class ShortcutTests
{
    // ── 동작 목록 · 검사 ─────────────────────────────────────────────────────

    [Fact]
    public void 기본_키는_지금까지_쓰던_키다()
    {
        var defaults = ShortcutCatalog.Defaults;

        Assert.Equal("Ctrl+S", defaults["save"]);
        Assert.Equal("Ctrl+Shift+F", defaults["searchVault"]);
        Assert.Equal("Esc", defaults["closeFindBar"]);
        Assert.Equal("F1", defaults["shortcuts"]);
        Assert.Equal("Ctrl+Q", defaults["exit"]);
        Assert.Null(defaults["lock"]);
        Assert.All(defaults.Values.OfType<string>(), key => Assert.Null(ShortcutCatalog.Reject(key)));
    }

    [Theory]
    [InlineData("Ctrl+C")] [InlineData("Ctrl+V")] [InlineData("Ctrl+Z")] [InlineData("Ctrl+A")]
    [InlineData("Enter")] [InlineData("Ctrl+B")] [InlineData("Ctrl+Shift+X")] [InlineData("Ctrl+E")] [InlineData("Shift+Left")] [InlineData("Ctrl+Backspace")] [InlineData("Shift+PageDown")]
    [InlineData("Ctrl+Tab")] [InlineData("Ctrl+PageUp")] [InlineData("Alt+F4")]
    [InlineData("S")] [InlineData("Shift+S")] [InlineData("5")]
    public void 본문이_쓰는_키와_수식키_없는_글자는_고를_수_없다(string key)
        => Assert.NotNull(ShortcutCatalog.Reject(key));

    [Theory]
    [InlineData("Ctrl+K")] [InlineData("Ctrl+G")] [InlineData("F7")] [InlineData("Ctrl+Shift+K")] [InlineData("Alt+1")]
    public void 비어_있는_키는_고를_수_있다(string key) => Assert.Null(ShortcutCatalog.Reject(key));

    [Fact]
    public void 저장된_값은_그_줄만_검사해_잘못되면_기본_키로_되돌린다()
    {
        var invalid = new List<string>();
        var map = ShortcutCatalog.Resolve(new Dictionary<string, string>
        {
            ["closeTab"] = "Ctrl+K",      // 그대로
            ["save"] = "",                // 없음
            ["refresh"] = "Ctrl+C",       // 막힌 키 → 기본 F5
            ["lock"] = "Ctrl+K",          // 목록에서 앞선 '탭 닫기'와 겹침 → 기본(없음)
            ["nope"] = "Ctrl+K",          // 모르는 동작 — 무시
        }, invalid.Add);

        Assert.Equal("Ctrl+K", map["closeTab"]);
        Assert.Null(map["save"]);
        Assert.Equal("F5", map["refresh"]);
        Assert.Null(map["lock"]);
        Assert.Equal(["lock", "refresh"], invalid.Order());
        Assert.False(map.ContainsKey("nope"));
    }

    // ── 키 글자 ↔ WPF 키 ─────────────────────────────────────────────────────

    [Fact]
    public void 누른_키는_막는_키_목록과_같은_이름으로_적힌다()
    {
        Assert.Equal("Ctrl+Shift+F", ShortcutsDialog.ToText(Key.F, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.Equal("Esc", ShortcutsDialog.ToText(Key.Escape, ModifierKeys.None));
        Assert.Equal("Ctrl+1", ShortcutsDialog.ToText(Key.D1, ModifierKeys.Control));

        // 이름이 둘인 키(Next/PageDown · Return/Enter · Back)가 막는 키와 맞아야 거부된다
        Assert.NotNull(ShortcutCatalog.Reject(ShortcutsDialog.ToText(Key.Next, ModifierKeys.Shift)));
        Assert.NotNull(ShortcutCatalog.Reject(ShortcutsDialog.ToText(Key.Return, ModifierKeys.None)));
        Assert.NotNull(ShortcutCatalog.Reject(ShortcutsDialog.ToText(Key.Back, ModifierKeys.Control)));
    }

    [Fact]
    public void 키_글자는_WPF_단축키로_돌아가고_못_읽으면_null()
    {
        var gesture = ShortcutsDialog.ToGesture("Ctrl+Shift+F")!;
        Assert.Equal((Key.F, ModifierKeys.Control | ModifierKeys.Shift), (gesture.Key, gesture.Modifiers));
        Assert.Equal(Key.Escape, ShortcutsDialog.ToGesture("Esc")!.Key);
        Assert.Equal(Key.D1, ShortcutsDialog.ToGesture("Ctrl+1")!.Key);

        Assert.Null(ShortcutsDialog.ToGesture("Hyper+X"));
        Assert.Null(ShortcutsDialog.ToGesture("Ctrl+NoSuchKey"));
        Assert.All(ShortcutCatalog.Defaults.Values.OfType<string>(), key => Assert.NotNull(ShortcutsDialog.ToGesture(key)));
    }

    // ── 목록 창 ViewModel ────────────────────────────────────────────────────

    [Fact]
    public async Task 목록_창에서_키를_바꾸면_막고_겹침을_알리고_저장해_바로_적용한다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await ShellFixture.BuildAsync(vault);
        var changed = 0;
        shell.ShortcutsChanged += (_, _) => changed++;
        var vm = new ShortcutsViewModel(shell);
        var lockRow = vm.Rows.Single(r => r.Action.Id == "lock");

        Assert.False(vm.ChangeCommand.CanExecute(null));          // 줄을 고르기 전
        vm.Selected = lockRow;

        vm.ChangeCommand.Execute(null);
        Assert.True(vm.IsCapturing);
        Assert.False(await vm.CaptureAsync("Ctrl+S"));
        Assert.Contains("'지금 탭 저장'", vm.Message);              // 겹침 — 누가 쓰는지
        Assert.False(vm.IsCapturing);

        vm.ChangeCommand.Execute(null);
        Assert.False(await vm.CaptureAsync("Ctrl+C"));
        Assert.Contains("고를 수 없습니다", vm.Message);

        vm.ChangeCommand.Execute(null);
        Assert.False(await vm.CaptureAsync("Esc"));                // 취소
        Assert.Null(lockRow.Key);
        Assert.Equal(0, changed);

        vm.ChangeCommand.Execute(null);
        Assert.True(await vm.CaptureAsync("Ctrl+K"));
        Assert.Equal("", vm.Message);
        Assert.Equal("Ctrl+K", shell.Shortcuts["lock"]);
        Assert.Equal(1, changed);

        // 다시 켜도 남는다
        var settings = new AppSettingsService(Path.Combine(vault.Root, "settings.json"));
        await settings.LoadAsync();
        Assert.Equal("Ctrl+K", settings.Current.Shortcuts!["lock"]);
        shell.Dispose();
    }

    [Fact]
    public async Task 초기화는_전체를_처음_키로_되돌린다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await ShellFixture.BuildAsync(vault);
        var vm = new ShortcutsViewModel(shell);
        vm.Selected = vm.Rows.Single(r => r.Action.Id == "exit");
        vm.ChangeCommand.Execute(null);
        Assert.True(await vm.CaptureAsync("Ctrl+Shift+Q"));

        vm.ResetCommand.Execute(null);
        await Task.Delay(50);

        Assert.Equal(ShortcutCatalog.Defaults, shell.Shortcuts);
        Assert.All(vm.Rows, r => Assert.Equal(r.Action.DefaultKey, r.Key));
        shell.Dispose();
    }

    /// 창은 띄우지 않는다 — 줄의 두 번 누르기 처리기를 직접 부른다.
    [Fact]
    public void 줄을_두_번_누르면_그_줄의_키를_받기_시작한다() => Run(() =>
    {
        using var vault = new TempVault();
        var (shell, _, _) = Wait(ShellFixture.BuildAsync(vault));
        var vm = new ShortcutsViewModel(shell);
        var dialog = new ShortcutsDialog { DataContext = vm };
        var lockRow = vm.Rows.Single(r => r.Action.Id == "lock");

        var handler = typeof(ShortcutsDialog).GetMethod("OnRowDoubleClick",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = System.Windows.Controls.Control.MouseDoubleClickEvent };
        handler.Invoke(dialog, [new System.Windows.Controls.ListViewItem { DataContext = lockRow }, args]);

        Assert.Same(lockRow, vm.Selected);
        Assert.True(lockRow.IsCapturing);
        Assert.True(args.Handled);
        dialog.Close();
        shell.Dispose();
    });

    [Fact]
    public async Task F1_명령은_목록_창을_요청한다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await ShellFixture.BuildAsync(vault);
        var asked = 0;
        shell.ShortcutsRequested += (_, _) => asked++;

        shell.CommandFor("shortcuts")!.Execute(null);

        Assert.Equal(1, asked);
        Assert.All(ShortcutCatalog.Actions, a => Assert.NotNull(shell.CommandFor(a.Id)));   // 모든 동작에 명령이 있다
        shell.Dispose();
    }

    // ── 창에 적용 ────────────────────────────────────────────────────────────

    [Fact]
    public void 창은_키_표로_단축키를_만들고_바꾸면_옛_키를_지운다() => Run(() =>
    {
        using var scene = TabScene.Open(1);
        KeyBinding? Bound(Key key, ModifierKeys modifiers)
            => scene.Window.InputBindings.OfType<KeyBinding>().SingleOrDefault(b => b.Key == key && b.Modifiers == modifiers);

        Assert.Same(scene.Shell.ExitCommand, Bound(Key.Q, ModifierKeys.Control)?.Command);
        Assert.Same(scene.Shell.OpenShortcutsCommand, Bound(Key.F1, ModifierKeys.None)?.Command);
        Assert.Equal(ShortcutCatalog.Defaults.Values.Count(k => k is not null), scene.Window.InputBindings.Count);
        Assert.Contains("(Ctrl+Q)", scene.Shell.ExitToolTip);

        var map = new Dictionary<string, string?>(scene.Shell.Shortcuts) { ["exit"] = "Ctrl+Shift+Q", ["lock"] = "Ctrl+K" };
        Wait(scene.Shell.SetShortcutsAsync(map));

        Assert.Null(Bound(Key.Q, ModifierKeys.Control));
        Assert.Same(scene.Shell.ExitCommand, Bound(Key.Q, ModifierKeys.Control | ModifierKeys.Shift)?.Command);
        Assert.Same(scene.Shell.LockCommand, Bound(Key.K, ModifierKeys.Control)?.Command);
        Assert.Contains("(Ctrl+Shift+Q)", scene.Shell.ExitToolTip);
    });
}

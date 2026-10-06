using System.Security.Cryptography;
using TextBean.Models;
using TextBean.Services;
using TextBean.ViewModels;
using static TextBean.Tests.ShellFixture;

namespace TextBean.Tests;

/// <summary>
/// 화면 흐름: 시작 · 키 변경 · 잠그기 · 오타 방지 · 전환 중 차단 (plan §2-7).
/// 사용자 요구 "test1(1234)·test2(5678) 에서 키를 바꾸면 열리는 문서가 뒤바뀐다"를 셸 수준에서 본다.
/// </summary>
public class KeyFlowTests
{
    private const string KeyA = "first-key-1234";
    private const string KeyB = "second-key-5678";

    private static async Task<string> WriteDoc(TempVault vault, string name, string key, string body)
    {
        var path = Path.Combine(vault.Root, name + ".tbx");
        await File.WriteAllBytesAsync(path, TestKeys.Codec(key).EncryptForNew(body));
        return path;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    // ── 시작 (K-17) ───────────────────────────────────────────────────────────

    [Fact]
    public async Task 빈_금고로_시작하면_처음_키를_두_번_입력받고_고지한다()
    {
        using var vault = new TempVault();
        var (shell, dlg, _, keys, _) = await BuildWithKeysAsync(vault);
        await shell.RefreshAsync();
        dlg.KeyEntries.Enqueue(new KeyEntry(KeyA, KeyA));

        await shell.StartAsync();

        Assert.True(keys.HasKey);
        Assert.Equal("처음 키 정하기", dlg.KeyPromptTitles.Single());
        Assert.Contains("잊으면", dlg.KeyPromptMessages.Single());
    }

    [Fact]
    public async Task 처음_키는_규칙과_두_번_입력이_맞아야_한다()
    {
        using var vault = new TempVault();
        var (shell, dlg, _, keys, _) = await BuildWithKeysAsync(vault);
        await shell.RefreshAsync();
        dlg.KeyEntries.Enqueue(new KeyEntry("short", "short"));            // 8자 미만
        dlg.KeyEntries.Enqueue(new KeyEntry(KeyA, KeyA + "x"));            // 두 번이 다름
        dlg.KeyEntries.Enqueue(new KeyEntry("비밀번호12345", "비밀번호12345")); // 한글
        dlg.KeyEntries.Enqueue(new KeyEntry(KeyA, KeyA));

        await shell.StartAsync();

        Assert.True(keys.HasKey);
        Assert.Equal(4, dlg.KeyPromptTitles.Count);
        Assert.Equal(3, dlg.ErrorCount);
    }

    [Fact]
    public async Task 새_방식_문서가_있으면_키를_한_번_입력받고_맞는_문서를_센다()
    {
        using var vault = new TempVault();
        await WriteDoc(vault, "test1", KeyA, "A");
        var (shell, dlg, _, keys, _) = await BuildWithKeysAsync(vault);
        await shell.RefreshAsync();
        dlg.KeyEntries.Enqueue(new KeyEntry(KeyA, null));

        await shell.StartAsync();

        Assert.True(keys.HasKey);
        Assert.Equal("키 입력", dlg.KeyPromptTitles.Single());
        Assert.Equal(0, dlg.ConfirmCount);                           // 맞는 문서가 있으면 묻지 않는다
        Assert.Equal(DocumentKeyState.Matches, FindNode(shell.Roots, "test1").KeyState);
        Assert.Contains("1/1", shell.KeyStatusText);
    }

    [Fact]
    public async Task 어느_문서와도_안_맞는_키는_오타일_수_있어_확인하고_취소하면_키를_쓰지_않는다()
    {
        using var vault = new TempVault();
        await WriteDoc(vault, "test1", KeyA, "A");
        var (shell, dlg, _, keys, _) = await BuildWithKeysAsync(vault);
        await shell.RefreshAsync();
        dlg.KeyEntries.Enqueue(new KeyEntry("first-key-1243", null));     // 오타
        dlg.ConfirmResult = false;

        await shell.StartAsync();

        Assert.False(keys.HasKey);
        Assert.Contains("하나도 없습니다", dlg.ConfirmMessages.Single());
        Assert.Equal(DocumentKeyState.NoKey, FindNode(shell.Roots, "test1").KeyState);
    }

    // ── 키 바꿔 끼우기 (K-1 셸 수준) ──────────────────────────────────────────

    [Fact]
    public async Task 키를_바꾸면_트리의_열리는_문서가_뒤바뀌고_파일은_그대로다()
    {
        using var vault = new TempVault();
        var test1 = await WriteDoc(vault, "test1", KeyA, "test1 내용");
        var test2 = await WriteDoc(vault, "test2", KeyB, "test2 내용");
        var before = (Hash(test1), Hash(test2));
        var (shell, dlg, _, _, _) = await BuildWithKeysAsync(vault);
        await shell.RefreshAsync();
        dlg.KeyEntries.Enqueue(new KeyEntry(KeyA, null));
        await shell.StartAsync();

        Assert.Equal(DocumentKeyState.Matches, FindNode(shell.Roots, "test1").KeyState);
        Assert.Equal(DocumentKeyState.DifferentKey, FindNode(shell.Roots, "test2").KeyState);
        Assert.True(FindNode(shell.Roots, "test2").IsDimmed);

        dlg.KeyEntries.Enqueue(new KeyEntry(KeyB, null));
        shell.ChangeKeyCommand.Execute(null);
        await Settle(shell);

        Assert.Equal(DocumentKeyState.DifferentKey, FindNode(shell.Roots, "test1").KeyState);
        Assert.Equal(DocumentKeyState.Matches, FindNode(shell.Roots, "test2").KeyState);
        Assert.Equal(before, (Hash(test1), Hash(test2)));
    }

    [Fact]
    public async Task 키를_바꾸면_열린_문서를_옛_키로_저장하고_닫는다()
    {
        using var vault = new TempVault();
        var doc = await WriteDoc(vault, "A문서", KeyA, "처음 값");
        await File.WriteAllTextAsync(Path.Combine(vault.Root, "참고.txt"), "평문");
        var (shell, dlg, _, _, _) = await BuildWithKeysAsync(vault, TestKeys.Service(KeyA));
        await shell.RefreshAsync();
        await shell.OpenAsync(doc);
        await shell.OpenAsync(Path.Combine(vault.Root, "참고.txt"));
        shell.Tabs.First(t => !t.IsPlainText).Text = "고친 값";

        dlg.KeyEntries.Enqueue(new KeyEntry(KeyB, null));
        shell.ChangeKeyCommand.Execute(null);
        await Settle(shell);

        Assert.Single(shell.Tabs);
        Assert.True(shell.Tabs[0].IsPlainText);                      // 키와 무관한 평문 탭은 둔다
        Assert.Equal("고친 값", TestKeys.Codec(KeyA).Decrypt(await File.ReadAllBytesAsync(doc)).Text);
    }

    [Fact]
    public async Task 키_전환을_취소하면_아무것도_바뀌지_않는다()
    {
        using var vault = new TempVault();
        var doc = await WriteDoc(vault, "A문서", KeyA, "값");
        var (shell, dlg, _, keys, _) = await BuildWithKeysAsync(vault, TestKeys.Service(KeyA));
        await shell.RefreshAsync();
        await shell.OpenAsync(doc);
        var generation = keys.Generation;

        dlg.KeyEntries.Enqueue(new KeyEntry(KeyB, null));
        dlg.ConfirmResult = false;
        shell.ChangeKeyCommand.Execute(null);
        await Settle(shell);

        Assert.Equal(generation, keys.Generation);
        Assert.Single(shell.Tabs);
    }

    // ── 잠그기 (K-18) ─────────────────────────────────────────────────────────

    [Fact]
    public async Task 잠그면_탭을_닫고_클립보드와_검색_창을_비우고_키를_버린다()
    {
        using var vault = new TempVault();
        var doc = await WriteDoc(vault, "A문서", KeyA, "AKIA 값");
        var (shell, _, _, keys, clipboard) = await BuildWithKeysAsync(vault, TestKeys.Service(KeyA));
        var search = new SearchViewModel(shell) { Query = "AKIA" };
        search.Results.Add(new SearchHit(doc, "A문서", false, 1, 0));
        await shell.RefreshAsync();
        await shell.OpenAsync(doc);

        // 앱에서는 편집기와 셸이 같은 클립보드 서비스를 쓴다. 시험용 편집기 팩터리는 따로 만들어 직접 넣는다.
        clipboard.Copy("AKIA 값");

        shell.LockCommand.Execute(null);
        await Settle(shell);

        Assert.False(keys.HasKey);
        Assert.Empty(shell.Tabs);
        Assert.Null(clipboard.Copied);
        Assert.Empty(search.Results);
        Assert.Equal("", search.Query);
        Assert.Equal(DocumentKeyState.NoKey, FindNode(shell.Roots, "A문서").KeyState);
        Assert.Equal("키 없음", shell.KeyStatusText);
    }

    /// 사용자 확인(2026-09-30): 한 번 입력하는 창에서 1234 가 받아들여졌다. 이 앱은 8자보다 짧은 키로 문서를 잠그지 않아
    /// 그런 키로 열리는 문서가 없다 — 받아 주면 아무것도 안 열리는 키가 켜진다.
    [Fact]
    public async Task 키_입력_창도_8자_미만은_바로_다시_묻는다()
    {
        using var vault = new TempVault();
        await WriteDoc(vault, "A문서", KeyA, "A 값");
        var (shell, dlg, _, keys, _) = await BuildWithKeysAsync(vault);
        await shell.RefreshAsync();

        dlg.KeyEntries.Enqueue(new KeyEntry("1234", null));
        dlg.KeyEntries.Enqueue(new KeyEntry(" " + KeyA, null));      // 앞뒤 공백 — 그런 키로 잠긴 문서도 없다
        dlg.KeyEntries.Enqueue(new KeyEntry(KeyA, null));
        shell.ChangeKeyCommand.Execute(null);
        await Settle(shell);

        Assert.Equal(new[] { "키 입력", "키 입력", "키 입력" }, dlg.KeyPromptTitles);
        Assert.Equal(0, dlg.ConfirmCount);                            // 짧은 키를 "쓸까요?"로 묻지 않고 바로 막았다
        Assert.True(keys.Matches(KeyA));
        Assert.Equal(DocumentKeyState.Matches, FindNode(shell.Roots, "A문서").KeyState);
    }

    [Fact]
    public async Task 키_입력_창의_8자_미만_안내는_새로_정하라는_말이_아니다()
    {
        using var vault = new TempVault();
        await WriteDoc(vault, "A문서", KeyA, "A 값");
        var (shell, dlg, _, _, _) = await BuildWithKeysAsync(vault);
        await shell.RefreshAsync();

        dlg.KeyEntries.Enqueue(new KeyEntry("1234", null));
        shell.ChangeKeyCommand.Execute(null);
        await Settle(shell);

        Assert.Contains("열리는 문서가 없습니다", dlg.LastErrorMessage);
    }

    /// 잠근 뒤 "키 변경"을 눌러야 풀리면 무엇을 눌러야 하는지 헷갈린다 (사용자 확인 2026-09-30).
    [Fact]
    public async Task 잠그면_버튼_이름이_키_입력이_되고_키를_넣으면_키_변경으로_돌아온다()
    {
        using var vault = new TempVault();
        await WriteDoc(vault, "A문서", KeyA, "A 값");
        var (shell, dlg, _, _, _) = await BuildWithKeysAsync(vault, TestKeys.Service(KeyA));
        await shell.RefreshAsync();
        var raised = new List<string?>();
        shell.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.Equal("키 변경", shell.ChangeKeyLabel);

        shell.LockCommand.Execute(null);
        await Settle(shell);
        Assert.Equal("키 입력", shell.ChangeKeyLabel);
        Assert.Contains(nameof(ShellViewModel.ChangeKeyLabel), raised);

        dlg.KeyEntries.Enqueue(new KeyEntry(KeyA, null));
        shell.ChangeKeyCommand.Execute(null);
        await Settle(shell);

        Assert.Equal("키 입력", dlg.KeyPromptTitles.Single());           // 창 제목도 버튼과 같은 말
        Assert.Equal("키 변경", shell.ChangeKeyLabel);
    }

    // ── 오타 방지 (K-7) ───────────────────────────────────────────────────────

    [Fact]
    public async Task 맞는_문서가_없는_키로_첫_문서를_만들면_다시_입력해야_하고_그_뒤는_묻지_않는다()
    {
        using var vault = new TempVault();
        var (shell, dlg, _, _, _) = await BuildWithKeysAsync(vault, TestKeys.Service(KeyA, inUse: false));
        await shell.RefreshAsync();

        dlg.PromptTextResult = "틀린재입력";
        dlg.KeyEntries.Enqueue(new KeyEntry("first-key-1243", null));
        shell.NewDocumentCommand.Execute(null);
        await Settle(shell);
        Assert.False(File.Exists(Path.Combine(vault.Root, "틀린재입력.tbx")));

        dlg.PromptTextResult = "첫문서";
        dlg.KeyEntries.Enqueue(new KeyEntry(KeyA, null));
        shell.NewDocumentCommand.Execute(null);
        await Settle(shell);
        Assert.True(File.Exists(Path.Combine(vault.Root, "첫문서.tbx")));

        dlg.PromptTextResult = "둘째";
        shell.NewDocumentCommand.Execute(null);
        await Settle(shell);

        Assert.True(File.Exists(Path.Combine(vault.Root, "둘째.tbx")));
        Assert.Equal(2, dlg.KeyPromptTitles.Count);                  // 둘째 문서는 묻지 않았다

        // 세 문서가 아니라 두 문서지만 salt 는 하나다 — 문서마다 새 salt 가 생기지 않는다
        var salts = Directory.GetFiles(vault.Root, "*.tbx")
            .Select(p => Convert.ToHexString(File.ReadAllBytes(p), 10, 16)).Distinct();
        Assert.Single(salts);
    }

    [Fact]
    public async Task 키가_없으면_새_문서를_만들지_않는다()
    {
        using var vault = new TempVault();
        var (shell, dlg, _, _, _) = await BuildWithKeysAsync(vault);
        await shell.RefreshAsync();

        dlg.PromptTextResult = "문서";
        shell.NewDocumentCommand.Execute(null);
        await Settle(shell);

        Assert.Empty(Directory.GetFiles(vault.Root, "*.tbx"));
        Assert.Equal("키 없음", dlg.LastErrorTitle);
    }

    /// 금고를 바꾸면 새 금고로 다시 판정한다. 옛 금고의 "맞는 문서"가 남으면 새 금고에서 첫 문서 재입력이 빠진다.
    [Fact]
    public async Task 금고_폴더를_바꾸면_새_금고로_다시_판정해_첫_문서_재입력이_살아난다()
    {
        using var vault = new TempVault();
        var (shell, dlg, _, keys, _) = await BuildWithKeysAsync(vault, TestKeys.Service(KeyA));
        await shell.RefreshAsync();
        var generation = keys.Generation;

        shell.ChangeRootCommand.Execute(null);        // 가짜 대화상자는 같은 폴더를 돌려준다 — 빈 금고
        await Settle(shell);
        Assert.NotEqual(generation, keys.Generation);

        dlg.PromptTextResult = "새금고문서";
        shell.NewDocumentCommand.Execute(null);
        await Settle(shell);

        Assert.Equal("키 확인", dlg.KeyPromptTitles.Single());
    }

    // ── 저장 거부 뒤 빠져나갈 길 (K-4 셸 수준) ──────────────────────────────────

    [Fact]
    public async Task 키가_바뀌어_저장할_수_없는_탭은_두_번_확인하고_버린_뒤_닫는다()
    {
        using var vault = new TempVault();
        var doc = await WriteDoc(vault, "A문서", KeyA, "원래 값");
        var (shell, dlg, _, keys, _) = await BuildWithKeysAsync(vault, TestKeys.Service(KeyA));
        await shell.RefreshAsync();
        await shell.OpenAsync(doc);
        var before = Hash(doc);

        keys.Activate(keys.Evaluate(KeyB, []));       // 탭을 닫지 않고 키가 바뀐 경우
        shell.Tabs[0].Text = "B 상태에서 친 값";

        dlg.ConfirmResult = false;
        Assert.False(await shell.CloseTabAsync(shell.Tabs[0]));
        Assert.Single(shell.Tabs);                                   // 버리지 않으면 닫지 않는다

        dlg.ConfirmResult = true;
        var confirmsBefore = dlg.ConfirmCount;
        Assert.True(await shell.CloseTabAsync(shell.Tabs[0]));

        Assert.Empty(shell.Tabs);
        Assert.Equal(2, dlg.ConfirmCount - confirmsBefore);          // 두 번 확인
        Assert.Equal(before, Hash(doc));                             // 다른 키로 다시 잠그지 않았다
    }

    [Fact]
    public async Task 저장할_수_없는_탭이_있어도_버리기로_하면_종료가_막히지_않는다()
    {
        using var vault = new TempVault();
        var doc = await WriteDoc(vault, "A문서", KeyA, "원래 값");
        var (shell, _, _, keys, _) = await BuildWithKeysAsync(vault, TestKeys.Service(KeyA));
        await shell.RefreshAsync();
        await shell.OpenAsync(doc);
        keys.Activate(keys.Evaluate(KeyB, []));
        shell.Tabs[0].Text = "값";

        Assert.True(await shell.SaveAllForExitAsync());
    }

    // ── 전환 중 차단 (K-6 · K-13) ─────────────────────────────────────────────

    [Fact]
    public async Task 키를_전환하는_동안에는_열기_만들기_옮기기_검색을_받지_않는다()
    {
        using var vault = new TempVault();
        var docA = await WriteDoc(vault, "A문서", KeyA, "AKIA");
        var docB = await WriteDoc(vault, "B문서", KeyA, "AKIA");
        vault.Dir("폴더");
        var keys = TestKeys.Service(KeyA);
        var tree = new TreeService(vault.Root);
        var gated = new GatedDocumentStore(new DocumentStore(tree, new KeyDocumentCodec(keys)));
        var dlg = new FakeDialogs();
        var settings = new AppSettingsService(Path.Combine(vault.Root, "settings.json"));
        await settings.LoadAsync();
        var shell = new ShellViewModel(settings, dlg, gated, tree, new FakeEditorFactory(gated, dlg),
                                       new SearchService(tree, gated), new FakeExplorerLauncher(), keys, new FakeClipboard());
        await shell.RefreshAsync();
        await shell.OpenAsync(docA);
        shell.Tabs[0].Text = "고친 값";

        gated.Close();                                               // 전환의 "옛 키로 저장"에서 멈춘다
        dlg.KeyEntries.Enqueue(new KeyEntry(KeyB, null));
        shell.ChangeKeyCommand.Execute(null);
        var switching = shell.Pending;
        Assert.True(shell.IsKeyBusy);

        var previous = shell.Selected;
        shell.Selected = FindNode(shell.Roots, "B문서");               // 트리 클릭
        dlg.PromptTextResult = "새문서";
        shell.NewDocumentCommand.Execute(null);
        shell.NewFolderCommand.Execute(null);
        var search = await shell.RunSearchAsync("AKIA", SearchScope.All);
        var canDrop = shell.EvaluateDrop(docB, Path.Combine(vault.Root, "폴더"));

        gated.Open();
        await switching;

        Assert.Same(previous, shell.Selected);                       // 선택을 되돌렸다
        Assert.False(FindNode(shell.Roots, "B문서").IsSelected);
        Assert.True(search.Canceled);
        Assert.False(canDrop);
        Assert.False(File.Exists(Path.Combine(vault.Root, "새문서.tbx")));
        Assert.False(Directory.Exists(Path.Combine(vault.Root, "새문서")));
        Assert.Empty(shell.Tabs);                                    // 전환은 끝까지 갔다
        Assert.False(shell.IsKeyBusy);
    }

    // ── 적대적 검토 후속 ──────────────────────────────────────────────────────

    private static async Task<(ShellViewModel Shell, FakeDialogs Dlg, GatedDocumentStore Gated)> BuildGatedAsync(
        TempVault vault, VaultKeyService keys)
    {
        var tree = new TreeService(vault.Root);
        var gated = new GatedDocumentStore(new DocumentStore(tree, new KeyDocumentCodec(keys)));
        var dlg = new FakeDialogs();
        var settings = new AppSettingsService(Path.Combine(vault.Root, "settings.json"));
        await settings.LoadAsync();
        var shell = new ShellViewModel(settings, dlg, gated, tree, new FakeEditorFactory(gated, dlg),
                                       new SearchService(tree, gated), new FakeExplorerLauncher(), keys, new FakeClipboard());
        await shell.RefreshAsync();
        return (shell, dlg, gated);
    }

    /// <summary>
    /// TreeView 흉내. 노드를 누르면 이전 선택을 조용히 풀고(한 번의 SelectedItemChanged) 셸에 새 노드를 넣는다.
    /// 코드가 IsSelected 를 바꿔도 같은 경로로 셸에 다시 들어온다 — 실제 앱의 재진입이 이것이다.
    /// </summary>
    private sealed class FakeTreeView
    {
        private readonly ShellViewModel _shell;
        private bool _changing;

        public FakeTreeView(ShellViewModel shell)
        {
            _shell = shell;
            foreach (var node in All()) node.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TreeNodeViewModel.IsSelected)) OnSelectionChanged(node);
            };
        }

        public void Click(TreeNodeViewModel node) => node.IsSelected = true;

        private void OnSelectionChanged(TreeNodeViewModel node)
        {
            if (_changing) return;

            if (node.IsSelected)
            {
                _changing = true;
                foreach (var other in All().Where(n => !ReferenceEquals(n, node) && n.IsSelected)) other.IsSelected = false;
                _changing = false;
                _shell.Selected = node;
            }
            else if (!All().Any(n => n.IsSelected))
            {
                _shell.Selected = null;
            }
        }

        private IEnumerable<TreeNodeViewModel> All()
        {
            var pending = new Stack<TreeNodeViewModel>(_shell.Roots);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                yield return node;
                foreach (var child in node.Children) pending.Push(child);
            }
        }
    }

    /// 선택 되돌리기가 TreeView 를 거쳐 세터로 다시 들어오면, 선택이 null 로 굳거나 트리와 셸이 서로 다른 노드를 가리켰다.
    [Fact]
    public async Task 잠그는_동안_다른_문서를_눌러도_선택은_원래_문서로_돌아오고_트리와_어긋나지_않는다()
    {
        using var vault = new TempVault();
        await WriteDoc(vault, "A문서", KeyA, "A 값");
        await WriteDoc(vault, "B문서", KeyA, "B 값");
        var keys = TestKeys.Service(KeyA);
        var (shell, _, gated) = await BuildGatedAsync(vault, keys);
        var view = new FakeTreeView(shell);
        var a = FindNode(shell.Roots, "A문서");
        var b = FindNode(shell.Roots, "B문서");

        view.Click(a);
        await Settle(shell);
        shell.Tabs[0].Text = "고친 값";

        gated.Close();                                               // 잠그기의 "옛 키로 저장"에서 멈춘다
        shell.LockCommand.Execute(null);
        var locking = shell.Pending;
        Assert.True(shell.IsKeyBusy);

        view.Click(b);

        Assert.Same(a, shell.Selected);
        Assert.True(a.IsSelected);                                   // 트리에도 A 가 선택돼 보인다 — 셸과 같은 노드
        Assert.False(b.IsSelected);

        gated.Open();
        await locking;

        // 탭이 모두 닫히면 선택을 비운다(J-5) — 트리와 셸이 함께 비어야 한다
        Assert.False(keys.HasKey);
        Assert.Empty(shell.Tabs);
        Assert.Null(shell.Selected);
        Assert.False(a.IsSelected || b.IsSelected);
    }

    /// 새 금고를 그린 뒤 키를 다시 판정하는 사이 문서를 열면, 그 탭은 옛 세대에 묶여 판정이 끝나는 순간부터 저장이 거부됐다.
    [Fact]
    public async Task 금고를_바꾼_뒤_다시_판정하는_동안에는_문서를_열지_않는다()
    {
        using var vault = new TempVault();
        using var next = new TempVault();
        await WriteDoc(next, "새금고문서", KeyA, "값");           // 다른 salt — 판정이 키를 새로 만든다
        var derivation = new GatedKeyDerivation();
        var (shell, dlg, _) = await BuildGatedAsync(vault, TestKeys.Service(KeyA, derivation));
        dlg.PickFolderResult = next.Root;

        derivation.Close();
        shell.ChangeRootCommand.Execute(null);
        var changing = shell.Pending;
        await derivation.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(shell.IsKeyBusy);

        var node = FindNode(shell.Roots, "새금고문서");
        shell.Selected = node;

        derivation.Open();
        await changing;

        Assert.Empty(shell.Tabs);
        Assert.NotSame(node, shell.Selected);
        Assert.False(shell.IsKeyBusy);

        shell.Selected = node;                                       // 끝난 뒤 누르면 열리고, 저장도 된다
        await Settle(shell);
        shell.Tabs[0].Text = "고친 값";
        Assert.True(await shell.Tabs[0].TrySaveAsync());
    }

    /// 탭 하나 닫기는 저장이 한 번만 돌았다. 저장을 기다리는 사이 친 글자는 수정됨으로 남았는데 탭은 닫혔다.
    [Fact]
    public async Task 탭을_닫으며_저장하는_사이_친_글자도_저장한_뒤_닫는다()
    {
        using var vault = new TempVault();
        var doc = await WriteDoc(vault, "A문서", KeyA, "원래 값");
        var (shell, _, gated) = await BuildGatedAsync(vault, TestKeys.Service(KeyA));
        await shell.OpenAsync(doc);
        var tab = shell.Tabs[0];
        tab.Text = "첫 값";

        gated.Close();
        var closing = shell.CloseTabAsync(tab);
        tab.Text = "첫 값 더";                                       // 저장을 기다리는 사이의 입력
        gated.Open();

        Assert.True(await closing);
        Assert.Empty(shell.Tabs);
        Assert.Equal("첫 값 더", (await TestKeys.Store(vault.Root, KeyA).LoadAsync(doc)).Text);
    }

    /// 종료 저장은 탭을 한 번씩만 돌았다. 뒤 탭을 저장하는 사이 앞 탭(보던 탭)에 친 글자는 다시 보지 않고 종료했다 (적대적 검토 2차).
    [Fact]
    public async Task 종료하며_뒤_탭을_저장하는_사이_앞_탭에_친_글자도_저장한다()
    {
        using var vault = new TempVault();
        var docA = await WriteDoc(vault, "A문서", KeyA, "");
        var docB = await WriteDoc(vault, "B문서", KeyA, "");
        var (shell, _, gated) = await BuildGatedAsync(vault, TestKeys.Service(KeyA));
        await shell.OpenAsync(docA);
        await shell.OpenAsync(docB);
        var (a, b) = (shell.Tabs[0], shell.Tabs[1]);
        a.Text = "A1";
        b.Text = "B1";

        var saves = 0;
        gated.DuringSave = () => { if (++saves == 2) a.Text = "A1 더"; };     // B 를 저장하는 사이 A 에 입력

        Assert.True(await shell.SaveAllForExitAsync());

        Assert.False(a.IsDirty);
        Assert.Equal("A1 더", (await TestKeys.Store(vault.Root, KeyA).LoadAsync(docA)).Text);
        Assert.Equal("B1", (await TestKeys.Store(vault.Root, KeyA).LoadAsync(docB)).Text);
    }

    // ── 검색 (K-9) ────────────────────────────────────────────────────────────

    [Fact]
    public async Task 다른_키_문서는_검색에서_따로_세고_불완전_결과로_본다()
    {
        using var vault = new TempVault();
        await WriteDoc(vault, "A문서", KeyA, "AKIA");
        var docB = await WriteDoc(vault, "B문서", KeyB, "AKIA");
        var (shell, _, _, _, _) = await BuildWithKeysAsync(vault, TestKeys.Service(KeyA));
        await shell.RefreshAsync();

        var all = await shell.RunSearchAsync("AKIA", SearchScope.All);

        Assert.Single(all.Hits);
        Assert.Equal(1, all.OtherKeyCount);
        Assert.Equal(0, all.UnreadableCount);
        Assert.False(all.IsComplete);

        // "이 문서" 범위 — 안 열린 문서와 열린(잠긴) 탭 모두 같은 규칙
        var closed = await shell.RunSearchAsync("AKIA", SearchScope.Document, scopeTarget: docB);
        await shell.OpenAsync(docB);
        var open = await shell.RunSearchAsync("AKIA", SearchScope.Document, scopeTarget: docB);

        Assert.Equal((1, 0), (closed.OtherKeyCount, closed.UnreadableCount));
        Assert.Equal((1, 0), (open.OtherKeyCount, open.UnreadableCount));
    }

    // ── 옛 방식 문서 (D-073) ──────────────────────────────────────────────────

    /// <summary>
    /// 4단계 뒤에도 남긴 옛 방식 처리. 트리의 이 표시는 변환 시험이 보고 있었는데 그 파일과 함께 사라졌고,
    /// 검색 쪽은 원래 짝 시험이 없었다 — 줄을 지워도 전부 통과했다 [실측 — 4단계 검토].
    /// 옛 방식이 "손상"으로 보이면 사용자가 지워도 되는 파일로 잘못 본다.
    /// </summary>
    [Fact]
    public async Task 옛_방식_문서는_트리에서_흐리게_열_수_없다고_보이고_검색에서_따로_센다()
    {
        using var vault = new TempVault();
        await WriteDoc(vault, "A문서", KeyA, "AKIA");
        var legacy = vault.WriteRaw("옛문서.tbx", LegacyShape.Bytes());
        var (shell, _, _, _, _) = await BuildWithKeysAsync(vault, TestKeys.Service(KeyA));
        await shell.RefreshAsync();

        var node = FindNode(shell.Roots, "옛문서");
        Assert.Equal(DocumentKeyState.Legacy, node.KeyState);
        Assert.True(node.IsDimmed);
        Assert.Contains("열 수 없습니다", node.KeyStateTip);

        var all = await shell.RunSearchAsync("AKIA", SearchScope.All);
        var one = await shell.RunSearchAsync("AKIA", SearchScope.Document, scopeTarget: legacy);
        Assert.Equal((1, 0, false), (all.LegacyCount, all.UnreadableCount, all.IsComplete));
        Assert.Equal((1, 0, false), (one.LegacyCount, one.UnreadableCount, one.IsComplete));

        // 검색 창 문구. 명령은 기다릴 수 없는 실행이라 마지막 문구가 바뀌는 것을 기다린다 (폴링 아님 — LL-079)
        var search = new SearchViewModel(shell) { Query = "AKIA" };
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        search.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SearchViewModel.Status) && search.Status != "찾는 중…") finished.TrySetResult();
        };
        search.RunCommand.Execute(null);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains("열 수 없는 옛 방식 문서 1개", search.Status);
    }

    // ── 다른 키 문서의 파일 조작 (K-16) ───────────────────────────────────────

    [Fact]
    public async Task 다른_키_문서도_이름을_바꾸고_옮길_수_있고_내용은_그대로다()
    {
        using var vault = new TempVault();
        var docB = await WriteDoc(vault, "B문서", KeyB, "B 값");
        var folder = vault.Dir("폴더");
        var before = Hash(docB);
        var (shell, dlg, _, _, _) = await BuildWithKeysAsync(vault, TestKeys.Service(KeyA));
        await shell.RefreshAsync();

        await shell.DropAsync(docB, folder);
        var moved = Path.Combine(folder, "B문서.tbx");

        Assert.Equal(before, Hash(moved));
        Assert.Equal(DocumentKeyState.DifferentKey, FindNode(shell.Roots, "B문서").KeyState);
        Assert.Equal(0, dlg.ErrorCount);
    }
}

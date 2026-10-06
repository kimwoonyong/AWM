using TextBean.Services;
using TextBean.ViewModels;

namespace TextBean.Tests;

/// <summary>
/// ShellViewModel 조립과 트리 탐색. 탭 테스트가 같은 것을 다시 만들면
/// 한쪽만 고쳐져 두 파일이 서로 다른 앱을 시험하게 된다.
/// </summary>
internal static class ShellFixture
{
    internal static async Task<(ShellViewModel shell, FakeDialogs dlg, DocumentStore store)> BuildAsync(TempVault vault)
    {
        var (shell, dlg, store, _) = await BuildWithFactoryAsync(vault);
        return (shell, dlg, store);
    }

    /// 탭이 실제로 쓴 타이머를 봐야 하는 시험용.
    internal static async Task<(ShellViewModel shell, FakeDialogs dlg, DocumentStore store, FakeEditorFactory factory)>
        BuildWithFactoryAsync(TempVault vault)
    {
        var (shell, dlg, store, factory, _) = await BuildCoreAsync(vault);
        return (shell, dlg, store, factory);
    }

    /// <summary>
    /// 탐색기 명령이 무엇을 열려고 했는지 봐야 하는 시험용.
    /// 위 튜플에 항목을 더하지 않고 빌더를 따로 둔다 — 늘리면 그것을 분해하는 기존 호출 8곳이 컴파일되지 않는다.
    /// </summary>
    internal static async Task<(ShellViewModel shell, FakeDialogs dlg, FakeExplorerLauncher explorer)>
        BuildWithLauncherAsync(TempVault vault)
    {
        var (shell, dlg, _, _, explorer, _, _) = await BuildCoreAsync(vault, null);
        return (shell, dlg, explorer);
    }

    /// <summary>
    /// 키 흐름을 봐야 하는 시험용. <paramref name="keys"/> 를 안 주면 키가 없는 채로 시작한다 —
    /// 앱을 막 켠 상태와 같다. 셸과 코덱이 같은 키 서비스를 쓴다.
    /// </summary>
    internal static async Task<(ShellViewModel shell, FakeDialogs dlg, DocumentStore store, VaultKeyService keys,
                                FakeClipboard clipboard)>
        BuildWithKeysAsync(TempVault vault, VaultKeyService? keys = null)
    {
        var (shell, dlg, store, _, _, service, clipboard) =
            await BuildCoreAsync(vault, keys ?? new VaultKeyService(new FastKeyDerivation()));
        return (shell, dlg, store, service, clipboard);
    }

    private static async Task<(ShellViewModel, FakeDialogs, DocumentStore, FakeEditorFactory, FakeExplorerLauncher)>
        BuildCoreAsync(TempVault vault)
    {
        var (shell, dlg, store, factory, explorer, _, _) = await BuildCoreAsync(vault, null);
        return (shell, dlg, store, factory, explorer);
    }

    private static async Task<(ShellViewModel, FakeDialogs, DocumentStore, FakeEditorFactory, FakeExplorerLauncher,
                               VaultKeyService, FakeClipboard)>
        BuildCoreAsync(TempVault vault, VaultKeyService? keys)
    {
        // 기존 시험은 키가 들어간 상태에서 시작한다 — 키 흐름과 무관한 동작을 본다
        keys ??= TestKeys.Service();
        var tree = new TreeService(vault.Root);
        var store = new DocumentStore(tree, new KeyDocumentCodec(keys));
        var dlg = new FakeDialogs();
        var settings = new AppSettingsService(Path.Combine(vault.Root, "settings.json"));
        await settings.LoadAsync();
        var factory = new FakeEditorFactory(store, dlg);
        var search = new SearchService(tree, store);
        var explorer = new FakeExplorerLauncher();
        var clipboard = new FakeClipboard();
        var shell = new ShellViewModel(settings, dlg, store, tree, factory, search, explorer, keys, clipboard);
        return (shell, dlg, store, factory, explorer, keys, clipboard);
    }

    /// 명령이 끝나기를 기다린다. ShellViewModel.Guarded 가 Task 를 붙들고 있어
    /// 폴링이 필요 없다 — 폴링 방식은 부하가 걸리면 20% 확률로 시간 초과했다 [실측].
    internal static Task Settle(ShellViewModel shell) => shell.Pending;

    internal static TreeNodeViewModel FindNode(IEnumerable<TreeNodeViewModel> nodes, string name)
        => TryFind(nodes, name) ?? throw new InvalidOperationException($"노드를 찾지 못함: {name}");

    private static TreeNodeViewModel? TryFind(IEnumerable<TreeNodeViewModel> nodes, string name)
    {
        foreach (var node in nodes)
        {
            if (node.Name == name) return node;

            var hit = TryFind(node.Children, name);
            if (hit is not null) return hit;
        }
        return null;
    }
}

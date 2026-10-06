using TextBean.Services;
using TextBean.ViewModels;

namespace TextBean.Tests;

/// <summary>
/// 문서 안에서 찾기 (Ctrl+F). 서비스가 필요 없다 — 이미 메모리에 Text 가 있다.
/// 상태는 탭마다 따로 간다: 탭 A 에서 찾던 것이 탭 B 에 새어 나가면
/// 사용자는 B 에서 엉뚱한 자리가 강조된 것을 본다.
/// </summary>
public class DocumentSearchTests
{
    private static async Task<EditorViewModel> OpenAsync(TempVault vault, string name, string body)
    {
        var store = new DocumentStore(new TreeService(vault.Root), TestKeys.Codec());
        var path = Path.Combine(vault.Root, name + ".tbx");
        await store.CreateAsync(path);
        await store.SaveAsync(path, body);

        var vm = new EditorViewModel(store, new FakeClipboard(), new FakeDialogs(), new FakeAutoSaveTimer());
        await vm.LoadAsync(path);
        return vm;
    }

    [Fact]
    public async Task 일치_위치를_모두_찾는다()
    {
        using var vault = new TempVault();
        var vm = await OpenAsync(vault, "A", "xxAKIAyyAKIAzzAKIA");

        vm.SetSearch("AKIA");

        Assert.Equal([2, 8, 14], vm.Matches);
        Assert.Equal(0, vm.CurrentMatch);          // 첫 일치가 현재
    }

    [Fact]
    public async Task 대소문자를_무시한다()
    {
        using var vault = new TempVault();
        var vm = await OpenAsync(vault, "A", "Akia AKIA akia");

        vm.SetSearch("aKiA");

        Assert.Equal(3, vm.Matches.Count);
    }

    [Fact]
    public async Task 겹치는_일치는_한_번만_센다()
    {
        using var vault = new TempVault();
        var vm = await OpenAsync(vault, "A", "aaaa");

        vm.SetSearch("aa");

        // 다음/이전으로 돌 수 있는 자리 수와 같아야 한다
        Assert.Equal([0, 2], vm.Matches);
    }

    [Fact]
    public async Task 다음으로_돌다_끝에서_처음으로_돌아온다()
    {
        using var vault = new TempVault();
        var vm = await OpenAsync(vault, "A", "A_A_A");
        vm.SetSearch("A");
        Assert.Equal(3, vm.Matches.Count);

        Assert.True(vm.MoveToNextMatch());
        Assert.Equal(1, vm.CurrentMatch);
        Assert.True(vm.MoveToNextMatch());
        Assert.Equal(2, vm.CurrentMatch);

        // 한 바퀴 — 끝에서 멈추면 "더 없다"와 구분이 안 된다
        Assert.True(vm.MoveToNextMatch());
        Assert.Equal(0, vm.CurrentMatch);
    }

    [Fact]
    public async Task 이전으로_돌다_처음에서_끝으로_돌아온다()
    {
        using var vault = new TempVault();
        var vm = await OpenAsync(vault, "A", "A_A_A");
        vm.SetSearch("A");

        Assert.True(vm.MoveToPrevMatch());
        Assert.Equal(2, vm.CurrentMatch);
    }

    [Fact]
    public async Task 이동하면_그_자리를_보여달라는_신호가_나간다()
    {
        using var vault = new TempVault();
        var vm = await OpenAsync(vault, "A", "xxAKIAyyAKIA");

        var revealed = new List<int>();
        vm.RevealRequested += (_, index) => revealed.Add(index);

        vm.SetSearch("AKIA");
        vm.MoveToNextMatch();

        // 이 신호가 없으면 View 가 Select·ScrollToLine 을 못 해 화면이 안 따라간다 [실측]
        Assert.Equal([2, 8], revealed);
    }

    [Fact]
    public async Task 일치가_없으면_현재_자리가_없다()
    {
        using var vault = new TempVault();
        var vm = await OpenAsync(vault, "A", "내용");

        vm.SetSearch("없는값");

        Assert.Empty(vm.Matches);
        Assert.Equal(-1, vm.CurrentMatch);
        Assert.False(vm.MoveToNextMatch());
        Assert.False(vm.MoveToPrevMatch());
    }

    [Fact]
    public async Task 빈_검색어는_일치를_만들지_않는다()
    {
        using var vault = new TempVault();
        var vm = await OpenAsync(vault, "A", "내용");

        foreach (var q in new[] { "", "  " })
        {
            vm.SetSearch(q);
            Assert.Empty(vm.Matches);
            Assert.Equal(-1, vm.CurrentMatch);
        }
    }

    [Fact]
    public async Task 본문이_바뀌면_일치_목록이_다시_계산된다()
    {
        using var vault = new TempVault();
        var vm = await OpenAsync(vault, "A", "AKIA");
        vm.SetSearch("AKIA");
        Assert.Single(vm.Matches);

        vm.Text = "AKIA AKIA AKIA";

        // 안 다시 세면 강조가 엉뚱한 자리에 남아 '이 값이 일치했다'고 잘못 알려준다
        Assert.Equal(3, vm.Matches.Count);
    }

    [Fact]
    public async Task 본문이_줄어들어도_현재_자리가_범위를_벗어나지_않는다()
    {
        using var vault = new TempVault();
        var vm = await OpenAsync(vault, "A", "AKIA AKIA AKIA");
        vm.SetSearch("AKIA");
        vm.MoveToNextMatch();
        vm.MoveToNextMatch();
        Assert.Equal(2, vm.CurrentMatch);

        vm.Text = "AKIA";

        Assert.Single(vm.Matches);
        Assert.Equal(0, vm.CurrentMatch);
    }

    [Fact]
    public async Task 검색을_지우면_목록도_비워진다()
    {
        using var vault = new TempVault();
        var vm = await OpenAsync(vault, "A", "AKIA");
        vm.SetSearch("AKIA");

        vm.ClearSearch();

        // 강조가 남으면 화면에 어느 값이 민감한지 표시해 두는 셈이 된다
        Assert.Empty(vm.Matches);
        Assert.Equal(-1, vm.CurrentMatch);
        Assert.Equal("", vm.SearchQuery);
    }

    [Fact]
    public async Task 문서를_바꾸면_검색_상태가_따라오지_않는다()
    {
        using var vault = new TempVault();
        var store = new DocumentStore(new TreeService(vault.Root), TestKeys.Codec());
        var a = Path.Combine(vault.Root, "A.tbx");
        var b = Path.Combine(vault.Root, "B.tbx");
        await store.CreateAsync(a);
        await store.SaveAsync(a, "AKIA");
        await store.CreateAsync(b);
        await store.SaveAsync(b, "관계없음");

        var vm = new EditorViewModel(store, new FakeClipboard(), new FakeDialogs(), new FakeAutoSaveTimer());
        await vm.LoadAsync(a);
        vm.SetSearch("AKIA");
        Assert.Single(vm.Matches);

        await vm.LoadAsync(b);

        Assert.Empty(vm.Matches);
        Assert.Equal("", vm.SearchQuery);
    }

    [Fact]
    public async Task 일치_길이를_알려준다()
    {
        using var vault = new TempVault();
        var vm = await OpenAsync(vault, "A", "AKIA");

        vm.SetSearch("AKI");

        // View 가 강조 사각형을 그리려면 시작 위치와 길이가 둘 다 필요하다
        Assert.Equal(3, vm.MatchLength);
    }
}

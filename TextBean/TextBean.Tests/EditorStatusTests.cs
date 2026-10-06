using TextBean.Services;
using TextBean.Services.Interfaces;
using TextBean.ViewModels;

namespace TextBean.Tests;

public class EditorStatusTests
{
    private static EditorViewModel Build(TempVault vault, out DocumentStore store)
    {
        store = new DocumentStore(new TreeService(vault.Root), TestKeys.Codec());
        return new EditorViewModel(store, new FakeClipboard(), new FakeDialogs(), new FakeAutoSaveTimer());
    }

    [Fact]
    public async Task 상태표시가_문서없음_저장됨_수정됨을_구분한다()
    {
        using var vault = new TempVault();
        var editor = Build(vault, out var store);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);

        Assert.Contains("문서를 선택", editor.StatusText);

        await editor.LoadAsync(path);
        Assert.Equal("저장됨", editor.StatusText);

        var notified = new List<string?>();
        editor.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        editor.Text = "입력";
        Assert.Equal("저장 중…", editor.StatusText);
        Assert.Contains(nameof(EditorViewModel.StatusText), notified);   // 알림이 없으면 화면이 안 바뀐다

        await editor.TrySaveAsync();
        Assert.Equal("저장됨", editor.StatusText);
    }

    [Fact]
    public async Task 읽기_전용이면_상태표시가_그렇게_말한다()
    {
        using var vault = new TempVault();
        var editor = Build(vault, out _);

        await editor.LoadAsync(vault.WriteRaw("가짜.tbx", "문서가 아님"u8.ToArray()));

        Assert.Contains("읽기 전용", editor.StatusText);
    }

    /// <summary>
    /// 트리 아이콘은 View 가 이 값을 보고 고른다 — 이모지 문자열을 VM 이 들고 있으면
    /// 글꼴·환경에 따라 폭과 색이 달라지는 것을 제어할 수 없다.
    /// 실제로 어떤 그림이 나오는지는 오프스크린 렌더로 눈으로 확인한다(scratchpad/iconprobe).
    /// </summary>
    [Fact]
    public void 폴더와_문서가_구분된다()
    {
        var folder = new TreeNodeViewModel(new Models.TreeNode(@"C:\v\폴더1", "폴더1", true, []));
        var document = new TreeNodeViewModel(new Models.TreeNode(@"C:\v\문서.tbx", "문서", false, []));

        Assert.True(folder.IsFolder);
        Assert.False(document.IsFolder);
    }
}

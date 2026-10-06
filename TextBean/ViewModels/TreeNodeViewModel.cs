using System.Collections.ObjectModel;
using TextBean.Models;

namespace TextBean.ViewModels;

public sealed class TreeNodeViewModel : ObservableObject
{
    private bool _isExpanded = true;
    private bool _isSelected;

    public TreeNodeViewModel(TreeNode node)
    {
        FullPath = node.FullPath;
        Name = node.Name;
        IsFolder = node.IsFolder;
        IsPlainText = node.IsPlainText;
        Children = new ObservableCollection<TreeNodeViewModel>(node.Children.Select(c => new TreeNodeViewModel(c)));
    }

    public string FullPath { get; }

    public string Name { get; }

    public bool IsFolder { get; }

    /// <summary>
    /// 암호화되지 않은 참고 파일(.txt)인가. 이 값을 안 나르면 평문 파일에 자물쇠 아이콘이 붙는다.
    /// 생성자에서 옮겨 담는 것을 빠뜨리기 쉬운 자리다 — Unreadable 이 이미 그렇게 누락돼 있다.
    /// </summary>
    public bool IsPlainText { get; }

    // 아이콘은 View 가 IsFolder 와 IsPlainText 를 보고 고른다. 이모지 문자열을 VM 이 들고 있으면
    // 글꼴·환경에 따라 폭과 색이 달라지는 것을 여기서 제어할 수 없다.

    /// 금고 루트 노드는 드래그 대상이 아니다. 끌면 금고 구조가 깨진다.
    public bool IsRoot { get; init; }

    public ObservableCollection<TreeNodeViewModel> Children { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }

    /// TreeView.SelectedItem 은 읽기 전용이라 바인딩으로 선택을 되돌릴 수 없다.
    /// ItemContainerStyle 로 이 속성을 양방향 묶어 코드에서 선택을 옮긴다.
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    private DocumentKeyState _keyState = DocumentKeyState.Checking;

    /// <summary>
    /// 지금 키로 이 문서가 열리는가. 새로고침·키 전환 뒤 헤더만 읽어 채운다.
    /// 모든 .tbx 에 이미 자물쇠 그림이 있어 "다른 키"를 자물쇠로 보이면 뜻이 겹친다 — 흐리게 보인다.
    /// </summary>
    public DocumentKeyState KeyState
    {
        get => _keyState;
        set
        {
            if (!Set(ref _keyState, value)) return;
            Raise(nameof(IsDimmed));
            Raise(nameof(KeyStateTip));
        }
    }

    /// 판정은 여기서 한다 — XAML 에서 상태 목록을 나열하지 않는다 (PROHIBITED-UI-03).
    public bool IsDimmed => !IsFolder && !IsPlainText && KeyState is not (DocumentKeyState.Matches or DocumentKeyState.Checking);

    public string? KeyStateTip => IsFolder || IsPlainText ? null : KeyState switch
    {
        DocumentKeyState.DifferentKey => "지금 키로 열리지 않는 문서입니다",
        DocumentKeyState.NoKey => "키를 넣어야 열 수 있습니다",
        DocumentKeyState.Legacy => "옛 방식(Windows 계정) 문서입니다 — 이 버전에서는 열 수 없습니다",
        DocumentKeyState.Corrupted => "손상된 문서입니다",
        DocumentKeyState.Unreadable => "읽을 수 없는 파일입니다",
        _ => null
    };
}

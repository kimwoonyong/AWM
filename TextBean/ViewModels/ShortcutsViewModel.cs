using System.Collections.ObjectModel;

namespace TextBean.ViewModels;

/// 목록 창의 한 줄.
public sealed class ShortcutRow(ShortcutAction action, string? key) : ObservableObject
{
    public ShortcutAction Action { get; } = action;
    public string Name => Action.Name;

    private string? _key = key;
    public string? Key
    {
        get => _key;
        set { if (Set(ref _key, value)) Raise(nameof(KeyText)); }
    }

    private bool _isCapturing;
    public bool IsCapturing
    {
        get => _isCapturing;
        set { if (Set(ref _isCapturing, value)) Raise(nameof(KeyText)); }
    }

    public string KeyText => IsCapturing ? "키를 누르세요 (Esc 취소)" : Key ?? "없음";
}

/// <summary>
/// 단축키 목록 창 (D-110 · D-114). 줄을 고르고 [키 바꾸기] → 다음에 누른 키로 바꾼다.
/// 막힌 키 · 겹치는 키는 바꾸지 않고 이유를 보인다. [초기화]는 전체를 기본 키로 되돌린다.
/// 바꾸면 그 자리에서 저장 · 적용한다.
/// </summary>
public sealed class ShortcutsViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;

    public ShortcutsViewModel(ShellViewModel shell)
    {
        _shell = shell;
        Rows = [.. ShortcutCatalog.Actions.Select(a => new ShortcutRow(a, shell.Shortcuts.GetValueOrDefault(a.Id)))];

        ChangeCommand = new RelayCommand(_ => BeginCapture(), _ => Selected is not null);
        ResetCommand = new RelayCommand(_ => _ = ResetAsync());
    }

    public ObservableCollection<ShortcutRow> Rows { get; }

    public IReadOnlyList<FixedShortcut> FixedKeys => ShortcutCatalog.FixedKeys;

    private ShortcutRow? _selected;
    public ShortcutRow? Selected
    {
        get => _selected;
        set
        {
            var previous = _selected;
            if (!Set(ref _selected, value)) return;

            // 다른 줄로 옮기면 받던 키를 버린다
            if (previous is not null) previous.IsCapturing = false;
            Message = "";
            ChangeCommand.RaiseCanExecuteChanged();
        }
    }

    private string _message = "";
    public string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    public bool IsCapturing => Selected?.IsCapturing == true;

    public RelayCommand ChangeCommand { get; }
    public RelayCommand ResetCommand { get; }

    private void BeginCapture()
    {
        if (Selected is null) return;
        Message = "";
        Selected.IsCapturing = true;
        Raise(nameof(IsCapturing));
    }

    /// <summary>
    /// 키를 받는 중에 눌린 키(화면이 "Ctrl+Shift+F" 꼴로 만든다). Esc 는 취소다.
    /// 바꿨으면 true. 막혔거나 겹치면 이유를 Message 에 두고 받기를 끝낸다.
    /// </summary>
    public async Task<bool> CaptureAsync(string key)
    {
        if (Selected is not { IsCapturing: true } row) return false;

        row.IsCapturing = false;
        Raise(nameof(IsCapturing));

        if (key == "Esc") return false;

        if (ShortcutCatalog.Reject(key) is { } reason)
        {
            Message = reason;
            return false;
        }

        if (Rows.FirstOrDefault(r => r != row && string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase)) is { } owner)
        {
            Message = $"{key} 는 '{owner.Name}'가 쓰고 있습니다.";
            return false;
        }

        row.Key = key;
        Message = "";
        await SaveAsync();
        return true;
    }

    private async Task ResetAsync()
    {
        foreach (var row in Rows)
        {
            row.IsCapturing = false;
            row.Key = row.Action.DefaultKey;
        }
        Raise(nameof(IsCapturing));
        Message = "";
        await SaveAsync();
    }

    private Task SaveAsync() => _shell.SetShortcutsAsync(Rows.ToDictionary(r => r.Action.Id, r => r.Key));
}

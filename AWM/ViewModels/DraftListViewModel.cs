using AWM.Models;
using AWM.Services;
using AWM.Services.Interfaces;

namespace AWM.ViewModels;

public sealed class DraftListViewModel : ObservableObject
{
    private readonly IDraftStore _store;
    private IReadOnlyList<StoredDraft> _drafts = [];
    private StoredDraft? _selected;
    private string _message = "불러오는 중…";
    private bool _isError;

    public DraftListViewModel(IDraftStore store)
    {
        _store = store;
        OpenCommand = new RelayCommand(_ => CloseRequested?.Invoke(), _ => Selected is not null);
    }

    /// <summary>
    /// 「열기」. 창이 듣고 DialogResult = true 로 닫는다.
    /// </summary>
    public event Action? CloseRequested;

    public RelayCommand OpenCommand { get; }

    public IReadOnlyList<StoredDraft> Drafts
    {
        get => _drafts;
        private set => Set(ref _drafts, value);
    }

    public StoredDraft? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
                OpenCommand.RaiseCanExecuteChanged();
        }
    }

    public string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    public bool IsError
    {
        get => _isError;
        private set => Set(ref _isError, value);
    }

    public async Task LoadAsync()
    {
        try
        {
            var result = await _store.ListAsync();
            Drafts = result.Drafts;
            Selected = result.Drafts.FirstOrDefault();

            // 읽지 못한 폴더는 빼되 숨기지 않는다 (D-009)
            IsError = result.UnreadableCount > 0;
            Message = result switch
            {
                { UnreadableCount: > 0 } => $"초안 {result.Drafts.Count}개 · 읽지 못한 폴더 {result.UnreadableCount}개 — {_store.Root}",
                { Drafts.Count: 0 } => $"저장된 초안이 없습니다 — {_store.Root}",
                _ => $"초안 {result.Drafts.Count}개 — {_store.Root}",
            };
        }
        catch (Exception ex) when (DraftStore.IsUnreadable(ex))
        {
            IsError = true;
            Message = $"목록을 읽지 못했습니다: {ex.Message}";
        }
    }
}

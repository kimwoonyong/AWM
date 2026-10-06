using System.Collections.ObjectModel;
using System.IO;
using TextBean.Models;

namespace TextBean.ViewModels;

/// <summary>
/// 금고 검색 창의 상태. 실제 검색과 문서 열기는 <see cref="ShellViewModel"/> 이 한다 —
/// 탭·트리·취소 토큰을 이미 그쪽이 쥐고 있고, 두 곳에서 관리하면 어긋난다.
/// </summary>
public sealed class SearchViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;

    private string _query = "";
    private SearchScope _scope = SearchScope.All;
    private bool _matchCase;
    private bool _isRunning;
    private string _status = "";
    private SearchHit? _selected;

    public SearchViewModel(ShellViewModel shell)
    {
        _shell = shell;

        RunCommand    = new RelayCommand(_ => Run(), _ => !IsRunning && Query.Trim().Length > 0);
        CancelCommand = new RelayCommand(_ => _shell.CancelSearch(), _ => IsRunning);
        OpenHitCommand = new RelayCommand(p => { if (p is SearchHit hit) Open(hit); });
        PickFolderCommand = new RelayCommand(_ => PickFolder());
        PickDocumentCommand = new RelayCommand(_ => PickDocument());

        _shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.Selected) && _scopeFolder is null)
                Raise(nameof(ScopeFolderName));       // 아직 안 골랐으면 트리 선택을 따라 보여준다

            if (e.PropertyName == nameof(ShellViewModel.ActiveTab) && _scopeDocument is null)
            {
                Raise(nameof(ScopeDocument));       // 아직 안 골랐으면 보고 있는 탭을 따라 보여준다
                Raise(nameof(ScopeDocumentName));
            }
        };

        // 이 창은 닫아도 다시 쓴다. 키가 바뀌거나 잠기면 옛 키로 찾은 결과를 비운다 —
        // 남기면 자리를 비운 사이 누구나 "A문서 3곳 일치"를 보고, 결과를 누르면 다른 키 탭이 열린다.
        _shell.KeyContextReset += (_, locked) => Reset(clearQuery: locked);
    }

    private void Reset(bool clearQuery)
    {
        _shell.CancelSearch();
        Results.Clear();
        NameResults.Clear();
        HasBlindSpots = false;
        Raise(nameof(HasBlindSpots));
        Status = "";

        // 잠그기면 검색어도 지운다 — 검색어 자체가 비밀값일 수 있다
        if (clearQuery) Query = "";
    }


    /// 결과는 반드시 새 컬렉션에 담는다. Roots·Tabs 를 재활용하면 한쪽 필터가 양쪽에 걸린다 (LL-007).
    public ObservableCollection<SearchHit> Results { get; } = [];

    /// 이름 일치는 복호화가 0회라 입력 즉시 갱신된다. 본문 결과와 섞지 않는다.
    public ObservableCollection<SearchHit> NameResults { get; } = [];

    public string Query
    {
        get => _query;
        set
        {
            if (!Set(ref _query, value)) return;

            RunCommand.RaiseCanExecuteChanged();
            _ = RefreshNamesAsync();          // 이름 검색만 즉시. 본문은 Enter 로 (P-2)
        }
    }

    public SearchScope Scope
    {
        get => _scope;
        set
        {
            if (!Set(ref _scope, value)) return;

            Raise(nameof(IsAllScope));
            Raise(nameof(IsFolderScope));
            Raise(nameof(IsDocumentScope));
            _ = RefreshNamesAsync();
        }
    }

    // 라디오 버튼용. 범위를 착각하면 "금고에 없다"는 거짓 결론이 나오므로 화면에 늘 드러낸다.
    public bool IsAllScope      { get => Scope == SearchScope.All;      set { if (value) Scope = SearchScope.All; } }
    public bool IsFolderScope   { get => Scope == SearchScope.Folder;   set { if (value) Scope = SearchScope.Folder; } }
    public bool IsDocumentScope { get => Scope == SearchScope.Document; set { if (value) Scope = SearchScope.Document; } }

    public bool MatchCase
    {
        get => _matchCase;
        set => Set(ref _matchCase, value);
    }

    private string? _scopeFolder;

    /// <summary>
    /// 검색할 폴더. 한 번 고르면 트리 선택이 바뀌어도 따라 바뀌지 않는다 —
    /// 결과를 보는 중 범위가 발밑에서 바뀌면 방금 본 항목이 사라진다.
    /// 아직 안 골랐으면 트리 선택을 따른다.
    /// </summary>
    public string? ScopeFolder => _scopeFolder ?? _shell.ScopeFolderFor(SearchScope.Folder);

    /// 이름을 박아 둔다 — "폴더 하위"만 쓰여 있으면 어느 폴더인지 몰라 범위 착각이 난다.
    public string ScopeFolderName
        => ScopeFolder is { } folder ? Path.GetFileName(folder) : "고르지 않음";

    /// 트리에서 "이 폴더에서 찾기"로 열 때. 대상을 확정한 채 창이 뜬다.
    public void UseFolder(string folder)
    {
        _scopeFolder = folder;
        Scope = SearchScope.Folder;

        Raise(nameof(ScopeFolder));
        Raise(nameof(ScopeFolderName));
    }

    private void PickFolder()
    {
        var picked = _shell.PickScopeFolder(ScopeFolder);
        if (picked is null) return;

        _scopeFolder = picked;
        Scope = SearchScope.Folder;

        Raise(nameof(ScopeFolder));
        Raise(nameof(ScopeFolderName));
        _ = RefreshNamesAsync();
    }

    private string? _scopeDocument;

    /// <summary>
    /// 검색할 문서. 열려 있지 않아도 된다 — 열린 탭 중에서만 고를 수 있으면
    /// "금고에 없다"는 거짓 답이 나온다. 안 골랐으면 지금 보는 탭을 쓴다.
    /// </summary>
    public string? ScopeDocument => _scopeDocument ?? _shell.ActiveTab?.CurrentPath;

    public string ScopeDocumentName
        => ScopeDocument is { } path ? Path.GetFileNameWithoutExtension(path) : "고르지 않음";

    private void PickDocument()
    {
        var picked = _shell.PickVaultDocument(ScopeDocument);
        if (picked is null) return;

        _scopeDocument = picked;
        Scope = SearchScope.Document;

        Raise(nameof(ScopeDocument));
        Raise(nameof(ScopeDocumentName));
        _ = RefreshNamesAsync();
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!Set(ref _isRunning, value)) return;

            RunCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
        }
    }

    /// 결과 헤더. 범위와 "못 본 것"을 같이 적는다.
    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>
    /// 결과를 "없다"로 읽어도 되는가. 읽지 못한 문서나 폴더가 있으면 그렇게 읽으면 안 된다 —
    /// 사용자가 키를 새로 발급받는 경로가 여기서 갈린다.
    /// </summary>
    public bool HasBlindSpots { get; private set; }

    /// <summary>
    /// 목록에서 지금 짚고 있는 항목. **여는 것은 여기에 매달지 않는다** —
    /// ListBox 는 이미 선택된 항목을 다시 클릭해도 선택 변화를 내지 않아서,
    /// 탭을 닫고 같은 결과를 다시 눌러도 아무 일이 안 일어난다 [실측].
    /// 열기는 <see cref="OpenHitCommand"/> 가 클릭마다 받는다.
    /// </summary>
    public SearchHit? SelectedResult
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public RelayCommand RunCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand OpenHitCommand { get; }
    public RelayCommand PickFolderCommand { get; }
    public RelayCommand PickDocumentCommand { get; }

    private void Run() => _ = RunAsync();

    private async Task RunAsync()
    {
        IsRunning = true;
        Status = "찾는 중…";

        try
        {
            var target = Scope == SearchScope.Document ? ScopeDocument : ScopeFolder;
            var outcome = await _shell.RunSearchAsync(Query.Trim(), Scope, MatchCase, target);
            if (outcome.Canceled) { Status = "취소했습니다."; return; }

            Results.Clear();
            foreach (var hit in outcome.Hits) Results.Add(hit);

            HasBlindSpots = !outcome.IsComplete;
            Raise(nameof(HasBlindSpots));
            Status = Describe(outcome);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private string Describe(SearchOutcome outcome)
    {
        var where = Scope switch
        {
            SearchScope.Folder => $"'{ScopeFolderName}' 아래",
            SearchScope.Document => $"'{ScopeDocumentName}'",
            _ => "금고 전체",
        };

        // 범위를 결과 문구에 박아 둔다. 빈 결과일수록 어디를 뒤졌는지가 중요하다.
        var head = outcome.Hits.Count == 0
            ? $"{where} — 일치 없음"
            : $"{where} — 문서 {outcome.Hits.Count}개에서 {outcome.TotalMatches}곳";

        var blind = new List<string>();
        if (outcome.OtherKeyCount > 0) blind.Add($"지금 키로 열리지 않아 보지 않은 문서 {outcome.OtherKeyCount}개");
        if (outcome.LegacyCount > 0) blind.Add($"열 수 없는 옛 방식 문서 {outcome.LegacyCount}개");
        if (outcome.UnreadableCount > 0) blind.Add($"읽지 못한 문서 {outcome.UnreadableCount}건");
        if (outcome.UnreadableFolderCount > 0) blind.Add($"열지 못한 폴더 {outcome.UnreadableFolderCount}개");

        // 이걸 빼면 '다 뒤졌고 없다'가 되지만 실제로는 통째로 안 본 영역이 있다
        return blind.Count == 0 ? head : $"{head} · {string.Join(" · ", blind)} — 전부 확인하지는 못했습니다";
    }

    private int _nameGeneration;

    private async Task RefreshNamesAsync()
    {
        // 타이핑마다 돌므로 여러 번이 겹친다. 세대를 붙여 늦게 끝난 옛 결과를 버린다 —
        // 안 하면 각자 자기 결과를 덧붙여 같은 항목이 여러 번 보인다 [실측].
        var generation = ++_nameGeneration;

        var q = Query.Trim();
        if (q.Length == 0) { NameResults.Clear(); return; }

        // 이름은 평문이라 복호화가 0회다 — 본문 검색보다 약 800배 빠르다 [실측]
        var names = await _shell.SearchNamesAsync(q, Scope, MatchCase, ScopeFolder);
        if (generation != _nameGeneration) return;

        NameResults.Clear();
        foreach (var hit in names) NameResults.Add(hit);
    }

    private void Open(SearchHit hit)
    {
        // 폴더는 열 문서가 없다. 대신 그 폴더를 범위로 잡아 준다.
        if (hit.IsFolder)
        {
            _scopeFolder = hit.FullPath;
            _shell.SelectPath(hit.FullPath);
            Scope = SearchScope.Folder;

            Raise(nameof(ScopeFolder));
            Raise(nameof(ScopeFolderName));
            return;
        }

        _ = _shell.OpenHitGuardedAsync(hit, Query.Trim());
    }
}

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using AWM.Models;
using AWM.Services;
using AWM.Services.Interfaces;

namespace AWM.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    // ClaudeCli 가 넘기는 --model 값과 같아야 한다. 실명은 기록하지 않는다 (add-draft-storage D-005)
    private const string ModelAlias = "sonnet";

    private readonly IDraftService _drafts;
    private readonly IClipboardService _clipboard;
    private readonly IDraftStore _store;
    private readonly IDialogService _dialogs;
    private readonly IBrowserLauncher _browser;
    private CancellationTokenSource? _generation;

    // 화면에 열린 초안의 폴더. null 이면 아직 디스크에 없다
    private StoredDraft? _current;
    // _current 가 없을 때 저장할 바탕(만든 시각·요청). 자동 저장이 실패한 생성 결과 등
    private SavedDraft? _pending;
    // 프로그램이 결과 칸을 채우는 중 — 이때의 변경은 사용자의 수정이 아니다
    private bool _applying;
    private bool _saving;

    private string _topic = "";
    private string _keywords = "";
    private string _extra = "";
    private string _title = "";
    private string _body = "";
    private string _tagsText = "";
    private bool _isBusy;
    private bool _isDirty;
    private bool _isError;
    private string _status = "주제를 입력하고 「초안 만들기」를 누르세요.";

    public MainViewModel(IDraftService drafts, IClipboardService clipboard, IDraftStore store, IDialogService dialogs,
        IBrowserLauncher browser)
    {
        _drafts = drafts;
        _clipboard = clipboard;
        _store = store;
        _dialogs = dialogs;
        _browser = browser;

        GenerateCommand = new RelayCommand(async _ => await GenerateAsync(), _ => CanGenerate);
        CancelCommand = new RelayCommand(_ => CancelGeneration(), _ => IsBusy);
        CopyTitleCommand = new RelayCommand(_ => Copy(Title, "제목을 복사했습니다."), _ => Title.Length > 0);
        // 텍스트로 붙일 곳에는 표시 기호(##·>·---)를 빼고 넘긴다 (add-naver-blog-format D-008). 태그 줄은 끝에 함께 (D-010)
        CopyBodyCommand = new RelayCommand(_ => Copy(FormatBody().PlainText, "본문과 태그를 복사했습니다."), _ => Body.Length > 0);
        CopyForNaverCommand = new RelayCommand(_ => CopyForNaver(), _ => Body.Length > 0);
        SendToNaverCommand = new RelayCommand(_ => SendToNaver(), _ => Body.Length > 0);
        SaveCommand = new RelayCommand(async _ => await SaveAsync(), _ => !IsBusy && HasContent);
        OpenListCommand = new RelayCommand(async _ => await OpenFromListAsync(), _ => !IsBusy);
    }

    public RelayCommand GenerateCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand CopyTitleCommand { get; }
    public RelayCommand CopyBodyCommand { get; }
    public RelayCommand CopyForNaverCommand { get; }
    public RelayCommand SendToNaverCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand OpenListCommand { get; }

    public string Topic
    {
        get => _topic;
        set
        {
            if (Set(ref _topic, value))
                GenerateCommand.RaiseCanExecuteChanged();
        }
    }

    public string Keywords
    {
        get => _keywords;
        set => Set(ref _keywords, value);
    }

    public string Extra
    {
        get => _extra;
        set => Set(ref _extra, value);
    }

    public string Title
    {
        get => _title;
        set
        {
            if (!Set(ref _title, value))
                return;
            CopyTitleCommand.RaiseCanExecuteChanged();
            OnResultEdited();
        }
    }

    public string Body
    {
        get => _body;
        set
        {
            if (!Set(ref _body, value))
                return;
            CopyBodyCommand.RaiseCanExecuteChanged();
            CopyForNaverCommand.RaiseCanExecuteChanged();
            SendToNaverCommand.RaiseCanExecuteChanged();
            OnResultEdited();
        }
    }

    public string TagsText
    {
        get => _tagsText;
        set
        {
            if (Set(ref _tagsText, value))
                OnResultEdited();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value))
                return;
            GenerateCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
            SaveCommand.RaiseCanExecuteChanged();
            OpenListCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (Set(ref _isDirty, value))
                OnPropertyChanged(nameof(CurrentLabel));
        }
    }

    /// <summary>
    /// 창 닫기·새로 만들기·불러오기 전에 물어야 하는가. 내용이 비었으면 묻지 않는다.
    /// </summary>
    public bool HasUnsavedChanges => IsDirty && HasContent;

    /// <summary>
    /// 결과 칸 머리에 보이는 「폴더 이름 · 저장됨/수정됨」.
    /// </summary>
    public string CurrentLabel => _current is not null
        ? $"{_current.FolderName} · {(IsDirty ? "수정됨" : "저장됨")}"
        : HasContent ? "저장 안 됨" : "";

    public bool IsError
    {
        get => _isError;
        private set => Set(ref _isError, value);
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    private bool CanGenerate => !IsBusy && !string.IsNullOrWhiteSpace(Topic);

    private bool HasContent => Title.Length > 0 || Body.Length > 0;

    /// <summary>
    /// 진행 중인 생성을 멈춘다. 앱 종료 때도 부른다 — claude.exe 는 취소 콜백 안에서 바로 종료된다.
    /// </summary>
    public void CancelGeneration() => _generation?.Cancel();

    /// <summary>
    /// 저장 안 한 수정이 있으면 「예/아니요/취소」를 묻는다. 진행해도 되면 true.
    /// 수정이 없으면 기다리지 않고 바로 끝난다 — 호출자는 같은 스택에서 이어진다고 보고 써야 한다 (LL-080).
    /// </summary>
    public async Task<bool> ConfirmLeaveAsync()
    {
        if (!HasUnsavedChanges)
            return true;

        return _dialogs.AskSaveChanges() switch
        {
            SaveChoice.Save => await SaveAsync(),
            SaveChoice.Discard => true,
            _ => false,
        };
    }

    private async Task GenerateAsync()
    {
        if (!CanGenerate || !await ConfirmLeaveAsync())
            return;

        var request = new DraftRequest(Topic, Keywords, Extra);
        var generation = new CancellationTokenSource();
        _generation = generation;
        IsBusy = true;
        IsError = false;

        var elapsed = Stopwatch.StartNew();
        using var ticking = new CancellationTokenSource();
        var ticker = ShowProgressAsync(elapsed, ticking.Token);

        try
        {
            var draft = await _drafts.CreateAsync(request, generation.Token);
            ticking.Cancel();
            await ticker;
            var done = $"완료 · {elapsed.Elapsed.TotalSeconds:0}초";

            var now = DateTimeOffset.Now;
            var generated = new SavedDraft
            {
                CreatedAt = now,
                UpdatedAt = now,
                Request = request,
                Title = draft.Title,
                Body = draft.Body,
                Tags = [.. draft.Tags],
                Model = ModelAlias,
            };
            ShowDraft(generated);
            SetUnsaved(generated);

            // 생성이 끝나면 바로 남긴다 (add-draft-storage D-002). 실패해도 화면의 글은 그대로 두고 저장 안 됨으로 표시
            try
            {
                var created = await _store.CreateAsync(generated);
                SetSaved(created);
                Report($"{done} · 저장했습니다 — {created.FolderName}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Report($"{done} · 저장하지 못했습니다: {ex.Message} — 「저장」으로 다시 시도하세요.", isError: true);
            }
        }
        catch (OperationCanceledException) when (generation.IsCancellationRequested)
        {
            ticking.Cancel();
            await ticker;
            Report("취소했습니다.");
        }
        catch (ClaudeCliException ex)
        {
            ticking.Cancel();
            await ticker;
            Report(ex.Message, isError: true);
        }
        finally
        {
            ticking.Cancel();
            _generation = null;
            generation.Dispose();
            IsBusy = false;
        }
    }

    private async Task<bool> SaveAsync()
    {
        // 저장 버튼을 빠르게 두 번 누르면 새 폴더가 둘 생길 수 있다
        if (_saving)
            return false;
        _saving = true;

        try
        {
            var now = DateTimeOffset.Now;
            if (_current is null)
            {
                var basis = _pending ?? new SavedDraft { CreatedAt = now, Request = new DraftRequest(Topic, Keywords, Extra) };
                var created = await _store.CreateAsync(FromScreen(basis, now));
                SetSaved(created);
                Report($"저장했습니다 — {created.FolderName}");
                return true;
            }

            var (stored, renameFailed) = await _store.SaveAsync(_current.Folder, FromScreen(_current.Draft, now));
            SetSaved(stored);
            if (renameFailed)
                Report($"내용은 저장했지만 폴더 이름은 바꾸지 못했습니다. 다른 프로그램이 폴더를 쓰고 있을 수 있습니다 — {stored.FolderName}", isError: true);
            else
                Report($"저장했습니다 — {stored.FolderName}");
            return true;
        }
        catch (DraftFolderMissingException ex)
        {
            if (!_dialogs.AskSaveAsNew(Path.GetFileName(ex.Folder)))
            {
                Report("저장하지 않았습니다 — 초안 폴더를 찾을 수 없습니다.", isError: true);
                return false;
            }

            // 만든 날짜·요청은 그대로 두고 새 폴더로
            SetUnsaved(_current!.Draft);
            _saving = false;
            return await SaveAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report($"저장하지 못했습니다: {ex.Message}", isError: true);
            return false;
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task OpenFromListAsync()
    {
        var folder = _dialogs.PickDraft();
        if (folder is null || !await ConfirmLeaveAsync())
            return;

        try
        {
            var stored = await _store.OpenAsync(folder);
            ShowDraft(stored.Draft);
            SetSaved(stored);
            Report($"열었습니다 — {stored.FolderName}");
        }
        catch (Exception ex) when (DraftStore.IsUnreadable(ex))
        {
            Report($"초안을 열지 못했습니다: {ex.Message}", isError: true);
        }
    }

    /// <summary>
    /// 결과 칸과 입력 칸을 채운다. 입력 칸은 그 글을 만든 요청 (add-draft-storage D-011).
    /// </summary>
    private void ShowDraft(SavedDraft draft)
    {
        _applying = true;
        try
        {
            Topic = draft.Request.Topic;
            Keywords = draft.Request.Keywords;
            Extra = draft.Request.Extra;
            Title = draft.Title;
            Body = draft.Body;
            TagsText = string.Join(" ", draft.Tags.Select(tag => "#" + tag));
        }
        finally
        {
            _applying = false;
        }
    }

    private SavedDraft FromScreen(SavedDraft basis, DateTimeOffset now) => basis with
    {
        UpdatedAt = now,
        Title = Title,
        Body = Body,
        Tags = ParseTags(TagsText),
    };

    // 화면은 「#a #b」, 파일은 # 없이 (build-blog-autopost DraftService 와 같은 모양)
    private static List<string> ParseTags(string text) =>
        text.Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => tag.TrimStart('#'))
            .Where(tag => tag.Length > 0)
            .ToList();

    private void SetSaved(StoredDraft stored)
    {
        _current = stored;
        _pending = null;
        IsDirty = false;
        OnPropertyChanged(nameof(CurrentLabel));
    }

    private void SetUnsaved(SavedDraft basis)
    {
        _current = null;
        _pending = basis;
        IsDirty = true;
        OnPropertyChanged(nameof(CurrentLabel));
    }

    private void OnResultEdited()
    {
        SaveCommand.RaiseCanExecuteChanged();
        if (!_applying)
            IsDirty = true;
        OnPropertyChanged(nameof(CurrentLabel));
    }

    private void Report(string message, bool isError = false)
    {
        IsError = isError;
        Status = message;
    }

    private async Task ShowProgressAsync(Stopwatch elapsed, CancellationToken ct)
    {
        Status = "생성 중… 0:00";
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                Status = $"생성 중… {elapsed.Elapsed:m\\:ss}";
        }
        catch (OperationCanceledException)
        {
            // 생성이 끝나 표시를 멈춘 것
        }
    }

    // 태그 줄은 본문 끝에 붙인다 — 네이버가 본문의 #단어를 태그로 등록한다 [실측] (add-naver-blog-format D-010)
    private FormattedBody FormatBody() => NaverFormat.Format(Body, ParseTags(TagsText));

    private bool CopyForNaver()
    {
        var formatted = FormatBody();
        if (_clipboard.TrySetHtml(formatted.Html, formatted.PlainText))
        {
            Report("본문과 태그를 네이버용으로 복사했습니다. 네이버 글쓰기 본문 칸에 Ctrl+V 하세요.");
            return true;
        }

        Report("다른 프로그램이 클립보드를 쓰고 있어 복사하지 못했습니다. 다시 눌러 주세요.", isError: true);
        return false;
    }

    /// <summary>
    /// 본문 네이버용 복사 + 크롬 글쓰기 열기. 붙여넣기·발행은 사람이 한다 (add-naver-blog-format D-009).
    /// </summary>
    private void SendToNaver()
    {
        if (!CopyForNaver())
            return;

        try
        {
            _browser.OpenNaverWrite();
            Report("본문과 태그를 복사하고 네이버 글쓰기를 열었습니다. 본문 칸에 Ctrl+V → 제목 「복사」로 제목을 붙이세요.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // 복사는 이미 됐다 — 둘을 나눠 알린다
            Report($"본문은 복사했지만 크롬을 열지 못했습니다: {ex.Message}", isError: true);
        }
    }

    private void Copy(string text, string doneMessage)
    {
        if (_clipboard.TrySetText(text))
            Report(doneMessage);
        else
            Report("다른 프로그램이 클립보드를 쓰고 있어 복사하지 못했습니다. 다시 눌러 주세요.", isError: true);
    }
}

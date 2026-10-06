using System.Collections.ObjectModel;
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
    private const int MaxPhotos = 10;
    private static readonly TimeSpan HotkeyWindow = TimeSpan.FromMinutes(10);
    private static readonly string[] PhotoExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp"];

    private readonly IDraftService _drafts;
    private readonly IClipboardService _clipboard;
    private readonly IDraftStore _store;
    private readonly IDialogService _dialogs;
    private readonly IBrowserLauncher _browser;
    private readonly IImageShrinker _shrinker;
    private readonly PasteSequencer _sequencer;
    private IGlobalHotkey? _hotkey;
    private CancellationTokenSource? _generation;

    // 화면에 열린 초안의 폴더. null 이면 아직 디스크에 없다
    private StoredDraft? _current;
    // _current 가 없을 때 저장할 바탕(만든 시각·요청). 자동 저장이 실패한 생성 결과 등
    private SavedDraft? _pending;
    // 프로그램이 결과 칸을 채우는 중 — 이때의 변경은 사용자의 수정이 아니다
    private bool _applying;
    private bool _saving;
    private bool _pasting;

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
        IBrowserLauncher browser, IImageShrinker shrinker, PasteSequencer sequencer)
    {
        _drafts = drafts;
        _clipboard = clipboard;
        _store = store;
        _dialogs = dialogs;
        _browser = browser;
        _shrinker = shrinker;
        _sequencer = sequencer;

        GenerateCommand = new RelayCommand(async _ => await GenerateAsync(), _ => CanGenerate);
        CancelCommand = new RelayCommand(_ => CancelGeneration(), _ => IsBusy);
        CopyTitleCommand = new RelayCommand(_ => Copy(Title, "제목을 복사했습니다."), _ => Title.Length > 0);
        // 텍스트로 붙일 곳에는 표시 기호(##·>·---)와 사진 줄을 빼고 넘긴다. 태그 줄은 끝에 함께 (add-naver-blog-format D-008·D-010)
        CopyBodyCommand = new RelayCommand(_ => Copy(FormatBody().PlainText, "본문과 태그를 복사했습니다."), _ => Body.Length > 0);
        CopyForNaverCommand = new RelayCommand(_ => CopyForNaver(), _ => Body.Length > 0);
        SendToNaverCommand = new RelayCommand(_ => SendToNaver(), _ => Body.Length > 0);
        SaveCommand = new RelayCommand(async _ => await SaveAsync(), _ => !IsBusy && HasContent);
        OpenListCommand = new RelayCommand(async _ => await OpenFromListAsync(), _ => !IsBusy);
        AddPhotosCommand = new RelayCommand(async _ => await AddPhotosAsync(), _ => !IsBusy);
        PlacePhotosCommand = new RelayCommand(async _ => await PlacePhotosAsync(),
            _ => !IsBusy && Body.Trim().Length > 0 && Photos.Any(photo => !photo.IsPlaced && !photo.IsMissing));
        RearmCommand = new RelayCommand(_ => Rearm(), _ => Body.Length > 0 && _hotkey is not null && !_pasting);
    }

    public RelayCommand GenerateCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand CopyTitleCommand { get; }
    public RelayCommand CopyBodyCommand { get; }
    public RelayCommand CopyForNaverCommand { get; }
    public RelayCommand SendToNaverCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand OpenListCommand { get; }
    public RelayCommand AddPhotosCommand { get; }
    public RelayCommand PlacePhotosCommand { get; }

    /// <summary>
    /// 크롬을 새로 열지 않고 단축키만 다시 켠다 — 같은 글쓰기 탭에서 다시 붙일 때 (D-017).
    /// </summary>
    public RelayCommand RearmCommand { get; }

    public ObservableCollection<PhotoItemViewModel> Photos { get; } = [];

    public string PhotosHeader
    {
        get
        {
            var unplaced = Photos.Count(photo => !photo.IsPlaced);
            return Photos.Count == 0 ? "사진 없음" : unplaced == 0 ? $"사진 {Photos.Count}장" : $"사진 {Photos.Count}장 · 배치 안 됨 {unplaced}";
        }
    }

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
            RearmCommand.RaiseCanExecuteChanged();
            RefreshPhotos();
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
            AddPhotosCommand.RaiseCanExecuteChanged();
            PlacePhotosCommand.RaiseCanExecuteChanged();
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
    /// 전역 단축키는 창 핸들이 필요해 창을 만든 뒤 붙인다.
    /// </summary>
    public void AttachHotkey(IGlobalHotkey hotkey)
    {
        _hotkey = hotkey;
        hotkey.Pressed += async (_, _) => await PasteToNaverAsync();
        hotkey.Expired += (_, _) => Report($"{hotkey.Gesture} 대기 시간이 지났습니다 — 「네이버로 보내기」를 다시 누르세요.");
    }

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
        var ticker = ShowProgressAsync(elapsed, ticking.Token, "생성 중");

        try
        {
            // 새 초안 폴더로 갈 사진: 지금 붙은 사진 전부를 원본 경로로 들고, 새 폴더에서 받을 이름(01, 02, …)을 미리 정한다.
            // 빈 images\ 에 같은 순서로 복사하면 같은 이름이 나온다 — Claude 에게 알려 준 이름과 맞는다
            var sources = PhotoSources();
            var (inputs, skipped) = await PrepareForClaudeAsync(sources);
            var draft = await _drafts.CreateAsync(request, inputs, generation.Token);
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
                Images = sources.Select(source => source.FileName).ToList(),
            };
            ShowDraft(generated);
            SetPhotos(sources.Select(source => (source.FileName, (string?)source.Path)));
            SetUnsaved(generated);

            // 생성이 끝나면 바로 남긴다 (add-draft-storage D-002). 실패해도 화면의 글은 그대로 두고 저장 안 됨으로 표시
            try
            {
                var created = await _store.CreateAsync(generated);
                await MaterializePhotosAsync(created.Folder);
                SetSaved(created);
                Report($"{done} · 저장했습니다 — {created.FolderName}{SkippedNote(skipped)}");
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

    private async Task AddPhotosAsync()
    {
        var picked = _dialogs.PickImages()
            .Where(path => PhotoExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            .ToList();
        if (picked.Count == 0)
            return;

        var room = MaxPhotos - Photos.Count;
        var note = picked.Count > room ? $" · 사진은 {MaxPhotos}장까지라 {picked.Count - Math.Max(room, 0)}장은 넣지 않았습니다" : "";
        var added = 0;
        foreach (var path in picked.Take(Math.Max(room, 0)))
        {
            try
            {
                if (_current is not null)
                {
                    // 저장된 초안이면 바로 images\ 로 복사한다 (D-005)
                    var name = await _store.AddImageAsync(_current.Folder, path);
                    Photos.Add(NewPhoto(name, null));
                }
                else
                {
                    // 저장 전이면 원본 경로를 들고 있다가 저장할 때 복사한다
                    Photos.Add(NewPhoto(PlannedName(Photos.Count, path), path));
                }
                added++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                note += $" · {Path.GetFileName(path)} 을(를) 넣지 못했습니다: {ex.Message}";
            }
        }

        if (added > 0)
            MarkPhotosChanged();
        Report($"사진 {added}장을 넣었습니다.{note}", isError: note.Length > 0 && added == 0);
    }

    private void RemovePhoto(PhotoItemViewModel photo)
    {
        // 본문의 그 사진 줄을 지우고 목록에서 뺀다. 파일은 지우지 않는다 — 사용자 데이터 (D-005)
        Photos.Remove(photo);
        if (photo.IsPlaced)
            Body = PhotoMarkers.RemoveFile(Body, photo.FileName);
        MarkPhotosChanged();
        Report($"{photo.FileName} 을(를) 뺐습니다.");
    }

    private async Task PlacePhotosAsync()
    {
        var unplaced = Photos.Where(photo => !photo.IsPlaced && !photo.IsMissing)
            .Select(photo => new PhotoSource(photo.FileName, ResolvePhoto(photo)!))
            .Where(source => source.Path is not null)
            .ToList();
        if (unplaced.Count == 0 || Body.Trim().Length == 0)
            return;

        var generation = new CancellationTokenSource();
        _generation = generation;
        IsBusy = true;
        IsError = false;
        var elapsed = Stopwatch.StartNew();
        using var ticking = new CancellationTokenSource();
        var ticker = ShowProgressAsync(elapsed, ticking.Token, "사진 배치 중");

        try
        {
            var (inputs, skipped) = await PrepareForClaudeAsync(unplaced);
            var placements = await _drafts.PlacePhotosAsync(Body, inputs, generation.Token);
            ticking.Cancel();
            await ticker;

            // 글자는 바꾸지 않고 사진 줄만 끼운다 (D-015)
            Body = PhotoMarkers.InsertAfterBlocks(Body, placements);
            var left = Photos.Count(photo => !photo.IsPlaced);
            Report($"사진 {placements.Count}장을 배치했습니다 · {elapsed.Elapsed.TotalSeconds:0}초"
                   + (left > 0 ? $" · 배치 안 됨 {left}장" : "") + SkippedNote(skipped));
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
                await MaterializePhotosAsync(created.Folder);
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

            // 만든 날짜·요청은 그대로 두고 새 폴더로. 사진 원본도 옛 폴더와 함께 사라졌다 — 찾을 수 있는 것만 남는다
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
            // 목록에 없더라도 본문이 가리키는 사진은 보이게 한다
            var files = stored.Draft.Images.Concat(PhotoMarkers.PlacedFiles(stored.Draft.Body))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            SetSaved(stored);
            SetPhotos(files.Select(file => (file, (string?)null)));
            Report($"열었습니다 — {stored.FolderName}");
        }
        catch (Exception ex) when (DraftStore.IsUnreadable(ex))
        {
            Report($"초안을 열지 못했습니다: {ex.Message}", isError: true);
        }
    }

    // ===== 사진

    private sealed record PhotoSource(string FileName, string? Path);

    /// <summary>
    /// 새 초안 폴더로 옮길 사진: 붙은 순서대로 받을 이름 01, 02, … 과 지금 원본 위치.
    /// </summary>
    private List<PhotoSource> PhotoSources() =>
        Photos.Select((photo, index) => (photo, path: ResolvePhoto(photo)))
            .Where(item => item.path is not null)
            .Select((item, index) => new PhotoSource(PlannedName(index, item.path!), item.path))
            .ToList();

    private static string PlannedName(int index, string path) => $"{index + 1:00}{Path.GetExtension(path).ToLowerInvariant()}";

    private string? ResolvePhoto(PhotoItemViewModel photo) =>
        photo.SourcePath ?? (_current is null ? null : _store.FindImage(_current.Folder, photo.FileName));

    /// <summary>
    /// Claude 에게 보낼 줄인 사본. 읽거나 줄이지 못한 사진은 빼고 개수를 돌려준다. 줄이기는 무거워 백그라운드에서 (LL-034).
    /// </summary>
    private async Task<(List<ImageInput> Inputs, int Skipped)> PrepareForClaudeAsync(IReadOnlyList<PhotoSource> sources)
    {
        var inputs = new List<ImageInput>();
        var skipped = 0;
        foreach (var source in sources)
        {
            try
            {
                var original = await _store.ReadImageAsync(source.Path!);
                inputs.Add(await Task.Run(() => _shrinker.Shrink(source.FileName, original)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                skipped++;
            }
        }
        return (inputs, skipped);
    }

    /// <summary>
    /// 아직 원본 경로만 있는 사진을 초안 images\ 로 복사한다. 받은 이름이 미리 정한 이름과 다르면 본문 줄도 고친다.
    /// </summary>
    private async Task MaterializePhotosAsync(string folder)
    {
        foreach (var photo in Photos.Where(photo => photo.SourcePath is not null).ToList())
        {
            var name = await _store.AddImageAsync(folder, photo.SourcePath!);
            if (!string.Equals(name, photo.FileName, StringComparison.OrdinalIgnoreCase))
            {
                _applying = true;
                try
                {
                    Body = Body.Replace($"| {photo.FileName}]", $"| {name}]", StringComparison.OrdinalIgnoreCase);
                }
                finally
                {
                    _applying = false;
                }
                photo.FileName = name;
            }
            photo.SourcePath = null;
        }
        RefreshPhotos();
    }

    private void SetPhotos(IEnumerable<(string FileName, string? SourcePath)> photos)
    {
        Photos.Clear();
        foreach (var (fileName, sourcePath) in photos)
            Photos.Add(NewPhoto(fileName, sourcePath));
        RefreshPhotos();
    }

    private PhotoItemViewModel NewPhoto(string fileName, string? sourcePath) => new(fileName, sourcePath, RemovePhoto);

    private void MarkPhotosChanged()
    {
        RefreshPhotos();
        if (!_applying)
            IsDirty = true;
        SaveCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 본문의 사진 줄로 배치 여부·설명을 다시 매긴다.
    /// </summary>
    private void RefreshPhotos()
    {
        var slots = PhotoMarkers.Slots(Body);
        foreach (var photo in Photos)
        {
            var slot = slots.FirstOrDefault(s => string.Equals(s.File, photo.FileName, StringComparison.OrdinalIgnoreCase));
            photo.Update(slot?.Description, missing: ResolvePhoto(photo) is null && _current is not null);
        }
        OnPropertyChanged(nameof(PhotosHeader));
        PlacePhotosCommand.RaiseCanExecuteChanged();
    }

    private static string SkippedNote(int skipped) => skipped > 0 ? $" · 읽지 못한 사진 {skipped}장은 빼고 보냈습니다" : "";

    // ===== 공통

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
        // 붙은 사진 전부(추가 순서). 배치 여부는 본문으로 판단한다 (D-010)
        Images = Photos.Select(photo => photo.FileName).ToList(),
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
        RefreshPhotos();
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

    private async Task ShowProgressAsync(Stopwatch elapsed, CancellationToken ct, string what)
    {
        Status = $"{what}… 0:00";
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                Status = $"{what}… {elapsed.Elapsed:m\\:ss}";
        }
        catch (OperationCanceledException)
        {
            // 끝나 표시를 멈춘 것
        }
    }

    // ===== 네이버

    // 태그 줄은 본문 끝에 붙인다 — 네이버가 본문의 #단어를 태그로 등록한다 [실측] (add-naver-blog-format D-010)
    private FormattedBody FormatBody() => NaverFormat.Format(Body, ParseTags(TagsText));

    private bool CopyForNaver()
    {
        var formatted = FormatBody();
        if (_clipboard.TrySetHtml(formatted.Html, formatted.PlainText))
        {
            Report("본문과 태그를 네이버용으로 복사했습니다(사진 제외). 네이버 글쓰기 본문 칸에 Ctrl+V 하세요.");
            return true;
        }

        Report("다른 프로그램이 클립보드를 쓰고 있어 복사하지 못했습니다. 다시 눌러 주세요.", isError: true);
        return false;
    }

    /// <summary>
    /// 크롬 글쓰기 열기 + 단축키 대기. 사용자가 네이버 본문을 클릭하고 단축키를 누르면 글·사진을 차례로 붙인다 (D-003, D-007).
    /// 글만 복사해 두므로 단축키 없이 Ctrl+V 해도 글은 들어간다.
    /// </summary>
    private void SendToNaver()
    {
        if (!CopyForNaver())
            return;

        try
        {
            _browser.OpenNaverWrite();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // 복사는 이미 됐다 — 둘을 나눠 알린다
            Report($"본문은 복사했지만 크롬을 열지 못했습니다: {ex.Message}", isError: true);
            return;
        }

        if (_hotkey is null || !_hotkey.Arm(HotkeyWindow))
        {
            Report($"네이버 글쓰기를 열었습니다. 단축키 {_hotkey?.Gesture ?? ""}를 다른 프로그램이 쓰고 있어 켜지 못했습니다 — 본문 칸에 Ctrl+V 하면 글만 들어갑니다.",
                isError: true);
            return;
        }

        var photos = PhotoMarkers.Slots(Body).Count(slot => slot.File is not null);
        Report($"네이버 글쓰기를 열었습니다. 본문 칸을 클릭하고 {_hotkey.Gesture} — 글{(photos > 0 ? $"·사진 {photos}장" : "")}을 순서대로 붙입니다(10분 동안, 멈추려면 Esc).");
    }

    private void Rearm()
    {
        if (_hotkey is null)
            return;
        if (!_hotkey.Arm(HotkeyWindow))
        {
            Report($"단축키 {_hotkey.Gesture}를 다른 프로그램이 쓰고 있어 켜지 못했습니다.", isError: true);
            return;
        }
        Report($"단축키를 다시 켰습니다. 네이버 글쓰기 본문을 클릭하고 {_hotkey.Gesture} (10분 동안, 멈추려면 Esc).");
    }

    private async Task PasteToNaverAsync()
    {
        if (_pasting || _hotkey is null)
            return;
        _pasting = true;
        RearmCommand.RaiseCanExecuteChanged();
        _hotkey.Disarm();
        try
        {
            var segments = PhotoMarkers.Segments(Body, ParseTags(TagsText), ResolveFileForPaste, out var skipped);
            var result = await _sequencer.RunAsync(segments, Title);
            var skippedNote = skipped > 0 ? $" · 사진이 없는 자리 {skipped}개는 건너뜀" : "";
            switch (result.Outcome)
            {
                case PasteOutcome.Completed:
                    Report($"글·사진을 모두 붙였습니다(조각 {result.Total}개){skippedNote}. 제목이 복사돼 있습니다 — 제목 칸에 Ctrl+V 하세요.");
                    break;
                case PasteOutcome.NotTargetWindow:
                    _hotkey.Arm(HotkeyWindow);
                    Report($"크롬 네이버 글쓰기 본문을 클릭한 뒤 {_hotkey.Gesture} 를 눌러 주세요.", isError: true);
                    break;
                case PasteOutcome.ModifiersHeld:
                    _hotkey.Arm(HotkeyWindow);
                    Report($"Ctrl·Alt 를 뗀 뒤 다시 {_hotkey.Gesture} 를 눌러 주세요.", isError: true);
                    break;
                // 멈춘 경우는 바로 다시 켠다 — 네이버에서 되돌린 뒤 같은 탭에서 다시 누를 수 있게 (D-017).
                // 다 붙인 경우는 켜지 않는다 — 두 번 붙는 실수를 막는다
                case PasteOutcome.WindowChanged:
                case PasteOutcome.Escaped:
                    _hotkey.Arm(HotkeyWindow);
                    Report($"조각 {result.Done}/{result.Total} 에서 멈췄습니다. 네이버에서 Ctrl+Z 로 되돌린 뒤 본문을 클릭하고 다시 {_hotkey.Gesture} (10분 동안).", isError: true);
                    break;
                case PasteOutcome.ClipboardFailed:
                    _hotkey.Arm(HotkeyWindow);
                    Report($"조각 {result.Done + 1}/{result.Total} 을 클립보드에 넣지 못해 멈췄습니다. 되돌린 뒤 다시 {_hotkey.Gesture} 를 눌러 주세요.", isError: true);
                    break;
            }
        }
        catch (InvalidOperationException ex)
        {
            Report($"붙이기를 멈췄습니다: {ex.Message}", isError: true);
        }
        finally
        {
            _pasting = false;
            RearmCommand.RaiseCanExecuteChanged();
        }
    }

    private string? ResolveFileForPaste(string fileName)
    {
        var photo = Photos.FirstOrDefault(p => string.Equals(p.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        if (photo?.SourcePath is not null)
            return photo.SourcePath;
        return _current is null ? null : _store.FindImage(_current.Folder, fileName);
    }

    private void Copy(string text, string doneMessage)
    {
        if (_clipboard.TrySetText(text))
            Report(doneMessage);
        else
            Report("다른 프로그램이 클립보드를 쓰고 있어 복사하지 못했습니다. 다시 눌러 주세요.", isError: true);
    }
}

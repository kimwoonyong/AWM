using AWM.Models;

namespace AWM.ViewModels;

/// <summary>
/// 설정 창. 「저장」을 누르면 Result 에 새 설정을 담고 창을 닫게 한다. 파일에 쓰는 것은 메인 창 쪽(SettingsStore).
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _original;
    private readonly string _defaultFolder;
    private readonly Func<string, string?> _pickFolder;

    private DraftLength _length;
    private ToneKind _tone;
    private string _customTone;
    private string _tagMinText;
    private string _tagMaxText;
    private string? _draftsFolder;

    public SettingsViewModel(AppSettings current, string defaultFolder, Func<string, string?> pickFolder)
    {
        _original = current;
        _defaultFolder = defaultFolder;
        _pickFolder = pickFolder;
        _length = current.Length;
        _tone = current.Tone;
        _customTone = current.CustomTone;
        _tagMinText = current.TagMin.ToString();
        _tagMaxText = current.TagMax.ToString();
        _draftsFolder = current.DraftsFolder;

        SaveCommand = new RelayCommand(_ => Save(), _ => Error.Length == 0);
        ChangeFolderCommand = new RelayCommand(_ => ChangeFolder());
        DefaultFolderCommand = new RelayCommand(_ => DraftsFolder = null, _ => _draftsFolder is not null);
    }

    /// <summary>
    /// 「저장」이면 창이 듣고 DialogResult = true 로 닫는다.
    /// </summary>
    public event Action? CloseRequested;

    public AppSettings? Result { get; private set; }

    public RelayCommand SaveCommand { get; }
    public RelayCommand ChangeFolderCommand { get; }
    public RelayCommand DefaultFolderCommand { get; }

    // ── 분량 (라디오 버튼은 bool 로 묶는다)
    public bool IsShort { get => _length == DraftLength.Short; set => SetLength(value, DraftLength.Short); }
    public bool IsNormal { get => _length == DraftLength.Normal; set => SetLength(value, DraftLength.Normal); }
    public bool IsLong { get => _length == DraftLength.Long; set => SetLength(value, DraftLength.Long); }

    // ── 말투
    public bool IsFriendly { get => _tone == ToneKind.Friendly; set => SetTone(value, ToneKind.Friendly); }
    public bool IsInformative { get => _tone == ToneKind.Informative; set => SetTone(value, ToneKind.Informative); }
    public bool IsDiary { get => _tone == ToneKind.Diary; set => SetTone(value, ToneKind.Diary); }
    public bool IsCustom { get => _tone == ToneKind.Custom; set => SetTone(value, ToneKind.Custom); }

    public string CustomTone
    {
        get => _customTone;
        set
        {
            if (Set(ref _customTone, value))
                OnPropertyChanged(nameof(CustomToneCount));
        }
    }

    /// <summary>
    /// 칸 아래 글자 수. 한도는 칸의 MaxLength 가 막는다 — 넘게는 입력되지 않는다 (D-013).
    /// </summary>
    public string CustomToneCount => $"{_customTone.Length:N0} / {AppSettings.CustomToneLimit:N0}자";

    // ── 태그
    public string TagMinText
    {
        get => _tagMinText;
        set
        {
            if (Set(ref _tagMinText, value))
                OnValidityChanged();
        }
    }

    public string TagMaxText
    {
        get => _tagMaxText;
        set
        {
            if (Set(ref _tagMaxText, value))
                OnValidityChanged();
        }
    }

    // ── 저장 위치
    public string? DraftsFolder
    {
        get => _draftsFolder;
        private set
        {
            if (!Set(ref _draftsFolder, value))
                return;
            OnPropertyChanged(nameof(FolderDisplay));
            DefaultFolderCommand.RaiseCanExecuteChanged();
        }
    }

    public string FolderDisplay => _draftsFolder ?? $"{_defaultFolder} (기본)";

    /// <summary>
    /// 입력이 잘못됐을 때 창 안에 보이는 문구. 비어 있으면 저장할 수 있다.
    /// </summary>
    public string Error =>
        int.TryParse(_tagMinText.Trim(), out var min) && int.TryParse(_tagMaxText.Trim(), out var max)
        && AppSettings.IsValidTagRange(min, max)
            ? ""
            : $"태그 개수는 1~{AppSettings.TagLimit} 사이의 숫자로, 최소가 최대보다 크지 않게 넣어 주세요.";

    private void SetLength(bool selected, DraftLength length)
    {
        if (!selected || _length == length)
            return;
        _length = length;
        OnPropertyChanged(nameof(IsShort));
        OnPropertyChanged(nameof(IsNormal));
        OnPropertyChanged(nameof(IsLong));
    }

    private void SetTone(bool selected, ToneKind tone)
    {
        if (!selected || _tone == tone)
            return;
        _tone = tone;
        OnPropertyChanged(nameof(IsFriendly));
        OnPropertyChanged(nameof(IsInformative));
        OnPropertyChanged(nameof(IsDiary));
        OnPropertyChanged(nameof(IsCustom));
    }

    private void OnValidityChanged()
    {
        OnPropertyChanged(nameof(Error));
        SaveCommand.RaiseCanExecuteChanged();
    }

    private void ChangeFolder()
    {
        if (_pickFolder(_draftsFolder ?? _defaultFolder) is { } folder)
            DraftsFolder = folder;
    }

    private void Save()
    {
        if (Error.Length > 0)
            return;
        Result = _original with
        {
            Length = _length,
            Tone = _tone,
            // 칸이 한도를 막지만 코드로 넣은 값까지 한 번 더 자른다
            CustomTone = (_customTone.Length > AppSettings.CustomToneLimit ? _customTone[..AppSettings.CustomToneLimit] : _customTone).Trim(),
            TagMin = int.Parse(_tagMinText.Trim()),
            TagMax = int.Parse(_tagMaxText.Trim()),
            DraftsFolder = _draftsFolder,
        };
        CloseRequested?.Invoke();
    }
}

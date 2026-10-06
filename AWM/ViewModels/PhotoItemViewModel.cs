namespace AWM.ViewModels;

/// <summary>
/// 「사진」 목록 한 줄. FileName 은 초안 images\ 안의 이름(아직 저장 전이면 저장할 때 받을 이름).
/// SourcePath 가 있으면 아직 초안 폴더로 복사하지 않은 원본이다.
/// </summary>
public sealed class PhotoItemViewModel : ObservableObject
{
    private string _description = "";
    private bool _isPlaced;
    private bool _isMissing;

    public PhotoItemViewModel(string fileName, string? sourcePath, Action<PhotoItemViewModel> remove)
    {
        FileName = fileName;
        SourcePath = sourcePath;
        RemoveCommand = new RelayCommand(_ => remove(this));
    }

    public string FileName { get; set; }

    public string? SourcePath { get; set; }

    public RelayCommand RemoveCommand { get; }

    public string Label => IsMissing
        ? $"{FileName} — 파일을 찾을 수 없음"
        : IsPlaced ? $"{FileName} — {(_description.Length > 0 ? _description : "배치됨")}" : $"{FileName} — 배치 안 됨";

    public bool IsPlaced
    {
        get => _isPlaced;
        private set => Set(ref _isPlaced, value);
    }

    public bool IsMissing
    {
        get => _isMissing;
        private set => Set(ref _isMissing, value);
    }

    /// <param name="description">본문 사진 줄의 설명. 배치되지 않았으면 null.</param>
    public void Update(string? description, bool missing)
    {
        _description = description ?? "";
        IsPlaced = description is not null;
        IsMissing = missing;
        OnPropertyChanged(nameof(Label));
    }
}

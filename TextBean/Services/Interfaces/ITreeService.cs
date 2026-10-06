using TextBean.Models;

namespace TextBean.Services.Interfaces;

/// <summary>
/// 파일시스템 게이트웨이. 디스크에 닿는 코드는 여기와 DocumentStore 밖에서 금지 (PROHIBITED-CUSTOM-03).
/// </summary>
public interface ITreeService
{
    string Root { get; }

    /// <summary>
    /// 루트를 이 인스턴스 안에서 교체한다. 새 TreeService를 만들어 갈아끼우면
    /// DocumentStore가 생성자에서 붙잡은 옛 인스턴스가 그대로 남아,
    /// 트리는 새 금고를 보여주는데 문서는 하나도 안 열리는 상태가 된다.
    /// </summary>
    void SetRoot(string root);

    Task<TreeNode> ScanAsync(CancellationToken ct = default);

    string CreateFolder(string parentFullPath, string name);

    /// .bak 도 함께 옮긴다. 두고 가면 옛 이름의 고아 백업이 남는다.
    string Rename(string fullPath, string newName);

    void MoveToTrash(string fullPath, string stamp);

    /// <summary>
    /// 옮겨도 되는지 판정한다. 디스크를 바꾸지 않는다.
    /// OS도 잘못된 이동을 대부분 막아 주지만 이유를 설명하지 않으므로, 사유를 우리가 만든다.
    /// </summary>
    MoveCheck CheckMove(string sourceFullPath, string destinationFolder);

    /// <summary>
    /// 본체와 .history 하위 트리를 함께 옮기고 새 경로를 돌려준다.
    /// CheckMove 가 거부한 이동이면 <see cref="InvalidOperationException"/>.
    /// </summary>
    string Move(string sourceFullPath, string destinationFolder);

    /// <summary>
    /// 이 항목이 만들어 낼 수 있는 가장 긴 파생 경로 길이. 폴더면 하위 전체를 본다.
    /// 폴더 자신만 재면 검사를 통과시킨 뒤 자손 문서가 저장·삭제 불가가 된다.
    /// </summary>
    int DeepestDerivedLength(string fullPath, string stamp);

    void EmptyTrash();

    int CountTrashItems();

    /// 메모리 트리가 아니라 디스크를 확인한다 — 트리는 F5로만 갱신된다.
    bool NameTaken(string parentFullPath, string fileOrFolderName);

    /// 파일 존재 확인. ViewModel이 File.Exists를 직접 부르지 않게 하는 통로다
    /// (PROHIBITED-CUSTOM-03 — 파일 I/O는 이 게이트웨이 안에서만).
    bool FileExists(string fullPath);

    /// 금고 루트가 디스크에 있는지. 하위 항목이 없어진 것과 금고 자체가 없어진 것을 가르는 데 쓴다.
    bool RootExists();

    /// <summary>
    /// 루트 밖이면, 또는 루트 아래 이미 있는 구간 중 하나라도 링크(재파스 포인트)면 예외.
    /// 링크면 <see cref="LinkedPathException"/> 이다 — 스캔 뒤 밖에서 폴더가 정션으로 바뀌면
    /// 글자로는 금고 안인데 실제로는 금고 밖에 쓰거나 지운다.
    /// </summary>
    void EnsureInsideRoot(string fullPath);
}

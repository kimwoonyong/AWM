namespace TextBean.Services.Interfaces;

/// <summary>
/// Windows 탐색기로 금고 안의 위치를 보여준다. 앱이 외부 셸을 부르는 유일한 통로다
/// (PROHIBITED-CUSTOM-07). ViewModel 은 이 인터페이스만 알고, 테스트는 가짜로 바꿔 끼운다 —
/// 그러지 않으면 테스트를 돌릴 때마다 진짜 탐색기 창이 뜬다.
///
/// 두 메서드 모두 대상을 찾지 못하면 <c>false</c> 를 돌려준다. 조용히 다른 곳을 열지 않는다 —
/// explorer.exe 는 없는 경로나 260자 넘는 경로에서 바탕 화면이나 엉뚱한 실제 파일을 열고,
/// 종료 코드는 성공이든 실패든 1이라 앱이 알아챌 방법이 없었다 [실측].
/// </summary>
public interface IExplorerLauncher
{
    /// 폴더 <b>안</b>을 연다.
    Task<bool> OpenFolderAsync(string folderFullPath);

    /// 상위 폴더를 열고 그 항목을 선택해 둔다.
    Task<bool> RevealAsync(string itemFullPath);
}

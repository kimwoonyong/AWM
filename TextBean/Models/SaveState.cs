namespace TextBean.Models;

/// <summary>
/// 탭 마커가 바인딩하는 상태.
/// 자동 저장(D-015) 때문에 "수정됨"은 마지막 입력 후 1.5초 + 저장 9~95ms 동안만 켜진다 [실측] —
/// 그것만으로는 표시로 쓸모가 없다. 지속되는 위험 상태인 Failed 를 따로 가른다.
/// </summary>
public enum SaveState
{
    Saved,
    Saving,
    Failed,
    ReadOnly
}

namespace TextBean.Services;

/// <summary>
/// 금고 안 경로가 링크(정션·심볼릭 링크)를 거쳐 간다. 따라가면 금고 밖에 쓰거나 지운다.
/// 루트 밖 경로 거부와 같은 부류라 <see cref="UnauthorizedAccessException"/> 을 상속한다 — 기존 catch 가 그대로 잡는다.
/// 메시지는 금고 안의 링크 자리만 알린다. 링크가 가리키는 곳은 금고 밖 정보라 넣지 않는다.
/// </summary>
public sealed class LinkedPathException(string linkInsideVault, bool appArea)
    : UnauthorizedAccessException(MessageFor(linkInsideVault, appArea))
{
    // 앱 영역(.history · .trash)은 트리에 없어 F5 로는 풀리지 않는다. 같은 안내를 주면
    // 사용자는 새로고침하고 다시 눌러 같은 알림을 계속 본다.
    private static string MessageFor(string link, bool appArea)
        => $"금고 안 '{link}' 이(가) 다른 위치를 가리키는 링크로 바뀌어 있어 멈췄습니다.\n\n" +
           "금고 밖에 쓰거나 지우지 않았습니다. " +
           (appArea
               ? "이 폴더는 앱이 이력·휴지통을 두는 곳이라 새로고침으로는 풀리지 않습니다. 금고 폴더를 탐색기에서 확인해주세요."
               : "F5 로 새로고침하세요.");
}

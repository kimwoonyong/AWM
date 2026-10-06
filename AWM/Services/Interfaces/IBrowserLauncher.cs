namespace AWM.Services.Interfaces;

public interface IBrowserLauncher
{
    /// <summary>
    /// 크롬으로 네이버 블로그 글쓰기를 연다. 로그인이 안 돼 있으면 네이버가 로그인 뒤 글쓰기로 보낸다 [실측 — 302].
    /// 크롬이 없으면 <see cref="InvalidOperationException"/>, 실행 실패는 <see cref="System.ComponentModel.Win32Exception"/>.
    /// </summary>
    void OpenNaverWrite();
}

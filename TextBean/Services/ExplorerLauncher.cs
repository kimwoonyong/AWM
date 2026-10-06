using System.IO;                      // UseWPF 가 암시적 using 에서 System.IO 를 뺀다
using System.Reflection;
using System.Runtime.InteropServices;
using TextBean.Services.Interfaces;

namespace TextBean.Services;

/// <summary>
/// 셸 API(PIDL)로 탐색기를 연다. P/Invoke 는 이 파일 밖에서 금지다 (PROHIBITED-CUSTOM-07).
/// WPF 를 참조하지 않으므로 KeyDocumentCodec 과 같이 Services/ 에 둔다.
///
/// explorer.exe 에 경로 문자열을 넘기는 방식을 쓰지 않는 이유 [실측]:
/// - 260자를 넘으면 앞 259자만 남기고 잘라 해석해, 금고 안의 <b>다른 실제 파일</b>을 선택했다
/// - 없는 경로면 바탕 화면이나 '문서' 폴더를 열었고, 종료 코드는 성공·실패 모두 1이었다
/// - 누를 때마다 새 창이 떴다
/// PIDL 방식은 없는 경로를 NULL 로 돌려주고, 1,373자 경로까지 정확히 열었고, 같은 폴더 창을 재사용했다.
///
/// 문자열 경로는 ILCreateFromPathW 에 한 번 넘기고, 그 뒤로는 PIDL 만 쓴다.
/// 경로를 셸에 문자열로 실행시키면(UseShellExecute) 없어진 폴더 이름 옆의 .cmd 가 실행됐다 [실측].
/// </summary>
public sealed class ExplorerLauncher : IExplorerLauncher
{
    private const uint SeeMaskClassName = 0x00000001;
    private const uint SeeMaskIdList = 0x00000004;
    private const uint SeeMaskFlagNoUi = 0x00000400;
    private const int SwShowNormal = 1;

    private const uint ShgfiPidl = 0x00000008;
    private const uint ShgfiAttributes = 0x00000800;
    private const uint ShgfiAttrSpecified = 0x00020000;
    private const uint SfgaoFolder = 0x20000000;
    private const uint SfgaoStream = 0x00400000;

    private const string Shell32 = "shell32.dll";

    /// <summary>
    /// shell32 를 System32 의 전체 경로로만 로드한다.
    ///
    /// <c>[DefaultDllImportSearchPaths(System32)]</c> 만으로는 막히지 않는다 [실측]. 호스트가 넘기는 네이티브 탐색 경로
    /// (NATIVE_DLL_SEARCH_DIRECTORIES)가 그 특성보다 먼저 탐색되기 때문이다 [실측]. 그 경로는 배치마다 다르다:
    ///   - 비단일파일 빌드: 앱 폴더
    ///   - 단일 파일 게시(IncludeNativeLibrariesForSelfExtract): 추출 폴더 %TEMP%\.net\TextBean\{hash}\
    /// 두 경우 모두 특성만 둔 상태에서 가짜 shell32.dll 이 로드돼 EntryPointNotFoundException 이 났고,
    /// 리졸버를 두자 System32 것만 로드됐다 [실측]. 리졸버는 그 모든 탐색보다 먼저 불린다.
    /// 빌드도 테스트도 이 누락을 잡지 못한다.
    ///
    /// <b>이 리졸버는 이 어셈블리의 shell32 호출만 지킨다.</b> WPF 자체의 P/Invoke 는 exe 폴더를 먼저 뒤져,
    /// exe 옆에 심긴 가짜 DLL 을 창을 띄우는 것만으로 로드한다 [실측 — 이번 변경 전부터 있던 성질].
    /// 프로세스 전체의 방어는 exe 를 남이 파일을 떨굴 수 없는 폴더에 두는 것뿐이다 (D-031).
    ///
    /// 리졸버는 어셈블리당 하나다. 이 어셈블리의 P/Invoke 는 이 파일에만 있고(PROHIBITED-CUSTOM-07),
    /// shell32 가 아니면 IntPtr.Zero 를 돌려 기본 동작에 맡긴다.
    /// </summary>
    static ExplorerLauncher()
        => NativeLibrary.SetDllImportResolver(typeof(ExplorerLauncher).Assembly, ResolveFromSystem32);

    private static IntPtr ResolveFromSystem32(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        => string.Equals(libraryName, Shell32, StringComparison.OrdinalIgnoreCase)
            ? NativeLibrary.Load(Path.Combine(Environment.SystemDirectory, Shell32))
            : IntPtr.Zero;

    // 새 창을 만들 때 호출이 0.3~0.6초 걸리고, UI 스레드에서 부르면 화면이 그만큼 멈춘다 [실측].
    // 스레드 풀(MTA)에서도 셸 호출은 정상 동작한다 — 별도 CoInitialize 가 필요 없다 [실측].
    public Task<bool> OpenFolderAsync(string folderFullPath) => Task.Run(() => OpenFolder(folderFullPath));

    public Task<bool> RevealAsync(string itemFullPath) => Task.Run(() => Reveal(itemFullPath));

    private static bool Reveal(string path)
    {
        // 없는 경로면 NULL 이다. 실패 판정은 이것만 믿는다 — GetLastError 는 성공해도 87 이 남아 있다 [실측].
        var pidl = ILCreateFromPathW(path);
        if (pidl == IntPtr.Zero) return false;

        try
        {
            // cidl=0 이면 pidlFolder 자체를 "선택할 항목"으로 보고 그 상위 폴더를 연다.
            return SHOpenFolderAndSelectItems(pidl, 0, IntPtr.Zero, 0) >= 0;
        }
        finally
        {
            ILFree(pidl);
        }
    }

    private static bool OpenFolder(string path)
    {
        var pidl = ILCreateFromPathW(path);
        if (pidl == IntPtr.Zero) return false;

        try
        {
            // 진짜 폴더일 때만 연다. 트리를 새로 읽기 전에 밖에서 폴더가 지워지고 같은 이름의 파일·바로가기가
            // 생기면, 아래 ShellExecuteExW 는 TRUE 를 돌려주면서 아무 창도 열지 않았다 [실측] —
            // 사용자는 반응 없음을 보고 "찾지 못하면 알림"(사용자 확정 Q-2)이 지켜지지 않았다.
            // zip 은 셸에서 폴더이면서 스트림이라 STREAM 으로 걸러낸다 [실측 분류].
            if (!IsRealFolder(pidl)) return false;

            var info = new ShellExecuteInfo
            {
                cbSize = Marshal.SizeOf<ShellExecuteInfo>(),

                // CLASSNAME + "folder": 위의 폴더 검사를 지나온 뒤에도 폴더로만 다룬다(검사와 호출 사이 경쟁에 대비).
                // 폴더 자리의 실행 파일은 이 지정만으로도 실행되지 않았다 [실측 — 대조군: 직접 실행하면 돈다].
                // FLAG_NO_UI 는 이 프로세스의 UI 만 막는다. 검사와 호출 사이에 폴더가 지워지면 탐색기 자체가
                // "위치를 사용할 수 없습니다" 창을 띄운다 [실측] — 마이크로초 경쟁이라 받아들인다.
                // 여기서 돌아오는 TRUE 는 "탐색기에 넘겼다"는 뜻일 뿐이다. 실패 판정은 위의 두 검사가 맡는다.
                fMask = SeeMaskIdList | SeeMaskClassName | SeeMaskFlagNoUi,
                lpVerb = "open",
                lpClass = "folder",
                lpIDList = pidl,
                nShow = SwShowNormal,
            };
            return ShellExecuteExW(ref info);
        }
        finally
        {
            ILFree(pidl);
        }
    }

    /// <summary>
    /// 셸이 보기에 진짜 폴더인가. 파일 I/O(Directory.Exists)가 아니라 이미 만든 PIDL 에 대한 질의다 —
    /// 파일 I/O 는 TreeService/DocumentStore 밖에서 금지다 (PROHIBITED-CUSTOM-03).
    /// ATTR_SPECIFIED 로 두 속성만 묻는다. 전부 물으면 셸이 느린 속성까지 계산한다.
    /// </summary>
    private static bool IsRealFolder(IntPtr pidl)
    {
        var info = new ShFileInfo { dwAttributes = SfgaoFolder | SfgaoStream };
        var ok = SHGetFileInfoW(pidl, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(),
                                ShgfiPidl | ShgfiAttributes | ShgfiAttrSpecified) != IntPtr.Zero;

        return ok && (info.dwAttributes & SfgaoFolder) != 0 && (info.dwAttributes & SfgaoStream) == 0;
    }

    // 실제 방어는 위의 리졸버다. DefaultDllImportSearchPaths(System32) 는 리졸버가 없을 때의 OS 탐색 범위를
    // 좁히는 보조 장치일 뿐이고, 이것만으로는 앱 폴더의 가짜 DLL 을 막지 못했다 [실측].
    //
    // LibraryImport 가 아니라 DllImport 다. LibraryImport 는 AllowUnsafeBlocks 가 필요해
    // 이 프로젝트 설정으로는 SYSLIB1062 로 빌드되지 않는다 [실측].

    [DllImport(Shell32, CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr ILCreateFromPathW(string pszPath);

    [DllImport(Shell32, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void ILFree(IntPtr pidl);

    [DllImport(Shell32, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr apidl, uint dwFlags);

    [DllImport(Shell32, CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteExW(ref ShellExecuteInfo info);

    // SHGFI_PIDL 이면 첫 인자가 경로 문자열이 아니라 PIDL 이다
    [DllImport(Shell32, CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr SHGetFileInfoW(IntPtr pidl, uint dwFileAttributes, ref ShFileInfo psfi,
                                                uint cbFileInfo, uint uFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;           // SHGFI_ICON 을 쓰지 않으므로 비어 있다 — 해제할 것이 없다
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string? lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIconOrMonitor;
        public IntPtr hProcess;
    }
}

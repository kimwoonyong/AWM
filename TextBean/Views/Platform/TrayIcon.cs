using System.Windows.Threading;
using TextBean.Services.Interfaces;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace TextBean.Views.Platform;

/// <summary>
/// 알림 영역 아이콘 — WinForms NotifyIcon (D-093). 조립 루트에서만 만든다. 시험은 만들지 않는다(IL 검사, D-103).
/// 클릭 · 메뉴 이벤트는 WPF 디스패처로 넘겨 올린다. WinForms 창 메시지 처리 안에서 바로 돌리면 예외가 WinForms 쪽 처리로 가
/// 앱의 FailSafe(로그 · 클립보드 정리)를 건너뛴다 [추정 — 계획 검토]. 키 입력창도 메뉴 클릭 스택 밖에서 뜬다.
/// </summary>
public sealed class TrayIcon : ITrayIcon
{
    // TextBean.csproj 의 EmbeddedResource LogicalName 과 같아야 한다
    private const string IconResource = "TextBean.TrayIcon.ico";

    // NotifyIcon.Text 는 이보다 길면 던진다 [문서]
    private const int MaxToolTip = 127;

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Drawing.Icon _image;
    private readonly Drawing.Font _bold;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _lock;
    private readonly Forms.ToolStripMenuItem _enterKey;
    private readonly Forms.NotifyIcon _icon;

    public event EventHandler? OpenRequested;
    public event EventHandler? LockRequested;
    public event EventHandler? EnterKeyRequested;
    public event EventHandler? ExitRequested;

    public TrayIcon()
    {
        using (var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream(IconResource)
                            ?? throw new InvalidOperationException($"{IconResource} 자원이 없다 — TextBean.csproj 의 EmbeddedResource 를 본다"))
        {
            // 배율이 반영된 작은 아이콘 크기의 프레임을 고른다 — 큰 프레임을 줄이면 흐리다
            _image = new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        }

        _menu = new Forms.ContextMenuStrip();
        _bold = new Drawing.Font(_menu.Font, Drawing.FontStyle.Bold);

        var open = new Forms.ToolStripMenuItem("TextBean 열기") { Font = _bold };
        open.Click += (_, _) => Post(() => OpenRequested?.Invoke(this, EventArgs.Empty));

        _lock = new Forms.ToolStripMenuItem("잠그기");
        _lock.Click += (_, _) => Post(() => LockRequested?.Invoke(this, EventArgs.Empty));

        _enterKey = new Forms.ToolStripMenuItem("키 입력…");
        _enterKey.Click += (_, _) => Post(() => EnterKeyRequested?.Invoke(this, EventArgs.Empty));

        var exit = new Forms.ToolStripMenuItem("종료");
        exit.Click += (_, _) => Post(() => ExitRequested?.Invoke(this, EventArgs.Empty));

        _menu.Items.AddRange([open, new Forms.ToolStripSeparator(), _lock, _enterKey, new Forms.ToolStripSeparator(), exit]);

        _icon = new Forms.NotifyIcon { Icon = _image, ContextMenuStrip = _menu, Text = "TextBean" };

        // 클릭 = 열기. 토글하지 않는다 — 보이는 창을 클릭으로 숨기면 "눌렀더니 사라졌다"가 된다 (J-9)
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Post(() => OpenRequested?.Invoke(this, EventArgs.Empty)); };
        _icon.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Post(() => OpenRequested?.Invoke(this, EventArgs.Empty)); };
    }

    private void Post(Action raise) => _dispatcher.BeginInvoke(raise);

    public void Show() => _icon.Visible = true;

    public void Update(string toolTip, bool hasKey)
    {
        _icon.Text = toolTip.Length <= MaxToolTip ? toolTip : toolTip[..MaxToolTip];
        _lock.Visible = hasKey;
        _enterKey.Visible = !hasKey;
    }

    public void Dispose()
    {
        // 숨기지 않고 해제하면 마우스를 올릴 때까지 유령 아이콘이 남는다 [문서]
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _bold.Dispose();
        _image.Dispose();
    }
}

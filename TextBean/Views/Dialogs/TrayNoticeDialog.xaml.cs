using System.Windows;

namespace TextBean.Views.Dialogs;

/// 첫 ✕ 안내 (D-109). 숨기기 = true, 취소 · 창 닫기 = false.
public partial class TrayNoticeDialog : Window
{
    public TrayNoticeDialog() => InitializeComponent();

    public string Text => $"{Heading.Text}\n{new System.Windows.Documents.TextRange(Body.ContentStart, Body.ContentEnd).Text}";

    private void OnHide(object sender, RoutedEventArgs e) => DialogResult = true;
}

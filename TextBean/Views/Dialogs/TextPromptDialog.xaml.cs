using System.Windows;

namespace TextBean.Views.Dialogs;

public partial class TextPromptDialog : Window
{
    public TextPromptDialog(string title, string initial)
    {
        InitializeComponent();
        Title = title;
        Input.Text = initial;
        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
    }

    public string Value => Input.Text;

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}

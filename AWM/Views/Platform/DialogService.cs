using System.Windows;
using AWM.Models;
using AWM.Services.Interfaces;
using AWM.ViewModels;
using AWM.Views;

namespace AWM.Views.Platform;

public sealed class DialogService(IDraftStore store) : IDialogService
{
    /// <summary>
    /// 메인 창. 조립 루트가 창을 만든 뒤 넣는다 — 대화 상자가 메인 창 위에 뜨게.
    /// </summary>
    public Window? Owner { get; set; }

    public SaveChoice AskSaveChanges()
    {
        var answer = Show("고친 내용을 저장하지 않았습니다. 저장할까요?", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        return answer switch
        {
            MessageBoxResult.Yes => SaveChoice.Save,
            MessageBoxResult.No => SaveChoice.Discard,
            _ => SaveChoice.Cancel,
        };
    }

    public bool AskSaveAsNew(string missingFolderName) =>
        Show($"초안 폴더를 찾을 수 없습니다.\n{missingFolderName}\n\n새 폴더로 저장할까요?",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public string? PickDraft()
    {
        var viewModel = new DraftListViewModel(store);
        var window = new DraftListWindow(viewModel) { Owner = Owner };
        return window.ShowDialog() == true ? viewModel.Selected?.Folder : null;
    }

    public IReadOnlyList<string> PickImages()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "사진 추가",
            Multiselect = true,
            Filter = "사진 (jpg, png, gif, webp)|*.jpg;*.jpeg;*.png;*.gif;*.webp",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileNames : [];
    }

    public AppSettings? EditSettings(AppSettings current, string defaultDraftsFolder)
    {
        var viewModel = new SettingsViewModel(current, defaultDraftsFolder, PickFolder);
        var window = new SettingsWindow(viewModel) { Owner = Owner };
        return window.ShowDialog() == true ? viewModel.Result : null;
    }

    private string? PickFolder(string initial)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "초안 저장 위치", InitialDirectory = initial };
        return dialog.ShowDialog(Owner) == true ? dialog.FolderName : null;
    }

    private MessageBoxResult Show(string text, MessageBoxButton buttons, MessageBoxImage icon) =>
        Owner is null
            ? MessageBox.Show(text, "AWM", buttons, icon)
            : MessageBox.Show(Owner, text, "AWM", buttons, icon);
}

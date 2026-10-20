using System.Windows;
using System.Windows.Controls;
using SynologyShareManager.Models;
using SynologyShareManager.Services;

namespace SynologyShareManager.Views;

public partial class LinkEditorWindow : Window
{
    private readonly DsmApiClient _client;
    private readonly SharingLink? _original;
    public LinkEditorWindow(DsmApiClient client, SharingLink? original = null)
    {
        InitializeComponent();
        _client = client; _original = original;
        AvailablePicker.SelectedDate = DateTime.Today;
        ExpirePicker.SelectedDate = DateTime.Today.AddDays(7);
        if (original != null)
        {
            Title = "DK Synology Share Manager — 공유 링크 수정";
            HeaderText.Text = $"공유 링크 수정 — {original.Owner}";
            PathInput.Text = original.Path;
            BrowseButton.IsEnabled = false;
            AvailablePicker.SelectedDate = SharingLink.DatePart(original.AvailableRaw) ?? DateTime.Today;
            ExpirePicker.SelectedDate = SharingLink.DatePart(original.ExpiredRaw) ?? DateTime.Today.AddDays(7);
            AvailableCheck.IsChecked = !SharingLink.IsUnset(original.AvailableRaw);
            ExpireCheck.IsChecked = !SharingLink.IsUnset(original.ExpiredRaw);
            (PasswordMode.Items[0] as ComboBoxItem)!.Content = "기존 비밀번호 유지";
            PasswordMode.SelectedIndex = 0;
        }
        else
        {
            (PasswordMode.Items[2] as ComboBoxItem)!.IsEnabled = false;
            PasswordHint.Text = "암호 없음이 기본값입니다.";
        }
    }
    private void DateCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (AvailablePicker == null || ExpirePicker == null) return;
        AvailablePicker.IsEnabled = AvailableCheck.IsChecked == true;
        ExpirePicker.IsEnabled = ExpireCheck.IsChecked == true;
    }
    private void PasswordMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (PasswordInput != null) PasswordInput.IsEnabled = PasswordMode.SelectedIndex == 1;
    }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FilePickerWindow(_client) { Owner = this };
        if (picker.ShowDialog() == true) PathInput.Text = picker.SelectedPath;
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_original == null && string.IsNullOrWhiteSpace(PathInput.Text))
        {
            MessageBox.Show("NAS에서 공유할 파일 또는 폴더를 선택하세요."); return;
        }
        DateTime? available = AvailableCheck.IsChecked == true ? AvailablePicker.SelectedDate : null;
        DateTime? expires = ExpireCheck.IsChecked == true ? ExpirePicker.SelectedDate : null;
        if ((AvailableCheck.IsChecked == true && !available.HasValue) || (ExpireCheck.IsChecked == true && !expires.HasValue))
        {
            MessageBox.Show("지정한 날짜를 선택하세요."); return;
        }
        if (available.HasValue && expires.HasValue && available.Value.Date > expires.Value.Date)
        {
            MessageBox.Show("만료일은 시작일보다 빠를 수 없습니다."); return;
        }
        PasswordAction action = PasswordMode.SelectedIndex switch
        {
            1 => PasswordAction.Change,
            2 => PasswordAction.Remove,
            _ => PasswordAction.Keep
        };
        if (action == PasswordAction.Change && (PasswordInput.Password.Length is < 1 or > 16))
        {
            MessageBox.Show("비밀번호는 1~16자로 입력하세요."); return;
        }
        SaveButton.IsEnabled = false;
        try
        {
            SharingLink result;
            if (_original == null)
                result = await _client.CreateLinkAsync(PathInput.Text, action == PasswordAction.Change ? PasswordInput.Password : null, available, expires);
            else
                result = await _client.EditLinkAsync(_original, action, PasswordInput.Password, available, expires);
            if (_original == null)
                MessageBox.Show("공유 링크를 생성했습니다.\n\n" + result.Url, "완료");
            DialogResult = true;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "공유 링크 저장 실패", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { SaveButton.IsEnabled = true; }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

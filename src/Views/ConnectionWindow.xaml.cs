using System.Windows;
using SynologyShareManager.Services;

namespace SynologyShareManager.Views;

public partial class ConnectionWindow : Window
{
    private readonly bool _renewDevice;
    public string DsmAddress => AddressBox.Text.Trim().TrimEnd('/');
    public string Account => AccountBox.Text.Trim();
    public string Password => PasswordInput.Password;
    public string Otp => OtpBox.Text.Trim();
    public bool SaveCredentials => SaveCheck.IsChecked == true;
    public bool RememberDevice => SaveCredentials && RememberDeviceCheck.IsChecked == true;
    public string SavedDeviceId { get; private set; } = "";
    public string SavedDeviceName { get; private set; } = "";

    public ConnectionWindow(bool renewDevice = false)
    {
        _renewDevice = renewDevice;
        InitializeComponent();
        try
        {
            var existing = SettingsStore.Load();
            if (existing is { } settings)
            {
                AddressBox.Text = settings.Address;
                AccountBox.Text = settings.Account;
                PasswordInput.Password = settings.Password;
                SavedDeviceId = settings.DeviceId;
                SavedDeviceName = settings.DeviceName;
                RememberDeviceCheck.IsChecked = renewDevice || !string.IsNullOrEmpty(settings.DeviceId);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("저장된 설정을 불러올 수 없습니다.\n" + ex.Message +
                "\n기존 설정을 유지한 채 새 로그인 정보로 연결할 수 있습니다.",
                "설정 읽기 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        if (renewDevice)
            RememberDeviceCheck.IsChecked = true;
        OtpBox.Focus();
    }

    private void SaveCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (RememberDeviceCheck != null)
        {
            RememberDeviceCheck.IsEnabled = SaveCheck.IsChecked == true;
            if (SaveCheck.IsChecked != true) RememberDeviceCheck.IsChecked = false;
        }
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DsmAddress) || string.IsNullOrWhiteSpace(Account) || string.IsNullOrEmpty(Password))
        {
            MessageBox.Show("NAS 주소, 계정, 비밀번호를 입력하세요.");
            return;
        }
        if (_renewDevice && string.IsNullOrWhiteSpace(Otp))
        {
            MessageBox.Show("저장된 장치 인증이 만료되었거나 거부되었습니다. OTP 인증번호를 입력해 다시 로그인하세요.",
                "DSM 재인증", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

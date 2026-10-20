namespace SynologyShareManager.Models;

public sealed class AppSettings
{
    public int FormatVersion { get; set; } = 1;
    public string DsmAddress { get; set; } = "";
    public string Account { get; set; } = "";
    public string PasswordDpapi { get; set; } = "";
    public string TrustedDeviceIdDpapi { get; set; } = "";
    public string TrustedDeviceName { get; set; } = "";
}

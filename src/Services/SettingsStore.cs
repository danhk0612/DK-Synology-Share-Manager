using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SynologyShareManager.Models;

namespace SynologyShareManager.Services;

// Windows DPAPI current-user protection. The encrypted password alone cannot be moved
// to another Windows account and decrypted there. No secrets are stored in logs.
public static class SettingsStore
{
    public static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "SynologyShareManager.config.json");

    public static (string Address, string Account, string Password, string DeviceId, string DeviceName)? Load()
    {
        if (!File.Exists(FilePath)) return null;
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath))
            ?? throw new InvalidDataException("연결 설정 형식을 읽을 수 없습니다.");
        if (settings.FormatVersion != 1) throw new InvalidDataException("지원하지 않는 설정 버전입니다.");
        var deviceId = string.IsNullOrWhiteSpace(settings.TrustedDeviceIdDpapi) ? "" :
            Encoding.UTF8.GetString(Unprotect(Convert.FromBase64String(settings.TrustedDeviceIdDpapi)));
        return (settings.DsmAddress, settings.Account,
            Encoding.UTF8.GetString(Unprotect(Convert.FromBase64String(settings.PasswordDpapi))),
            deviceId, settings.TrustedDeviceName);
    }

    public static void Save(string address, string account, string password, string? deviceId = null, string? deviceName = null)
    {
        var settings = new AppSettings
        {
            DsmAddress = address, Account = account,
            PasswordDpapi = Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(password))),
            TrustedDeviceIdDpapi = string.IsNullOrWhiteSpace(deviceId) ? "" :
                Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(deviceId))),
            TrustedDeviceName = string.IsNullOrWhiteSpace(deviceId) ? "" : deviceName ?? ""
        };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Remove()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public IntPtr Data; }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out Blob output);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out Blob output);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr handle);

    private static byte[] Protect(byte[] data) => Transform(data, true);
    private static byte[] Unprotect(byte[] data) => Transform(data, false);
    private static byte[] Transform(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            bool ok;
            Blob output;
            if (encrypt)
                ok = CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            else
                ok = CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 암호화 설정을 읽거나 저장하지 못했습니다.");
            try
            {
                var result = new byte[output.Length];
                Marshal.Copy(output.Data, result, 0, output.Length);
                return result;
            }
            finally { LocalFree(output.Data); }
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, input.Data, bytes.Length);
            Marshal.FreeHGlobal(input.Data);
        }
    }
}

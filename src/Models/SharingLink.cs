using System.Globalization;
using System.Text.Json;

namespace SynologyShareManager.Models;

public sealed record SharingLink(
    string Id, string Name, string Path, string Url, string Owner,
    bool IsFolder, bool HasPassword, string Status,
    string AvailableRaw, string ExpiredRaw)
{
    public string TypeLabel => IsFolder ? "폴더" : "파일";
    public string PasswordLabel => HasPassword ? "설정됨" : "없음";
    public string StatusLabel => Status switch
    {
        "valid" => "유효", "invalid" => "시작 전", "expired" => "만료", "broken" => "손상", _ => Status
    };
    public string AvailableLabel => IsUnset(AvailableRaw) ? "즉시" : DateText(AvailableRaw);
    public string ExpiredLabel => DateText(ExpiredRaw);
    public static bool IsUnset(string? value) => string.IsNullOrWhiteSpace(value) || value == "0";
    public static string DateText(string? value)
    {
        if (IsUnset(value)) return "무제한";
        var actual = value!;
        return actual[..Math.Min(10, actual.Length)];
    }
    public static DateTime? DatePart(string? value)
    {
        if (IsUnset(value)) return null;
        if (value!.Length >= 10 && DateTime.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date)) return date;
        throw new FormatException("DSM 날짜 응답 형식을 인식할 수 없습니다.");
    }
    public static string FormDate(DateTime? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "0";
    public static SharingLink FromJson(JsonElement json) => new(
        Field(json, "id"), Field(json, "name"), Field(json, "path"), Field(json, "url"),
        Field(json, "link_owner"), Bool(json, "isFolder"), Bool(json, "has_password"),
        Field(json, "status"), Field(json, "date_available"), Field(json, "date_expired"));

    private static string Field(JsonElement json, string name) => json.TryGetProperty(name, out var x)
        ? x.ValueKind switch { JsonValueKind.String => x.GetString() ?? "", JsonValueKind.Number => x.ToString(), _ => "" }
        : "";
    private static bool Bool(JsonElement json, string name) => json.TryGetProperty(name, out var x) &&
        (x.ValueKind == JsonValueKind.True || (x.ValueKind == JsonValueKind.String &&
         string.Equals(x.GetString(), "true", StringComparison.OrdinalIgnoreCase)));
}

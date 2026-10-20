using System.IO;
using System.Net.Http;
using System.Text.Json;
using SynologyShareManager.Models;

namespace SynologyShareManager.Services;

public sealed class DsmApiClient : IDisposable
{
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };
    private readonly Dictionary<string, ApiEntry> _specs = new(StringComparer.Ordinal);
    private string _baseAddress = "";
    private string? _sid;
    private string? _synoToken;
    public string Account { get; private set; } = "";
    public string Address => _baseAddress;
    public bool IsConnected => _sid != null;
    public string? TrustedDeviceId { get; private set; }
    public bool SupportsThumbnails => _specs.ContainsKey("SYNO.FileStation.Thumb");

    public async Task ConnectAsync(string rawAddress, string account, string password, string otp,
        string? trustedDeviceId = null, bool rememberDevice = false, string? trustedDeviceName = null)
    {
        if (!Uri.TryCreate(rawAddress.Trim().TrimEnd('/'), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("DSM 주소는 https://서버:포트 형식으로 입력하세요.");
        if (_sid != null) await LogoutAsync();
        TrustedDeviceId = null;
        _baseAddress = uri.GetLeftPart(UriPartial.Authority);
        _specs.Clear();
        var available = await RequestAsync("entry.cgi", new Dictionary<string, string>
        {
            ["api"] = "SYNO.API.Info", ["version"] = "1", ["method"] = "query",
            ["query"] = "SYNO.API.Auth,SYNO.FileStation.Info,SYNO.FileStation.Sharing,SYNO.FileStation.List,SYNO.FileStation.Thumb"
        }, false);
        foreach (var key in new[] { "SYNO.API.Auth", "SYNO.FileStation.Info", "SYNO.FileStation.Sharing", "SYNO.FileStation.List" })
        {
            if (!available.TryGetProperty(key, out var x)) throw new InvalidOperationException($"DSM API 미지원: {key}");
            _specs[key] = new ApiEntry(x.GetProperty("path").GetString() ?? "",
                x.GetProperty("minVersion").GetInt32(), x.GetProperty("maxVersion").GetInt32());
        }
        if (available.TryGetProperty("SYNO.FileStation.Thumb", out var thumb))
        {
            var specification = new ApiEntry(thumb.GetProperty("path").GetString() ?? "",
                thumb.GetProperty("minVersion").GetInt32(), thumb.GetProperty("maxVersion").GetInt32());
            if (specification.Min <= 2 && specification.Max >= 2 &&
                (specification.Path.StartsWith("entry.cgi", StringComparison.Ordinal) ||
                 specification.Path.StartsWith("FileStation/", StringComparison.Ordinal)))
                _specs["SYNO.FileStation.Thumb"] = specification;
        }
        CheckVersion("SYNO.API.Auth", 6);
        CheckVersion("SYNO.FileStation.Info", 2);
        CheckVersion("SYNO.FileStation.Sharing", 3);
        CheckVersion("SYNO.FileStation.List", 2);

        var login = new Dictionary<string, string>
        {
            ["api"] = "SYNO.API.Auth", ["version"] = "6", ["method"] = "login",
            ["account"] = account, ["passwd"] = password,
            ["session"] = "FileStation", ["format"] = "sid", ["enable_syno_token"] = "yes"
        };
        if (!string.IsNullOrWhiteSpace(otp)) login["otp_code"] = otp.Trim();
        if (rememberDevice)
        {
            var name = string.IsNullOrWhiteSpace(trustedDeviceName) ? "DK Synology Share Manager (Windows)" : trustedDeviceName;
            login["device_name"] = name;
            if (!string.IsNullOrWhiteSpace(otp)) login["enable_device_token"] = "yes";
            else if (!string.IsNullOrWhiteSpace(trustedDeviceId)) login["device_id"] = trustedDeviceId;
        }
        JsonElement auth;
        try { auth = await RequestAsync(_specs["SYNO.API.Auth"].Path, login, false); }
        finally { login["passwd"] = ""; login.Remove("otp_code"); }
        _sid = auth.TryGetProperty("sid", out var s) ? s.GetString() : null;
        if (string.IsNullOrWhiteSpace(_sid)) throw new InvalidOperationException("DSM 로그인 세션을 받지 못했습니다.");
        _synoToken = auth.TryGetProperty("synotoken", out var token) ? token.GetString() : null;
        if (rememberDevice)
        {
            var did = auth.TryGetProperty("did", out var device) ? device.GetString() : null;
            TrustedDeviceId = !string.IsNullOrWhiteSpace(did) ? did :
                string.IsNullOrWhiteSpace(otp) ? trustedDeviceId : null;
        }
        Account = account;
        try
        {
            var manager = await CallAsync("SYNO.FileStation.Info", 2, "get");
            if (!manager.TryGetProperty("is_manager", out var isManager) || isManager.ValueKind != JsonValueKind.True)
                throw new UnauthorizedAccessException("File Station 관리자 권한이 필요합니다.");
        }
        catch { await LogoutAsync(); throw; }
    }

    public async Task LogoutAsync()
    {
        if (_sid == null) return;
        try
        {
            await CallAsync("SYNO.API.Auth", 6, "logout", new() { ["session"] = "FileStation" });
        }
        finally { _sid = null; _synoToken = null; Account = ""; }
    }

    public async Task<IReadOnlyList<SharingLink>> GetLinksAsync(bool forceClean = false)
    {
        var data = await CallAsync("SYNO.FileStation.Sharing", 3, "list", new()
        {
            ["offset"] = "0", ["limit"] = "0", ["force_clean"] = forceClean ? "true" : "false"
        });
        var list = ParseLinks(data);
        if (data.GetProperty("total").GetInt32() != list.Count)
            throw new InvalidOperationException("DSM 링크 전체 건수와 응답 건수가 일치하지 않아 목록을 갱신하지 않았습니다.");
        return list;
    }

    public async Task<SharingLink> GetLinkAsync(string id)
    {
        var json = await CallAsync("SYNO.FileStation.Sharing", 3, "getinfo", new()
        {
            ["id"] = JsonSerializer.Serialize(id)
        });
        return SharingLink.FromJson(json);
    }

    public async Task<SharingLink> CreateLinkAsync(string path, string? password, DateTime? available, DateTime? expires)
    {
        var parameters = new Dictionary<string, string> { ["path"] = JsonSerializer.Serialize(path) };
        if (!string.IsNullOrEmpty(password)) parameters["password"] = JsonSerializer.Serialize(password);
        parameters["date_available"] = available.HasValue ? JsonSerializer.Serialize(SharingLink.FormDate(available)) : "0";
        parameters["date_expired"] = expires.HasValue ? JsonSerializer.Serialize(SharingLink.FormDate(expires)) : "0";
        var data = await CallAsync("SYNO.FileStation.Sharing", 3, "create", parameters);
        if (!data.TryGetProperty("links", out var links) || links.GetArrayLength() != 1)
            throw new InvalidOperationException("공유 링크 생성 결과가 한 건이 아닙니다. DSM에서 직접 확인하세요.");
        var item = links[0];
        if (item.TryGetProperty("error", out var err) && err.GetInt32() != 0)
            throw new InvalidOperationException($"공유 링크 생성 실패: DSM 코드 {err.GetInt32()}");
        var id = item.GetProperty("id").GetString();
        if (string.IsNullOrEmpty(id)) throw new InvalidOperationException("DSM이 생성된 링크 ID를 반환하지 않았습니다.");
        try { return await GetLinkAsync(id); }
        catch (Exception ex) { throw new InvalidOperationException("공유 링크는 생성됐지만 상세 확인에 실패했습니다. 다시 생성하지 말고 목록을 새로고침하세요.", ex); }
    }

    public async Task<SharingLink> EditLinkAsync(SharingLink original, PasswordAction passwordAction, string? newPassword,
        DateTime? available, DateTime? expires)
    {
        var before = await GetLinkAsync(original.Id);
        AssertIdentity(original, before);
        var p = new Dictionary<string, string> { ["id"] = JsonSerializer.Serialize(original.Id) };
        if (passwordAction == PasswordAction.Change) p["password"] = JsonSerializer.Serialize(newPassword ?? "");
        else if (passwordAction == PasswordAction.Remove) p["password"] = JsonSerializer.Serialize("");
        if (SharingLink.DatePart(original.AvailableRaw) != available)
            p["date_available"] = available.HasValue ? JsonSerializer.Serialize(SharingLink.FormDate(available)) : "0";
        if (SharingLink.DatePart(original.ExpiredRaw) != expires)
            p["date_expired"] = expires.HasValue ? JsonSerializer.Serialize(SharingLink.FormDate(expires)) : "0";
        if (p.Count == 1) return before;
        await CallAsync("SYNO.FileStation.Sharing", 3, "edit", p);
        var result = await GetLinkAsync(original.Id);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            AssertIdentity(original, result);
            if (SharingLink.DatePart(result.AvailableRaw) == available &&
                SharingLink.DatePart(result.ExpiredRaw) == expires &&
                (passwordAction != PasswordAction.Change || result.HasPassword) &&
                (passwordAction != PasswordAction.Remove || !result.HasPassword))
                return result;

            // Some DSM changes may not be visible in the first immediate getinfo response.
            // Retry verification only after a mismatch; never repeat the edit request.
            if (attempt < 2)
            {
                await Task.Delay(attempt == 0 ? 350 : 750);
                result = await GetLinkAsync(original.Id);
            }
        }

        static string DisplayDate(DateTime? date) => date?.ToString("yyyy-MM-dd") ?? "미지정";
        var mismatches = new List<string>();
        var actualAvailable = SharingLink.DatePart(result.AvailableRaw);
        var actualExpires = SharingLink.DatePart(result.ExpiredRaw);
        if (actualAvailable != available)
            mismatches.Add($"시작일: 요청 {DisplayDate(available)} / 재조회 {DisplayDate(actualAvailable)}");
        if (actualExpires != expires)
            mismatches.Add($"만료일: 요청 {DisplayDate(expires)} / 재조회 {DisplayDate(actualExpires)}");
        if (passwordAction == PasswordAction.Change && !result.HasPassword)
            mismatches.Add("비밀번호: 설정 요청 / 재조회 시 미설정");
        if (passwordAction == PasswordAction.Remove && result.HasPassword)
            mismatches.Add("비밀번호: 해제 요청 / 재조회 시 설정됨");
        throw new InvalidOperationException(
            "DSM이 수정 요청을 수락했지만 저장 후 확인한 값이 다릅니다.\n" +
            string.Join("\n", mismatches) +
            "\n실제로 반영되었을 수 있으니 새로고침으로 확인하세요. 다시 저장하지 마세요.");
    }

    public async Task DeleteLinkAsync(SharingLink original)
    {
        var before = await GetLinkAsync(original.Id);
        AssertIdentity(original, before);
        await CallAsync("SYNO.FileStation.Sharing", 3, "delete", new() { ["id"] = JsonSerializer.Serialize(original.Id) });
    }

    public async Task<IReadOnlyList<NasFile>> BrowseAsync(string? folderPath)
    {
        string method = folderPath == null ? "list_share" : "list";
        var args = new Dictionary<string, string>
        {
            ["offset"] = "0", ["limit"] = "0",
            ["additional"] = folderPath == null ? "[\"time\"]" : "[\"size\",\"time\",\"type\"]"
        };
        if (folderPath != null) args["folder_path"] = JsonSerializer.Serialize(folderPath);
        var response = await CallAsync("SYNO.FileStation.List", 2, method, args);
        var property = folderPath == null ? "shares" : "files";
        var files = new List<NasFile>();
        if (response.TryGetProperty(property, out var items))
            foreach (var item in items.EnumerateArray())
                files.Add(NasFile.FromJson(item));
        if (response.TryGetProperty("total", out var total) && total.GetInt32() != files.Count)
            throw new InvalidOperationException("폴더 목록이 불완전합니다.");
        return files.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<byte[]?> GetThumbnailAsync(string path)
    {
        if (!SupportsThumbnails || _sid == null) return null;
        var parameters = new Dictionary<string, string>
        {
            ["api"] = "SYNO.FileStation.Thumb", ["version"] = "2", ["method"] = "get",
            ["path"] = JsonSerializer.Serialize(path), ["size"] = "small", ["_sid"] = _sid
        };
        if (!string.IsNullOrEmpty(_synoToken)) parameters["SynoToken"] = _synoToken;
        var query = string.Join("&", parameters.Select(pair =>
            Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        // FileStation.Thumb.get returns binary image data, not the JSON API envelope.
        using var request = new HttpRequestMessage(HttpMethod.Get,
            _baseAddress + "/webapi/" + _specs["SYNO.FileStation.Thumb"].Path + "?" + query);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode ||
            (response.Content.Headers.ContentLength is long size && size > 2 * 1024 * 1024)) return null;
        // Reject error responses and oversized image payloads; never retrieve originals.
        var mime = response.Content.Headers.ContentType?.MediaType ?? "";
        if (!mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(mime, "application/octet-stream", StringComparison.OrdinalIgnoreCase)) return null;
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();
        var block = new byte[16384];
        int count;
        while ((count = await stream.ReadAsync(block)) > 0)
        {
            if (buffer.Length + count > 2 * 1024 * 1024) return null;
            buffer.Write(block, 0, count);
        }
        return buffer.ToArray();
    }

    private static void AssertIdentity(SharingLink expected, SharingLink actual)
    {
        if (expected.Id != actual.Id || expected.Owner != actual.Owner || expected.Path != actual.Path || expected.Url != actual.Url)
            throw new InvalidOperationException("링크 ID·소유자·경로·URL이 이전 조회와 달라 작업을 중단했습니다. 목록을 갱신하세요.");
    }

    private static List<SharingLink> ParseLinks(JsonElement data)
    {
        var result = new List<SharingLink>();
        if (data.TryGetProperty("links", out var links))
            foreach (var link in links.EnumerateArray()) result.Add(SharingLink.FromJson(link));
        if (result.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != result.Count)
            throw new InvalidOperationException("DSM 응답에 중복 링크 ID가 있습니다.");
        return result;
    }

    private void CheckVersion(string name, int version)
    {
        var x = _specs[name];
        if (x.Min > version || x.Max < version) throw new NotSupportedException($"{name} v{version} 미지원");
        if (!x.Path.StartsWith("entry.cgi", StringComparison.Ordinal) &&
            !x.Path.StartsWith("FileStation/", StringComparison.Ordinal))
            throw new InvalidOperationException("DSM API 경로가 예상한 범위를 벗어납니다.");
    }

    private Task<JsonElement> CallAsync(string api, int version, string method, Dictionary<string, string>? extras = null)
    {
        var parameters = extras ?? new();
        parameters["api"] = api;
        parameters["version"] = version.ToString();
        parameters["method"] = method;
        return RequestAsync(_specs[api].Path, parameters, true);
    }

    private async Task<JsonElement> RequestAsync(string path, Dictionary<string, string> parameters, bool authenticated)
    {
        string url = _baseAddress + "/webapi/" + path;
        if (authenticated)
        {
            if (_sid == null) throw new InvalidOperationException("DSM에 먼저 로그인하세요.");
            // DSM 7.4.1 actual probe: SID in POST body alone produced code 119.
            // Include the SID and SynoToken in query and POST, never write URLs to logs.
            url += "?_sid=" + Uri.EscapeDataString(_sid);
            parameters["_sid"] = _sid;
            if (!string.IsNullOrEmpty(_synoToken))
            {
                url += "&SynoToken=" + Uri.EscapeDataString(_synoToken);
                parameters["SynoToken"] = _synoToken;
            }
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(parameters)
        };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        var root = json.RootElement;
        if (!root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
        {
            string code = root.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var c)
                ? c.ToString() : "unknown";
            throw new InvalidOperationException($"DSM API 요청 실패 ({parameters["api"]}.{parameters["method"]}, 오류 코드 {code}).");
        }
        if (root.TryGetProperty("data", out var data)) return data.Clone();
        using var empty = JsonDocument.Parse("{}");
        return empty.RootElement.Clone();
    }

    public void Dispose() => _http.Dispose();
    private sealed record ApiEntry(string Path, int Min, int Max);
}

public enum PasswordAction { Keep, Change, Remove }

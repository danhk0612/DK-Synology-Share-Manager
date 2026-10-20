using System.ComponentModel;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace SynologyShareManager.Models;

public sealed class NasFile : INotifyPropertyChanged
{
    public string Name { get; }
    public string Path { get; }
    public bool IsDirectory { get; }
    public long? Size { get; }
    public DateTimeOffset? Created { get; }
    public DateTimeOffset? Modified { get; }
    public string Extension { get; }
    public string DisplayName => IsDirectory ? "[폴더] " + Name : Name;
    public string TypeLabel => IsDirectory ? "파일 폴더" :
        string.IsNullOrEmpty(Extension) ? "파일" : Extension.TrimStart('.').ToUpperInvariant() + " 파일";
    public string SizeLabel => IsDirectory || Size == null ? "—" :
        Size.Value < 1024 ? $"{Size.Value:N0} B" :
        Size.Value < 1024 * 1024 ? $"{Size.Value / 1024d:N1} KB" :
        Size.Value < 1024L * 1024 * 1024 ? $"{Size.Value / (1024d * 1024):N1} MB" :
        $"{Size.Value / (1024d * 1024 * 1024):N2} GB";
    public string CreatedLabel => Created?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? "—";
    public string ModifiedLabel => Modified?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? "—";

    private static readonly HashSet<string> ThumbnailExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".png", ".bmp", ".gif", ".tif", ".tiff", ".heic",
        ".webp", ".arw", ".cr2", ".nef", ".dng", ".raw",
        ".mp4", ".mov", ".mkv", ".avi", ".wmv", ".mpeg", ".mpg", ".m4v", ".webm"
    };
    public bool SupportsThumbnail => !IsDirectory && ThumbnailExtensions.Contains(Extension);
    private BitmapSource? _thumbnail;
    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (ReferenceEquals(_thumbnail, value)) return;
            _thumbnail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
        }
    }

    private NasFile(string name, string path, bool isDirectory, long? size,
        DateTimeOffset? created, DateTimeOffset? modified, string extension)
    {
        Name = name; Path = path; IsDirectory = isDirectory;
        Size = size; Created = created; Modified = modified;
        Extension = extension;
    }

    public static NasFile FromJson(JsonElement item)
    {
        var name = item.GetProperty("name").GetString() ?? "";
        var path = item.GetProperty("path").GetString() ?? "";
        var isDirectory = item.TryGetProperty("isdir", out var dir) && dir.ValueKind == JsonValueKind.True;
        long? size = null;
        DateTimeOffset? created = null, modified = null;
        string extension = System.IO.Path.GetExtension(name);
        if (item.TryGetProperty("additional", out var extra) && extra.ValueKind == JsonValueKind.Object)
        {
            if (extra.TryGetProperty("size", out var sizeValue) && sizeValue.ValueKind == JsonValueKind.Number &&
                sizeValue.TryGetInt64(out var length)) size = length;
            if (extra.TryGetProperty("time", out var times) && times.ValueKind == JsonValueKind.Object)
            {
                created = GetTimestamp(times, "crtime");
                modified = GetTimestamp(times, "mtime");
            }
            if (extra.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
            {
                var ext = type.GetString();
                if (!string.IsNullOrWhiteSpace(ext)) extension = "." + ext.TrimStart('.');
            }
        }
        return new NasFile(name, path, isDirectory, size, created, modified, extension);
    }

    private static DateTimeOffset? GetTimestamp(JsonElement times, string key)
    {
        if (!times.TryGetProperty(key, out var raw) || raw.ValueKind != JsonValueKind.Number ||
            !raw.TryGetInt64(out var seconds) || seconds <= 0)
            return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(seconds); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

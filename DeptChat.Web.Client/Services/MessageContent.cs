using System.Globalization;

namespace DeptChat.Web.Client.Services;

public enum ContentKind { Text, Image, ImageUploading, File, FileUploading, Voice, Deleted }

public sealed record ParsedContent(
    ContentKind Kind,
    string Text,
    string? Url = null,
    string? FileName = null,
    long? FileSizeBytes = null,
    string? VoiceDataUri = null,
    int VoiceSeconds = 0);

/// <summary>
/// Message content markers shared with the server and the Android app. Formats must stay
/// byte-compatible with DeptChat.Mobile/ViewModels/ChatRoomViewModel.cs:
///   [image:{url}] or [image:{url}:{caption}]      ([image:uploading...] while uploading)
///   [file:{url}:{name}:{sizeBytes}]                ([file:uploading:{name}:{size}] while uploading)
///   [voice:data:audio/m4a;base64,{base64}:{seconds}]
/// </summary>
public static class MessageContent
{
    public const string DeletedText = "🚫 This message was deleted";
    public const string VoicePrefix = "[voice:data:audio/m4a;base64,";

    public static ParsedContent Parse(string? content, bool isDeleted = false)
    {
        content ??= string.Empty;
        if (isDeleted) return new ParsedContent(ContentKind.Deleted, DeletedText);

        if (content.StartsWith("[voice:", StringComparison.OrdinalIgnoreCase))
        {
            var end = content.EndsWith(']') ? content.Length - 1 : content.Length;
            var lastColon = content.LastIndexOf(':', Math.Max(0, end - 1));
            var seconds = 0;
            string? data = null;
            if (lastColon > 6 && int.TryParse(content.AsSpan(lastColon + 1, end - lastColon - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
            {
                seconds = s;
                data = content.Substring(7, lastColon - 7);
            }
            else
            {
                data = content.Substring(7, end - 7);
            }
            return new ParsedContent(ContentKind.Voice, string.Empty, VoiceDataUri: data, VoiceSeconds: seconds);
        }

        if (content.StartsWith("[image:", StringComparison.OrdinalIgnoreCase) && content.EndsWith(']'))
        {
            var inner = content.Substring(7, content.Length - 8);
            if (inner.StartsWith("uploading", StringComparison.OrdinalIgnoreCase))
            {
                return new ParsedContent(ContentKind.ImageUploading, string.Empty);
            }
            var firstColon = inner.IndexOf(':');
            return firstColon > 0
                ? new ParsedContent(ContentKind.Image, inner.Substring(firstColon + 1), Url: inner.Substring(0, firstColon))
                : new ParsedContent(ContentKind.Image, string.Empty, Url: inner);
        }

        if (content.StartsWith("[file:", StringComparison.OrdinalIgnoreCase) && content.EndsWith(']'))
        {
            var parts = content.Substring(6, content.Length - 7).Split(':');
            if (parts.Length >= 2)
            {
                long? size = parts.Length >= 3 && long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) ? b : null;
                var kind = parts[0].Equals("uploading", StringComparison.OrdinalIgnoreCase) ? ContentKind.FileUploading : ContentKind.File;
                return new ParsedContent(kind, string.Empty, Url: kind == ContentKind.File ? parts[0] : null, FileName: parts[1], FileSizeBytes: size);
            }
        }

        return new ParsedContent(ContentKind.Text, content);
    }

    public static string ImageMarker(string url) => $"[image:{url}]";

    /// <summary>The mobile parser splits on ':' so a ':' in the file name would break it.</summary>
    public static string FileMarker(string url, string fileName, long sizeBytes)
        => $"[file:{url}:{SafeFileName(fileName)}:{sizeBytes.ToString(CultureInfo.InvariantCulture)}]";

    public static string VoiceMarker(string base64, int seconds)
        => $"{VoicePrefix}{base64}:{seconds.ToString(CultureInfo.InvariantCulture)}]";

    public static string SafeFileName(string name)
    {
        var cleaned = name.Replace(':', '_').Replace(']', ')').Replace('[', '(').Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "file" : cleaned;
    }

    /// <summary>Short preview text for lists (mirrors the server's SanitizePreview wording).</summary>
    public static string Preview(string? content)
    {
        var parsed = Parse(content);
        return parsed.Kind switch
        {
            ContentKind.Image or ContentKind.ImageUploading => string.IsNullOrWhiteSpace(parsed.Text) ? "📷 Photo" : "📷 " + parsed.Text,
            ContentKind.File or ContentKind.FileUploading => "📄 " + (parsed.FileName ?? "Document"),
            ContentKind.Voice => "🎙️ Voice message",
            _ => parsed.Text
        };
    }

    public static string FormatSize(long? bytes)
    {
        if (bytes is not { } b || b <= 0) return string.Empty;
        if (b < 1024) return $"{b} B";
        if (b < 1024 * 1024) return $"{b / 1024.0:0} KB";
        return $"{b / (1024.0 * 1024.0):0.0} MB";
    }

    public static string FormatDuration(double seconds)
    {
        var s = (int)Math.Max(0, Math.Round(seconds));
        return $"{s / 60}:{s % 60:D2}";
    }

    public static string FileTypeLabel(string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? string.Empty).TrimStart('.').ToUpperInvariant();
        return string.IsNullOrEmpty(ext) ? "FILE" : (ext.Length > 4 ? ext[..4] : ext);
    }
}

using System.Text.RegularExpressions;
using DeptChat.Infrastructure.Persistence;
using DeptChat.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DeptChat.WebApi.Services;

/// <summary>
/// Disappearing messages: every 2 minutes, empties messages whose ExpiresAt has passed (for everyone),
/// removes their pins, deletes their uploaded photo/file when no visible message still uses it, fixes
/// the chat's last-message preview and tells the open apps/web pages ("MessagesExpired").
/// Rows are kept (only emptied) so message sequence numbers and sync stay intact; every read already
/// hides messages past ExpiresAt, so this job only frees the content.
/// </summary>
public sealed class DisappearingMessagesService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private const int BatchSize = 500;
    private static readonly Regex UploadRef = new(@"/uploads/(\d{4}-\d{2})/([A-Za-z0-9_\-\.]+)", RegexOptions.Compiled);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<ChatHub> _hub;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<DisappearingMessagesService> _logger;

    public DisappearingMessagesService(IServiceScopeFactory scopeFactory, IHubContext<ChatHub> hub,
        IWebHostEnvironment env, ILogger<DisappearingMessagesService> logger)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
        _env = env;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the database bootstrap (new columns) finish first.
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await RunBatchAsync(stoppingToken) == BatchSize) { }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Disappearing messages cleanup failed; retrying next round.");
            }

            try { await Task.Delay(Interval, stoppingToken); } catch (TaskCanceledException) { return; }
        }
    }

    /// <summary>Empties one batch of expired messages. Returns how many were emptied.</summary>
    private async Task<int> RunBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;

        var expired = await db.Messages
            .Where(m => m.ExpiresAt != null && m.ExpiresAt <= now && m.Content != "")
            .OrderBy(m => m.ExpiresAt)
            .Take(BatchSize)
            .ToListAsync(ct);
        if (expired.Count == 0) return 0;

        var files = new HashSet<(string Month, string Name)>();
        foreach (var m in expired)
        {
            foreach (Match match in UploadRef.Matches(m.Content)) files.Add((match.Groups[1].Value, match.Groups[2].Value));
            m.Content = string.Empty;
        }

        var ids = expired.Select(m => m.Id).ToList();
        var pins = await db.MessagePins.Where(p => ids.Contains(p.MessageId)).ToListAsync(ct);
        db.MessagePins.RemoveRange(pins);
        await db.SaveChangesAsync(ct);

        // Last-message preview of each affected chat: the latest message still visible, or none.
        var convIds = expired.Select(m => m.ConversationId).Distinct().ToList();
        var convs = await db.Conversations.Where(c => convIds.Contains(c.Id)).ToListAsync(ct);
        foreach (var conv in convs)
        {
            var latest = await db.Messages
                .Where(m => m.ConversationId == conv.Id && m.SequenceNumber > conv.ClearedUpToSequence
                            && (m.ExpiresAt == null || m.ExpiresAt > now))
                .OrderByDescending(m => m.SequenceNumber)
                .Select(m => m.Content)
                .FirstOrDefaultAsync(ct);
            conv.LastMessagePreview = latest == null ? null : BuildPreview(latest);
        }
        await db.SaveChangesAsync(ct);

        // Uploaded photos/files: delete only when no visible message (e.g. a forwarded copy) still uses them.
        var webRoot = string.IsNullOrEmpty(_env.WebRootPath) ? Path.Combine(AppContext.BaseDirectory, "wwwroot") : _env.WebRootPath;
        foreach (var (month, name) in files)
        {
            var stillUsed = await db.Messages.AnyAsync(m => m.Content.Contains(name)
                                                            && (m.ExpiresAt == null || m.ExpiresAt > now), ct);
            if (stillUsed) continue;
            var path = Path.Combine(webRoot, "uploads", month, name);
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete expired upload {File}", name);
            }
        }

        foreach (var conv in convs)
        {
            await _hub.Clients.Groups($"Room_{conv.Id}", $"User_{conv.CreatedByUserId}", $"Dept_{conv.DepartmentId}")
                .SendAsync("MessagesExpired", conv.Id, ct);
        }

        _logger.LogInformation("Disappearing messages: emptied {Count} message(s) in {Chats} chat(s), {Files} file(s) checked.",
            expired.Count, convs.Count, files.Count);
        return expired.Count;
    }

    // Same preview text as ChatHub.SendMessage.
    private static string BuildPreview(string content)
    {
        if (content.StartsWith("[voice:", StringComparison.OrdinalIgnoreCase)) return "🎙️ Voice message";
        if (content.StartsWith("[image:", StringComparison.OrdinalIgnoreCase)) return "📷 Photo";
        if (content.StartsWith("[file:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = content.Trim('[', ']').Split(':');
            return (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2])) ? $"📄 {parts[2]}" : "📎 Document";
        }
        return content.Length > 80 ? content.Substring(0, 77) + "..." : content;
    }
}

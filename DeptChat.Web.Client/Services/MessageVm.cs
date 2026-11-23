using DeptChat.Shared.DTOs;
using DeptChat.Shared.Enums;

namespace DeptChat.Web.Client.Services;

/// <summary>One bubble in the chat pane: a server message or a local pending/uploading one.</summary>
public sealed class MessageVm
{
    public Guid? Id { get; set; }
    public string ClientGeneratedId { get; set; } = string.Empty;
    public long Seq { get; set; }
    public Guid SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public UserRole SenderRole { get; set; }
    public string Content { get; private set; } = string.Empty;
    public ParsedContent Parsed { get; private set; } = new(ContentKind.Text, string.Empty);
    public MessageDeliveryStatus Status { get; set; } = MessageDeliveryStatus.Pending;
    public DateTime SentAt { get; set; }
    public bool IsDeleted { get; private set; }
    public bool IsForwarded { get; set; }
    /// <summary>Disappearing messages: hidden after this time (UTC); null = never.</summary>
    public DateTime? ExpiresAt { get; set; }
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value <= DateTime.UtcNow;
    /// <summary>No ack within the timeout, or the server refused it.</summary>
    public bool Failed { get; set; }
    /// <summary>Upload placeholder that is never sent as-is.</summary>
    public bool LocalOnly { get; set; }

    public void SetContent(string content, bool isDeleted)
    {
        Content = content ?? string.Empty;
        IsDeleted = isDeleted;
        Parsed = MessageContent.Parse(Content, isDeleted);
    }

    public void MarkDeleted() => SetContent(MessageContent.DeletedText, true);

    public void Raise(MessageDeliveryStatus status)
    {
        if ((int)status > (int)Status) Status = status;
    }

    public static MessageVm From(ChatMessageDto dto)
    {
        var vm = new MessageVm
        {
            Id = dto.Id,
            ClientGeneratedId = dto.ClientGeneratedId,
            Seq = dto.SequenceNumber,
            SenderId = dto.SenderId,
            SenderName = dto.SenderName,
            SenderRole = dto.SenderRole,
            Status = dto.Status,
            SentAt = dto.SentAt,
            IsForwarded = dto.IsForwarded,
            ExpiresAt = dto.ExpiresAt
        };
        vm.SetContent(dto.Content, dto.IsDeleted);
        return vm;
    }

    public void Apply(ChatMessageDto dto)
    {
        Id = dto.Id;
        Seq = dto.SequenceNumber;
        SenderId = dto.SenderId;
        SenderName = dto.SenderName;
        SenderRole = dto.SenderRole;
        SentAt = dto.SentAt;
        IsForwarded = dto.IsForwarded;
        ExpiresAt = dto.ExpiresAt;
        Failed = false;
        LocalOnly = false;
        Raise(dto.Status);
        SetContent(dto.Content, dto.IsDeleted);
    }
}

/// <summary>Latest activity in a conversation, so the lists can update their preview.</summary>
public sealed record ChatActivity(Guid ConversationId, Guid DepartmentId, string Preview, DateTime At);

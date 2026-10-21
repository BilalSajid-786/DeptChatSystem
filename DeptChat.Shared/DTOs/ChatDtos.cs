using DeptChat.Shared.Enums;

namespace DeptChat.Shared.DTOs;

public record ConversationSummaryDto(
    Guid Id,
    Guid DepartmentId,
    string DepartmentName,
    Guid CreatedByUserId,
    string CreatedByUserName,
    string? LastMessagePreview,
    DateTime? LastMessageAt,
    int UnreadCount,
    ConversationStatus Status
);

public record ChatMessageDto(
    Guid Id,
    Guid ConversationId,
    Guid SenderId,
    string SenderName,
    UserRole SenderRole,
    string ClientGeneratedId,
    long SequenceNumber,
    string Content,
    MessageDeliveryStatus Status,
    DateTime SentAt
);

public record SendMessageDto(
    Guid ConversationId,
    string ClientGeneratedId,
    string Content
);

public record ConversationSyncResponseDto(
    Guid ConversationId,
    long LastSequenceNumber,
    List<ChatMessageDto> Messages
);

public record InAppNotificationToastDto(
    Guid ConversationId,
    Guid DepartmentId,
    string DepartmentName,
    string SenderName,
    string ContentPreview,
    DateTime SentAt
);

public record UserPresenceDto(
    Guid UserId,
    bool IsOnline,
    bool IsInRoom,
    DateTime? LastSeenAt
);


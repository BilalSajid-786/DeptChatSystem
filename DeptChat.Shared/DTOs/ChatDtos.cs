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
    ConversationStatus Status,
    bool IsOnline = false,
    bool IsUserBlocked = false
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
    DateTime SentAt,
    bool IsDeleted = false,
    bool IsForwarded = false
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

public record PaginatedMessagesDto(
    Guid ConversationId,
    long? EarliestSequenceNumber,
    long? LatestSequenceNumber,
    bool HasMore,
    List<ChatMessageDto> Messages
);

public record AttachmentUploadResultDto(
    string Url,
    string FileName,
    long FileSizeBytes,
    string ContentType,
    bool IsImage
);

public record AvailableDepartmentUserDto(
    Guid Id,
    string FullName,
    string? Email,
    UserRole Role,
    bool IsOnline,
    bool HasExistingChat,
    bool IsBlocked = false
);

public record ForwardMessageRequestDto(
    Guid MessageId,
    Guid TargetDepartmentId,
    Guid TargetUserId
);

public record BlockUserRequestDto(
    string? Reason = null
);

public record StartConversationWithUserRequest(
    Guid DepartmentId,
    Guid TargetUserId
);

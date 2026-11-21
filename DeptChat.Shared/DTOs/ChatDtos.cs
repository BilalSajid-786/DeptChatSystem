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
    bool IsUserBlocked = false,
    bool IsPinned = false   // staff only: pinned by me at the top of my department chat list
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
    bool IsForwarded = false,
    bool IsPinned = false, // pinned by the caller (personal pin)
    DateTime? ExpiresAt = null // disappearing messages: hide after this time (UTC); null = never
);

/// <summary>Disappearing messages for the whole company: seconds after sending (0 = off).</summary>
public record DisappearingMessagesSettingDto(int Seconds);

public record SendMessageDto(
    Guid ConversationId,
    string ClientGeneratedId,
    string Content
);

public record ConversationSyncResponseDto(
    Guid ConversationId,
    long LastSequenceNumber,
    List<ChatMessageDto> Messages,
    long ClearedUpToSequence = 0 // messages up to here are cleared for the caller: remove them locally
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
    List<ChatMessageDto> Messages,
    long ClearedUpToSequence = 0 // messages up to here are cleared for the caller: remove them locally
);

/// <summary>Result of "clear chat": for the caller only, or for everyone (Admin/Manager).</summary>
public record ClearChatResultDto(
    Guid ConversationId,
    long ClearedUpToSequence,
    bool ForEveryone
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

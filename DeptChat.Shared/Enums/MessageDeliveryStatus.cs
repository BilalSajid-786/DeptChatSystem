namespace DeptChat.Shared.Enums;

public enum MessageDeliveryStatus
{
    Pending = 1,    // ⏳ Client outbox / sending
    Sent = 2,       // ✓ Committed to DB on server
    Delivered = 3,  // ✓✓ Delivered to recipient device
    Read = 4        // Read by recipient in room
}

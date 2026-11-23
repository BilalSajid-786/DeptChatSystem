using DeptChat.Shared.Enums;

namespace DeptChat.Web.Client.Services;

public static class Format
{
    /// <summary>The API sends UTC times; some arrive without a "Z", so treat unspecified as UTC.</summary>
    public static DateTime ToLocal(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value.ToLocalTime(),
            DateTimeKind.Local => value,
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime()
        };

    public static string Time(DateTime utc) => ToLocal(utc).ToString("HH:mm");

    /// <summary>List timestamp: time today, "Yesterday", weekday this week, else date.</summary>
    public static string ListTime(DateTime? utc)
    {
        if (utc is not { } value) return string.Empty;
        var local = ToLocal(value);
        var today = DateTime.Now.Date;
        if (local.Date == today) return local.ToString("HH:mm");
        if (local.Date == today.AddDays(-1)) return "Yesterday";
        if (local.Date > today.AddDays(-7)) return local.ToString("ddd");
        return local.ToString("dd/MM/yyyy");
    }

    public static string DayLabel(DateTime utc)
    {
        var local = ToLocal(utc).Date;
        var today = DateTime.Now.Date;
        if (local == today) return "Today";
        if (local == today.AddDays(-1)) return "Yesterday";
        return local.ToString("dddd, d MMMM yyyy");
    }

    public static string LastSeen(DateTime? utc)
    {
        if (utc is not { } value) return "offline";
        var local = ToLocal(value);
        var today = DateTime.Now.Date;
        if (local.Date == today) return $"last seen today at {local:HH:mm}";
        if (local.Date == today.AddDays(-1)) return $"last seen yesterday at {local:HH:mm}";
        return $"last seen {local:dd/MM/yyyy}";
    }

    public static string LongDate(DateTime utc) => ToLocal(utc).ToString("MMM dd, yyyy h:mm tt");

    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1
            ? parts[0][..1].ToUpperInvariant()
            : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }

    private static readonly string[] AvatarColors = { "#0369A1", "#075E54", "#475569", "#7C3AED", "#B45309", "#BE185D", "#0F766E", "#334155" };

    /// <summary>Stable avatar colour per person.</summary>
    public static string AvatarColor(Guid id)
    {
        var bytes = id.ToByteArray();
        return AvatarColors[bytes[0] % AvatarColors.Length];
    }

    public static string RoleLabel(UserRole role) => role switch
    {
        UserRole.Admin => "Admin",
        UserRole.Manager => "Manager",
        _ => "Customer"
    };

    public static string Badge(int count) => count > 99 ? "99+" : count.ToString();
}

namespace DeptChat.Web.Client.Services;

public enum ToastKind { Info, Success, Error, Message }

public sealed class ToastItem
{
    public Guid Id { get; } = Guid.NewGuid();
    public ToastKind Kind { get; init; }
    public string? Title { get; init; }
    public string Text { get; init; } = string.Empty;
    /// <summary>Optional page to open when the toast is clicked.</summary>
    public string? Link { get; init; }
    public string? Initial { get; init; }
}

/// <summary>Toast stack shown top-right (success/error messages and new-message toasts).</summary>
public sealed class ToastService
{
    private readonly List<ToastItem> _items = new();

    public IReadOnlyList<ToastItem> Items => _items;
    public event Action? Changed;

    public void Show(string text, ToastKind kind = ToastKind.Info, string? title = null, string? link = null, string? initial = null)
    {
        var item = new ToastItem { Text = text, Kind = kind, Title = title, Link = link, Initial = initial };
        _items.Add(item);
        if (_items.Count > 4) _items.RemoveAt(0);
        Changed?.Invoke();
        _ = DismissLaterAsync(item.Id, kind == ToastKind.Error ? 7000 : 5000);
    }

    public void Success(string text) => Show(text, ToastKind.Success);
    public void Error(string text) => Show(text, ToastKind.Error);

    public void Dismiss(Guid id)
    {
        if (_items.RemoveAll(t => t.Id == id) > 0) Changed?.Invoke();
    }

    private async Task DismissLaterAsync(Guid id, int ms)
    {
        await Task.Delay(ms);
        Dismiss(id);
    }
}

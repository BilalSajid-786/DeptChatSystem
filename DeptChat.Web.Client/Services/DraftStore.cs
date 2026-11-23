namespace DeptChat.Web.Client.Services;

/// <summary>
/// Unsent text per chat, so leaving a chat (Back, Esc, opening another chat) and coming back keeps
/// what was typed. In memory only (this tab, until reload) and keyed by user, and cleared on sign-out,
/// so on a shared computer the next person never sees someone else's draft.
/// </summary>
public sealed class DraftStore : IDisposable
{
    private readonly Dictionary<(Guid UserId, Guid ConversationId), string> _drafts = new();
    private readonly AuthSession _auth;

    public DraftStore(AuthSession auth)
    {
        _auth = auth;
        _auth.Changed += OnAuthChanged;
    }

    public string Get(Guid conversationId)
        => _drafts.TryGetValue((_auth.UserId, conversationId), out var text) ? text : "";

    public void Set(Guid conversationId, string? text)
    {
        var key = (_auth.UserId, conversationId);
        if (string.IsNullOrWhiteSpace(text)) _drafts.Remove(key);
        else _drafts[key] = text;
    }

    private void OnAuthChanged()
    {
        if (!_auth.IsSignedIn) _drafts.Clear();
    }

    public void Dispose() => _auth.Changed -= OnAuthChanged;
}

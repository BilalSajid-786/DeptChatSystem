using System.Text.Json;
using DeptChat.Shared.DTOs;
using DeptChat.Shared.Enums;
using Microsoft.JSInterop;

namespace DeptChat.Web.Client.Services;

/// <summary>
/// The signed-in user for this browser tab. Stores the login response (incl. JWT) in
/// localStorage ("keep me signed in") or sessionStorage.
///
/// Phase 1: signing out only clears this browser. It deliberately does NOT call
/// POST api/auth/logout (that deletes all of the user's phone push tokens) and does NOT
/// revoke its own session (that sends ForceLogout to every device of the user).
/// </summary>
public sealed class AuthSession : IDisposable
{
    private const string StorageKey = "deptchat_auth";
    private readonly BrowserInterop _browser;
    private DotNetObjectReference<AuthSession>? _selfRef;
    private bool _persistent = true;

    public AuthSession(BrowserInterop browser) => _browser = browser;

    public AuthResponseDto? User { get; private set; }
    public bool IsSignedIn => User != null;
    public bool IsAdmin => User?.Role == UserRole.Admin;
    public bool IsManager => User?.Role == UserRole.Manager;
    public bool IsStaff => User?.Role is UserRole.Admin or UserRole.Manager;
    public Guid UserId => User?.UserId ?? Guid.Empty;

    /// <summary>Shown on the sign-in page after a forced or expired sign-out.</summary>
    public string? SignOutReason { get; set; }

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        try
        {
            var json = await _browser.LoadAsync(StorageKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                User = JsonSerializer.Deserialize<AuthResponseDto>(json, JsonOptions);
                _persistent = await _browser.IsPersistentAsync(StorageKey);
            }
        }
        catch
        {
            User = null;
        }

        _selfRef = DotNetObjectReference.Create(this);
        try { await _browser.WatchStorageAsync(_selfRef, StorageKey); } catch { }
    }

    public async Task SignInAsync(AuthResponseDto response, bool keepSignedIn)
    {
        User = response;
        SignOutReason = null;
        _persistent = keepSignedIn;
        await _browser.SaveAsync(StorageKey, JsonSerializer.Serialize(response, JsonOptions), keepSignedIn);
        Changed?.Invoke();
    }

    /// <summary>Updates the stored name/phone/address after a profile edit.</summary>
    public async Task ApplyProfileAsync(UserProfileDto profile)
    {
        if (User == null) return;
        User = User with
        {
            FullName = profile.FullName,
            PhoneNumber = profile.PhoneNumber,
            Address = profile.Address,
            Username = profile.Username
        };
        await _browser.SaveAsync(StorageKey, JsonSerializer.Serialize(User, JsonOptions), _persistent);
        Changed?.Invoke();
    }

    public async Task SignOutAsync(string? reason = null)
    {
        if (User == null && reason == null) return;
        User = null;
        SignOutReason = reason;
        await _browser.RemoveAsync(StorageKey);
        Changed?.Invoke();
    }

    [JSInvokable]
    public Task OnSignedOutElsewhere()
    {
        if (User == null) return Task.CompletedTask;
        User = null;
        SignOutReason = "You signed out in another tab.";
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public void Dispose() => _selfRef?.Dispose();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

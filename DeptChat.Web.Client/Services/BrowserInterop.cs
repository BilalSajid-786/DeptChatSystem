using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DeptChat.Web.Client.Services;

/// <summary>Typed wrapper around wwwroot/js/deptchat.js.</summary>
public sealed class BrowserInterop
{
    private readonly IJSRuntime _js;

    public BrowserInterop(IJSRuntime js) => _js = js;

    public ValueTask<string?> LoadAsync(string key) => _js.InvokeAsync<string?>("deptchat.storage.load", key);
    public ValueTask<bool> SaveAsync(string key, string value, bool persistent) => _js.InvokeAsync<bool>("deptchat.storage.save", key, value, persistent);
    public ValueTask<string?> LoadLocalAsync(string key) => _js.InvokeAsync<string?>("deptchat.storage.loadLocal", key);
    public ValueTask<bool> SaveLocalAsync(string key, string value) => _js.InvokeAsync<bool>("deptchat.storage.saveLocal", key, value);
    public ValueTask<bool> IsPersistentAsync(string key) => _js.InvokeAsync<bool>("deptchat.storage.isPersistent", key);
    public ValueTask RemoveAsync(string key) => _js.InvokeVoidAsync("deptchat.storage.remove", key);
    public ValueTask WatchStorageAsync<T>(DotNetObjectReference<T> reference, string key) where T : class
        => _js.InvokeVoidAsync("deptchat.storage.watch", reference, key);

    public ValueTask<string> DeviceIdAsync() => _js.InvokeAsync<string>("deptchat.deviceId");
    public ValueTask<string> DeviceNameAsync() => _js.InvokeAsync<string>("deptchat.deviceName");

    public ValueTask ScrollToBottomAsync(ElementReference el) => _js.InvokeVoidAsync("deptchat.scroll.toBottom", el);
    public ValueTask<bool> IsNearBottomAsync(ElementReference el) => _js.InvokeAsync<bool>("deptchat.scroll.isNearBottom", el);
    public ValueTask<double> ScrollHeightAsync(ElementReference el) => _js.InvokeAsync<double>("deptchat.scroll.height", el);
    public ValueTask KeepScrollPositionAsync(ElementReference el, double oldHeight) => _js.InvokeVoidAsync("deptchat.scroll.keepPosition", el, oldHeight);
    public ValueTask WatchScrollAsync<T>(ElementReference el, DotNetObjectReference<T> reference) where T : class
        => _js.InvokeVoidAsync("deptchat.scroll.watch", el, reference);

    public ValueTask<bool> IsVisibleAsync() => _js.InvokeAsync<bool>("deptchat.visibility.isVisible");
    public ValueTask WatchVisibilityAsync<T>(DotNetObjectReference<T> reference) where T : class
        => _js.InvokeVoidAsync("deptchat.visibility.watch", reference);

    public ValueTask AttachComposerAsync<T>(ElementReference el, DotNetObjectReference<T> reference) where T : class
        => _js.InvokeVoidAsync("deptchat.composer.attach", el, reference);
    public ValueTask ResetComposerAsync(ElementReference el) => _js.InvokeVoidAsync("deptchat.composer.reset", el);
    public ValueTask FocusAsync(ElementReference el) => _js.InvokeVoidAsync("deptchat.composer.focus", el);
    public ValueTask AttachDropAsync(ElementReference zone, string inputId) => _js.InvokeVoidAsync("deptchat.attachDrop", zone, inputId);

    public ValueTask<bool> CopyAsync(string text) => _js.InvokeAsync<bool>("deptchat.copy", text);

    public ValueTask<string> NotificationPermissionAsync() => _js.InvokeAsync<string>("deptchat.notify.permission");
    public ValueTask<string> RequestNotificationPermissionAsync() => _js.InvokeAsync<string>("deptchat.notify.request");
    public ValueTask ShowNotificationAsync(string title, string body) => _js.InvokeVoidAsync("deptchat.notify.show", title, body);

    public ValueTask<bool> VoiceSupportedAsync() => _js.InvokeAsync<bool>("deptchat.voice.isSupported");
    public ValueTask<string> StartRecordingAsync() => _js.InvokeAsync<string>("deptchat.voice.start");
    public ValueTask<int> RecordingElapsedAsync() => _js.InvokeAsync<int>("deptchat.voice.elapsed");
    public ValueTask<VoiceRecording?> StopRecordingAsync() => _js.InvokeAsync<VoiceRecording?>("deptchat.voice.stop");
    public ValueTask CancelRecordingAsync() => _js.InvokeVoidAsync("deptchat.voice.cancel");
    public ValueTask RevokeObjectUrlAsync(string? url) => _js.InvokeVoidAsync("deptchat.voice.revoke", url);

    public ValueTask PlayAudioAsync<T>(DotNetObjectReference<T> reference, string id, string src, double rate) where T : class
        => _js.InvokeVoidAsync("deptchat.audio.play", reference, id, src, rate);
    public ValueTask PauseAudioAsync() => _js.InvokeVoidAsync("deptchat.audio.pause");
    public ValueTask SetAudioRateAsync(double rate) => _js.InvokeVoidAsync("deptchat.audio.setRate", rate);
    public ValueTask StopAudioAsync() => _js.InvokeVoidAsync("deptchat.audio.stop");
}

public sealed class VoiceRecording
{
    public string Base64 { get; set; } = string.Empty;
    public int Seconds { get; set; }
    public string PreviewUrl { get; set; } = string.Empty;
}

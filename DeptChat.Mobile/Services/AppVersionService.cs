using System.Text.Json;
using DeptChat.Shared.DTOs;

namespace DeptChat.Mobile.Services;

public class AppVersionCheckResult
{
    public bool IsUpdateRequired { get; set; }
    public bool IsUpdateAvailable { get; set; }
    public string CurrentDisplayVersion { get; set; } = string.Empty;
    public int CurrentBuildNumber { get; set; }
    public AppVersionDto? RemoteVersion { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public class AppVersionService
{
    private readonly HttpClient _httpClient;
    private readonly ServerConfigService _serverConfig;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AppVersionService(HttpClient httpClient, ServerConfigService serverConfig)
    {
        _httpClient = httpClient;
        _serverConfig = serverConfig;
    }

    /// <summary>
    /// Checks the server API for the latest and minimum supported app versions.
    /// Determines whether the app must be restricted/locked until the user updates.
    /// </summary>
    public async Task<AppVersionCheckResult> CheckVersionAsync()
    {
        var localVersion = AppInfo.Current.VersionString;
        int localBuild = int.TryParse(AppInfo.Current.BuildString, out var b) ? b : 1;

        var result = new AppVersionCheckResult
        {
            CurrentDisplayVersion = localVersion,
            CurrentBuildNumber = localBuild
        };

        try
        {
            var baseUrl = _serverConfig.CurrentUrl.TrimEnd('/');
            var requestUrl = $"{baseUrl}/api/app/version";

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var response = await _httpClient.GetAsync(requestUrl, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                result.ErrorMessage = $"Server returned HTTP {response.StatusCode}";
                return result;
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            var dto = JsonSerializer.Deserialize<AppVersionDto>(json, _jsonOptions);

            if (dto == null)
            {
                result.ErrorMessage = "Failed to deserialize version info from server.";
                return result;
            }

            result.RemoteVersion = dto;

            // Required update rule:
            // 1. Build number is strictly below server's MinimumRequiredBuildNumber
            // 2. OR ForceUpdate is set to true on server and client is below LatestBuildNumber
            bool updateRequired = localBuild < dto.MinimumRequiredBuildNumber ||
                                 (dto.ForceUpdate && localBuild < dto.LatestBuildNumber);

            // Optional update rule:
            bool updateAvailable = localBuild < dto.LatestBuildNumber;

            result.IsUpdateRequired = updateRequired;
            result.IsUpdateAvailable = updateAvailable;

            return result;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            System.Diagnostics.Debug.WriteLine($"[AppVersionService] CheckVersionAsync error: {ex.Message}");
            return result;
        }
    }
}

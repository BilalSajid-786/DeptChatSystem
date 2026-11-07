namespace DeptChat.Shared.DTOs;

public record AppVersionDto(
    string LatestVersion,
    int LatestBuildNumber,
    string MinimumRequiredVersion,
    int MinimumRequiredBuildNumber,
    bool ForceUpdate,
    string DownloadUrl,
    string ReleaseNotes
);

using DeptChat.Mobile.Services;

namespace DeptChat.Mobile.Views;

public partial class ForceUpdatePage : ContentPage
{
    private AppVersionCheckResult? _result;

    public ForceUpdatePage(AppVersionCheckResult result)
    {
        InitializeComponent();
        _result = result;
        PopulateData();
    }

    public ForceUpdatePage()
    {
        InitializeComponent();
    }

    private void PopulateData()
    {
        if (_result == null) return;

        CurrentVersionLabel.Text = $"v{_result.CurrentDisplayVersion}";
        LatestVersionLabel.Text = $"v{_result.RemoteVersion?.LatestVersion ?? "1.0.1"}";

        if (!string.IsNullOrWhiteSpace(_result.RemoteVersion?.ReleaseNotes))
        {
            ReleaseNotesLabel.Text = _result.RemoteVersion.ReleaseNotes;
        }
    }

    private async void OnDownloadClicked(object sender, EventArgs e)
    {
        var downloadUrl = _result?.RemoteVersion?.DownloadUrl;
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            downloadUrl = "https://179.197.255.148.sslip.io/api/app/download-apk";
        }

        try
        {
            await Launcher.Default.OpenAsync(new Uri(downloadUrl));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Download Error", $"Could not open browser for update:\n{ex.Message}", "OK");
        }
    }

    private async void OnCheckAgainClicked(object sender, EventArgs e)
    {
        var versionService = IPlatformApplication.Current?.Services.GetService<AppVersionService>();
        if (versionService == null) return;

        var check = await versionService.CheckVersionAsync();
        if (!check.IsUpdateRequired)
        {
            // App has been updated! Restore normal navigation
            if (Application.Current?.Windows.Count > 0)
            {
                Application.Current.Windows[0].Page = new AppShell();
            }
        }
        else
        {
            _result = check;
            PopulateData();
            await DisplayAlert("Update Required", $"You are still running v{check.CurrentDisplayVersion}. Please install the new APK (v{check.RemoteVersion?.LatestVersion}) to continue.", "OK");
        }
    }

    /// <summary>
    /// Intercepts Android hardware back button so users cannot bypass the update gate.
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        return true;
    }
}

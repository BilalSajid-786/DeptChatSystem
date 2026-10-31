using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DeptChat.Shared.DTOs;
using DeptChat.Shared.Enums;

namespace DeptChat.Mobile.Services;

public class AuthService
{
    private const string AuthStorageKey = "auth_current_user";
    private readonly HttpClient _httpClient;
    private readonly ServerConfigService _serverConfig;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AuthResponseDto? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser != null && !string.IsNullOrEmpty(CurrentUser.Token);
    public string? Token => CurrentUser?.Token;
    public Guid? UserId => CurrentUser?.UserId;
    public UserRole? Role => CurrentUser?.Role;
    public Guid? ClientId => CurrentUser?.ClientId;
    public string? ClientName => CurrentUser?.ClientName;
    public string? PhoneNumber => CurrentUser?.PhoneNumber;
    public string? Address => CurrentUser?.Address;
    public List<Guid> DepartmentIds => CurrentUser?.DepartmentIds ?? new();

    public AuthService(HttpClient httpClient, ServerConfigService serverConfig)
    {
        _httpClient = httpClient;
        _serverConfig = serverConfig;
        TryRestoreUser();
    }

    private string BuildUrl(string path) => $"{_serverConfig.CurrentUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    public bool TryRestoreUser()
    {
        try
        {
            var json = Preferences.Default.Get(AuthStorageKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(json))
            {
                CurrentUser = JsonSerializer.Deserialize<AuthResponseDto>(json, JsonOptions);
                return IsAuthenticated;
            }
        }
        catch { }
        return false;
    }

    public string? LastError { get; private set; }

    public async Task<bool> LoginAsync(string identifier, string password)
    {
        LastError = null;
        try
        {
            var response = await _httpClient.PostAsJsonAsync(BuildUrl("api/auth/login"), new LoginRequestDto(identifier, password));
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || 
                    response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    LastError = "Invalid email or password.";
                }
                else
                {
                    LastError = $"Server returned {(int)response.StatusCode}: {response.ReasonPhrase}";
                }
                return false;
            }

            var content = await response.Content.ReadAsStringAsync();
            CurrentUser = JsonSerializer.Deserialize<AuthResponseDto>(content, JsonOptions);
            if (CurrentUser != null)
            {
                Preferences.Default.Set(AuthStorageKey, content);
                return true;
            }
            LastError = "Invalid server response.";
            return false;
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
            LastError = $"Connection failed ({_serverConfig.CurrentUrl}): {detail}";
            return false;
        }
    }

    public async Task<bool> RegisterAsync(string fullName, string? email, string password, string companyCode, string? phoneNumber = null, string? address = null)
    {
        LastError = null;
        try
        {
            var reqDto = new RegisterRequestDto(fullName, string.IsNullOrWhiteSpace(email) ? null : email.Trim(), password, companyCode, string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim(), address);
            var response = await _httpClient.PostAsJsonAsync(BuildUrl("api/auth/register"), reqDto);
            if (!response.IsSuccessStatusCode)
            {
                var errContent = await response.Content.ReadAsStringAsync();
                LastError = !string.IsNullOrWhiteSpace(errContent) ? errContent : $"Registration failed ({(int)response.StatusCode})";
                return false;
            }

            var content = await response.Content.ReadAsStringAsync();
            CurrentUser = JsonSerializer.Deserialize<AuthResponseDto>(content, JsonOptions);
            if (CurrentUser != null)
            {
                Preferences.Default.Set(AuthStorageKey, content);
                return true;
            }
            LastError = "Invalid server response.";
            return false;
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
            LastError = $"Connection failed ({_serverConfig.CurrentUrl}): {detail}";
            return false;
        }
    }

    public async Task<bool> UpdateProfileAsync(string fullName, string? phoneNumber, string? address)
    {
        LastError = null;
        try
        {
            if (string.IsNullOrEmpty(Token)) return false;

            using var req = new HttpRequestMessage(HttpMethod.Put, BuildUrl("api/auth/profile"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            var reqDto = new UpdateProfileRequestDto(fullName, phoneNumber, address);
            req.Content = JsonContent.Create(reqDto);

            var response = await _httpClient.SendAsync(req);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                LastError = !string.IsNullOrWhiteSpace(err) ? err : $"Failed to update profile ({(int)response.StatusCode})";
                return false;
            }

            var content = await response.Content.ReadAsStringAsync();
            var profile = JsonSerializer.Deserialize<UserProfileDto>(content, JsonOptions);
            if (profile != null && CurrentUser != null)
            {
                CurrentUser = CurrentUser with
                {
                    FullName = profile.FullName,
                    PhoneNumber = profile.PhoneNumber,
                    Address = profile.Address
                };
                Preferences.Default.Set(AuthStorageKey, JsonSerializer.Serialize(CurrentUser, JsonOptions));
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    public void Logout()
    {
        CurrentUser = null;
        try
        {
            Preferences.Default.Remove(AuthStorageKey);
        }
        catch { }
    }
}

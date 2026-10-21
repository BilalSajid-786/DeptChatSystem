using DeptChat.Mobile.Services;
using DeptChat.Mobile.ViewModels;
using DeptChat.Mobile.Views;

namespace DeptChat.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Live Public Backend URL for multi-device testing (worldwide HTTPS tunnel)
        var serverConfig = new ServerConfigService();
        builder.Services.AddSingleton(serverConfig);

        var httpClientHandler = FastHttpHandler.CreateHandler();
        var httpClient = new HttpClient(httpClientHandler, disposeHandler: false);
        builder.Services.AddSingleton(httpClient);

        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<ChatApiService>();

        var signalR = new SignalRService(serverConfig.CurrentUrl);
        serverConfig.OnUrlChanged += (newUrl) =>
        {
            signalR.UpdateBaseUrl(newUrl);
        };
        builder.Services.AddSingleton(signalR);

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "deptchat_outbox.db3");
        var outboxService = new LocalOutboxService(dbPath);
        _ = outboxService.InitializeAsync();
        builder.Services.AddSingleton(outboxService);

#if ANDROID
        builder.Services.AddSingleton<IAudioService, Platforms.Android.AndroidAudioService>();
#else
        builder.Services.AddSingleton<IAudioService, DefaultAudioService>();
#endif

        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<LoginPage>();

        builder.Services.AddTransient<DepartmentsViewModel>();
        builder.Services.AddTransient<DepartmentsPage>();

        builder.Services.AddTransient<DepartmentConversationsViewModel>();
        builder.Services.AddTransient<DepartmentConversationsPage>();

        builder.Services.AddTransient<ChatRoomViewModel>();
        builder.Services.AddTransient<ChatRoomPage>();

        return builder.Build();
    }
}

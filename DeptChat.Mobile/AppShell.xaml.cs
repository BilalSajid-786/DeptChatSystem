using DeptChat.Mobile.Views;

namespace DeptChat.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Register navigation routes
        Routing.RegisterRoute(nameof(DepartmentConversationsPage), typeof(DepartmentConversationsPage));
        Routing.RegisterRoute(nameof(ChatRoomPage), typeof(ChatRoomPage));

        try
        {
            var authJson = Preferences.Default.Get("auth_current_user", string.Empty);
            if (!string.IsNullOrWhiteSpace(authJson))
            {
                CurrentItem = DepartmentsShellContent;
            }
        }
        catch { }
    }
}

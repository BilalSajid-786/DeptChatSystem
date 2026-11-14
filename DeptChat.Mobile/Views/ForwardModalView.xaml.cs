namespace DeptChat.Mobile.Views;

/// <summary>
/// Forward-message popup shown from the chat screen (Admins). Inherits the page's
/// ChatRoomViewModel as its BindingContext; created the first time it is opened.
/// </summary>
public partial class ForwardModalView : ContentView
{
    public ForwardModalView()
    {
        InitializeComponent();
    }
}

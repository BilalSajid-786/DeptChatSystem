namespace DeptChat.Mobile.Views;

public partial class ImageViewerPage : ContentPage
{
    public ImageViewerPage(string imageSource)
    {
        InitializeComponent();
        if (!string.IsNullOrEmpty(imageSource))
        {
            ViewerImage.Source = ImageSource.FromFile(imageSource);
            if (ViewerImage.Source == null || (imageSource.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || imageSource.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                ViewerImage.Source = ImageSource.FromUri(new Uri(imageSource));
            }
        }
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}

namespace DeptChat.Mobile.Views;

public partial class ImageViewerPage : ContentPage
{
    private double _currentScale = 1;
    private double _startScale = 1;
    private double _xOffset = 0;
    private double _yOffset = 0;

    public ImageViewerPage(string imageSource)
    {
        InitializeComponent();
        if (!string.IsNullOrEmpty(imageSource))
        {
            if (imageSource.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                imageSource.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                ViewerImage.Source = ImageSource.FromUri(new Uri(imageSource));
            }
            else
            {
                ViewerImage.Source = ImageSource.FromFile(imageSource);
            }
        }
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }

    private void OnPinchUpdated(object sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status == GestureStatus.Started)
        {
            _startScale = ViewerImage.Scale;
            ViewerImage.AnchorX = e.ScaleOrigin.X;
            ViewerImage.AnchorY = e.ScaleOrigin.Y;
        }
        else if (e.Status == GestureStatus.Running)
        {
            _currentScale += (e.Scale - 1) * _startScale;
            _currentScale = Math.Max(1, Math.Min(_currentScale, 5));
            ViewerImage.Scale = _currentScale;
        }
        else if (e.Status == GestureStatus.Completed)
        {
            if (_currentScale <= 1.05)
            {
                ResetZoom(animated: true);
            }
        }
    }

    private void OnPanUpdated(object sender, PanUpdatedEventArgs e)
    {
        if (ViewerImage.Scale <= 1)
        {
            _xOffset = 0;
            _yOffset = 0;
            ViewerImage.TranslationX = 0;
            ViewerImage.TranslationY = 0;
            return;
        }

        switch (e.StatusType)
        {
            case GestureStatus.Running:
                var transX = _xOffset + e.TotalX;
                var transY = _yOffset + e.TotalY;

                var maxTransX = (ViewerImage.Width * (ViewerImage.Scale - 1)) / 2;
                var maxTransY = (ViewerImage.Height * (ViewerImage.Scale - 1)) / 2;

                ViewerImage.TranslationX = Math.Max(-maxTransX, Math.Min(maxTransX, transX));
                ViewerImage.TranslationY = Math.Max(-maxTransY, Math.Min(maxTransY, transY));
                break;

            case GestureStatus.Completed:
                _xOffset = ViewerImage.TranslationX;
                _yOffset = ViewerImage.TranslationY;
                break;
        }
    }

    private void OnDoubleTapped(object sender, TappedEventArgs e)
    {
        if (ViewerImage.Scale > 1.2)
        {
            ResetZoom(animated: true);
        }
        else
        {
            _currentScale = 2.5;
            ViewerImage.AnchorX = 0.5;
            ViewerImage.AnchorY = 0.5;
            ViewerImage.ScaleTo(2.5, 200, Easing.CubicOut);
        }
    }

    private void ResetZoom(bool animated = false)
    {
        _currentScale = 1;
        _startScale = 1;
        _xOffset = 0;
        _yOffset = 0;
        ViewerImage.AnchorX = 0.5;
        ViewerImage.AnchorY = 0.5;

        if (animated)
        {
            ViewerImage.ScaleTo(1, 200, Easing.CubicOut);
            ViewerImage.TranslateTo(0, 0, 200, Easing.CubicOut);
        }
        else
        {
            ViewerImage.Scale = 1;
            ViewerImage.TranslationX = 0;
            ViewerImage.TranslationY = 0;
        }
    }
}

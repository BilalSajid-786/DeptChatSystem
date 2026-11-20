namespace DeptChat.Mobile.Views;

/// <summary>
/// WhatsApp-style waveform bars for a voice message, drawn as ONE lightweight view (no child views),
/// so voice rows stay cheap to scroll and recycle. Bars before <see cref="Progress"/> use
/// <see cref="PlayedColor"/>, the rest <see cref="UnplayedColor"/>.
/// Bar heights come from <see cref="Levels"/> (read from the audio in the background by
/// VoiceWaveforms); until they are ready, a fixed placeholder pattern picked from <see cref="Seed"/>
/// is shown. Drawing allocates nothing and reads no audio.
/// Touches pass through to the slider placed on top of it.
/// </summary>
public class VoiceWaveformView : GraphicsView, IDrawable
{
    private const int BarCount = Services.VoiceWaveforms.BarCount;
    private const float BarWidth = 3f;
    private const float MinBarFraction = 0.18f;

    public static readonly BindableProperty ProgressProperty =
        BindableProperty.Create(nameof(Progress), typeof(double), typeof(VoiceWaveformView), 0.0, propertyChanged: Redraw);

    public static readonly BindableProperty SeedProperty =
        BindableProperty.Create(nameof(Seed), typeof(int), typeof(VoiceWaveformView), 0, propertyChanged: Redraw);

    public static readonly BindableProperty LevelsProperty =
        BindableProperty.Create(nameof(Levels), typeof(byte[]), typeof(VoiceWaveformView), null, propertyChanged: Redraw);

    public static readonly BindableProperty PlayedColorProperty =
        BindableProperty.Create(nameof(PlayedColor), typeof(Color), typeof(VoiceWaveformView), Colors.SeaGreen, propertyChanged: Redraw);

    public static readonly BindableProperty UnplayedColorProperty =
        BindableProperty.Create(nameof(UnplayedColor), typeof(Color), typeof(VoiceWaveformView), Color.FromArgb("#B0BEC5"), propertyChanged: Redraw);

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public int Seed
    {
        get => (int)GetValue(SeedProperty);
        set => SetValue(SeedProperty, value);
    }

    /// <summary>One value per bar (0-255) from the real audio, or null for the placeholder pattern.</summary>
    public byte[]? Levels
    {
        get => (byte[]?)GetValue(LevelsProperty);
        set => SetValue(LevelsProperty, value);
    }

    public Color PlayedColor
    {
        get => (Color)GetValue(PlayedColorProperty);
        set => SetValue(PlayedColorProperty, value);
    }

    public Color UnplayedColor
    {
        get => (Color)GetValue(UnplayedColorProperty);
        set => SetValue(UnplayedColorProperty, value);
    }

    public VoiceWaveformView()
    {
        Drawable = this;
        InputTransparent = true;
        BackgroundColor = Colors.Transparent;
    }

    private static void Redraw(BindableObject bindable, object oldValue, object newValue) =>
        ((VoiceWaveformView)bindable).Invalidate();

    public void Draw(ICanvas canvas, RectF rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var gap = (rect.Width - BarCount * BarWidth) / (BarCount - 1);
        if (gap < 1f) gap = 1f;

        var playedBars = (int)Math.Round(Math.Clamp(Progress, 0.0, 1.0) * BarCount);
        var midY = rect.Y + rect.Height / 2f;
        var state = (uint)Seed | 1u;
        var levels = Levels;
        var useLevels = levels != null && levels.Length == BarCount;

        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeSize = BarWidth;

        for (var i = 0; i < BarCount; i++)
        {
            // Small deterministic pseudo-random sequence (xorshift), shaped so the middle is a bit louder.
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            var random = (state % 1000) / 1000f;
            var shape = 0.55f + 0.45f * MathF.Sin(MathF.PI * (i + 0.5f) / BarCount);
            var fraction = useLevels
                ? MinBarFraction + (1f - MinBarFraction) * (levels![i] / 255f)
                : MinBarFraction + (1f - MinBarFraction) * random * shape;

            var half = Math.Max(BarWidth / 2f, rect.Height * fraction / 2f);
            var x = rect.X + BarWidth / 2f + i * (BarWidth + gap);

            canvas.StrokeColor = i < playedBars ? PlayedColor : UnplayedColor;
            canvas.DrawLine(x, midY - half + BarWidth / 2f, x, midY + half - BarWidth / 2f);
        }
    }
}

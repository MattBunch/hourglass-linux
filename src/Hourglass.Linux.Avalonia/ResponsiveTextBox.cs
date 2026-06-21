using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Hourglass.Linux.Avalonia;

public sealed class ResponsiveTextBox : TextBox
{
    public static readonly StyledProperty<double> MinFontSizeProperty = AvaloniaProperty.Register<ResponsiveTextBox, double>(
        nameof(MinFontSize),
        ResponsiveTextSizing.PrimaryMinimumFontSize);

    public static readonly StyledProperty<double> MaxFontSizeProperty = AvaloniaProperty.Register<ResponsiveTextBox, double>(
        nameof(MaxFontSize),
        ResponsiveTextSizing.PrimaryBaseFontSize);

    private const double WidthSafetyInset = 2;

    private MeasurementKey? previousMeasurement;
    private bool isUpdatingFontSize;

    protected override Type StyleKeyOverride => typeof(TextBox);

    public double MinFontSize
    {
        get => this.GetValue(MinFontSizeProperty);
        set => this.SetValue(MinFontSizeProperty, value);
    }

    public double MaxFontSize
    {
        get => this.GetValue(MaxFontSizeProperty);
        set => this.SetValue(MaxFontSizeProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        this.UpdateFontSize();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        this.UpdateFontSize();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (!this.isUpdatingFontSize && IsMeasurementProperty(change.Property))
        {
            this.UpdateFontSize();
        }
    }

    private static bool IsMeasurementProperty(AvaloniaProperty property)
    {
        return property == TextProperty
            || property == PlaceholderTextProperty
            || property == FontFamilyProperty
            || property == FontStyleProperty
            || property == FontWeightProperty
            || property == FontStretchProperty
            || property == LetterSpacingProperty
            || property == FlowDirectionProperty
            || property == PaddingProperty
            || property == BorderThicknessProperty
            || property == MinFontSizeProperty
            || property == MaxFontSizeProperty;
    }

    private void UpdateFontSize()
    {
        double availableWidth = this.Bounds.Width
            - this.Padding.Left
            - this.Padding.Right
            - this.BorderThickness.Left
            - this.BorderThickness.Right
            - WidthSafetyInset;

        if (!double.IsFinite(availableWidth) || availableWidth <= 0)
        {
            return;
        }

        string measurementText = ResponsiveTextSizing.GetMeasurementText(this.Text, this.PlaceholderText);
        var measurement = new MeasurementKey(
            measurementText,
            availableWidth,
            this.MinFontSize,
            this.MaxFontSize,
            this.FontFamily,
            this.FontStyle,
            this.FontWeight,
            this.FontStretch,
            this.LetterSpacing,
            this.FlowDirection);

        if (measurement == this.previousMeasurement)
        {
            return;
        }

        this.previousMeasurement = measurement;
        var typeface = new Typeface(this.FontFamily, this.FontStyle, this.FontWeight, this.FontStretch);
        double measuredTextWidth = ResponsiveTextMeasurement.MeasureWidth(
            measurementText,
            typeface,
            this.MaxFontSize,
            this.FlowDirection,
            this.LetterSpacing);
        double nextFontSize = ResponsiveTextSizing.CalculateFittedFontSize(
            availableWidth,
            measuredTextWidth,
            this.MinFontSize,
            this.MaxFontSize);

        if (Math.Abs(this.FontSize - nextFontSize) < 0.01)
        {
            return;
        }

        this.isUpdatingFontSize = true;
        try
        {
            this.FontSize = nextFontSize;
        }
        finally
        {
            this.isUpdatingFontSize = false;
        }
    }

    private readonly record struct MeasurementKey(
        string Text,
        double AvailableWidth,
        double MinimumFontSize,
        double MaximumFontSize,
        FontFamily FontFamily,
        FontStyle FontStyle,
        FontWeight FontWeight,
        FontStretch FontStretch,
        double LetterSpacing,
        FlowDirection FlowDirection);
}

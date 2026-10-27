using Resonant.Core.Localization;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace Resonant.App.Controls;

/// <summary>
/// Needle and strobe pitch meter on a -50..+50 cent scale. The needle is clamped to the scale but off-scale errors
/// are flagged. Needle and strobe are shown together; reduced motion keeps the strobe stationary.
/// </summary>
public sealed partial class PitchMeter : UserControl
{
    public static readonly DependencyProperty CentsProperty = DependencyProperty.Register(
        nameof(Cents), typeof(double), typeof(PitchMeter), new PropertyMetadata(double.NaN, OnVisualChanged));

    public static readonly DependencyProperty IsLockedProperty = DependencyProperty.Register(
        nameof(IsLocked), typeof(bool), typeof(PitchMeter), new PropertyMetadata(false, OnVisualChanged));

    public static readonly DependencyProperty IsStaleProperty = DependencyProperty.Register(
        nameof(IsStale), typeof(bool), typeof(PitchMeter), new PropertyMetadata(false, OnVisualChanged));

    private const double StripePeriod = 40.0;
    private const double StrobePixelsPerSecondPerCent = 8.0;

    private readonly UISettings _uiSettings = new();
    private readonly Stopwatch _clock = new();
    private double _offset;
    private TimeSpan _last;
    private double _displayCents = double.NaN;

    public PitchMeter()
    {
        InitializeComponent();
        BuildStripes();
        Loaded += (_, _) =>
        {
            CompositionTarget.Rendering += OnRendering;
            _clock.Restart();
            _last = TimeSpan.Zero;
        };
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
        UpdateVisuals();
    }

    /// <summary>Signed error in cents; NaN means no reading, and no needle is drawn.</summary>
    public double Cents
    {
        get => (double)GetValue(CentsProperty);
        set => SetValue(CentsProperty, value);
    }

    public bool IsLocked
    {
        get => (bool)GetValue(IsLockedProperty);
        set => SetValue(IsLockedProperty, value);
    }

    public bool IsStale
    {
        get => (bool)GetValue(IsStaleProperty);
        set => SetValue(IsStaleProperty, value);
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PitchMeter)d).UpdateVisuals();

    /// <summary>Re-reads the localized automation name after a language change.</summary>
    public void RefreshAutomation() => UpdateVisuals();

    private void BuildStripes()
    {
        var ticks = new GeometryGroup();
        var minorTicks = new GeometryGroup();
        for (var tick = -10; tick <= 10; tick++)
        {
            if (tick == 0)
            {
                continue;
            }

            var major = tick % 2 == 0;
            var angle = tick * 7.0 * Math.PI / 180.0;
            var radius = major ? 164 : 175;
            (major ? ticks : minorTicks).Children.Add(new LineGeometry
            {
                StartPoint = new Point(260 + Math.Sin(angle) * radius, 220 - Math.Cos(angle) * radius),
                EndPoint = new Point(260 + Math.Sin(angle) * 181, 220 - Math.Cos(angle) * 181),
            });
        }
        ScaleTicks.Data = ticks;
        MinorTicks.Data = minorTicks;
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        for (var i = 0; i <= 16; i++)
        {
            group.Children.Add(new RectangleGeometry { Rect = new Rect(i * StripePeriod, 0, StripePeriod / 2, 50) });
        }

        StrobeStripes.Data = group;
    }

    private void UpdateVisuals()
    {
        var cents = Cents;
        var hasReading = double.IsFinite(cents);
        NeedleHub.Visibility = hasReading ? Visibility.Visible : Visibility.Collapsed;
        NeedleHub.Opacity = IsStale ? 0.4 : 1.0;
        StrobeClip.Visibility = hasReading ? Visibility.Visible : Visibility.Collapsed;
        StrobeClip.Opacity = IsStale ? 0.12 : 1.0;

        if (!hasReading)
        {
            NeedleAccent.Visibility = Visibility.Collapsed;
            NeedlePrimary.Visibility = Visibility.Collapsed;
            NeedleRetained.Visibility = Visibility.Collapsed;
            OffScaleLeft.Visibility = Visibility.Collapsed;
            OffScaleRight.Visibility = Visibility.Collapsed;
            AutomationProperties.SetName(this, Text.Get("Pitch meter: no pitch detected"));
            return;
        }

        if (!double.IsFinite(_displayCents))
        {
            _displayCents = cents;
        }
        SetNeedleAngle(_displayCents);
        NeedleAccent.Visibility = !IsLocked && !IsStale ? Visibility.Visible : Visibility.Collapsed;
        NeedlePrimary.Visibility = IsLocked && !IsStale ? Visibility.Visible : Visibility.Collapsed;
        NeedleRetained.Visibility = IsStale ? Visibility.Visible : Visibility.Collapsed;

        var offScale = !IsStale && Math.Abs(cents) > 50.0;
        OffScaleLeft.Visibility = offScale && cents < 0 ? Visibility.Visible : Visibility.Collapsed;
        OffScaleRight.Visibility = offScale && cents > 0 ? Visibility.Visible : Visibility.Collapsed;

        var description = IsStale
            ? Text.Get("last reading, no current pitch")
            : IsLocked
            ? Text.Get("PitchInTune")
            : Resonant.Core.Localization.Text.Format("{0:0.0} cents {1}{2}", Math.Abs(cents), Resonant.Core.Localization.Text.Get(cents < 0 ? Text.Get("flat") : Text.Get("sharp")), Resonant.Core.Localization.Text.Get(offScale ? Text.Get(", off scale") : string.Empty));
        AutomationProperties.SetName(this, Resonant.Core.Localization.Text.Format("Pitch meter: {0}", description));
    }

    private void OnRendering(object? sender, object e)
    {
        var now = _clock.Elapsed;
        var dt = (now - _last).TotalSeconds;
        _last = now;

        if (double.IsFinite(Cents) && !IsStale)
        {
            _displayCents = _uiSettings.AnimationsEnabled && double.IsFinite(_displayCents)
                ? _displayCents + (Cents - _displayCents) * (1.0 - Math.Exp(-Math.Min(dt, 0.1) / 0.14))
                : Cents;
            SetNeedleAngle(_displayCents);
        }
        else if (!double.IsFinite(Cents))
        {
            _displayCents = double.NaN;
        }

        if (!_uiSettings.AnimationsEnabled || !double.IsFinite(Cents) || IsLocked || IsStale)
        {
            return;
        }

        var speed = Math.Clamp(Cents, -50.0, 50.0) * StrobePixelsPerSecondPerCent;
        _offset = (_offset + (speed * Math.Min(dt, 0.1))) % StripePeriod;
        if (_offset < 0)
        {
            _offset += StripePeriod;
        }

        StrobeShift.X = _offset - StripePeriod;
    }

    private void SetNeedleAngle(double cents)
    {
        var angle = Math.Clamp(cents, -50.0, 50.0) * 1.4;
        AccentRotation.Angle = angle;
        PrimaryRotation.Angle = angle;
        RetainedRotation.Angle = angle;
    }
}

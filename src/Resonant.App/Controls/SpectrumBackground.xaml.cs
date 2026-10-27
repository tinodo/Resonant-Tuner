using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Resonant.Core.Dsp;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace Resonant.App.Controls;

public sealed partial class SpectrumBackground : UserControl
{
    private readonly DispatcherQueueTimer _timer;
    private readonly Stopwatch _clock = new();
    private readonly UISettings _uiSettings = new();
    private readonly AccessibilitySettings _accessibility = new();
    private readonly float[] _levels = new float[SpectrumAnalyzer.BandCount];
    private readonly float[] _peaks = new float[SpectrumAnalyzer.BandCount];
    private readonly double[] _peakHold = new double[SpectrumAnalyzer.BandCount];
    private readonly PathFigure _contour = new() { IsClosed = true, Segments = new PathSegmentCollection() };
    private readonly PathFigure _peakContour = new() { Segments = new PathSegmentCollection() };
    private readonly QuadraticBezierSegment[] _segments = new QuadraticBezierSegment[SpectrumAnalyzer.BandCount];
    private readonly QuadraticBezierSegment[] _peakSegments = new QuadraticBezierSegment[SpectrumAnalyzer.BandCount];
    private readonly LineSegment _bottomRight = new();
    private readonly LineSegment _bottomLeft = new();
    private readonly float[] _ideal = new float[SpectrumAnalyzer.BandCount];
    private readonly PathFigure _idealContour = new() { Segments = new PathSegmentCollection() };
    private readonly QuadraticBezierSegment[] _idealSegments = new QuadraticBezierSegment[SpectrumAnalyzer.BandCount];
    private double _idealHz = double.NaN;
    private TimeSpan _last;

    public SpectrumBackground()
    {
        InitializeComponent();
        for (var band = 0; band < SpectrumAnalyzer.BandCount; band++)
        {
            _segments[band] = new QuadraticBezierSegment();
            _peakSegments[band] = new QuadraticBezierSegment();
            _idealSegments[band] = new QuadraticBezierSegment();
            _contour.Segments.Add(_segments[band]);
            _peakContour.Segments.Add(_peakSegments[band]);
            _idealContour.Segments.Add(_idealSegments[band]);
        }

        _contour.Segments.Add(_bottomRight);
        _contour.Segments.Add(_bottomLeft);
        SpectrumFill.Data = new PathGeometry { Figures = new PathFigureCollection { _contour } };
        PeakLine.Data = new PathGeometry { Figures = new PathFigureCollection { _peakContour } };
        IdealLine.Data = new PathGeometry { Figures = new PathFigureCollection { _idealContour } };
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(33);
        _timer.Tick += (_, _) => Refresh();
        SizeChanged += (_, _) => UpdateLayoutGeometry();
        Loaded += (_, _) =>
        {
            AppCulture.Changed += OnCultureChanged;
            if (_culture != System.Globalization.CultureInfo.CurrentUICulture.Name)
            {
                OnCultureChanged(null, EventArgs.Empty);
            }
        };
        Unloaded += (_, _) => AppCulture.Changed -= OnCultureChanged;
        Loaded += (_, _) =>
        {
            _clock.Restart();
            _last = TimeSpan.Zero;
            _timer.Start();
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            _clock.Stop();
            Array.Clear(_levels);
            Array.Clear(_peaks);
            Array.Clear(_peakHold);
            Plot.Opacity = 0;
            _idealHz = double.NaN;
            IdealPlot.Visibility = Visibility.Collapsed;
        };
    }

    public Func<ReadOnlyMemory<float>>? SpectrumProvider { get; set; }

    /// <summary>Pitch of the selected or detected target; NaN when there is none.</summary>
    public Func<double>? IdealFrequencyProvider { get; set; }

    private string _culture = System.Globalization.CultureInfo.CurrentUICulture.Name;

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        _culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
        Bindings.Update();
    }

    private void Refresh()
    {
        var now = _clock.Elapsed;
        var elapsed = Math.Clamp((now - _last).TotalSeconds, 0, 0.1);
        _last = now;
        var highContrast = _accessibility.HighContrast;
        Plot.Visibility = highContrast ? Visibility.Collapsed : Visibility.Visible;
        UpdateIdeal(highContrast || Visibility != Visibility.Visible);
        if (Visibility != Visibility.Visible || highContrast)
        {
            return;
        }

        var spectrum = SpectrumProvider?.Invoke() ?? ReadOnlyMemory<float>.Empty;
        var bands = spectrum.Span;
        var animate = _uiSettings.AnimationsEnabled;
        float maximum = 0;
        for (var band = 0; band < _levels.Length; band++)
        {
            var target = band < bands.Length ? bands[band] : 0;
            var duration = target > _levels[band] ? 0.045 : 0.24;
            _levels[band] = animate
                ? (float)(_levels[band] + (target - _levels[band]) * (1 - Math.Exp(-elapsed / duration)))
                : target;
            if (_levels[band] < 0.002f)
            {
                _levels[band] = 0;
            }

            if (!animate || bands.IsEmpty || _levels[band] >= _peaks[band])
            {
                _peaks[band] = _levels[band];
                _peakHold[band] = 0.45;
            }
            else
            {
                _peakHold[band] -= elapsed;
                if (_peakHold[band] <= 0)
                {
                    _peaks[band] = Math.Max(_levels[band], _peaks[band] - (float)(elapsed * 0.12));
                }
            }

            maximum = Math.Max(maximum, _peaks[band]);
        }

        Plot.Opacity = Math.Clamp(maximum * 5, 0, 1);
        UpdateContours();
    }

    private void UpdateLayoutGeometry()
    {
        Plot.Clip = new RectangleGeometry { Rect = new Rect(0, 0, ActualWidth, ActualHeight) };
        IdealPlot.Clip = new RectangleGeometry { Rect = new Rect(0, 0, ActualWidth, ActualHeight) };
        var baseline = Math.Max(0, ActualHeight - 20);
        var grid = new GeometryGroup();
        foreach (var frequency in new[] { 100.0, 1000.0, 10000.0 })
        {
            var position = FrequencyPosition(frequency);
            grid.Children.Add(new LineGeometry { StartPoint = new Point(position, 0), EndPoint = new Point(position, baseline) });
        }

        foreach (var decibels in new[] { -60.0, -40.0, -20.0 })
        {
            var position = baseline * (1 - (decibels - SpectrumAnalyzer.FloorDb) / -SpectrumAnalyzer.FloorDb);
            grid.Children.Add(new LineGeometry { StartPoint = new Point(0, position), EndPoint = new Point(ActualWidth, position) });
        }

        FrequencyGrid.Data = grid;
        Canvas.SetTop(LowLabel, baseline + 4);
        Canvas.SetTop(MidLabel, baseline + 4);
        Canvas.SetTop(HighLabel, baseline + 4);
        Canvas.SetLeft(MidLabel, Math.Max(0, FrequencyPosition(1000) - 14));
        Canvas.SetLeft(HighLabel, Math.Max(0, ActualWidth - 38));
        UpdateContours();
        UpdateIdealContour();
    }

    private void UpdateIdeal(bool suppressed)
    {
        var hz = suppressed ? double.NaN : IdealFrequencyProvider?.Invoke() ?? double.NaN;
        if (hz.Equals(_idealHz))
        {
            return;
        }

        _idealHz = hz;
        ReferenceSpectrum.ForFundamental(hz).CopyTo(_ideal, 0);
        IdealPlot.Visibility = _ideal.Max() > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateIdealContour();
    }

    private void UpdateIdealContour()
    {
        const double Scale = 0.6;
        var baseline = Math.Max(0, ActualHeight - 20);
        var height = baseline * 0.9 * Scale;
        var spacing = ActualWidth / (_ideal.Length - 1);
        _idealContour.StartPoint = new Point(0, baseline - _ideal[0] * height);
        for (var band = 0; band < _ideal.Length; band++)
        {
            var next = Math.Min(band + 1, _ideal.Length - 1);
            _idealSegments[band].Point1 = new Point(band * spacing, baseline - _ideal[band] * height);
            _idealSegments[band].Point2 = new Point((band + next) * spacing / 2, baseline - (_ideal[band] + _ideal[next]) * height / 2);
        }
    }

    private double FrequencyPosition(double frequency) =>
        ActualWidth * Math.Log(frequency / SpectrumAnalyzer.MinimumHz) / Math.Log(SpectrumAnalyzer.MaximumHz / SpectrumAnalyzer.MinimumHz);

    private void UpdateContours()
    {
        var baseline = Math.Max(0, ActualHeight - 20);
        var height = baseline * 0.9;
        var spacing = ActualWidth / (_levels.Length - 1);
        _contour.StartPoint = new Point(0, baseline - _levels[0] * height);
        _peakContour.StartPoint = new Point(0, baseline - _peaks[0] * height);
        for (var band = 0; band < _levels.Length; band++)
        {
            var next = Math.Min(band + 1, _levels.Length - 1);
            _segments[band].Point1 = new Point(band * spacing, baseline - _levels[band] * height);
            _segments[band].Point2 = new Point((band + next) * spacing / 2, baseline - (_levels[band] + _levels[next]) * height / 2);
            _peakSegments[band].Point1 = new Point(band * spacing, baseline - _peaks[band] * height);
            _peakSegments[band].Point2 = new Point((band + next) * spacing / 2, baseline - (_peaks[band] + _peaks[next]) * height / 2);
        }

        _bottomRight.Point = new Point(ActualWidth, baseline);
        _bottomLeft.Point = new Point(0, baseline);
    }
}
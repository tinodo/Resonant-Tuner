using Resonant.Core.Dsp;
using Resonant.Core.Tuning;

namespace Resonant.Core.Audio;

/// <summary>
/// Background analysis worker: every hop it copies the newest window from the ring buffer, estimates the pitch and
/// publishes an immutable <see cref="PitchReading"/>. Never touches the UI thread.
/// </summary>
public sealed class PitchEngine
{
    public static readonly TimeSpan DefaultHop = TimeSpan.FromMilliseconds(33);

    private static readonly TimeSpan ResetAfterInvalid = TimeSpan.FromMilliseconds(750);

    private readonly SampleRingBuffer _ring;
    private readonly IPitchEstimator _estimator;
    private readonly TimeProvider _time;
    private readonly TimeSpan _hop;
    private readonly PitchTracker _tracker = new();
    private PitchSearchRange _range = PitchSearchRange.Full;
    private float[] _spectrum = [];

    public PitchEngine(SampleRingBuffer ring, IPitchEstimator estimator, TimeProvider? timeProvider = null, TimeSpan? hop = null)
    {
        _ring = ring ?? throw new ArgumentNullException(nameof(ring));
        _estimator = estimator ?? throw new ArgumentNullException(nameof(estimator));
        _time = timeProvider ?? TimeProvider.System;
        _hop = hop ?? DefaultHop;
    }

    public event Action<PitchReading>? ReadingProduced;

    public ReadOnlyMemory<float> Spectrum => Volatile.Read(ref _spectrum);

    /// <summary>Current search band; safe to set from any thread.</summary>
    public PitchSearchRange SearchRange
    {
        get => Volatile.Read(ref _range);
        set => Volatile.Write(ref _range, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Analysis window length in samples: three periods of the lowest searched frequency, bounded to 4096..8192 samples at 48 kHz.</summary>
    public static int WindowLength(int sampleRate, double minHz)
    {
        var maxWindow = (int)Math.Ceiling(8192.0 * sampleRate / 48000.0);
        var minWindow = (int)Math.Ceiling(4096.0 * sampleRate / 48000.0);
        var wanted = (int)Math.Ceiling(3.0 * sampleRate / minHz);
        return Math.Clamp(wanted, minWindow, maxWindow);
    }

    public async Task RunAsync(int sampleRate, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sampleRate, 0);
        var buffer = new float[WindowLength(sampleRate, NoteMath.MinSupportedHz) + 16];
        var spectrumBuffer = new float[SpectrumAnalyzer.WindowSize];
        var spectrumAnalyzer = new SpectrumAnalyzer();
        var invalidSince = 0L;
        var hasInvalidStreak = false;
        PitchSearchRange? previousRange = null;
        _tracker.Reset();

        using var timer = new PeriodicTimer(_hop, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var range = SearchRange;
                if (range != previousRange)
                {
                    _tracker.Reset();
                    hasInvalidStreak = false;
                    previousRange = range;
                }

                var length = Math.Min(WindowLength(sampleRate, range.MinHz), buffer.Length);
                var now = _time.GetTimestamp();

                PitchReading reading;
                if (!_ring.TryCopyLatest(buffer.AsSpan(0, length)))
                {
                    reading = PitchReading.Invalid(now, PitchRejection.InsufficientData);
                }
                else
                {
                    var estimate = _tracker.Track(_estimator.Estimate(buffer.AsSpan(0, length), sampleRate, range));
                    reading = estimate.IsValid
                        ? PitchReading.Valid(now, estimate)
                        : PitchReading.Invalid(now, estimate.Rejection, estimate.Rms, estimate.IsClipping, estimate.Confidence);
                }

                if (reading.IsValid)
                {
                    hasInvalidStreak = false;
                }
                else
                {
                    if (!hasInvalidStreak)
                    {
                        hasInvalidStreak = true;
                        invalidSince = now;
                    }
                    else if (_time.GetElapsedTime(invalidSince, now) > ResetAfterInvalid)
                    {
                        _tracker.Reset();
                        hasInvalidStreak = false;
                    }
                }

                Volatile.Write(ref _spectrum, _ring.TryCopyLatest(spectrumBuffer)
                    ? spectrumAnalyzer.Analyze(spectrumBuffer, sampleRate)
                    : []);
                ReadingProduced?.Invoke(reading);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Volatile.Write(ref _spectrum, []);
        }
    }
}

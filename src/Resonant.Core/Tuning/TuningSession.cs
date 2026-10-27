using Resonant.Core.Audio;

namespace Resonant.Core.Tuning;

/// <summary>
/// Turns pitch readings into tuning guidance. In-tune requires 300 ms of confident samples inside tolerance and is
/// released beyond tolerance + 1 cent; invalid input clears the lock immediately. Thread-safe.
/// </summary>
public sealed class TuningSession : ITuningSession
{
    public static readonly TimeSpan LockDwell = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan StaleLockAfter = TimeSpan.FromMilliseconds(150);
    public const double ReleaseMarginCents = 1.0;
    public const double OffScaleCents = 50.0;
    public const double StringSwitchMarginCents = 20.0;

    private const double BoundaryEpsilon = 1e-9;

    private readonly object _sync = new();
    private readonly TimeProvider _time;

    private TuningSettings _settings = TuningSettings.Default;
    private IReadOnlyList<TuningTarget> _targets = [];
    private int? _manualIndex;
    private int? _autoIndex;
    private int _lockKey = -1;
    private long? _inToleranceSince;
    private long? _lastValidTimestamp;
    private bool _locked;
    private TuningFeedback _current = TuningFeedback.None;
    private TuningFeedback? _lastValid;

    public TuningSession(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
    }

    public TuningFeedback Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public IReadOnlyList<TuningTarget> Targets
    {
        get
        {
            lock (_sync)
            {
                return _targets;
            }
        }
    }

    public int? ManualTargetIndex
    {
        get
        {
            lock (_sync)
            {
                return _manualIndex;
            }
        }
    }

    public PitchSearchRange SearchRange
    {
        get
        {
            lock (_sync)
            {
                if (_targets.Count == 0)
                {
                    return PitchSearchRange.Full;
                }

                if (_manualIndex is { } index && index < _targets.Count)
                {
                    return Clamp(_targets[index].Hz * 0.5, _targets[index].Hz * 2.2);
                }

                var low = _targets.Min(t => t.Hz);
                var high = _targets.Max(t => t.Hz);
                return Clamp(low * 0.7, high * 1.6);
            }
        }
    }

    public void Configure(TuningPreset? preset, TuningSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_sync)
        {
            _settings = settings.Clamped();
            _targets = preset?.GetTargets(_settings) ?? [];
            if (_manualIndex is { } index && index >= _targets.Count)
            {
                _manualIndex = null;
            }

            ResetInternal();
        }
    }

    public void SelectTarget(int? targetIndex)
    {
        lock (_sync)
        {
            _manualIndex = targetIndex is { } index && index >= 0 && index < _targets.Count ? index : null;
            ResetInternal();
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            ResetInternal();
        }
    }

    public TuningFeedback Update(PitchReading reading)
    {
        lock (_sync)
        {
            var usable = reading.IsValid &&
                double.IsFinite(reading.Hz) &&
                reading.Hz is >= NoteMath.MinSupportedHz and <= NoteMath.MaxSupportedHz;

            _current = usable ? Measure(reading) : HoldOrExpire(reading.Timestamp);
            return _current;
        }
    }

    public TuningFeedback Tick()
    {
        lock (_sync)
        {
            var now = _time.GetTimestamp();
            if (_lastValidTimestamp is { } lastTs && _time.GetElapsedTime(lastTs, now) > StaleLockAfter)
            {
                _current = HoldOrExpire(now);
            }

            return _current;
        }
    }

    private TuningFeedback Measure(PitchReading reading)
    {
        var hz = reading.Hz;
        TuningTarget? target = null;
        Note note;
        double targetHz;
        int key;

        if (_targets.Count == 0)
        {
            note = NoteMath.NearestNote(hz, _settings.A4Hz, out _);
            targetHz = NoteMath.FrequencyFromMidi(note.Midi, _settings.A4Hz);
            key = 1000 + note.Midi;
        }
        else
        {
            key = ChooseTargetIndex(hz);
            target = _targets[key];
            note = target.Note;
            targetHz = target.Hz;
        }

        var cents = NoteMath.Cents(hz, targetHz);
        var absolute = Math.Abs(cents);
        var tolerance = _settings.ToleranceCents;

        if (key != _lockKey)
        {
            _lockKey = key;
            _locked = false;
            _inToleranceSince = null;
        }

        if (_locked)
        {
            if (absolute > tolerance + ReleaseMarginCents + BoundaryEpsilon)
            {
                _locked = false;
                _inToleranceSince = null;
            }
        }
        else if (absolute <= tolerance + BoundaryEpsilon)
        {
            _inToleranceSince ??= reading.Timestamp;
            if (_time.GetElapsedTime(_inToleranceSince.Value, reading.Timestamp) >= LockDwell)
            {
                _locked = true;
            }
        }
        else
        {
            _inToleranceSince = null;
        }

        var correction = _locked
            ? Correction.InTune
            : absolute <= tolerance + BoundaryEpsilon
                ? Correction.Hold
                : cents < 0 ? Correction.TuneUp : Correction.TuneDown;

        var feedback = new TuningFeedback
        {
            HasReading = true,
            Target = target,
            Note = note,
            MeasuredHz = hz,
            TargetHz = targetHz,
            Cents = cents,
            Confidence = reading.Confidence,
            Correction = correction,
            IsLocked = _locked,
            IsOffScale = absolute > OffScaleCents,
        };

        _lastValid = feedback;
        _lastValidTimestamp = reading.Timestamp;
        return feedback;
    }

    private TuningFeedback HoldOrExpire(long timestamp)
    {
        _locked = false;
        _inToleranceSince = null;

        if (_lastValid is null || _lastValidTimestamp is not { } lastTs)
        {
            return TuningFeedback.None;
        }

        if (_time.GetElapsedTime(lastTs, timestamp) > StaleAfter)
        {
            _lastValid = null;
            _lastValidTimestamp = null;
            _autoIndex = null;
            _lockKey = -1;
            return TuningFeedback.None;
        }

        return _lastValid with
        {
            IsStale = true,
            IsLocked = false,
            Correction = _lastValid.Correction == Correction.InTune ? Correction.Hold : _lastValid.Correction,
        };
    }

    private int ChooseTargetIndex(double hz)
    {
        if (_manualIndex is { } manual)
        {
            return manual;
        }

        var best = 0;
        var bestAbs = double.MaxValue;
        for (var i = 0; i < _targets.Count; i++)
        {
            var distance = Math.Abs(NoteMath.Cents(hz, _targets[i].Hz));
            if (distance < bestAbs)
            {
                bestAbs = distance;
                best = i;
            }
        }

        if (_autoIndex is not { } current)
        {
            _autoIndex = best;
            return best;
        }

        if (best != current)
        {
            var currentAbs = Math.Abs(NoteMath.Cents(hz, _targets[current].Hz));
            if (currentAbs - bestAbs >= StringSwitchMarginCents)
            {
                _autoIndex = best;
            }
        }

        return _autoIndex ?? best;
    }

    private void ResetInternal()
    {
        _autoIndex = null;
        _lockKey = -1;
        _inToleranceSince = null;
        _lastValidTimestamp = null;
        _locked = false;
        _lastValid = null;
        _current = TuningFeedback.None;
    }

    private static PitchSearchRange Clamp(double min, double max) =>
        new(Math.Max(NoteMath.MinSupportedHz, min), Math.Min(NoteMath.MaxSupportedHz, max));
}

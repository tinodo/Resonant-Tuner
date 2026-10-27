namespace Resonant.Core.Dsp;

/// <summary>Sine reference tone with a short attack and release envelope. Not thread-safe; call from one render thread.</summary>
public sealed class ReferenceToneSynth
{
    private enum Stage
    {
        Attack,
        Sustain,
        Release,
        Done,
    }

    public const double DefaultVolume = 0.12;

    private readonly double _phaseStep;
    private readonly double _volume;
    private readonly double _attackStep;
    private readonly double _releaseStep;
    private double _phase;
    private double _envelope;
    private Stage _stage = Stage.Attack;

    public ReferenceToneSynth(int sampleRate, double frequencyHz, double volume = DefaultVolume, double attackMs = 15, double releaseMs = 40)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sampleRate, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(frequencyHz, 0);
        _phaseStep = frequencyHz / sampleRate;
        _volume = Math.Clamp(volume, 0.0, 1.0);
        _attackStep = 1.0 / Math.Max(1.0, attackMs / 1000.0 * sampleRate);
        _releaseStep = 1.0 / Math.Max(1.0, releaseMs / 1000.0 * sampleRate);
    }

    public bool IsFinished => _stage == Stage.Done;

    public void BeginRelease()
    {
        if (_stage != Stage.Done)
        {
            _stage = Stage.Release;
        }
    }

    /// <summary>Fills the span with mono samples; silence once the release has finished.</summary>
    public void Render(Span<float> destination)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            switch (_stage)
            {
                case Stage.Attack:
                    _envelope += _attackStep;
                    if (_envelope >= 1.0)
                    {
                        _envelope = 1.0;
                        _stage = Stage.Sustain;
                    }

                    break;
                case Stage.Release:
                    _envelope -= _releaseStep;
                    if (_envelope <= 0.0)
                    {
                        _envelope = 0.0;
                        _stage = Stage.Done;
                    }

                    break;
                default:
                    break;
            }

            if (_stage == Stage.Done)
            {
                destination[i] = 0f;
                continue;
            }

            destination[i] = (float)(Math.Sin(2.0 * Math.PI * _phase) * _volume * _envelope);
            _phase += _phaseStep;
            if (_phase >= 1.0)
            {
                _phase -= 1.0;
            }
        }
    }
}

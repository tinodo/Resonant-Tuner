using Resonant.Core.Audio;

namespace Resonant.Core.Dsp;

public sealed class PitchTracker
{
    public const double MinimumConfidence = 0.93;
    public const int AcquisitionFrames = 3;
    public const int ChangeFrames = 5;

    private const int MedianWindow = 5;
    private const double JumpCents = 80;
    private const double CandidateSpreadCents = 30;
    private const double Smoothing = 0.45;

    private readonly double[] _history = new double[MedianWindow];
    private int _position;
    private bool _tracking;
    private double _trackedCents;
    private double _candidateCents;
    private int _candidateFrames;

    public void Reset()
    {
        _tracking = false;
        _candidateFrames = 0;
        _position = 0;
        _trackedCents = 0;
        Array.Clear(_history);
    }

    public PitchEstimate Track(PitchEstimate estimate)
    {
        if (!estimate.IsValid || !double.IsFinite(estimate.Hz) || estimate.Hz <= 0
            || !double.IsFinite(estimate.Confidence) || estimate.Confidence < MinimumConfidence
            || estimate.IsClipping)
        {
            _candidateFrames = 0;
            return estimate with
            {
                Hz = 0,
                Rejection = estimate.IsValid ? PitchRejection.Uncertain : estimate.Rejection,
            };
        }

        var cents = 1200 * Math.Log2(estimate.Hz);
        if (!_tracking || Math.Abs(cents - _trackedCents) > JumpCents)
        {
            if (_candidateFrames == 0 || Math.Abs(cents - _candidateCents) > CandidateSpreadCents)
            {
                _candidateCents = cents;
                _candidateFrames = 1;
            }
            else
            {
                _candidateFrames++;
                _candidateCents += (cents - _candidateCents) / _candidateFrames;
            }

            if (_candidateFrames < (_tracking ? ChangeFrames : AcquisitionFrames))
            {
                return estimate with { Hz = 0, Rejection = PitchRejection.Uncertain };
            }

            _tracking = true;
            _trackedCents = _candidateCents;
            Array.Fill(_history, _trackedCents);
            _position = 0;
        }
        else
        {
            _history[_position] = cents;
            _position = (_position + 1) % MedianWindow;
            Span<double> ordered = stackalloc double[MedianWindow];
            _history.CopyTo(ordered);
            ordered.Sort();
            _trackedCents += (ordered[MedianWindow / 2] - _trackedCents) * Smoothing;
        }

        _candidateFrames = 0;
        return estimate with { Hz = Math.Pow(2, _trackedCents / 1200) };
    }
}
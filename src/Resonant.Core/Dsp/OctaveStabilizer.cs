namespace Resonant.Core.Dsp;

/// <summary>
/// Keeps octave jumps from flipping the displayed pitch: a reading roughly one octave away from the stable pitch
/// is accepted only after it has persisted for several consecutive frames. All other readings pass through.
/// </summary>
public sealed class OctaveStabilizer
{
    private const double OctaveTolerance = 0.06;
    private const double PendingTolerance = 0.03;

    private readonly int _confirmFrames;
    private double _stableHz;
    private double _pendingHz;
    private int _pendingCount;

    public OctaveStabilizer(int confirmFrames = 4)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(confirmFrames, 1);
        _confirmFrames = confirmFrames;
    }

    public void Reset()
    {
        _stableHz = 0;
        _pendingHz = 0;
        _pendingCount = 0;
    }

    /// <summary>Returns true if the reading may be shown.</summary>
    public bool Accept(double hz)
    {
        if (!double.IsFinite(hz) || hz <= 0)
        {
            return false;
        }

        if (_stableHz <= 0)
        {
            _stableHz = hz;
            return true;
        }

        var octaves = Math.Abs(Math.Log2(hz / _stableHz));
        var isOctaveJump = Math.Abs(octaves - 1.0) < OctaveTolerance;
        if (!isOctaveJump)
        {
            _stableHz = hz;
            _pendingCount = 0;
            return true;
        }

        if (_pendingCount > 0 && Math.Abs(Math.Log2(hz / _pendingHz)) < PendingTolerance)
        {
            _pendingCount++;
        }
        else
        {
            _pendingHz = hz;
            _pendingCount = 1;
        }

        if (_pendingCount < _confirmFrames)
        {
            return false;
        }

        _stableHz = hz;
        _pendingCount = 0;
        return true;
    }
}

using Resonant.Core.Audio;

namespace Resonant.Core.Dsp;

/// <summary>
/// Original YIN-style periodicity estimator: DC removal, difference function, cumulative-mean normalisation,
/// absolute-threshold dip search and parabolic interpolation of the lag. Ambiguous input is rejected, never guessed.
/// Not thread-safe; buffers are reused so the steady-state path does not allocate.
/// </summary>
public sealed class YinPitchEstimator : IPitchEstimator
{
    public const double DefaultThreshold = 0.15;

    private const double SilenceRms = 0.0015;
    private const double AbsoluteSilenceRms = 0.0005;
    private const double QuietMinimumConfidence = 0.98;
    private const float ClipLevel = 0.99f;
    private const int MinClippedSamples = 3;

    private readonly double _threshold;
    private float[] _work = [];
    private double[] _diff = [];
    private double[] _cmnd = [];

    public YinPitchEstimator(double threshold = DefaultThreshold)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(threshold, 0.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(threshold, 1.0);
        _threshold = threshold;
    }

    public PitchEstimate Estimate(ReadOnlySpan<float> samples, int sampleRate, PitchSearchRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        var n = samples.Length;
        if (sampleRate <= 0 || n < 64 || !(range.MinHz > 0) || !(range.MaxHz > range.MinHz))
        {
            return Reject(PitchRejection.InsufficientData);
        }

        double sum = 0;
        var clipped = 0;
        for (var i = 0; i < n; i++)
        {
            var s = samples[i];
            if (!float.IsFinite(s))
            {
                return Reject(PitchRejection.InvalidSamples);
            }

            sum += s;
            if (Math.Abs(s) >= ClipLevel)
            {
                clipped++;
            }
        }

        EnsureCapacity(n);
        var mean = sum / n;
        double energy = 0;
        for (var i = 0; i < n; i++)
        {
            var v = samples[i] - mean;
            _work[i] = (float)v;
            energy += v * v;
        }

        var rms = Math.Sqrt(energy / n);
        var clipping = clipped >= MinClippedSamples;
        if (rms < AbsoluteSilenceRms)
        {
            return Reject(PitchRejection.Silence, rms, clipping);
        }

        var tauMin = Math.Max(2, (int)Math.Floor(sampleRate / range.MaxHz));
        var tauMax = (int)Math.Ceiling(sampleRate / range.MinHz);
        var maxLag = tauMax + 1;
        var window = n - maxLag - 1;
        if (tauMin >= tauMax || window < Math.Max(32, tauMax / 2))
        {
            return Reject(PitchRejection.InsufficientData, rms, clipping);
        }

        var work = _work;
        var diff = _diff;
        for (var tau = 1; tau <= maxLag; tau++)
        {
            double acc = 0;
            for (var j = 0; j < window; j++)
            {
                double d = (double)work[j] - work[j + tau];
                acc += d * d;
            }

            diff[tau] = acc;
        }

        var cmnd = _cmnd;
        cmnd[0] = 1.0;
        double running = 0;
        for (var tau = 1; tau <= maxLag; tau++)
        {
            running += diff[tau];
            cmnd[tau] = running > 0 ? diff[tau] * tau / running : 1.0;
        }

        var best = -1;
        for (var tau = tauMin; tau <= tauMax; tau++)
        {
            if (cmnd[tau] < _threshold)
            {
                var minimum = tau;
                while (tau + 1 <= tauMax && cmnd[tau + 1] < _threshold)
                {
                    tau++;
                    if (cmnd[tau] < cmnd[minimum])
                    {
                        minimum = tau;
                    }
                }

                best = minimum;
                break;
            }
        }

        if (best < 0)
        {
            var argMin = tauMin;
            for (var tau = tauMin + 1; tau <= tauMax; tau++)
            {
                if (cmnd[tau] < cmnd[argMin])
                {
                    argMin = tau;
                }
            }

            return Reject(PitchRejection.Uncertain, rms, clipping, Math.Clamp(1.0 - cmnd[argMin], 0.0, 1.0));
        }

        var lag = (double)best;
        if (best > 1 && best < maxLag)
        {
            var radius = Math.Min(3, Math.Min(best - 1, maxLag - best));
            double valueSum = 0;
            double offsetValueSum = 0;
            double squaredOffsetValueSum = 0;
            double squaredOffsetSum = 0;
            double fourthOffsetSum = 0;
            for (var offset = -radius; offset <= radius; offset++)
            {
                var squaredOffset = offset * offset;
                var value = diff[best + offset];
                valueSum += value;
                offsetValueSum += offset * value;
                squaredOffsetValueSum += squaredOffset * value;
                squaredOffsetSum += squaredOffset;
                fourthOffsetSum += squaredOffset * squaredOffset;
            }

            var count = 2 * radius + 1;
            var curvature = (squaredOffsetValueSum - squaredOffsetSum * valueSum / count)
                / (fourthOffsetSum - squaredOffsetSum * squaredOffsetSum / count);
            if (curvature > 1e-12)
            {
                var slope = offsetValueSum / squaredOffsetSum;
                var shift = -slope / (2 * curvature);
                if (Math.Abs(shift) <= radius)
                {
                    lag += shift;
                }
            }
        }

        var hz = sampleRate / lag;
        var confidence = Math.Clamp(1.0 - cmnd[best], 0.0, 1.0);
        if (rms < SilenceRms && confidence < QuietMinimumConfidence)
        {
            return Reject(PitchRejection.Uncertain, rms, clipping, confidence);
        }

        if (!double.IsFinite(hz) || hz < range.MinHz * 0.98 || hz > range.MaxHz * 1.02)
        {
            return Reject(PitchRejection.OutOfRange, rms, clipping, confidence);
        }

        return new PitchEstimate(hz, confidence, rms, clipping, PitchRejection.None);
    }

    private static PitchEstimate Reject(PitchRejection reason, double rms = 0, bool clipping = false, double confidence = 0) =>
        new(0, confidence, rms, clipping, reason);

    private void EnsureCapacity(int n)
    {
        if (_work.Length >= n)
        {
            return;
        }

        _work = new float[n];
        _diff = new double[n + 2];
        _cmnd = new double[n + 2];
    }
}

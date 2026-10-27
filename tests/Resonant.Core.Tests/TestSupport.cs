namespace Resonant.Core.Tests;

/// <summary>Deterministic clock: one tick equals one millisecond.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => 1000;

    public override long GetTimestamp() => _ticks;

    public void AdvanceMs(long milliseconds) => _ticks += milliseconds;

    public void SetMs(long milliseconds) => _ticks = milliseconds;
}

/// <summary>Reproducible synthetic fixtures. Versioned by seed and parameters, never recorded audio.</summary>
internal static class SignalFactory
{
    public static float[] Sine(double hz, int sampleRate, int length, double phaseCycles = 0, double amplitude = 0.5)
    {
        var x = new float[length];
        for (var i = 0; i < length; i++)
        {
            x[i] = (float)(amplitude * Math.Sin(2.0 * Math.PI * ((hz * i / sampleRate) + phaseCycles)));
        }

        return x;
    }

    /// <summary>Harmonic series with optional exponential decay, as a plucked-string stand-in.</summary>
    public static float[] Harmonic(double hz, int sampleRate, int length, double[] harmonicAmplitudes, double decayPerSecond, double phaseCycles = 0)
    {
        var x = new float[length];
        for (var i = 0; i < length; i++)
        {
            var t = (double)i / sampleRate;
            double v = 0;
            for (var h = 0; h < harmonicAmplitudes.Length; h++)
            {
                var f = hz * (h + 1);
                if (f >= sampleRate / 2.0)
                {
                    break;
                }

                v += harmonicAmplitudes[h] * Math.Sin(2.0 * Math.PI * ((f * t) + (phaseCycles * (h + 1))));
            }

            x[i] = (float)(v * Math.Exp(-decayPerSecond * t));
        }

        return x;
    }

    public static void AddNoise(float[] x, double snrDb, int seed)
    {
        double energy = 0;
        foreach (var s in x)
        {
            energy += s * (double)s;
        }

        var signalRms = Math.Sqrt(energy / x.Length);
        var noiseRms = signalRms / Math.Pow(10.0, snrDb / 20.0);
        var random = new Random(seed);
        for (var i = 0; i < x.Length; i++)
        {
            var u1 = 1.0 - random.NextDouble();
            var u2 = random.NextDouble();
            var gaussian = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            x[i] += (float)(gaussian * noiseRms);
        }
    }

    public static float[] Sum(float[] a, float[] b)
    {
        var x = new float[a.Length];
        for (var i = 0; i < x.Length; i++)
        {
            x[i] = a[i] + b[i];
        }

        return x;
    }
}

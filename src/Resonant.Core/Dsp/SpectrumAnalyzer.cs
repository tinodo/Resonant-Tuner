using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonant.Core.Dsp;

public sealed class SpectrumAnalyzer
{
    public const int WindowSize = 4096;
    public const int BandCount = 64;
    public const double MinimumHz = 40;
    public const double MaximumHz = 12000;
    public const double FloorDb = -72;

    private readonly Complex[] _transform = new Complex[WindowSize];
    private readonly double[] _window = new double[WindowSize];
    private readonly double _amplitudeScale;

    public SpectrumAnalyzer()
    {
        double windowSum = 0;
        for (var index = 0; index < WindowSize; index++)
        {
            _window[index] = 0.5 * (1 - Math.Cos(2 * Math.PI * index / (WindowSize - 1)));
            windowSum += _window[index];
        }

        _amplitudeScale = 2 / windowSum;
    }

    public float[] Analyze(ReadOnlySpan<float> samples, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        if (samples.Length != WindowSize)
        {
            throw new ArgumentException($"Expected {WindowSize} samples.", nameof(samples));
        }

        var bands = new float[BandCount];
        double mean = 0;
        foreach (var sample in samples)
        {
            if (!float.IsFinite(sample))
            {
                return bands;
            }

            mean += sample;
        }

        mean /= WindowSize;
        for (var index = 0; index < WindowSize; index++)
        {
            _transform[index] = new Complex((samples[index] - mean) * _window[index], 0);
        }

        Fourier.Forward(_transform, FourierOptions.Matlab);
        var maximumHz = Math.Min(MaximumHz, sampleRate / 2.0);
        if (maximumHz <= MinimumHz)
        {
            return bands;
        }

        var frequencyRatio = maximumHz / MinimumHz;
        var binHz = (double)sampleRate / WindowSize;
        for (var band = 0; band < BandCount; band++)
        {
            var lowerHz = MinimumHz * Math.Pow(frequencyRatio, (double)band / BandCount);
            var upperHz = MinimumHz * Math.Pow(frequencyRatio, (double)(band + 1) / BandCount);
            var firstBin = Math.Clamp((int)Math.Ceiling(lowerHz / binHz), 1, WindowSize / 2 - 1);
            var endBin = Math.Clamp((int)Math.Ceiling(upperHz / binHz), firstBin + 1, WindowSize / 2);
            double peak = 0;
            for (var bin = firstBin; bin < endBin; bin++)
            {
                peak = Math.Max(peak, _transform[bin].Magnitude * _amplitudeScale);
            }

            var decibels = 20 * Math.Log10(Math.Max(peak, 1e-12));
            bands[band] = (float)Math.Clamp((decibels - FloorDb) / -FloorDb, 0, 1);
        }

        return bands;
    }
}
namespace Resonant.Core.Dsp;

/// <summary>
/// What an ideal plucked string at a given pitch looks like on the live spectrum: the real analyzer is run over a
/// fundamental plus decaying harmonics, so the shape has the same bands and smoothing as the measured spectrum.
/// </summary>
public static class ReferenceSpectrum
{
    public const int HarmonicCount = 6;

    /// <summary>Amplitude of a harmonic relative to the fundamental; shared by the curve and the played tone so they match.</summary>
    public static double HarmonicWeight(int harmonic) => 1.0 / (harmonic * harmonic);

    /// <summary>Bands normalised so the tallest is 1; all zeros when the pitch is outside the analysed range.</summary>
    public static float[] ForFundamental(double fundamentalHz, int sampleRate = 48000)
    {
        var nyquist = sampleRate / 2.0;
        if (!double.IsFinite(fundamentalHz)
            || fundamentalHz < SpectrumAnalyzer.MinimumHz
            || fundamentalHz >= Math.Min(SpectrumAnalyzer.MaximumHz, nyquist))
        {
            return new float[SpectrumAnalyzer.BandCount];
        }

        var samples = new float[SpectrumAnalyzer.WindowSize];
        for (var harmonic = 1; harmonic <= HarmonicCount; harmonic++)
        {
            var hz = fundamentalHz * harmonic;
            if (hz >= nyquist)
            {
                break;
            }

            var amplitude = 0.5 * HarmonicWeight(harmonic);
            var step = 2 * Math.PI * hz / sampleRate;
            for (var index = 0; index < samples.Length; index++)
            {
                samples[index] += (float)(amplitude * Math.Sin(step * index));
            }
        }

        var bands = new SpectrumAnalyzer().Analyze(samples, sampleRate);
        var maximum = bands.Max();
        if (maximum > 0)
        {
            for (var band = 0; band < bands.Length; band++)
            {
                bands[band] /= maximum;
            }
        }

        return bands;
    }
}

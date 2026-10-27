using Resonant.Core.Audio;

namespace Resonant.Core.Dsp;

/// <summary>Estimates the fundamental of one mono window. Implementations are single-threaded and reuse buffers.</summary>
public interface IPitchEstimator
{
    PitchEstimate Estimate(ReadOnlySpan<float> samples, int sampleRate, PitchSearchRange range);
}

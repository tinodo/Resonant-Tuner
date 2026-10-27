using Resonant.Core.Audio;
using Resonant.Core.Dsp;
using Resonant.Core.Tuning;

namespace Resonant.Core.Tests;

[TestClass]
public sealed class PitchTrackingTests
{
    private static PitchEstimate Confident(double hz) => new(hz, 0.99, 0.1, false, PitchRejection.None);

    private static PitchTracker Acquired(double hz = 110)
    {
        var tracker = new PitchTracker();
        for (var frame = 0; frame < PitchTracker.AcquisitionFrames; frame++)
        {
            tracker.Track(Confident(hz));
        }

        return tracker;
    }

    [TestMethod]
    [DataRow(82.4069)]
    [DataRow(110.0)]
    [DataRow(146.8324)]
    [DataRow(196.0)]
    [DataRow(246.9417)]
    [DataRow(329.6276)]
    public void InitialPitchNeedsThreeConsistentConfidentFrames(double hz)
    {
        var tracker = new PitchTracker();
        for (var frame = 1; frame < PitchTracker.AcquisitionFrames; frame++)
        {
            Assert.IsFalse(tracker.Track(Confident(hz)).IsValid);
        }

        var acquired = tracker.Track(Confident(hz));
        Assert.IsTrue(acquired.IsValid);
        Assert.AreEqual(hz, acquired.Hz, 1e-8);
    }

    [TestMethod]
    public void WeakClippedOrNonFiniteInputCannotAcquirePitch()
    {
        var tracker = new PitchTracker();
        var unusable = new[]
        {
            Confident(110) with { Confidence = 0.89 },
            Confident(110) with { Confidence = double.NaN },
            Confident(110) with { Hz = double.NaN },
            Confident(110) with { Hz = double.PositiveInfinity },
            Confident(110) with { IsClipping = true },
        };
        foreach (var estimate in unusable)
        {
            for (var frame = 0; frame < 10; frame++)
            {
                var result = tracker.Track(estimate);
                Assert.IsFalse(result.IsValid);
                Assert.AreEqual(0.0, result.Hz);
            }
        }
    }

    [TestMethod]
    public void AlternatingNotesNeverInventAStablePitch()
    {
        var tracker = new PitchTracker();
        for (var frame = 0; frame < 40; frame++)
        {
            Assert.IsFalse(tracker.Track(Confident(frame % 2 == 0 ? 110 : 196)).IsValid);
        }
    }

    [TestMethod]
    public void MedianRejectsAnIsolatedInRangeSpike()
    {
        var tracker = Acquired();
        var spike = tracker.Track(Confident(110 * Math.Pow(2, 60.0 / 1200)));
        Assert.IsTrue(spike.IsValid);
        Assert.AreEqual(110.0, spike.Hz, 1e-8);
        Assert.AreEqual(110.0, tracker.Track(Confident(110)).Hz, 1e-8);
    }

    [TestMethod]
    public void SmallJitterIsReducedWithoutBiasingThePitch()
    {
        var tracker = Acquired();
        double[] jitter = [-6, 5, -4, 3, 0, 4, -5, 2, 0, -1];
        double rawEnergy = 0;
        double trackedEnergy = 0;
        for (var frame = 0; frame < 100; frame++)
        {
            var cents = jitter[frame % jitter.Length];
            var result = tracker.Track(Confident(110 * Math.Pow(2, cents / 1200)));
            Assert.IsTrue(result.IsValid);
            var trackedCents = NoteMath.Cents(result.Hz, 110);
            rawEnergy += cents * cents;
            trackedEnergy += trackedCents * trackedCents;
        }

        Assert.IsTrue(trackedEnergy < rawEnergy * 0.25,
            $"Tracked RMS {Math.Sqrt(trackedEnergy / 100):F2} cents vs input {Math.Sqrt(rawEnergy / 100):F2} cents.");
    }

    [TestMethod]
    [DataRow(-12.0)]
    [DataRow(12.0)]
    public void SmallTuningChangesSettleWithinEightFrames(double cents)
    {
        var tracker = Acquired();
        var target = 110 * Math.Pow(2, cents / 1200);
        var previousError = Math.Abs(cents);
        for (var frame = 0; frame < 8; frame++)
        {
            var result = tracker.Track(Confident(target));
            Assert.IsTrue(result.IsValid);
            var error = Math.Abs(NoteMath.Cents(result.Hz, target));
            Assert.IsTrue(error <= previousError + 1e-8);
            previousError = error;
        }

        Assert.IsTrue(previousError < 0.5, $"Still {previousError:F2} cents away after 264 ms.");
    }

    [TestMethod]
    [DataRow(82.4069)]
    [DataRow(146.8324)]
    [DataRow(220.0)]
    public void SustainedStringOrOctaveChangeIsConfirmedWithinFiveFrames(double hz)
    {
        var tracker = Acquired();
        for (var frame = 1; frame < PitchTracker.ChangeFrames; frame++)
        {
            Assert.IsFalse(tracker.Track(Confident(hz)).IsValid);
        }

        var changed = tracker.Track(Confident(hz));
        Assert.IsTrue(changed.IsValid);
        Assert.AreEqual(hz, changed.Hz, 1e-8);
    }

    [TestMethod]
    public void RepeatedNonConsecutiveNoiseCannotConfirmAStringChange()
    {
        var tracker = Acquired();
        for (var frame = 0; frame < 20; frame++)
        {
            Assert.IsFalse(tracker.Track(Confident(196)).IsValid);
            var resumed = tracker.Track(Confident(110));
            Assert.IsTrue(resumed.IsValid);
            Assert.AreEqual(110.0, resumed.Hz, 1e-8);
        }
    }

    [TestMethod]
    public void ResetRequiresFreshAcquisitionInsteadOfReusingTheOldNote()
    {
        var tracker = Acquired();
        tracker.Reset();
        Assert.IsFalse(tracker.Track(Confident(196)).IsValid);
        Assert.IsFalse(tracker.Track(Confident(196)).IsValid);
        var acquired = tracker.Track(Confident(196));
        Assert.IsTrue(acquired.IsValid);
        Assert.AreEqual(196.0, acquired.Hz, 1e-8);
    }

    [TestMethod]
    [DataRow(82.4069)]
    [DataRow(110.0)]
    [DataRow(146.8324)]
    [DataRow(196.0)]
    [DataRow(246.9417)]
    [DataRow(329.6276)]
    public void NoisyDecayingGuitarPlucksRemainAccurate(double hz)
    {
        const int sampleRate = 48000;
        var signal = SignalFactory.Harmonic(hz, sampleRate, 86400, [0.32, 0.16, 0.08, 0.04], 1.4);
        SignalFactory.AddNoise(signal, 20, (int)Math.Round(hz * 100));
        var range = new PitchSearchRange(82.4069 * 0.7, 329.6276 * 1.6);
        var length = PitchEngine.WindowLength(sampleRate, range.MinHz);
        var estimator = new YinPitchEstimator();
        var tracker = new PitchTracker();
        var errors = new List<double>();
        var worstFrame = string.Empty;
        double worstError = 0;
        for (var start = 0; start + length <= signal.Length; start += 1584)
        {
            var estimate = estimator.Estimate(signal.AsSpan(start, length), sampleRate, range);
            var tracked = tracker.Track(estimate);
            if (tracked.IsValid)
            {
                var error = Math.Abs(NoteMath.Cents(tracked.Hz, hz));
                errors.Add(error);
                if (error > worstError)
                {
                    worstError = error;
                    worstFrame = $"t={(double)start / sampleRate:F2}s, confidence={estimate.Confidence:F3}, RMS={estimate.Rms:F4}";
                }
            }
        }

        Assert.IsTrue(errors.Count >= 20, $"Only {errors.Count} usable frames at {hz} Hz.");
        Assert.IsTrue(errors.Max() < 3, $"Largest error at {hz} Hz: {errors.Max():F2} cents; {worstFrame}.");
        Assert.IsTrue(errors.Average() < 1, $"Mean error at {hz} Hz: {errors.Average():F2} cents.");
    }

    [TestMethod]
    public async Task TransientNonOctaveNoiseDoesNotChangeTheTrackedString()
    {
        var frequencies = new[] { 110.0, 110, 110, 110, 110, 196, 110, 110, 110, 110 };
        var ring = new SampleRingBuffer(16384);
        ring.Write(SignalFactory.Sine(110, 48000, 8192));
        var engine = new PitchEngine(ring, new SequenceEstimator(frequencies));
        var session = new TuningSession();
        session.Configure(TuningPresets.GuitarStandard, TuningSettings.Default);
        var feedback = new List<TuningFeedback>();
        var produced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.ReadingProduced += reading =>
        {
            feedback.Add(session.Update(reading));
            if (feedback.Count >= frequencies.Length)
            {
                produced.TrySetResult();
            }
        };
        using var cancellation = new CancellationTokenSource();
        var worker = engine.RunAsync(48000, cancellation.Token);
        try
        {
            await produced.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await cancellation.CancelAsync();
            await worker.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var tracked = feedback.Where(frame => frame.HasReading).ToArray();
        Assert.IsTrue(tracked.Length >= 5);
        Assert.IsTrue(tracked.All(frame => frame.Target?.StringNumber == 5),
            "One stray reading stole the A string target.");
        Assert.IsTrue(tracked.All(frame => Math.Abs(frame.Cents) < 1));
    }

    [TestMethod]
    [DataRow(44100, 0.0014)]
    [DataRow(48000, 0.0014)]
    [DataRow(44100, 0.0009)]
    [DataRow(48000, 0.0009)]
    public void QuietCleanHighECanAcquireWithoutAHardPluck(int sampleRate, double amplitude)
    {
        const double highE = 329.6276;
        var signal = SignalFactory.Harmonic(highE, sampleRate, sampleRate, [amplitude, amplitude * 0.2, amplitude * 0.1], 0);
        SignalFactory.AddNoise(signal, 24, 3296);
        var range = new PitchSearchRange(82.4069 * 0.7, highE * 1.6);
        var length = PitchEngine.WindowLength(sampleRate, range.MinHz);
        var estimator = new YinPitchEstimator();
        var tracker = new PitchTracker();
        var accepted = new List<PitchEstimate>();
        for (var start = 0; start + length <= signal.Length; start += (int)(sampleRate * 0.033))
        {
            var tracked = tracker.Track(estimator.Estimate(signal.AsSpan(start, length), sampleRate, range));
            if (tracked.IsValid)
            {
                accepted.Add(tracked);
            }
        }

        Assert.IsTrue(accepted.Count >= 20, $"Only {accepted.Count} quiet E4 frames accepted at amplitude {amplitude}.");
        Assert.IsTrue(accepted.All(frame => frame.Rms < 0.0015));
        Assert.IsTrue(accepted.All(frame => Math.Abs(NoteMath.Cents(frame.Hz, highE)) < 1));
    }

    [TestMethod]
    [DataRow(44100)]
    [DataRow(48000)]
    public void QuietNoisyHighEStillNeedsStrongPeriodicity(int sampleRate)
    {
        var range = new PitchSearchRange(82.4069 * 0.7, 329.6276 * 1.6);
        var length = PitchEngine.WindowLength(sampleRate, range.MinHz);
        var signal = SignalFactory.Sine(329.6276, sampleRate, length, amplitude: 0.0014);
        SignalFactory.AddNoise(signal, 12, 3296);
        var estimate = new YinPitchEstimator().Estimate(signal, sampleRate, range);

        Assert.IsTrue(estimate.Rms > 0.0005 && estimate.Rms < 0.0015);
        Assert.IsTrue(estimate.Confidence > 0.90 && estimate.Confidence < 0.98);
        Assert.IsFalse(estimate.IsValid);
        Assert.AreEqual(PitchRejection.Uncertain, estimate.Rejection);
    }

    [TestMethod]
    [DataRow(44100)]
    [DataRow(48000)]
    public void QuietNoiseDoesNotAcquireAPitch(int sampleRate)
    {
        var range = new PitchSearchRange(82.4069 * 0.7, 329.6276 * 1.6);
        var signal = new float[PitchEngine.WindowLength(sampleRate, range.MinHz)];
        var random = new Random(3296);
        var estimator = new YinPitchEstimator();
        var tracker = new PitchTracker();
        for (var frame = 0; frame < 10; frame++)
        {
            for (var index = 0; index < signal.Length; index++)
            {
                signal[index] = (float)((random.NextDouble() * 2 - 1) * 0.001);
            }

            var estimate = estimator.Estimate(signal, sampleRate, range);
            Assert.IsTrue(estimate.Rms > 0.0005 && estimate.Rms < 0.0015);
            Assert.IsFalse(estimate.IsValid);
            Assert.IsFalse(tracker.Track(estimate).IsValid);
        }
    }

    [TestMethod]
    [DataRow(44100)]
    [DataRow(48000)]
    public void NearSilenceStillProducesNoPitch(int sampleRate)
    {
        var signal = SignalFactory.Sine(329.6276, sampleRate, 4096, amplitude: 0.0003);
        var estimate = new YinPitchEstimator().Estimate(signal, sampleRate, PitchSearchRange.Full);

        Assert.IsFalse(estimate.IsValid);
        Assert.AreEqual(PitchRejection.Silence, estimate.Rejection);
    }

    private sealed class SequenceEstimator(double[] frequencies) : IPitchEstimator
    {
        private int _index;

        public PitchEstimate Estimate(ReadOnlySpan<float> samples, int sampleRate, PitchSearchRange range) =>
            new(frequencies[Math.Min(_index++, frequencies.Length - 1)], 0.99, 0.1, false, PitchRejection.None);
    }

    [TestMethod]
    public async Task DropoutsHoldTheStringExpireHonestlyAndAllowFreshAcquisition()
    {
        var frequencies = Enumerable.Repeat(110.0, 16)
            .Concat(Enumerable.Repeat(0.0, 3))
            .Concat(Enumerable.Repeat(110.0, 8))
            .Concat(Enumerable.Repeat(0.0, 28))
            .Concat(Enumerable.Repeat(196.0, 8))
            .ToArray();
        var clock = new ManualTimeProvider();
        var session = new TuningSession(clock);
        session.Configure(TuningPresets.GuitarStandard, TuningSettings.Default);
        var ring = new SampleRingBuffer(16384);
        ring.Write(SignalFactory.Sine(110, 48000, 8192));
        var engine = new PitchEngine(ring, new SequenceEstimator(frequencies), clock)
        {
            SearchRange = session.SearchRange,
        };
        var feedback = new List<TuningFeedback>();
        var produced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.ReadingProduced += reading =>
        {
            feedback.Add(session.Update(reading));
            clock.AdvanceMs(33);
            if (feedback.Count >= frequencies.Length)
            {
                produced.TrySetResult();
            }
        };
        using var cancellation = new CancellationTokenSource();
        var worker = engine.RunAsync(48000, cancellation.Token);
        try
        {
            await produced.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await cancellation.CancelAsync();
            await worker.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.IsTrue(feedback[15].IsLocked);
        Assert.IsTrue(feedback.Skip(16).Take(3).All(frame =>
            frame.HasReading && frame.IsStale && !frame.IsLocked && frame.Target?.StringNumber == 5));
        Assert.IsTrue(feedback[19].HasReading && !feedback[19].IsStale);
        Assert.AreEqual(5, feedback[19].Target!.StringNumber);
        Assert.IsFalse(feedback[54].HasReading);
        Assert.IsFalse(feedback[55].HasReading);
        Assert.IsFalse(feedback[56].HasReading);
        Assert.IsTrue(feedback[57].HasReading && !feedback[57].IsStale);
        Assert.AreEqual(3, feedback[57].Target!.StringNumber);
        Assert.AreEqual(196.0, feedback[57].MeasuredHz, 1e-8);
    }
}

[TestClass]
public sealed class SpectrumAnalyzerTests
{
    [TestMethod]
    [DataRow(70.0, 90.0)]
    [DataRow(1000.0, 1500.0)]
    public async Task PitchWorkerPublishesSpectrumOutsidePitchRangeAndClearsItOnStop(double minimumHz, double maximumHz)
    {
        var signal = SignalFactory.Sine(3000, 48000, 8192);
        var ring = new SampleRingBuffer(16384);
        ring.Write(signal);
        var engine = new PitchEngine(ring, new RejectingEstimator())
        {
            SearchRange = new PitchSearchRange(minimumHz, maximumHz),
        };
        var produced = new TaskCompletionSource<PitchReading>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.ReadingProduced += reading => produced.TrySetResult(reading);
        using var cancellation = new CancellationTokenSource();
        var worker = engine.RunAsync(48000, cancellation.Token);
        try
        {
            var reading = await produced.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(reading.IsValid);
            var expected = new SpectrumAnalyzer().Analyze(signal.AsSpan(signal.Length - SpectrumAnalyzer.WindowSize), 48000);
            Assert.IsTrue(expected.Max() > 0.8f);
            CollectionAssert.AreEqual(expected, engine.Spectrum.ToArray());
        }
        finally
        {
            await cancellation.CancelAsync();
            await worker.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.IsTrue(engine.Spectrum.IsEmpty);
    }

    private sealed class RejectingEstimator : IPitchEstimator
    {
        public PitchEstimate Estimate(ReadOnlySpan<float> samples, int sampleRate, PitchSearchRange range) =>
            new(0, 0, 0, false, PitchRejection.Uncertain);
    }

    [TestMethod]
    [DataRow(44100, 110.0)]
    [DataRow(48000, 440.0)]
    [DataRow(48000, 3000.0)]
    [DataRow(16000, 1000.0)]
    public void SinePeakAppearsAtItsActualFrequency(int sampleRate, double frequencyHz)
    {
        var signal = SignalFactory.Sine(frequencyHz, sampleRate, SpectrumAnalyzer.WindowSize);
        var bands = new SpectrumAnalyzer().Analyze(signal, sampleRate);
        var peakBand = Array.IndexOf(bands, bands.Max());
        var ratio = Math.Min(SpectrumAnalyzer.MaximumHz, sampleRate / 2.0) / SpectrumAnalyzer.MinimumHz;
        var lowerHz = SpectrumAnalyzer.MinimumHz * Math.Pow(ratio, (double)peakBand / bands.Length);
        var upperHz = SpectrumAnalyzer.MinimumHz * Math.Pow(ratio, (double)(peakBand + 1) / bands.Length);
        var resolution = (double)sampleRate / SpectrumAnalyzer.WindowSize;

        Assert.HasCount(SpectrumAnalyzer.BandCount, bands);
        Assert.IsTrue(frequencyHz >= lowerHz - resolution && frequencyHz <= upperHz + resolution,
            $"{frequencyHz} Hz peaked in {lowerHz:F1}..{upperHz:F1} Hz.");
        Assert.IsTrue(bands[peakBand] > 0.8f);
        Assert.IsTrue(bands.All(level => float.IsFinite(level) && level >= 0 && level <= 1));
    }

    [TestMethod]
    public void SilenceAndDirectCurrentProduceNoBars()
    {
        var analyzer = new SpectrumAnalyzer();
        var silence = analyzer.Analyze(new float[SpectrumAnalyzer.WindowSize], 48000);
        var directCurrent = analyzer.Analyze(Enumerable.Repeat(0.25f, SpectrumAnalyzer.WindowSize).ToArray(), 48000);

        CollectionAssert.AreEqual(new float[SpectrumAnalyzer.BandCount], silence);
        CollectionAssert.AreEqual(silence, directCurrent);
    }

    [TestMethod]
    public void TwentyDecibelDropIsNotNormalizedAway()
    {
        var analyzer = new SpectrumAnalyzer();
        var loud = analyzer.Analyze(SignalFactory.Sine(1500, 48000, SpectrumAnalyzer.WindowSize, amplitude: 0.5), 48000);
        var quiet = analyzer.Analyze(SignalFactory.Sine(1500, 48000, SpectrumAnalyzer.WindowSize, amplitude: 0.05), 48000);
        var peakBand = Array.IndexOf(loud, loud.Max());

        Assert.AreEqual(20.0, (loud[peakBand] - quiet[peakBand]) * -SpectrumAnalyzer.FloorDb, 0.01);
    }

    [TestMethod]
    public void InvalidFrameClearsBarsAndDoesNotPoisonTheNextFrame()
    {
        var analyzer = new SpectrumAnalyzer();
        var signal = SignalFactory.Sine(440, 48000, SpectrumAnalyzer.WindowSize);
        var expected = analyzer.Analyze(signal, 48000);
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var corrupted = (float[])signal.Clone();
            corrupted[100] = invalid;
            CollectionAssert.AreEqual(new float[SpectrumAnalyzer.BandCount], analyzer.Analyze(corrupted, 48000));
            CollectionAssert.AreEqual(expected, analyzer.Analyze(signal, 48000));
        }
    }
}

[TestClass]
public sealed class ReferenceSpectrumTests
{
    private static int BandOf(double frequencyHz)
    {
        var ratio = SpectrumAnalyzer.MaximumHz / SpectrumAnalyzer.MinimumHz;
        return (int)Math.Floor(Math.Log(frequencyHz / SpectrumAnalyzer.MinimumHz) / Math.Log(ratio) * SpectrumAnalyzer.BandCount);
    }

    [TestMethod]
    [DataRow(82.41)]
    [DataRow(146.83)]
    [DataRow(329.63)]
    public void ReferencePeaksAtTheTargetPitchAndIsNormalised(double frequencyHz)
    {
        var bands = ReferenceSpectrum.ForFundamental(frequencyHz);
        var peak = Array.IndexOf(bands, bands.Max());

        Assert.HasCount(SpectrumAnalyzer.BandCount, bands);
        Assert.AreEqual(1.0f, bands[peak]);
        Assert.IsLessThanOrEqualTo(1, Math.Abs(peak - BandOf(frequencyHz)));
        Assert.IsTrue(bands.All(level => float.IsFinite(level) && level >= 0 && level <= 1));
    }

    [TestMethod]
    public void ReferenceShowsASecondHarmonicBumpBelowTheFundamental()
    {
        var bands = ReferenceSpectrum.ForFundamental(110.0);
        var fundamental = bands.Skip(BandOf(110.0) - 1).Take(3).Max();
        var second = bands.Skip(BandOf(220.0) - 1).Take(3).Max();
        var between = bands[BandOf(165.0)];

        Assert.IsGreaterThan(0.7f, second);
        Assert.IsLessThan(fundamental, second);
        Assert.IsLessThan(second, between);
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(0.0)]
    [DataRow(20.0)]
    [DataRow(13000.0)]
    public void PitchesOutsideTheAnalysedRangeProduceNoReference(double frequencyHz)
    {
        CollectionAssert.AreEqual(new float[SpectrumAnalyzer.BandCount], ReferenceSpectrum.ForFundamental(frequencyHz));
    }
}

[TestClass]
public sealed class YinPitchEstimatorTests
{
    private static readonly double[] SineFrequencies = [NoteMath.FrequencyFromMidi(23, NoteMath.MinA4Hz), 30.8677, 41.2034, 55.0, 82.4069, 110.0, 196.0, 329.6276, 659.2551, 1000.0, 1400.0];

    private static int Window(int sampleRate) => PitchEngine.WindowLength(sampleRate, NoteMath.MinSupportedHz);

    [TestMethod]
    [DataRow(44100)]
    [DataRow(48000)]
    public void SteadySineIsMeasuredWithinHalfACentAcrossTheSupportedRange(int sampleRate)
    {
        var estimator = new YinPitchEstimator();
        var length = Window(sampleRate);

        foreach (var hz in SineFrequencies)
        {
            for (var p = 0; p < 8; p++)
            {
                var signal = SignalFactory.Sine(hz, sampleRate, length, phaseCycles: p * 0.127);
                var estimate = estimator.Estimate(signal, sampleRate, PitchSearchRange.Full);

                Assert.IsTrue(estimate.IsValid, $"{hz} Hz @ {sampleRate}: {estimate.Rejection}");
                var error = Math.Abs(NoteMath.Cents(estimate.Hz, hz));
                Assert.IsTrue(error <= 0.5, $"{hz} Hz @ {sampleRate}: {error:F3} cents");
            }
        }
    }

    [TestMethod]
    public void SilenceIsRejectedNotMeasured()
    {
        var estimate = new YinPitchEstimator().Estimate(new float[4800], 48000, PitchSearchRange.Full);
        Assert.IsFalse(estimate.IsValid);
        Assert.AreEqual(PitchRejection.Silence, estimate.Rejection);
    }

    [TestMethod]
    public void NonFiniteSamplesAreRejected()
    {
        var signal = SignalFactory.Sine(110, 48000, 4800);
        signal[100] = float.NaN;
        Assert.AreEqual(PitchRejection.InvalidSamples, new YinPitchEstimator().Estimate(signal, 48000, PitchSearchRange.Full).Rejection);

        signal[100] = float.PositiveInfinity;
        Assert.AreEqual(PitchRejection.InvalidSamples, new YinPitchEstimator().Estimate(signal, 48000, PitchSearchRange.Full).Rejection);
    }

    [TestMethod]
    public void DirectCurrentOffsetDoesNotChangeThePitch()
    {
        var signal = SignalFactory.Sine(196.0, 48000, 4800);
        for (var i = 0; i < signal.Length; i++)
        {
            signal[i] += 0.2f;
        }

        var estimate = new YinPitchEstimator().Estimate(signal, 48000, PitchSearchRange.Full);
        Assert.IsTrue(estimate.IsValid);
        Assert.AreEqual(0.0, NoteMath.Cents(estimate.Hz, 196.0), 0.5);
    }

    [TestMethod]
    public void WhiteNoiseIsNeverPaintedAsAPitch()
    {
        var random = new Random(7);
        var noise = new float[4800];
        for (var i = 0; i < noise.Length; i++)
        {
            noise[i] = (float)((random.NextDouble() * 2.0) - 1.0) * 0.3f;
        }

        Assert.IsFalse(new YinPitchEstimator().Estimate(noise, 48000, PitchSearchRange.Full).IsValid);
    }

    [TestMethod]
    public void TwoSimultaneousStringsAreNotReportedAsOneConfidentNote()
    {
        var a = SignalFactory.Sine(82.4069, 48000, 4800);
        var b = SignalFactory.Sine(110.0, 48000, 4800);
        var estimate = new YinPitchEstimator().Estimate(SignalFactory.Sum(a, b), 48000, PitchSearchRange.Full);
        Assert.IsFalse(estimate.IsValid, $"{estimate.Hz} Hz, confidence {estimate.Confidence}");
    }

    [TestMethod]
    public void ClippingIsFlagged()
    {
        var signal = SignalFactory.Sine(110.0, 48000, 4800, amplitude: 3.0);
        for (var i = 0; i < signal.Length; i++)
        {
            signal[i] = Math.Clamp(signal[i], -1f, 1f);
        }

        var estimate = new YinPitchEstimator().Estimate(signal, 48000, PitchSearchRange.Full);
        Assert.IsTrue(estimate.IsClipping);
    }

    [TestMethod]
    public void HarmonicRichDecayingNoteWithNoiseKeepsTheCorrectOctave()
    {
        const int sampleRate = 48000;
        var estimator = new YinPitchEstimator();
        var amplitudes = new[] { 0.4, 1.0, 0.8, 0.5, 0.3, 0.2 };
        var frequencies = new[] { 82.4069, 110.0, 146.8324, 195.9977, 246.9417, 329.6276 };
        var length = Window(sampleRate);

        var total = 0;
        var correctOctave = 0;
        var errors = new List<double>();
        var seed = 1;
        foreach (var hz in frequencies)
        {
            for (var p = 0; p < 20; p++)
            {
                var signal = SignalFactory.Harmonic(hz, sampleRate, length, amplitudes, decayPerSecond: 1.5, phaseCycles: p * 0.05);
                SignalFactory.AddNoise(signal, snrDb: 20, seed: seed++);
                var estimate = estimator.Estimate(signal, sampleRate, PitchSearchRange.Full);
                total++;
                if (!estimate.IsValid)
                {
                    continue;
                }

                var cents = NoteMath.Cents(estimate.Hz, hz);
                if (Math.Abs(cents) < 600)
                {
                    correctOctave++;
                    errors.Add(Math.Abs(cents));
                }
            }
        }

        errors.Sort();
        Assert.IsTrue(correctOctave >= 0.95 * total, $"{correctOctave}/{total} correct-octave readings");
        Assert.IsTrue(errors[errors.Count / 2] <= 1.0, $"median error {errors[errors.Count / 2]:F3} cents");
    }

    [TestMethod]
    public void ScopedManualSearchStillReportsActualCentsForANearbyWrongNote()
    {
        var range = new PitchSearchRange(55.0, 242.0);
        var signal = SignalFactory.Sine(103.83, 48000, PitchEngine.WindowLength(48000, range.MinHz));
        var estimate = new YinPitchEstimator().Estimate(signal, 48000, range);
        Assert.IsTrue(estimate.IsValid);
        Assert.AreEqual(-100.0, NoteMath.Cents(estimate.Hz, 110.0), 5.0);
    }
}

[TestClass]
public sealed class OctaveStabilizerTests
{
    [TestMethod]
    public void SingleOctaveJumpIsHeldBackUntilItPersists()
    {
        var stabilizer = new OctaveStabilizer(confirmFrames: 4);
        Assert.IsTrue(stabilizer.Accept(110.0));
        Assert.IsTrue(stabilizer.Accept(110.2));

        Assert.IsFalse(stabilizer.Accept(220.0));
        Assert.IsTrue(stabilizer.Accept(110.1));
        Assert.IsFalse(stabilizer.Accept(220.0));
        Assert.IsFalse(stabilizer.Accept(220.1));
        Assert.IsFalse(stabilizer.Accept(220.0));
        Assert.IsTrue(stabilizer.Accept(220.05));
    }

    [TestMethod]
    public void NonOctaveChangesPassThrough()
    {
        var stabilizer = new OctaveStabilizer();
        Assert.IsTrue(stabilizer.Accept(110.0));
        Assert.IsTrue(stabilizer.Accept(146.8));
        Assert.IsTrue(stabilizer.Accept(196.0));
    }

    [TestMethod]
    public void ResetAllowsAnImmediateOctaveChange()
    {
        var stabilizer = new OctaveStabilizer();
        Assert.IsTrue(stabilizer.Accept(110.0));
        stabilizer.Reset();
        Assert.IsTrue(stabilizer.Accept(220.0));
    }
}

[TestClass]
public sealed class SampleRingBufferTests
{
    [TestMethod]
    public void ReturnsTheNewestSamplesInOrderAcrossWrapAround()
    {
        var ring = new SampleRingBuffer(16);
        var source = Enumerable.Range(0, 40).Select(i => (float)i).ToArray();
        ring.Write(source.AsSpan(0, 13));
        ring.Write(source.AsSpan(13, 27));

        var window = new float[8];
        Assert.IsTrue(ring.TryCopyLatest(window));
        CollectionAssert.AreEqual(new float[] { 32, 33, 34, 35, 36, 37, 38, 39 }, window);
    }

    [TestMethod]
    public void NotEnoughDataReturnsFalse()
    {
        var ring = new SampleRingBuffer(16);
        ring.Write(new float[] { 1, 2, 3 });
        Assert.IsFalse(ring.TryCopyLatest(new float[4]));
    }

    [TestMethod]
    public void OversizedWriteKeepsOnlyTheNewestCapacitySamples()
    {
        var ring = new SampleRingBuffer(8);
        ring.Write(Enumerable.Range(0, 100).Select(i => (float)i).ToArray());
        var window = new float[4];
        Assert.IsTrue(ring.TryCopyLatest(window));
        CollectionAssert.AreEqual(new float[] { 96, 97, 98, 99 }, window);
        Assert.AreEqual(100, ring.TotalWritten);
    }

    [TestMethod]
    public void RequestLargerThanHalfTheCapacityIsRefused()
    {
        var ring = new SampleRingBuffer(8);
        ring.Write(new float[8]);
        Assert.IsFalse(ring.TryCopyLatest(new float[5]));
    }

    [TestMethod]
    public void ClearDiscardsHistory()
    {
        var ring = new SampleRingBuffer(16);
        ring.Write(new float[10]);
        ring.Clear();
        Assert.IsFalse(ring.TryCopyLatest(new float[4]));
        Assert.AreEqual(0, ring.TotalWritten);
    }
}

[TestClass]
public sealed class ReferenceToneSynthTests
{
    [TestMethod]
    [DataRow(44100, 82.4069)]
    [DataRow(48000, 82.4069)]
    [DataRow(44100, 440.0)]
    [DataRow(48000, 440.0)]
    public void RenderingInAudioQuantaMatchesOneContinuousRender(int sampleRate, double frequencyHz)
    {
        var expected = new float[sampleRate * 3];
        var actual = new float[expected.Length];
        var continuous = new ReferenceToneSynth(sampleRate, frequencyHz);
        var chunked = new ReferenceToneSynth(sampleRate, frequencyHz);
        continuous.Render(expected);

        int[] quantumSizes = [1, 479, 960, 257, 2048, 441, 480];
        var offset = 0;
        var quantumIndex = 0;
        while (offset < actual.Length)
        {
            var length = Math.Min(quantumSizes[quantumIndex % quantumSizes.Length], actual.Length - offset);
            chunked.Render(actual.AsSpan(offset, length));
            offset += length;
            quantumIndex++;
        }

        CollectionAssert.AreEqual(expected, actual, "Changing audio-frame boundaries must not restart the oscillator or its envelope.");
    }

    [TestMethod]
    public void RampsInAtLowVolumeAndStaysBounded()
    {
        var synth = new ReferenceToneSynth(48000, 110.0);
        var buffer = new float[4800];
        synth.Render(buffer);

        Assert.IsTrue(Math.Abs(buffer[0]) < 0.001f);
        Assert.IsTrue(buffer.Max(x => Math.Abs(x)) <= ReferenceToneSynth.DefaultVolume + 1e-6);
        Assert.IsTrue(buffer.Skip(2000).Max(x => Math.Abs(x)) > 0.05f);
    }

    [TestMethod]
    public void RendersTheRequestedFrequency()
    {
        const int sampleRate = 48000;
        var synth = new ReferenceToneSynth(sampleRate, 196.0);
        var warmup = new float[2400];
        synth.Render(warmup);
        var buffer = new float[4800];
        synth.Render(buffer);

        var estimate = new YinPitchEstimator().Estimate(buffer, sampleRate, PitchSearchRange.Full);
        Assert.IsTrue(estimate.IsValid);
        Assert.AreEqual(0.0, NoteMath.Cents(estimate.Hz, 196.0), 0.5);
    }

    [TestMethod]
    public void ReleaseFadesToSilenceAndFinishes()
    {
        var synth = new ReferenceToneSynth(48000, 110.0);
        synth.Render(new float[4800]);
        synth.BeginRelease();
        var tail = new float[4800];
        synth.Render(tail);

        Assert.IsTrue(synth.IsFinished);
        Assert.AreEqual(0f, tail[^1]);
    }
}

[TestClass]
public sealed class TunerStateResolverTests
{
    private static PitchReading Reading(bool valid, PitchRejection rejection, bool clipping = false) =>
        new(0, valid ? 110 : 0, 0.9, 0.1, clipping, valid, rejection);

    [TestMethod]
    public void MapsCaptureStatesDirectly()
    {
        Assert.AreEqual(TunerState.Stopped, TunerStateResolver.Resolve(new CaptureStatus(CaptureState.Stopped), null));
        Assert.AreEqual(TunerState.RequestingPermission, TunerStateResolver.Resolve(new CaptureStatus(CaptureState.RequestingPermission), null));
        Assert.AreEqual(TunerState.Starting, TunerStateResolver.Resolve(new CaptureStatus(CaptureState.Starting), null));
        Assert.AreEqual(TunerState.DeviceLost, TunerStateResolver.Resolve(new CaptureStatus(CaptureState.DeviceLost), Reading(true, PitchRejection.None)));
        Assert.AreEqual(TunerState.Error, TunerStateResolver.Resolve(new CaptureStatus(CaptureState.Error), null));
    }

    [TestMethod]
    public void RunningStateReflectsTheLatestReading()
    {
        var running = new CaptureStatus(CaptureState.Running);
        Assert.AreEqual(TunerState.Listening, TunerStateResolver.Resolve(running, null));
        Assert.AreEqual(TunerState.Listening, TunerStateResolver.Resolve(running, Reading(true, PitchRejection.None)));
        Assert.AreEqual(TunerState.NoSignal, TunerStateResolver.Resolve(running, Reading(false, PitchRejection.Silence)));
        Assert.AreEqual(TunerState.UncertainPitch, TunerStateResolver.Resolve(running, Reading(false, PitchRejection.Uncertain)));
        Assert.AreEqual(TunerState.Clipping, TunerStateResolver.Resolve(running, Reading(true, PitchRejection.None, clipping: true)));
    }
}

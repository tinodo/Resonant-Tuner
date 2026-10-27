using Resonant.Core.Audio;
using Resonant.Core.Tuning;

namespace Resonant.Core.Tests;

[TestClass]
public sealed class TuningSessionTests
{
    private const double A2 = 110.0;

    private static double HzAtCents(double targetHz, double cents) => targetHz * Math.Pow(2.0, cents / 1200.0);

    private static PitchReading Valid(ManualTimeProvider clock, double hz) =>
        new(clock.GetTimestamp(), hz, 0.95, 0.2, false, true, PitchRejection.None);

    private static PitchReading Invalid(ManualTimeProvider clock, PitchRejection reason = PitchRejection.Uncertain) =>
        PitchReading.Invalid(clock.GetTimestamp(), reason);

    private static (TuningSession Session, ManualTimeProvider Clock) Create(int? manualIndex = null, TuningSettings? settings = null)
    {
        var clock = new ManualTimeProvider();
        var session = new TuningSession(clock);
        session.Configure(TuningPresets.GuitarStandard, settings ?? TuningSettings.Default);
        session.SelectTarget(manualIndex);
        return (session, clock);
    }

    [TestMethod]
    public void FlatPitchTellsPlayerToTuneUpWithSignedNegativeCents()
    {
        var (session, clock) = Create(manualIndex: 1);
        var feedback = session.Update(Valid(clock, HzAtCents(A2, -6)));

        Assert.AreEqual(Correction.TuneUp, feedback.Correction);
        Assert.AreEqual(-6.0, feedback.Cents, 1e-6);
        Assert.AreEqual(A2, feedback.TargetHz, 1e-9);
        Assert.AreEqual(5, feedback.Target!.StringNumber);
        Assert.IsFalse(feedback.IsLocked);
    }

    [TestMethod]
    public void SharpPitchTellsPlayerToTuneDown()
    {
        var (session, clock) = Create(manualIndex: 1);
        var feedback = session.Update(Valid(clock, HzAtCents(A2, 7.5)));
        Assert.AreEqual(Correction.TuneDown, feedback.Correction);
        Assert.AreEqual(7.5, feedback.Cents, 1e-6);
    }

    [TestMethod]
    public void InTuneRequiresThreeHundredMillisecondsInsideTolerance()
    {
        var (session, clock) = Create(manualIndex: 1);
        TuningFeedback feedback = TuningFeedback.None;

        for (var t = 0; t < 300; t += 50)
        {
            clock.SetMs(t);
            feedback = session.Update(Valid(clock, HzAtCents(A2, 1.0)));
            Assert.AreEqual(Correction.Hold, feedback.Correction, $"t={t}");
            Assert.IsFalse(feedback.IsLocked);
        }

        clock.SetMs(300);
        feedback = session.Update(Valid(clock, HzAtCents(A2, 1.0)));
        Assert.AreEqual(Correction.InTune, feedback.Correction);
        Assert.IsTrue(feedback.IsLocked);
    }

    [TestMethod]
    public void ToleranceBoundaryIsInclusive()
    {
        var (session, clock) = Create(manualIndex: 1);
        var atBoundary = session.Update(Valid(clock, HzAtCents(A2, 2.0)));
        Assert.AreEqual(Correction.Hold, atBoundary.Correction);

        var (session2, clock2) = Create(manualIndex: 1);
        var beyond = session2.Update(Valid(clock2, HzAtCents(A2, 2.05)));
        Assert.AreEqual(Correction.TuneDown, beyond.Correction);

        var (session3, clock3) = Create(manualIndex: 1);
        var negativeBeyond = session3.Update(Valid(clock3, HzAtCents(A2, -2.05)));
        Assert.AreEqual(Correction.TuneUp, negativeBeyond.Correction);
    }

    [TestMethod]
    public void LockReleasesOnlyBeyondTolerancePlusOneCent()
    {
        var (session, clock) = Create(manualIndex: 1);
        for (var t = 0; t <= 300; t += 50)
        {
            clock.SetMs(t);
            session.Update(Valid(clock, HzAtCents(A2, 0.5)));
        }

        Assert.IsTrue(session.Current.IsLocked);

        clock.AdvanceMs(33);
        var held = session.Update(Valid(clock, HzAtCents(A2, 2.8)));
        Assert.IsTrue(held.IsLocked);
        Assert.AreEqual(Correction.InTune, held.Correction);

        clock.AdvanceMs(33);
        var released = session.Update(Valid(clock, HzAtCents(A2, 3.2)));
        Assert.IsFalse(released.IsLocked);
        Assert.AreEqual(Correction.TuneDown, released.Correction);
    }

    [TestMethod]
    public void InvalidInputClearsLockImmediately()
    {
        var (session, clock) = Create(manualIndex: 1);
        for (var t = 0; t <= 300; t += 50)
        {
            clock.SetMs(t);
            session.Update(Valid(clock, A2));
        }

        Assert.IsTrue(session.Current.IsLocked);

        clock.AdvanceMs(33);
        var feedback = session.Update(Invalid(clock));
        Assert.IsFalse(feedback.IsLocked);
        Assert.AreNotEqual(Correction.InTune, feedback.Correction);
        Assert.IsTrue(feedback.IsStale);
    }

    [TestMethod]
    public void FeedbackExpiresAfterFiveHundredMillisecondsWithoutValidReading()
    {
        var (session, clock) = Create(manualIndex: 1);
        session.Update(Valid(clock, HzAtCents(A2, -10)));

        clock.AdvanceMs(400);
        Assert.IsTrue(session.Update(Invalid(clock)).HasReading);

        clock.AdvanceMs(150);
        var expired = session.Update(Invalid(clock));
        Assert.IsFalse(expired.HasReading);
        Assert.AreEqual(Correction.None, expired.Correction);
    }

    [TestMethod]
    public void TickExpiresStaleFeedbackWithoutAnyReading()
    {
        var (session, clock) = Create(manualIndex: 1);
        session.Update(Valid(clock, HzAtCents(A2, -10)));
        clock.AdvanceMs(600);
        Assert.IsFalse(session.Tick().HasReading);
    }

    [TestMethod]
    public void ManualLockKeepsSelectedStringEvenWhenWrongNoteIsPlayed()
    {
        var (session, clock) = Create(manualIndex: 1);
        var e2 = 82.4069;
        var feedback = session.Update(Valid(clock, e2));

        Assert.AreEqual(5, feedback.Target!.StringNumber);
        Assert.IsTrue(feedback.Cents < -400, feedback.Cents.ToString());
        Assert.IsTrue(feedback.IsOffScale);
        Assert.AreEqual(Correction.TuneUp, feedback.Correction);
    }

    [TestMethod]
    public void AutomaticModePicksNearestStringAndResistsJitter()
    {
        var (session, clock) = Create();
        var feedback = session.Update(Valid(clock, HzAtCents(110.0, 5)));
        Assert.AreEqual(5, feedback.Target!.StringNumber);

        clock.AdvanceMs(33);
        feedback = session.Update(Valid(clock, HzAtCents(110.0, -40)));
        Assert.AreEqual(5, feedback.Target!.StringNumber);
    }

    [TestMethod]
    public void ChangingStringResetsLock()
    {
        var (session, clock) = Create();
        for (var t = 0; t <= 300; t += 50)
        {
            clock.SetMs(t);
            session.Update(Valid(clock, A2));
        }

        Assert.IsTrue(session.Current.IsLocked);

        clock.AdvanceMs(500);
        session.Update(Invalid(clock));
        clock.AdvanceMs(100);
        session.Update(Invalid(clock));
        var next = session.Update(Valid(clock, 146.8324));
        Assert.AreEqual(4, next.Target!.StringNumber);
        Assert.IsFalse(next.IsLocked);
    }

    [TestMethod]
    public void CapoShiftsTargetsAndCents()
    {
        var (session, clock) = Create(manualIndex: 0, settings: new TuningSettings { CapoSemitones = 2 });
        var f2Sharp = NoteMath.FrequencyFromMidi(42);
        var feedback = session.Update(Valid(clock, f2Sharp));
        Assert.AreEqual(0.0, feedback.Cents, 1e-9);
        Assert.AreEqual("F#2", feedback.Note!.Value.ToString());
    }

    [TestMethod]
    public void ChromaticModeReportsNearestNote()
    {
        var clock = new ManualTimeProvider();
        var session = new TuningSession(clock);
        session.Configure(null, TuningSettings.Default);

        var feedback = session.Update(Valid(clock, HzAtCents(440.0, 12)));
        Assert.IsNull(feedback.Target);
        Assert.AreEqual(69, feedback.Note!.Value.Midi);
        Assert.AreEqual(12.0, feedback.Cents, 1e-6);
    }

    [TestMethod]
    public void OutOfRangeAudioIsNotMislabelled()
    {
        var (session, clock) = Create();
        Assert.IsFalse(session.Update(Valid(clock, 3000)).HasReading);
        Assert.IsFalse(session.Update(Valid(clock, 10)).HasReading);
        Assert.IsFalse(session.Update(Valid(clock, double.NaN)).HasReading);
    }

    [TestMethod]
    public void ManualSearchRangeIsScopedAroundTheString()
    {
        var (session, _) = Create(manualIndex: 1);
        var range = session.SearchRange;
        Assert.IsTrue(range.MinHz <= 110.0 * 0.5 + 1e-9);
        Assert.IsTrue(range.MaxHz < 400);
    }
}

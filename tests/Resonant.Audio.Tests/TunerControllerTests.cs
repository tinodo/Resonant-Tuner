using Resonant.Core.Audio;
using Resonant.Core.Dsp;
using Resonant.Core.Tuning;

namespace Resonant.Audio.Tests;

[TestClass]
public sealed class TunerControllerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static (TunerController Controller, FakeAudioCapture Capture, FakeTonePlayer Player) Create(double toneHz = 110.0)
    {
        var capture = new FakeAudioCapture(toneHz);
        var player = new FakeTonePlayer();
        var controller = new TunerController(capture, player, new TuningSession(), new YinPitchEstimator());
        controller.ConfigureTuning(TuningPresets.GuitarStandard, TuningSettings.Default);
        return (controller, capture, player);
    }

    private static async Task<TunerSnapshot> WaitForAsync(TunerController controller, Func<TunerSnapshot, bool> predicate)
    {
        var tcs = new TaskCompletionSource<TunerSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, TunerSnapshot snapshot)
        {
            if (predicate(snapshot))
            {
                tcs.TrySetResult(snapshot);
            }
        }

        controller.SnapshotChanged += Handler;
        try
        {
            if (predicate(controller.Snapshot))
            {
                return controller.Snapshot;
            }

            return await tcs.Task.WaitAsync(Timeout);
        }
        finally
        {
            controller.SnapshotChanged -= Handler;
        }
    }

    [TestMethod]
    public async Task StartingCaptureProducesARealMeasuredReading()
    {
        var (controller, capture, _) = Create();
        await using (controller)
        {
            await controller.StartAsync(new CaptureRequest());
            var snapshot = await WaitForAsync(controller, s => s.Feedback.HasReading);

            Assert.AreEqual(1, capture.StartCount);
            Assert.AreEqual(TunerState.Listening, snapshot.State);
            Assert.AreEqual(110.0, snapshot.Feedback.MeasuredHz, 0.1);
            Assert.IsTrue(snapshot.Feedback.Correction is Correction.Hold or Correction.InTune);

            await controller.StopAsync();
            Assert.AreEqual(TunerState.Stopped, controller.Snapshot.State);
            Assert.IsFalse(controller.Snapshot.Feedback.HasReading);
            Assert.AreEqual(1, capture.StopCount);
        }
    }

    [TestMethod]
    public async Task PermissionDenialIsReportedAndNoReadingIsInvented()
    {
        var (controller, capture, _) = Create();
        capture.NextResult = CaptureStartResult.Failed(CaptureFailureKind.PermissionDenied, "Microphone access is off.");
        await using (controller)
        {
            await controller.StartAsync(new CaptureRequest());

            var snapshot = controller.Snapshot;
            Assert.AreEqual(TunerState.Error, snapshot.State);
            Assert.AreEqual(CaptureFailureKind.PermissionDenied, snapshot.Failure);
            Assert.AreEqual("Microphone access is off.", snapshot.Message);
            Assert.IsFalse(snapshot.Feedback.HasReading);
            Assert.IsNull(snapshot.Reading);
        }
    }

    [TestMethod]
    public async Task NoInputDeviceIsReportedAsAnError()
    {
        var (controller, capture, _) = Create();
        capture.NextResult = CaptureStartResult.Failed(CaptureFailureKind.NoInputDevice, "No microphone was found.");
        await using (controller)
        {
            await controller.StartAsync(new CaptureRequest());
            Assert.AreEqual(TunerState.Error, controller.Snapshot.State);
            Assert.AreEqual(CaptureFailureKind.NoInputDevice, controller.Snapshot.Failure);
        }
    }

    [TestMethod]
    public async Task DeviceLossStopsAnalysisAndClearsFeedback()
    {
        var (controller, capture, _) = Create();
        await using (controller)
        {
            await controller.StartAsync(new CaptureRequest());
            await WaitForAsync(controller, s => s.Feedback.HasReading);

            capture.SimulateDeviceLost();
            var snapshot = await WaitForAsync(controller, s => s.State == TunerState.DeviceLost);

            Assert.AreEqual(CaptureFailureKind.DeviceLost, snapshot.Failure);
            Assert.IsFalse(snapshot.Feedback.HasReading);
        }
    }

    [TestMethod]
    public async Task ReferenceToneLeavesCaptureRunningThroughStartAndStop()
    {
        var (controller, capture, player) = Create();
        await using (controller)
        {
            await controller.StartAsync(new CaptureRequest());
            await WaitForAsync(controller, s => s.Feedback.HasReading);

            var target = controller.Session.Targets[1];
            await controller.PlayReferenceAsync(target, null);

            Assert.AreEqual(110.0, player.LastFrequency!.Value, 1e-9);
            Assert.IsTrue(controller.IsReferencePlaying);
            Assert.AreEqual(0, capture.StopCount);
            Assert.AreEqual(CaptureState.Running, capture.Status.State);
            Assert.IsTrue(controller.Snapshot.Feedback.HasReading);

            await controller.StopReferenceAsync();
            Assert.IsFalse(controller.IsReferencePlaying);
            Assert.AreEqual(1, capture.StartCount);
            Assert.AreEqual(TunerState.Listening, controller.Snapshot.State);
        }
    }

    [TestMethod]
    public async Task InputSwitchRestartsListening()
    {
        var (controller, capture, _) = Create();
        await using (controller)
        {
            await controller.StartAsync(new CaptureRequest());
            var request = new CaptureRequest("usb-input", 1);

            await controller.ChangeInputAsync(request);

            Assert.AreEqual(request, capture.LastRequest);
            Assert.AreEqual(2, capture.StartCount);
            Assert.AreEqual(1, capture.StopCount);
            Assert.IsTrue(controller.IsListeningRequested);
            Assert.AreEqual(TunerState.Listening, controller.Snapshot.State);
        }
    }

    [TestMethod]
    public async Task InputSwitchDuringReferenceRestartsCaptureImmediately()
    {
        var (controller, capture, player) = Create();
        await using (controller)
        {
            await controller.StartAsync(new CaptureRequest());
            await controller.PlayReferenceAsync(controller.Session.Targets[0], null);
            var request = new CaptureRequest("usb-input", 1);

            await controller.ChangeInputAsync(request);

            Assert.AreEqual(2, capture.StartCount);
            Assert.AreEqual(request, capture.LastRequest);
            Assert.AreEqual(CaptureState.Running, capture.Status.State);
            Assert.IsTrue(player.IsPlaying);
            Assert.IsTrue(controller.IsListeningRequested);
            await controller.StopReferenceAsync();
            Assert.AreEqual(request, capture.LastRequest);
            Assert.AreEqual(2, capture.StartCount);
        }
    }

    [TestMethod]
    public async Task InputSwitchWhilePausedDoesNotStartCapture()
    {
        var (controller, capture, _) = Create();
        await using (controller)
        {
            await controller.StartAsync(new CaptureRequest());
            await controller.StopAsync();

            await controller.ChangeInputAsync(new CaptureRequest("usb-input", 1));

            Assert.AreEqual(1, capture.StartCount);
            Assert.AreEqual(CaptureState.Stopped, capture.Status.State);
            Assert.IsFalse(controller.IsListeningRequested);
        }
    }

    [TestMethod]
    public async Task ResumeDuringReferenceStartsCaptureImmediately()
    {
        var (controller, capture, player) = Create();
        await using (controller)
        {
            await controller.PlayReferenceAsync(controller.Session.Targets[0], null);
            var request = new CaptureRequest("usb-input", 1);

            await controller.StartAsync(request);

            Assert.AreEqual(1, capture.StartCount);
            Assert.AreEqual(request, capture.LastRequest);
            Assert.AreEqual(CaptureState.Running, capture.Status.State);
            Assert.IsTrue(player.IsPlaying);
            Assert.IsTrue(controller.IsListeningRequested);
            await controller.StopReferenceAsync();
            Assert.AreEqual(request, capture.LastRequest);
            Assert.AreEqual(1, capture.StartCount);
        }
    }

    [TestMethod]
    public async Task PauseDuringReferencePreventsAutomaticResume()
    {
        var (controller, capture, player) = Create();
        await using (controller)
        {
            await controller.StartAsync(new CaptureRequest());
            await controller.PlayReferenceAsync(controller.Session.Targets[0], null);

            await controller.StopAsync();

            Assert.IsTrue(player.IsPlaying);
            Assert.IsFalse(controller.IsListeningRequested);
            await controller.StopReferenceAsync();
            Assert.AreEqual(1, capture.StartCount);
            Assert.AreEqual(CaptureState.Stopped, capture.Status.State);
        }
    }

    [TestMethod]
    public async Task OutputSwitchKeepsReferenceNoteAndCaptureRunning()
    {
        var (controller, capture, player) = Create();
        await using (controller)
        {
            await controller.StartAsync(new CaptureRequest());
            var target = controller.Session.Targets[2];
            await controller.PlayReferenceAsync(target, null);

            await controller.ChangeOutputAsync("usb-output");

            Assert.AreEqual("usb-output", player.LastOutputDeviceId);
            Assert.AreEqual(target.Hz, player.LastFrequency!.Value, 1e-9);
            Assert.AreEqual(2, player.PlayCount);
            Assert.IsTrue(controller.IsReferencePlaying);
            Assert.AreEqual(1, capture.StartCount);
            Assert.AreEqual(0, capture.StopCount);
            Assert.AreEqual(CaptureState.Running, capture.Status.State);
            await controller.StopReferenceAsync();
            Assert.AreEqual(1, capture.StartCount);
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task OutputSwitchWithoutReferencePreservesListeningState(bool listening)
    {
        var (controller, capture, player) = Create();
        await using (controller)
        {
            if (listening)
            {
                await controller.StartAsync(new CaptureRequest());
            }

            await controller.ChangeOutputAsync("usb-output");

            Assert.AreEqual(0, player.PlayCount);
            Assert.AreEqual(listening ? 1 : 0, capture.StartCount);
            Assert.AreEqual(0, capture.StopCount);
            Assert.AreEqual(listening, controller.IsListeningRequested);
            Assert.AreEqual(listening ? CaptureState.Running : CaptureState.Stopped, capture.Status.State);
        }
    }

    [TestMethod]
    public async Task FailedOutputSwitchDoesNotInterruptCapture()
    {
        var (controller, capture, player) = Create();
        await using (controller)
        {
            await controller.StartAsync(new CaptureRequest());
            await controller.PlayReferenceAsync(controller.Session.Targets[0], null);
            player.NextResult = new ToneResult(false, "Selected output unavailable.");

            await controller.ChangeOutputAsync("missing-output");

            Assert.IsFalse(controller.IsReferencePlaying);
            Assert.AreEqual(1, capture.StartCount);
            Assert.AreEqual(0, capture.StopCount);
            Assert.AreEqual(CaptureState.Running, capture.Status.State);
            Assert.AreEqual(TunerState.Listening, controller.Snapshot.State);
            StringAssert.Contains(controller.Snapshot.ReferenceMessage ?? string.Empty, "Selected output unavailable.");
            Assert.IsNull(controller.Snapshot.Message);
        }
    }

    [TestMethod]
    public async Task OutputSwitchQueuedAfterStopDoesNotRestartReference()
    {
        var (controller, capture, player) = Create();
        await using (controller)
        {
            await controller.PlayReferenceAsync(controller.Session.Targets[0], null);
            var barrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            player.PlaybackBarrier = barrier.Task;
            var retune = controller.RetuneReferenceAsync(controller.Session.Targets[1], null);
            var stop = controller.StopReferenceAsync();
            var changeOutput = controller.ChangeOutputAsync("usb-output");
            barrier.SetResult(true);

            await Task.WhenAll(retune, stop, changeOutput).WaitAsync(Timeout);

            Assert.AreEqual(2, player.PlayCount);
            Assert.IsFalse(player.IsPlaying);
            Assert.IsFalse(controller.IsReferencePlaying);
            Assert.AreEqual(0, capture.StartCount);
        }
    }

    [TestMethod]
    public async Task FailedInputSwitchCanRecoverOnAnotherSelection()
    {
        var (controller, capture, _) = Create();
        await using (controller)
        {
            await controller.StartAsync(new CaptureRequest());
            capture.NextResult = new CaptureStartResult(false, null, CaptureFailureKind.PermissionDenied, "Input denied.");

            await controller.ChangeInputAsync(new CaptureRequest("denied-input", 0));

            Assert.AreEqual(TunerState.Error, controller.Snapshot.State);
            Assert.AreEqual(CaptureFailureKind.PermissionDenied, controller.Snapshot.Failure);
            Assert.IsFalse(controller.Snapshot.Feedback.HasReading);
            Assert.IsTrue(controller.IsListeningRequested);
            capture.NextResult = new CaptureStartResult(true, new AudioFormat(48000, 1));
            var request = new CaptureRequest("working-input", 0);

            await controller.ChangeInputAsync(request);

            Assert.AreEqual(TunerState.Listening, controller.Snapshot.State);
            Assert.AreEqual(CaptureFailureKind.None, controller.Snapshot.Failure);
            Assert.AreEqual(request, capture.LastRequest);
        }
    }

    [TestMethod]
    public async Task OutputSwitchDuringPausedReferenceDoesNotResumeCapture()
    {
        var (controller, capture, player) = Create();
        await using (controller)
        {
            await controller.StartAsync(new CaptureRequest());
            await controller.PlayReferenceAsync(controller.Session.Targets[0], null);
            await controller.StopAsync();

            await controller.ChangeOutputAsync("usb-output");

            Assert.AreEqual("usb-output", player.LastOutputDeviceId);
            Assert.IsTrue(controller.IsReferencePlaying);
            Assert.IsFalse(controller.IsListeningRequested);
            await controller.StopReferenceAsync();
            Assert.AreEqual(1, capture.StartCount);
            Assert.AreEqual(CaptureState.Stopped, capture.Status.State);
        }
    }

    [TestMethod]
    public async Task ReferenceSwitchKeepsCaptureRunning()
    {
        var (controller, capture, player) = Create();
        await using (controller)
        {
            await controller.StartAsync(new CaptureRequest());
            await controller.PlayReferenceAsync(controller.Session.Targets[0], "selected-output");

            await controller.RetuneReferenceAsync(controller.Session.Targets[5], "selected-output");

            Assert.AreEqual(controller.Session.Targets[5].Hz, player.LastFrequency!.Value, 1e-9);
            Assert.AreEqual("selected-output", player.LastOutputDeviceId);
            Assert.AreEqual(2, player.PlayCount);
            Assert.IsTrue(player.IsPlaying);
            Assert.IsTrue(controller.IsReferencePlaying);
            Assert.AreEqual(CaptureState.Running, capture.Status.State);
            Assert.AreEqual(1, capture.StartCount);
            Assert.AreEqual(0, capture.StopCount);

            await controller.StopReferenceAsync();
            Assert.AreEqual(1, capture.StartCount);
            Assert.AreEqual(TunerState.Listening, controller.Snapshot.State);
        }
    }

    [TestMethod]
    public async Task ReferenceSwitchWhenOffDoesNothing()
    {
        var (controller, capture, player) = Create();
        await using (controller)
        {
            await controller.RetuneReferenceAsync(controller.Session.Targets[1], null);

            Assert.AreEqual(0, player.PlayCount);
            Assert.IsNull(player.LastFrequency);
            Assert.IsFalse(controller.IsReferencePlaying);
            Assert.AreEqual(0, capture.StartCount);
            Assert.AreEqual(TunerState.Stopped, controller.Snapshot.State);
        }
    }

    [TestMethod]
    public async Task LatestQueuedReferenceWins()
    {
        var (controller, capture, player) = Create();
        await using (controller)
        {
            await controller.PlayReferenceAsync(controller.Session.Targets[0], null);
            var barrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            player.PlaybackBarrier = barrier.Task;
            var first = controller.RetuneReferenceAsync(controller.Session.Targets[1], null);
            var superseded = controller.RetuneReferenceAsync(controller.Session.Targets[2], null);
            var latest = controller.RetuneReferenceAsync(controller.Session.Targets[5], null);
            barrier.SetResult(true);

            await Task.WhenAll(first, superseded, latest).WaitAsync(Timeout);

            Assert.AreEqual(3, player.PlayCount);
            Assert.AreEqual(controller.Session.Targets[5].Hz, player.LastFrequency!.Value, 1e-9);
            Assert.IsTrue(controller.IsReferencePlaying);
            Assert.AreEqual(0, capture.StartCount);
        }
    }

    [TestMethod]
    public async Task StoppingReferenceCancelsQueuedSwitches()
    {
        var (controller, capture, player) = Create();
        await using (controller)
        {
            await controller.StartAsync(new CaptureRequest());
            await controller.PlayReferenceAsync(controller.Session.Targets[0], null);
            var barrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            player.PlaybackBarrier = barrier.Task;
            var first = controller.RetuneReferenceAsync(controller.Session.Targets[1], null);
            var queued = controller.RetuneReferenceAsync(controller.Session.Targets[5], null);
            var stop = controller.StopReferenceAsync();
            barrier.SetResult(true);

            await Task.WhenAll(first, queued, stop).WaitAsync(Timeout);

            Assert.AreEqual(2, player.PlayCount);
            Assert.IsFalse(player.IsPlaying);
            Assert.IsFalse(controller.IsReferencePlaying);
            Assert.AreEqual(1, capture.StartCount);
            Assert.AreEqual(0, capture.StopCount);
            Assert.AreEqual(TunerState.Listening, controller.Snapshot.State);
        }
    }

    [TestMethod]
    public async Task FailedReferenceSwitchDoesNotInterruptCapture()
    {
        var (controller, capture, player) = Create();
        await using (controller)
        {
            await controller.StartAsync(new CaptureRequest());
            await controller.PlayReferenceAsync(controller.Session.Targets[0], null);
            player.NextResult = new ToneResult(false, "Output disconnected.");

            await controller.RetuneReferenceAsync(controller.Session.Targets[5], null);

            Assert.IsFalse(controller.IsReferencePlaying);
            Assert.AreEqual(1, capture.StartCount);
            Assert.AreEqual(0, capture.StopCount);
            Assert.AreEqual(CaptureState.Running, capture.Status.State);
            Assert.AreEqual(TunerState.Listening, controller.Snapshot.State);
            StringAssert.Contains(controller.Snapshot.ReferenceMessage ?? string.Empty, "Output disconnected.");
            Assert.IsNull(controller.Snapshot.Message);
        }
    }

    [TestMethod]
    public async Task StoppingReferenceWithoutCaptureClearsMessageAndPublishesIdleState()
    {
        var (controller, capture, _) = Create();
        await using (controller)
        {
            TunerSnapshot? published = null;
            controller.SnapshotChanged += (_, snapshot) => published = snapshot;
            await controller.PlayReferenceAsync(controller.Session.Targets[1], null);

            Assert.IsTrue(controller.Snapshot.IsReferencePlaying);
            Assert.IsNull(controller.Snapshot.Message);
            Assert.IsNull(controller.Snapshot.ReferenceMessage);
            Assert.IsFalse(controller.Snapshot.Feedback.HasReading);
            Assert.IsTrue(controller.Spectrum.IsEmpty);

            await controller.StopReferenceAsync();

            Assert.AreEqual(TunerState.Stopped, controller.Snapshot.State);
            Assert.IsNull(controller.Snapshot.Message);
            Assert.IsNull(controller.Snapshot.ReferenceMessage);
            Assert.IsFalse(controller.Snapshot.IsReferencePlaying);
            Assert.IsNotNull(published);
            Assert.IsFalse(published.IsReferencePlaying);
            Assert.IsNull(published.Message);
            Assert.IsNull(published.ReferenceMessage);
            Assert.AreEqual(0, capture.StartCount);
        }
    }

    [TestMethod]
    public async Task FailedResumeDuringReferencePreservesPlaybackAndReportsInputFailure()
    {
        var (controller, capture, player) = Create();
        await using (controller)
        {
            await controller.PlayReferenceAsync(controller.Session.Targets[1], null);

            capture.NextResult = CaptureStartResult.Failed(CaptureFailureKind.DeviceUnavailable, "Microphone is busy.");
            await controller.StartAsync(new CaptureRequest());

            Assert.AreEqual(TunerState.Error, controller.Snapshot.State);
            Assert.AreEqual(CaptureFailureKind.DeviceUnavailable, controller.Snapshot.Failure);
            Assert.AreEqual("Microphone is busy.", controller.Snapshot.Message);
            Assert.IsTrue(player.IsPlaying);
            Assert.IsTrue(controller.IsReferencePlaying);
            Assert.IsTrue(controller.Spectrum.IsEmpty);

            capture.NextResult = new CaptureStartResult(true, new AudioFormat(48000, 1));
            await controller.StartAsync(new CaptureRequest());

            Assert.AreEqual(2, capture.StartCount);
            Assert.AreEqual(CaptureState.Running, capture.Status.State);
            Assert.AreEqual(CaptureFailureKind.None, controller.Snapshot.Failure);
            Assert.IsTrue(player.IsPlaying);
            Assert.IsTrue(controller.IsReferencePlaying);
        }
    }

    [TestMethod]
    public async Task OutputFailureDoesNotInvalidateAWorkingInput()
    {
        var (controller, _, player) = Create();
        await using (controller)
        {
            await controller.StartAsync(new CaptureRequest());
            player.NextResult = new ToneResult(false, "Speaker unavailable.");

            await controller.PlayReferenceAsync(controller.Session.Targets[1], null);

            Assert.IsFalse(controller.IsReferencePlaying);
            var snapshot = await WaitForAsync(controller, s => s.State == TunerState.Listening);
            StringAssert.Contains(snapshot.ReferenceMessage ?? string.Empty, "Speaker unavailable.");
            Assert.IsNull(snapshot.Message);
        }
    }

    [TestMethod]
    public async Task InputAndOutputFailuresAreReportedSimultaneouslyAndIndependently()
    {
        var (controller, capture, player) = Create();
        await using (controller)
        {
            player.NextResult = new ToneResult(false, "Speaker unavailable.");
            await controller.PlayReferenceAsync(controller.Session.Targets[1], null);
            capture.NextResult = CaptureStartResult.Failed(CaptureFailureKind.DeviceUnavailable, "Microphone is busy.");

            await controller.StartAsync(new CaptureRequest());

            var failed = controller.Snapshot;
            Assert.AreEqual(TunerState.Error, failed.State);
            Assert.AreEqual("Microphone is busy.", failed.Message);
            StringAssert.Contains(failed.ReferenceMessage ?? string.Empty, "Speaker unavailable.");

            capture.NextResult = new CaptureStartResult(true, new AudioFormat(48000, 1));
            await controller.StartAsync(new CaptureRequest());

            var recoveredInput = controller.Snapshot;
            Assert.AreEqual(TunerState.Listening, recoveredInput.State);
            Assert.IsNull(recoveredInput.Message);
            StringAssert.Contains(recoveredInput.ReferenceMessage ?? string.Empty, "Speaker unavailable.");
        }
    }

    [TestMethod]
    public async Task RecoveringOutputPreservesAnInputFailure()
    {
        var (controller, capture, player) = Create();
        await using (controller)
        {
            player.NextResult = new ToneResult(false, "Speaker unavailable.");
            await controller.PlayReferenceAsync(controller.Session.Targets[1], null);
            capture.NextResult = CaptureStartResult.Failed(CaptureFailureKind.DeviceUnavailable, "Microphone is busy.");
            await controller.StartAsync(new CaptureRequest());

            player.NextResult = new ToneResult(true, null);
            await controller.PlayReferenceAsync(controller.Session.Targets[1], null);

            var snapshot = controller.Snapshot;
            Assert.IsTrue(snapshot.IsReferencePlaying);
            Assert.IsNull(snapshot.ReferenceMessage);
            Assert.AreEqual(TunerState.Error, snapshot.State);
            Assert.AreEqual("Microphone is busy.", snapshot.Message);
        }
    }

    [TestMethod]
    public async Task StoppingReferenceKeepsAnInputFailureVisible()
    {
        var (controller, capture, player) = Create();
        await using (controller)
        {
            await controller.PlayReferenceAsync(controller.Session.Targets[1], null);
            capture.NextResult = CaptureStartResult.Failed(CaptureFailureKind.DeviceUnavailable, "Microphone is busy.");
            await controller.StartAsync(new CaptureRequest());
            player.SimulateOutputFailure("Speaker unplugged.");
            await WaitForAsync(controller, s => s.ReferenceMessage is not null);

            await controller.StopReferenceAsync();

            Assert.IsNull(controller.Snapshot.ReferenceMessage);
            Assert.AreEqual("Microphone is busy.", controller.Snapshot.Message);
            Assert.AreEqual(TunerState.Error, controller.Snapshot.State);
        }
    }

    [TestMethod]
    public async Task ManualStringLockReportsCentsAgainstTheSelectedString()
    {
        var (controller, _, _) = Create(toneHz: 110.0);
        await using (controller)
        {
            controller.SelectString(2);
            await controller.StartAsync(new CaptureRequest());
            var snapshot = await WaitForAsync(controller, s => s.Feedback.HasReading);

            Assert.AreEqual(4, snapshot.Feedback.Target!.StringNumber);
            Assert.IsTrue(snapshot.Feedback.Cents < -400);
        }
    }
}

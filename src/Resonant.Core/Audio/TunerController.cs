using Resonant.Core.Localization;
using Resonant.Core.Dsp;
using Resonant.Core.Tuning;

namespace Resonant.Core.Audio;

/// <summary><c>Message</c> describes microphone capture only; <c>ReferenceMessage</c> describes reference-tone output only.</summary>
public sealed record TunerSnapshot(
    TunerState State,
    string? Message,
    CaptureFailureKind Failure,
    PitchReading? Reading,
    TuningFeedback Feedback,
    bool IsReferencePlaying,
    string? ReferenceMessage = null);

/// <summary>
/// Coordinates capture, analysis and tuning guidance. Raises <see cref="SnapshotChanged"/> from worker threads;
/// the UI layer must marshal to its own thread. Capture and reference playback run independently;
/// the spectrum always comes from microphone capture, including acoustic pickup of the reference tone.
/// </summary>
public sealed class TunerController : IAsyncDisposable
{
    private const int RingCapacity = 65536;

    private readonly IAudioCapture _capture;
    private readonly IReferenceTonePlayer _player;
    private readonly ITuningSession _session;
    private readonly SampleRingBuffer _ring = new(RingCapacity);
    private readonly PitchEngine _engine;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateSync = new();

    private CancellationTokenSource? _engineCts;
    private Task? _engineTask;
    private string? _referenceMessage;
    private bool _listeningRequested;
    private bool _referencePlaying;
    private TuningTarget? _referenceTarget;
    private int _referenceVersion;
    private TunerState _state = TunerState.Stopped;
    private string? _message;
    private CaptureFailureKind _failure;
    private PitchReading? _lastReading;
    private bool _disposed;

    public TunerController(
        IAudioCapture capture,
        IReferenceTonePlayer player,
        ITuningSession session,
        IPitchEstimator estimator,
        TimeProvider? timeProvider = null)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _engine = new PitchEngine(_ring, estimator ?? throw new ArgumentNullException(nameof(estimator)), timeProvider);
        _engine.ReadingProduced += OnReading;
        _capture.StatusChanged += OnCaptureStatusChanged;
        _player.PlaybackFailed += OnPlaybackFailed;
    }

    public event EventHandler<TunerSnapshot>? SnapshotChanged;

    public TunerSnapshot Snapshot
    {
        get
        {
            lock (_stateSync)
            {
                return BuildSnapshot();
            }
        }
    }

    public ITuningSession Session => _session;

    public bool IsReferencePlaying => Volatile.Read(ref _referencePlaying);

    public bool IsListeningRequested => Volatile.Read(ref _listeningRequested);

    public ReadOnlyMemory<float> Spectrum => _capture.Status.State == CaptureState.Running
        ? _engine.Spectrum
        : ReadOnlyMemory<float>.Empty;

    /// <summary>Applies the preset and calibration and narrows or widens the analysis band to match.</summary>
    public void ConfigureTuning(TuningPreset? preset, TuningSettings settings)
    {
        _session.Configure(preset, settings);
        _engine.SearchRange = _session.SearchRange;
        Publish();
    }

    /// <summary>Locks the given string (index into the session targets), or null to return to automatic selection.</summary>
    public void SelectString(int? targetIndex)
    {
        _session.SelectTarget(targetIndex);
        _engine.SearchRange = _session.SearchRange;
        Publish();
    }

    public async Task StartAsync(CaptureRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Volatile.Write(ref _listeningRequested, true);
            await StartCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ChangeInputAsync(CaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_listeningRequested)
            {
                return;
            }

            await StopCoreAsync().ConfigureAwait(false);
            await StartCoreAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Volatile.Write(ref _listeningRequested, false);
            await StopCoreAsync().ConfigureAwait(false);
            SetState(TunerState.Stopped, null, CaptureFailureKind.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task PlayReferenceAsync(TuningTarget target, string? outputDeviceId) =>
        PlayReferenceCoreAsync(target, outputDeviceId, false, Interlocked.Increment(ref _referenceVersion));

    public Task RetuneReferenceAsync(TuningTarget target, string? outputDeviceId) =>
        PlayReferenceCoreAsync(target, outputDeviceId, true, Interlocked.Increment(ref _referenceVersion));

    public async Task ChangeOutputAsync(string? outputDeviceId)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_referencePlaying && _referenceTarget is { } target)
            {
                await ReplaceReferenceCoreAsync(target, outputDeviceId).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PlayReferenceCoreAsync(TuningTarget target, string? outputDeviceId, bool requireActive, int version)
    {
        ArgumentNullException.ThrowIfNull(target);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (requireActive && (!_referencePlaying || version != Volatile.Read(ref _referenceVersion)))
            {
                return;
            }

            await ReplaceReferenceCoreAsync(target, outputDeviceId).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ReplaceReferenceCoreAsync(TuningTarget target, string? outputDeviceId)
    {
        if (_referencePlaying)
        {
            await _player.StopAsync().ConfigureAwait(false);
        }

        var result = await _player.PlayAsync(target.Hz, outputDeviceId).ConfigureAwait(false);
        if (!result.Success)
        {
            Volatile.Write(ref _referencePlaying, false);
            _referenceTarget = null;
            SetReferenceMessage(Text.Get("Reference tone unavailable: ") + (result.Message ?? Text.Get("the output device could not start.")));
            return;
        }

        _referenceTarget = target;
        Volatile.Write(ref _referencePlaying, true);
        SetReferenceMessage(null);
    }

    public async Task StopReferenceAsync()
    {
        Interlocked.Increment(ref _referenceVersion);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _player.StopAsync().ConfigureAwait(false);
            Volatile.Write(ref _referencePlaying, false);
            _referenceTarget = null;
            SetReferenceMessage(null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _capture.StatusChanged -= OnCaptureStatusChanged;
        _player.PlaybackFailed -= OnPlaybackFailed;
        _engine.ReadingProduced -= OnReading;
        await StopCoreAsync().ConfigureAwait(false);
        await _player.StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private async Task StartCoreAsync(CaptureRequest request, CancellationToken cancellationToken)
    {
        if (_capture.Status.State == CaptureState.Running)
        {
            return;
        }

        SetState(TunerState.Starting, null, CaptureFailureKind.None);
        _session.Reset();
        _ring.Clear();

        var result = await _capture.StartAsync(request, _ring, cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Format is null)
        {
            var state = result.Failure == CaptureFailureKind.DeviceLost ? TunerState.DeviceLost : TunerState.Error;
            SetState(state, result.Message ?? Text.Get("The microphone could not be started."), result.Failure);
            return;
        }

        _engine.SearchRange = _session.SearchRange;
        _engineCts = new CancellationTokenSource();
        var token = _engineCts.Token;
        var sampleRate = result.Format.SampleRate;
        _engineTask = Task.Run(() => _engine.RunAsync(sampleRate, token), CancellationToken.None);
        lock (_stateSync)
        {
            _lastReading = null;
        }

        SetState(TunerState.Listening, null, CaptureFailureKind.None);
    }

    private async Task StopCoreAsync()
    {
        var cts = _engineCts;
        var task = _engineTask;
        _engineCts = null;
        _engineTask = null;

        if (cts is not null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }

        if (task is not null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        cts?.Dispose();
        await _capture.StopAsync().ConfigureAwait(false);
        _ring.Clear();
        _session.Reset();
        lock (_stateSync)
        {
            _lastReading = null;
        }
    }

    private void SetReferenceMessage(string? message)
    {
        lock (_stateSync)
        {
            _referenceMessage = message;
        }

        Publish();
    }

    private void OnReading(PitchReading reading)
    {
        if (_capture.Status.State != CaptureState.Running)
        {
            return;
        }

        _session.Update(reading);
        lock (_stateSync)
        {
            _lastReading = reading;
        }

        var state = TunerStateResolver.Resolve(_capture.Status, reading);
        SetState(state, _message, _failure, publish: true);
    }

    private void OnCaptureStatusChanged(object? sender, CaptureStatus status)
    {
        if (_disposed || status.State is not (CaptureState.DeviceLost or CaptureState.Error))
        {
            return;
        }

        _ = HandleCaptureLostAsync(status);
    }

    private async Task HandleCaptureLostAsync(CaptureStatus status)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            await StopCoreAsync().ConfigureAwait(false);
            var state = status.State == CaptureState.DeviceLost ? TunerState.DeviceLost : TunerState.Error;
            SetState(state, status.Message ?? Text.Get("The audio input stopped unexpectedly."), status.Failure);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnPlaybackFailed(object? sender, string message)
    {
        if (_disposed)
        {
            return;
        }

        _ = HandlePlaybackFailedAsync(message);
    }

    private async Task HandlePlaybackFailedAsync(string message)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            Volatile.Write(ref _referencePlaying, false);
            _referenceTarget = null;
            SetReferenceMessage(Text.Get("Reference tone stopped: ") + message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void SetState(TunerState state, string? message, CaptureFailureKind failure, bool publish = true)
    {
        lock (_stateSync)
        {
            _state = state;
            _message = message;
            _failure = failure;
        }

        if (publish)
        {
            Publish();
        }
    }

    private void Publish()
    {
        TunerSnapshot snapshot;
        lock (_stateSync)
        {
            snapshot = BuildSnapshot();
        }

        SnapshotChanged?.Invoke(this, snapshot);
    }

    private TunerSnapshot BuildSnapshot() =>
        new(_state, _message, _failure, _lastReading, _session.Current, Volatile.Read(ref _referencePlaying), _referenceMessage);
}

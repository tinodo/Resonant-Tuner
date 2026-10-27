namespace Resonant.Core.Audio;

public enum CaptureState
{
    Stopped,
    RequestingPermission,
    Starting,
    Running,
    DeviceLost,
    Error,
}

public enum CaptureFailureKind
{
    None,
    PermissionDenied,
    NoInputDevice,
    DeviceUnavailable,
    UnsupportedFormat,
    DeviceLost,
    Unknown,
}

public sealed record AudioFormat(int SampleRate, int Channels);

public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault);

/// <summary>Which device and channel to capture. A null device id means the system default input.</summary>
public sealed record CaptureRequest(string? DeviceId = null, int ChannelIndex = 0);

public sealed record CaptureStatus(
    CaptureState State,
    CaptureFailureKind Failure = CaptureFailureKind.None,
    string? Message = null);

/// <param name="RawProcessing">True or false when the adapter could determine it; null when unknown. Never assumed.</param>
public sealed record CaptureStartResult(
    bool Success,
    AudioFormat? Format,
    CaptureFailureKind Failure = CaptureFailureKind.None,
    string? Message = null,
    bool? RawProcessing = null)
{
    public static CaptureStartResult Failed(CaptureFailureKind failure, string message) =>
        new(false, null, failure, message);
}

/// <summary>Microphone capture boundary. The adapter selects one channel and writes mono floats to the sink.</summary>
public interface IAudioCapture : IAsyncDisposable
{
    CaptureStatus Status { get; }

    event EventHandler<CaptureStatus>? StatusChanged;

    Task<IReadOnlyList<AudioDeviceInfo>> GetInputDevicesAsync(CancellationToken cancellationToken = default);

    Task<CaptureStartResult> StartAsync(CaptureRequest request, ISampleSink sink, CancellationToken cancellationToken = default);

    Task StopAsync();
}

public sealed record ToneResult(bool Success, string? Message = null);

/// <summary>Reference tone playback boundary.</summary>
public interface IReferenceTonePlayer : IAsyncDisposable
{
    bool IsPlaying { get; }

    /// <summary>Raised when playback ends because the output failed, with a user-visible message.</summary>
    event EventHandler<string>? PlaybackFailed;

    Task<IReadOnlyList<AudioDeviceInfo>> GetOutputDevicesAsync(CancellationToken cancellationToken = default);

    Task<ToneResult> PlayAsync(double frequencyHz, string? outputDeviceId, CancellationToken cancellationToken = default);

    Task StopAsync();
}

/// <summary>Marshals work to the UI thread without Core depending on WinUI.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

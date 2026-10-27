using Resonant.Core.Audio;

namespace Resonant.Audio.Tests;

/// <summary>Handwritten capture fake. Fills the sink with a continuous sine so the real analysis worker has data.</summary>
internal sealed class FakeAudioCapture : IAudioCapture
{
    private readonly double _toneHz;

    public FakeAudioCapture(double toneHz = 110.0)
    {
        _toneHz = toneHz;
    }

    public CaptureStatus Status { get; private set; } = new(CaptureState.Stopped);

    public CaptureStartResult NextResult { get; set; } = new(true, new AudioFormat(48000, 1));

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public CaptureRequest? LastRequest { get; private set; }

    public event EventHandler<CaptureStatus>? StatusChanged;

    public Task<IReadOnlyList<AudioDeviceInfo>> GetInputDevicesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AudioDeviceInfo>>([new AudioDeviceInfo("default", "Default input", true)]);

    public Task<CaptureStartResult> StartAsync(CaptureRequest request, ISampleSink sink, CancellationToken cancellationToken = default)
    {
        StartCount++;
        LastRequest = request;
        var result = NextResult;
        if (!result.Success)
        {
            Status = new CaptureStatus(CaptureState.Error, result.Failure, result.Message);
            return Task.FromResult(result);
        }

        var rate = result.Format!.SampleRate;
        var samples = new float[rate];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(0.5 * Math.Sin(2.0 * Math.PI * _toneHz * i / rate));
        }

        sink.Write(samples);
        Status = new CaptureStatus(CaptureState.Running);
        return Task.FromResult(result);
    }

    public Task StopAsync()
    {
        StopCount++;
        Status = new CaptureStatus(CaptureState.Stopped);
        return Task.CompletedTask;
    }

    public void SimulateDeviceLost()
    {
        Status = new CaptureStatus(CaptureState.DeviceLost, CaptureFailureKind.DeviceLost, "Microphone unplugged");
        StatusChanged?.Invoke(this, Status);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeTonePlayer : IReferenceTonePlayer
{
    public bool IsPlaying { get; private set; }

    public ToneResult NextResult { get; set; } = new(true);

    public double? LastFrequency { get; private set; }

    public string? LastOutputDeviceId { get; private set; }

    public int PlayCount { get; private set; }

    public Task? PlaybackBarrier { get; set; }

    public event EventHandler<string>? PlaybackFailed;

    public Task<IReadOnlyList<AudioDeviceInfo>> GetOutputDevicesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AudioDeviceInfo>>([new AudioDeviceInfo("default", "Default output", true)]);

    public async Task<ToneResult> PlayAsync(double frequencyHz, string? outputDeviceId, CancellationToken cancellationToken = default)
    {
        PlayCount++;
        LastFrequency = frequencyHz;
        LastOutputDeviceId = outputDeviceId;
        if (PlaybackBarrier is { } barrier)
        {
            await barrier.WaitAsync(cancellationToken);
        }

        IsPlaying = NextResult.Success;
        return NextResult;
    }

    public Task StopAsync()
    {
        IsPlaying = false;
        return Task.CompletedTask;
    }

    public void SimulateOutputFailure(string message)
    {
        IsPlaying = false;
        PlaybackFailed?.Invoke(this, message);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

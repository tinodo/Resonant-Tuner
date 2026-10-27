using Resonant.Core.Audio;
using Windows.Devices.Enumeration;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Capture;
using Windows.Media.Devices;
using Windows.Media.Render;
using WinRT;
using Resonant.Core.Localization;

namespace Resonant.App.Audio;

/// <summary>
/// Microphone capture through Windows AudioGraph: device input node to frame output node. The input is never
/// routed to an output device (no monitoring). The quantum callback only copies one channel to the sink and returns.
/// </summary>
public sealed class AudioGraphCapture : IAudioCapture
{
    private const int MinSampleRate = 32000;

    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _statusSync = new();

    private AudioGraph? _graph;
    private AudioFrameOutputNode? _output;
    private ISampleSink? _sink;
    private int _channelIndex;
    private int _channels = 1;
    private float[] _mono = new float[4096];
    private bool _usingDefaultDevice;
    private bool _disposed;
    private CaptureStatus _status = new(CaptureState.Stopped);

    public CaptureStatus Status
    {
        get
        {
            lock (_statusSync)
            {
                return _status;
            }
        }
    }

    public event EventHandler<CaptureStatus>? StatusChanged;

    public async Task<IReadOnlyList<AudioDeviceInfo>> GetInputDevicesAsync(CancellationToken cancellationToken = default)
    {
        var devices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioCaptureSelector());
        var defaultId = MediaDevice.GetDefaultAudioCaptureId(AudioDeviceRole.Default);
        var list = new List<AudioDeviceInfo>(devices.Count);
        foreach (var device in devices)
        {
            list.Add(new AudioDeviceInfo(device.Id, device.Name, string.Equals(device.Id, defaultId, StringComparison.OrdinalIgnoreCase)));
        }

        return list;
    }

    public async Task<CaptureStartResult> StartAsync(CaptureRequest request, ISampleSink sink, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sink);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StopCore();
            SetStatus(new CaptureStatus(CaptureState.RequestingPermission));

            DeviceInformation? device = null;
            if (!string.IsNullOrEmpty(request.DeviceId))
            {
                try
                {
                    device = await DeviceInformation.CreateFromIdAsync(request.DeviceId);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
                {
                    return Fail(CaptureFailureKind.DeviceUnavailable, Text.Get("The selected microphone is no longer available. Choose another input and retry."));
                }
            }
            else
            {
                var all = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioCaptureSelector());
                if (all.Count == 0)
                {
                    return Fail(CaptureFailureKind.NoInputDevice, Text.Get("No microphone was found. Connect an input device, then retry."));
                }
            }

            SetStatus(new CaptureStatus(CaptureState.Starting));

            var settings = new AudioGraphSettings(AudioRenderCategory.Other)
            {
                QuantumSizeSelectionMode = QuantumSizeSelectionMode.LowestLatency,
            };

            var graphResult = await AudioGraph.CreateAsync(settings);
            if (graphResult.Status != AudioGraphCreationStatus.Success)
            {
                graphResult.Graph?.Dispose();
                return Fail(
                    graphResult.Status == AudioGraphCreationStatus.FormatNotSupported
                        ? CaptureFailureKind.UnsupportedFormat
                        : CaptureFailureKind.DeviceUnavailable,
                    Text.Format("Windows could not start the audio engine ({0}).", graphResult.Status));
            }

            var graph = graphResult.Graph;
            var format = graph.EncodingProperties;
            if (format.SampleRate < MinSampleRate)
            {
                graph.Dispose();
                return Fail(CaptureFailureKind.UnsupportedFormat, Text.Format("The audio engine runs at {0} Hz; this tuner needs at least {1} Hz.", format.SampleRate, MinSampleRate));
            }

            var inputResult = device is null
                ? await graph.CreateDeviceInputNodeAsync(MediaCategory.Other)
                : await graph.CreateDeviceInputNodeAsync(MediaCategory.Other, format, device);
            if (inputResult.Status != AudioDeviceNodeCreationStatus.Success)
            {
                graph.Dispose();
                return inputResult.Status switch
                {
                    AudioDeviceNodeCreationStatus.AccessDenied => Fail(CaptureFailureKind.PermissionDenied, Text.Get("Microphone access is off. Enable access in Windows Settings, then retry.")),
                    AudioDeviceNodeCreationStatus.FormatNotSupported => Fail(CaptureFailureKind.UnsupportedFormat, Text.Get("This microphone's audio format is not supported.")),
                    AudioDeviceNodeCreationStatus.DeviceNotAvailable => Fail(CaptureFailureKind.DeviceUnavailable, Text.Get("The microphone is unavailable or in use. Close other apps using it, then retry.")),
                    _ => Fail(CaptureFailureKind.Unknown, Text.Format("The microphone could not be opened ({0}).", inputResult.Status)),
                };
            }

            var input = inputResult.DeviceInputNode;
            var output = graph.CreateFrameOutputNode(format);
            input.AddOutgoingConnection(output);

            _graph = graph;
            _output = output;
            _sink = sink;
            _channels = Math.Max(1, (int)format.ChannelCount);
            _channelIndex = request.ChannelIndex >= 0 && request.ChannelIndex < _channels ? request.ChannelIndex : 0;
            _usingDefaultDevice = device is null;

            graph.QuantumStarted += OnQuantumStarted;
            graph.UnrecoverableErrorOccurred += OnUnrecoverableError;
            if (_usingDefaultDevice)
            {
                MediaDevice.DefaultAudioCaptureDeviceChanged += OnDefaultCaptureDeviceChanged;
            }

            graph.Start();
            SetStatus(new CaptureStatus(CaptureState.Running));

            // Whether Windows applied raw (unprocessed) capture cannot be confirmed here, so it is reported as unknown.
            return new CaptureStartResult(true, new AudioFormat((int)format.SampleRate, _channels));
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            StopCore();
            SetStatus(new CaptureStatus(CaptureState.Stopped));
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _disposed = true;
        _lifecycle.Dispose();
    }

    private CaptureStartResult Fail(CaptureFailureKind failure, string message)
    {
        SetStatus(new CaptureStatus(CaptureState.Error, failure, message));
        return CaptureStartResult.Failed(failure, message);
    }

    private void StopCore()
    {
        MediaDevice.DefaultAudioCaptureDeviceChanged -= OnDefaultCaptureDeviceChanged;

        var graph = _graph;
        _graph = null;
        _output = null;
        _sink = null;
        if (graph is null)
        {
            return;
        }

        graph.QuantumStarted -= OnQuantumStarted;
        graph.UnrecoverableErrorOccurred -= OnUnrecoverableError;
        try
        {
            graph.Stop();
        }
        finally
        {
            graph.Dispose();
        }
    }

    private void SetStatus(CaptureStatus status)
    {
        lock (_statusSync)
        {
            _status = status;
        }

        StatusChanged?.Invoke(this, status);
    }

    private void OnQuantumStarted(AudioGraph sender, object args)
    {
        var output = _output;
        var sink = _sink;
        if (output is null || sink is null)
        {
            return;
        }

        using var frame = output.GetFrame();
        CopyFrame(frame, sink);
    }

    private unsafe void CopyFrame(AudioFrame frame, ISampleSink sink)
    {
        using var buffer = frame.LockBuffer(AudioBufferAccessMode.Read);
        using var reference = buffer.CreateReference();
        reference.As<IMemoryBufferByteAccess>().GetBuffer(out var data, out var capacity);

        var byteCount = (int)Math.Min(buffer.Length, capacity);
        var floatCount = byteCount / sizeof(float);
        var channels = _channels;
        var frames = floatCount / channels;
        if (frames <= 0)
        {
            return;
        }

        if (_mono.Length < frames)
        {
            _mono = new float[frames];
        }

        var interleaved = new ReadOnlySpan<float>(data, floatCount);
        var channel = _channelIndex;
        var mono = _mono;
        for (var i = 0; i < frames; i++)
        {
            mono[i] = interleaved[(i * channels) + channel];
        }

        sink.Write(mono.AsSpan(0, frames));
    }

    private void OnUnrecoverableError(AudioGraph sender, AudioGraphUnrecoverableErrorOccurredEventArgs args) =>
        SetStatus(new CaptureStatus(
            CaptureState.DeviceLost,
            CaptureFailureKind.DeviceLost,
            Text.Get("The microphone stopped responding. Reconnect it, then press Start.")));

    private void OnDefaultCaptureDeviceChanged(object sender, DefaultAudioCaptureDeviceChangedEventArgs args)
    {
        if (_usingDefaultDevice && args.Role == AudioDeviceRole.Default && Status.State == CaptureState.Running)
        {
            SetStatus(new CaptureStatus(
                CaptureState.DeviceLost,
                CaptureFailureKind.DeviceLost,
                Text.Get("The default microphone changed. Press Start to listen on the new device.")));
        }
    }
}

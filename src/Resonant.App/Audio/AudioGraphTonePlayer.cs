using Resonant.Core.Audio;
using Resonant.Core.Dsp;
using Windows.Devices.Enumeration;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Devices;
using Windows.Media.MediaProperties;
using Windows.Media.Render;
using WinRT;
using Resonant.Core.Localization;

namespace Resonant.App.Audio;

/// <summary>
/// Reference tone playback through Windows AudioGraph: frame input node to device output node. Output failure is
/// reported through <see cref="PlaybackFailed"/> and never touches the microphone adapter.
/// </summary>
public sealed class AudioGraphTonePlayer : IReferenceTonePlayer
{
    private static readonly TimeSpan ReleaseWait = TimeSpan.FromMilliseconds(80);

    private readonly SemaphoreSlim _lifecycle = new(1, 1);

    private AudioGraph? _graph;
    private AudioFrameInputNode? _input;
    private AudioDeviceOutputNode? _output;
    private ReferenceToneSynth? _synth;
    private ReferenceToneSynth[] _harmonics = [];
    private int _channels = 1;
    private float[] _mono = new float[2048];
    private float[] _harmonicSamples = new float[2048];
    private int _renderedSamples;
    private int _failureReported;
    private bool _disposed;

    public bool IsPlaying => _graph is not null;

    public event EventHandler<string>? PlaybackFailed;

    public async Task<IReadOnlyList<AudioDeviceInfo>> GetOutputDevicesAsync(CancellationToken cancellationToken = default)
    {
        var devices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
        var defaultId = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
        var list = new List<AudioDeviceInfo>(devices.Count);
        foreach (var device in devices)
        {
            list.Add(new AudioDeviceInfo(device.Id, device.Name, string.Equals(device.Id, defaultId, StringComparison.OrdinalIgnoreCase)));
        }

        return list;
    }

    public async Task<ToneResult> PlayAsync(double frequencyHz, string? outputDeviceId, CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StopCore();

            var settings = new AudioGraphSettings(AudioRenderCategory.Media);
            if (!string.IsNullOrEmpty(outputDeviceId))
            {
                try
                {
                    settings.PrimaryRenderDevice = await DeviceInformation.CreateFromIdAsync(outputDeviceId);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
                {
                    return new ToneResult(false, Text.Get("The selected output device is no longer available."));
                }
            }

            var graphResult = await AudioGraph.CreateAsync(settings);
            if (graphResult.Status != AudioGraphCreationStatus.Success)
            {
                graphResult.Graph?.Dispose();
                return new ToneResult(false, Text.Format("Windows could not start audio output ({0}).", graphResult.Status));
            }

            var graph = graphResult.Graph;
            var outputResult = await graph.CreateDeviceOutputNodeAsync();
            if (outputResult.Status != AudioDeviceNodeCreationStatus.Success)
            {
                graph.Dispose();
                return new ToneResult(false, Text.Format("The output device could not be opened ({0}).", outputResult.Status));
            }

            var format = AudioEncodingProperties.CreatePcm(graph.EncodingProperties.SampleRate, graph.EncodingProperties.ChannelCount, 32);
            format.Subtype = MediaEncodingSubtypes.Float;
            var input = graph.CreateFrameInputNode(format);
            input.AddOutgoingConnection(outputResult.DeviceOutputNode);

            _channels = Math.Max(1, (int)format.ChannelCount);
            _synth = new ReferenceToneSynth((int)format.SampleRate, frequencyHz, volume: 0.20);
            var sampleRate = (int)format.SampleRate;
            var overtones = new List<ReferenceToneSynth>();
            for (var harmonic = 2; harmonic <= ReferenceSpectrum.HarmonicCount; harmonic++)
            {
                if (frequencyHz * harmonic >= sampleRate / 2.0)
                {
                    break;
                }

                overtones.Add(new ReferenceToneSynth(sampleRate, frequencyHz * harmonic, volume: 0.20 * ReferenceSpectrum.HarmonicWeight(harmonic)));
            }

            _harmonics = [.. overtones];
            _renderedSamples = 0;
            _failureReported = 0;
            _graph = graph;
            _input = input;
            _output = outputResult.DeviceOutputNode;

            input.QuantumStarted += OnInputQuantumStarted;
            input.AudioFrameCompleted += OnAudioFrameCompleted;
            graph.UnrecoverableErrorOccurred += OnUnrecoverableError;
            _output.Start();
            input.Start();
            graph.Start();
            _ = VerifyPlaybackAsync(graph);
            return new ToneResult(true);
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
            if (_synth is { } synth)
            {
                synth.BeginRelease();
                foreach (var overtone in _harmonics)
                {
                    overtone.BeginRelease();
                }
                await Task.Delay(ReleaseWait).ConfigureAwait(false);
            }

            StopCore();
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

    private void StopCore()
    {
        var graph = _graph;
        var input = _input;
        _graph = null;
        _input = null;
        _output = null;
        _synth = null;
        _harmonics = [];
        if (graph is null)
        {
            return;
        }

        if (input is not null)
        {
            input.QuantumStarted -= OnInputQuantumStarted;
            input.AudioFrameCompleted -= OnAudioFrameCompleted;
        }

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

    private void OnInputQuantumStarted(AudioFrameInputNode sender, FrameInputNodeQuantumStartedEventArgs args)
    {
        if (!ReferenceEquals(sender, _input) || _graph is null)
        {
            return;
        }
        var graph = _graph;
        try
        {
            FillInputQuantum(sender, args);
        }
        catch (Exception exception) when (exception is InvalidCastException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            if (graph is not null)
            {
                ReportPlaybackFailure(graph, Text.Format("Audio rendering failed: {0}", exception.Message));
            }
        }
    }

    private unsafe void FillInputQuantum(AudioFrameInputNode sender, FrameInputNodeQuantumStartedEventArgs args)
    {
        var synth = _synth;
        var needed = args.RequiredSamples;
        if (synth is null || needed <= 0)
        {
            return;
        }

        if (_mono.Length < needed)
        {
            _mono = new float[needed];
            _harmonicSamples = new float[needed];
        }

        synth.Render(_mono.AsSpan(0, needed));
        foreach (var overtone in _harmonics)
        {
            overtone.Render(_harmonicSamples.AsSpan(0, needed));
            for (var sample = 0; sample < needed; sample++)
            {
                _mono[sample] += _harmonicSamples[sample];
            }
        }

        var channels = _channels;
        var byteCount = (uint)(needed * channels * sizeof(float));
        var frame = new AudioFrame(byteCount);
        try
        {
            using (var buffer = frame.LockBuffer(AudioBufferAccessMode.Write))
            using (var reference = buffer.CreateReference())
            {
                reference.As<IMemoryBufferByteAccess>().GetBuffer(out var data, out var capacity);
                if (data == null || capacity < byteCount)
                {
                    throw new InvalidOperationException(Text.Get("Windows returned an incomplete output buffer."));
                }
                var floats = new Span<float>(data, (int)(capacity / sizeof(float)));
                for (var i = 0; i < needed; i++)
                {
                    for (var c = 0; c < channels; c++)
                    {
                        floats[(i * channels) + c] = _mono[i];
                    }
                }

                buffer.Length = byteCount;
            }

            sender.AddFrame(frame);
            Interlocked.Add(ref _renderedSamples, needed);
        }
        catch
        {
            frame.Dispose();
            throw;
        }
    }

    private static void OnAudioFrameCompleted(AudioFrameInputNode sender, AudioFrameCompletedEventArgs args) =>
        args.Frame.Dispose();

    private async Task VerifyPlaybackAsync(AudioGraph graph)
    {
        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        if (ReferenceEquals(graph, _graph) && Volatile.Read(ref _renderedSamples) == 0)
        {
            ReportPlaybackFailure(graph, Text.Get("The output device did not request any audio. Select another output in Sound & display."));
        }
    }

    private void OnUnrecoverableError(AudioGraph sender, AudioGraphUnrecoverableErrorOccurredEventArgs args)
    {
        if (_graph is { } graph)
        {
            ReportPlaybackFailure(graph, Text.Format("The output device stopped responding ({0}).", args.Error));
        }
    }

    private void ReportPlaybackFailure(AudioGraph graph, string message)
    {
        if (_disposed || !ReferenceEquals(graph, _graph) || Interlocked.Exchange(ref _failureReported, 1) != 0)
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!ReferenceEquals(graph, _graph))
                {
                    return;
                }
                StopCore();
            }
            finally
            {
                _lifecycle.Release();
            }

            PlaybackFailed?.Invoke(this, message);
        });
    }
}

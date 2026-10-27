namespace Resonant.Core.Audio;

/// <summary>Receives mono PCM samples (-1..1) from a capture adapter. Implementations must not block.</summary>
public interface ISampleSink
{
    void Write(ReadOnlySpan<float> monoSamples);
}

/// <summary>
/// Bounded single-producer / single-consumer ring buffer. The capture callback writes; the analysis worker
/// copies the most recent window. Old samples are overwritten; memory use never grows.
/// </summary>
public sealed class SampleRingBuffer : ISampleSink
{
    private readonly float[] _buffer;
    private readonly int _mask;
    private long _written;

    public SampleRingBuffer(int minimumCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumCapacity, 2);
        var capacity = 2;
        while (capacity < minimumCapacity)
        {
            capacity <<= 1;
        }

        _buffer = new float[capacity];
        _mask = capacity - 1;
    }

    public int Capacity => _buffer.Length;

    public long TotalWritten => Volatile.Read(ref _written);

    public void Write(ReadOnlySpan<float> monoSamples)
    {
        var position = _written;
        var length = monoSamples.Length;
        if (length == 0)
        {
            return;
        }

        if (length > _buffer.Length)
        {
            var skip = length - _buffer.Length;
            monoSamples = monoSamples[skip..];
            position += skip;
            length = _buffer.Length;
        }

        var start = (int)(position & _mask);
        var first = Math.Min(length, _buffer.Length - start);
        monoSamples[..first].CopyTo(_buffer.AsSpan(start));
        if (first < length)
        {
            monoSamples[first..].CopyTo(_buffer.AsSpan(0));
        }

        Volatile.Write(ref _written, position + length);
    }

    /// <summary>Copies the newest <paramref name="destination"/>.Length samples. Returns false if not enough data yet or the producer overran the read.</summary>
    public bool TryCopyLatest(Span<float> destination)
    {
        var count = destination.Length;
        if (count == 0 || count > _buffer.Length / 2)
        {
            return false;
        }

        var end = Volatile.Read(ref _written);
        if (end < count)
        {
            return false;
        }

        var begin = end - count;
        var start = (int)(begin & _mask);
        var first = Math.Min(count, _buffer.Length - start);
        _buffer.AsSpan(start, first).CopyTo(destination);
        if (first < count)
        {
            _buffer.AsSpan(0, count - first).CopyTo(destination[first..]);
        }

        return Volatile.Read(ref _written) - begin <= _buffer.Length;
    }

    /// <summary>Resets the buffer. Call only while the producer is stopped.</summary>
    public void Clear()
    {
        Array.Clear(_buffer);
        Volatile.Write(ref _written, 0);
    }
}

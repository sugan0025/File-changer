// Download speed and time left for engine downloads, averaged over the last seconds so the numbers shown while
// downloading don't jump with every network hiccup.

namespace Filee.Engines.Infrastructure;

/// <summary>Speed of a transfer computed from "bytes so far" samples over a sliding time window.</summary>
/// <param name="window">How far back the speed is averaged.</param>
/// <param name="settle">How long the measured span must be before a speed is given (default half a second).</param>
public sealed class TransferRate(TimeSpan window, TimeSpan? settle = null)
{
    private readonly List<(TimeSpan Time, long Bytes)> _samples = new();
    private int _head;
    private readonly double _settleSeconds = (settle ?? TimeSpan.FromSeconds(0.5)).TotalSeconds;

    public TransferRate()
        : this(TimeSpan.FromSeconds(5))
    {
    }

    /// <summary>Bytes per second over the window; 0 until enough time has passed to tell.</summary>
    public double BytesPerSecond { get; private set; }

    /// <summary>Records that <paramref name="bytes"/> bytes were transferred in total after <paramref name="elapsed"/>.</summary>
    public void Add(TimeSpan elapsed, long bytes)
    {
        _samples.Add((elapsed, bytes));
        // Keep one sample older than the window as the start of the measured span.
        while (_samples.Count - _head > 2 && elapsed - _samples[_head + 1].Time >= window)
            _head++;
        if (_head > 64)
        {
            _samples.RemoveRange(0, _head);
            _head = 0;
        }
        var (startTime, startBytes) = _samples[_head];
        var seconds = (elapsed - startTime).TotalSeconds;
        BytesPerSecond = seconds >= _settleSeconds && seconds > 0 ? Math.Max(0, bytes - startBytes) / seconds : BytesPerSecond;
    }

    /// <summary>Estimated time until <paramref name="total"/> bytes are reached, or null while the speed is unknown.</summary>
    public TimeSpan? Remaining(long done, long total) =>
        BytesPerSecond > 0 ? TimeSpan.FromSeconds(Math.Max(0, total - done) / BytesPerSecond) : null;
}

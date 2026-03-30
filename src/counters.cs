using System;
using System.Text;
using System.Threading;

enum CounterType { Raw, Memory, Timer }

enum Counter
{
    Worker,
    Block,
    Pixel,
    Sample,
    SampleBounce,
    SampleHit,
    SampleMiss,
    SampleTerminate,
    ShadowRayOccluded,
    ShadowRaySkipped,
    SceneObject,
    SceneBvhNodes,
    SceneBvhDepth,
    SceneTrace,
    SceneOcclude,
    MeshTriangle,
    MeshIntersect,
    ComposeFilterSample,
    OverlayLine,
    OverlayText,
    RtPeakWorkingSet,
    RtGcAllocatedBytes,
    RtGcCurrentBytes,
    RtGcGen0,
    RtGcGen1,
    RtGcGen2,
    TimeSetup,
    TimeRender,
    TimeCompose,
    TimeTotal,

    _Count
}

struct TimerScope : IDisposable
{
    private Counters _counters;
    private Counter _counter;
    private Timestamp _start;

    internal TimerScope(Counters counters, Counter counter)
    {
        _counters = counters;
        _counter = counter;
        _start = Timestamp.Now();
    }

    public void Dispose() => _counters.Bump(_counter, Timestamp.Now() - _start);
}

class Counters
{
    private readonly long[] _data = new long[(int)Counter._Count];
    private readonly ThreadLocal<long[]> _dataLocal = new ThreadLocal<long[]>(() => new long[(int)Counter._Count]);

    public TimerScope TimeScope(Counter c) => new TimerScope(this, c);

    public void Bump(Counter c) => _dataLocal.Value![(int)c]++;
    public void Bump(Counter c, long n) => _dataLocal.Value![(int)c] += n;
    public void Bump(Counter c, Timestamp duration) => _dataLocal.Value![(int)c] += (long)duration.Micros;

    public void Flush()
    {
        if (!_dataLocal.IsValueCreated)
            return;

        long[] local = _dataLocal.Value!;
        for (int i = 0; i < local.Length; ++i)
        {
            if (local[i] != 0)
            {
                Interlocked.Add(ref _data[i], local[i]);
                local[i] = 0;
            }
        }
    }

    private long GetFlushed(Counter c) => Interlocked.Read(ref _data[(int)c]);

    public string Dump()
    {
        Flush();
        FetchRuntimeValues();

        int maxNameLen = 0;
        for (int i = 0; i != (int)Counter._Count; ++i)
        {
            if (GetFlushed((Counter)i) == 0)
                continue;
            maxNameLen = Math.Max(maxNameLen, ((Counter)i).ToString().Length);
        }

        var sb = new StringBuilder();
        for (int i = 0; i != (int)Counter._Count; ++i)
        {
            long value = GetFlushed((Counter)i);
            if (value == 0)
                continue;

            if (sb.Length > 0)
                sb.Append('\n');

            string name = ((Counter)i).ToString();
            sb.Append(name);
            sb.Append(' ', maxNameLen - name.Length);
            sb.Append(": ");
            sb.Append(Type((Counter)i) switch {
                CounterType.Memory => FormatMem(value),
                CounterType.Timer  => $"{value / 1_000_000.0:F2}s",
                _                  => FormatNum(value),
            });
        }

        return sb.ToString();
    }

    private void FetchRuntimeValues()
    {
        Interlocked.Exchange(ref _data[(int)Counter.RtPeakWorkingSet], System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64);
        Interlocked.Exchange(ref _data[(int)Counter.RtGcAllocatedBytes], (long)GC.GetTotalAllocatedBytes());
        Interlocked.Exchange(ref _data[(int)Counter.RtGcCurrentBytes], GC.GetTotalMemory(false));
        Interlocked.Exchange(ref _data[(int)Counter.RtGcGen0], GC.CollectionCount(0));
        Interlocked.Exchange(ref _data[(int)Counter.RtGcGen1], GC.CollectionCount(1));
        Interlocked.Exchange(ref _data[(int)Counter.RtGcGen2], GC.CollectionCount(2));
    }

    private static CounterType Type(Counter c) => c switch {
        Counter.RtPeakWorkingSet or Counter.RtGcAllocatedBytes or Counter.RtGcCurrentBytes => CounterType.Memory,
        Counter.TimeSetup or Counter.TimeRender or Counter.TimeCompose or Counter.TimeTotal => CounterType.Timer,
        _ => CounterType.Raw
    };

    private static string FormatNum(long n)
    {
        if (n < 1_000)
            return n.ToString();
        if (n < 1_000_000)
            return $"{n / 1_000.0:F1}K";
        return $"{n / 1_000_000.0:F1}M";
    }

    private static string FormatMem(long n)
    {
        if (n < 1_024)
            return $"{n}B";
        if (n < 1_024 * 1_024)
            return $"{n / 1_024.0:F1}KiB";
        return $"{n / (1_024.0 * 1_024.0):F1}MiB";
    }
}

using System;
using System.Text;
using System.Threading;

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

    _Count
}

enum Timer
{
    Setup,
    Render,
    Compose,
    Total,
    _Count
}

struct TimerScope : IDisposable
{
    private Counters _counters;
    private Timer _timer;
    private Timestamp _start;

    internal TimerScope(Counters counters, Timer timer)
    {
        _counters = counters;
        _timer = timer;
        _start = Timestamp.Now();
    }

    public void Dispose() => _counters.Bump(_timer, Timestamp.Now() - _start);
}

class Counters
{
    private readonly long[] _data = new long[(int)Counter._Count];
    private readonly ThreadLocal<long[]> _dataLocal = new ThreadLocal<long[]>(() => new long[(int)Counter._Count]);
    private readonly long[] _times = new long[(int)Timer._Count]; // microseconds

    public TimerScope Scope(Timer t) => new TimerScope(this, t);

    public void Bump(Counter c) => _dataLocal.Value![(int)c]++;
    public void Bump(Counter c, long n) => _dataLocal.Value![(int)c] += n;
    public void Bump(Timer t, Timestamp duration) => Interlocked.Add(ref _times[(int)t], (long)duration.Micros);

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
    private long GetFlushed(Timer t) => Interlocked.Read(ref _times[(int)t]);

    public string Dump()
    {
        Flush();

        int maxNameLen = 0;
        for (int i = 0; i != (int)Counter._Count; ++i)
        {
            if (GetFlushed((Counter)i) == 0)
                continue;
            maxNameLen = Math.Max(maxNameLen, ((Counter)i).ToString().Length);
        }
        for (int i = 0; i != (int)Timer._Count; ++i)
        {
            if (GetFlushed((Timer)i) == 0)
                continue;
            maxNameLen = Math.Max(maxNameLen, ((Timer)i).ToString().Length);
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
            sb.Append(FormatNum(value));
        }

        for (int i = 0; i != (int)Timer._Count; ++i)
        {
            long micros = GetFlushed((Timer)i);
            if (micros == 0)
                continue;

            string name = ((Timer)i).ToString();
            sb.Append('\n');
            sb.Append(name);
            sb.Append(' ', maxNameLen - name.Length);
            sb.Append(": ");
            sb.Append($"{micros / 1_000_000.0:F2}s");
        }

        return sb.ToString();
    }

    private static string FormatNum(long n)
    {
        if (n < 1_000)
            return n.ToString();
        if (n < 1_000_000)
            return $"{n / 1_000.0:F1}K";
        return $"{n / 1_000_000.0:F1}M";
    }
}

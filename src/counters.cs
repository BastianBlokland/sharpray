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
    Composite,
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

    public void Dispose() => _counters.Set(_timer, Timestamp.Now() - _start);
}

class Counters
{
    private readonly ThreadLocal<long[]> _data = new ThreadLocal<long[]>(() => new long[(int)Counter._Count], trackAllValues: true);
    private readonly double[] _times = new double[(int)Timer._Count];

    public void Bump(Counter c) => _data.Value![(int)c]++;
    public void Bump(Counter c, long n) => _data.Value![(int)c] += n;

    public TimerScope Scope(Timer t) => new TimerScope(this, t);
    public void Set(Timer t, Timestamp duration) => _times[(int)t] = duration.Seconds;

    public string Dump()
    {
        Span<long> counters = stackalloc long[(int)Counter._Count];
        CollectCounters(counters);

        int maxNameLen = 0;
        for (int i = 0; i != (int)Counter._Count; ++i)
        {
            maxNameLen = Math.Max(maxNameLen, ((Counter)i).ToString().Length);
        }
        for (int i = 0; i != (int)Timer._Count; ++i)
        {
            if (_times[i] == 0)
                continue;
            maxNameLen = Math.Max(maxNameLen, ((Timer)i).ToString().Length);
        }

        var sb = new StringBuilder();
        for (int i = 0; i != (int)Counter._Count; ++i)
        {
            if (sb.Length > 0)
                sb.Append('\n');

            string name = ((Counter)i).ToString();
            sb.Append(name);
            sb.Append(' ', maxNameLen - name.Length);
            sb.Append(": ");
            sb.Append(FormatNum(counters[i]));
        }

        for (int i = 0; i != (int)Timer._Count; ++i)
        {
            if (_times[i] == 0)
                continue;

            string name = ((Timer)i).ToString();
            sb.Append('\n');
            sb.Append(name);
            sb.Append(' ', maxNameLen - name.Length);
            sb.Append(": ");
            sb.Append($"{_times[i]:F2}s");
        }

        return sb.ToString();
    }

    private void CollectCounters(Span<long> totals)
    {
        foreach (long[] local in _data.Values)
        {
            for (int i = 0; i < local.Length; ++i)
                totals[i] += local[i];
        }
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

using System;
using System.Text;
using System.Threading;

struct TimeScope : IDisposable
{
    private Counters _counters;
    private Counters.Type _type;
    private Timestamp _start;

    internal TimeScope(Counters counters, Counters.Type type)
    {
        _counters = counters;
        _type = type;
        _start = Timestamp.Now();
    }

    public void Dispose() => _counters.Bump(_type, Timestamp.Now() - _start);
}

class Counters
{
    public enum Type
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

    private enum Category { Raw, Memory, Time }

    private readonly long[] _data = new long[(int)Type._Count];
    private readonly ThreadLocal<long[]> _dataLocal = new ThreadLocal<long[]>(() => new long[(int)Type._Count]);

    public TimeScope TimeScope(Type c) => new TimeScope(this, c);

    public void Bump(Type c) => _dataLocal.Value![(int)c]++;
    public void Bump(Type c, long n) => _dataLocal.Value![(int)c] += n;
    public void Bump(Type c, Timestamp duration) => _dataLocal.Value![(int)c] += (long)duration.Micros;

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

    public string Dump()
    {
        Flush();
        FetchRuntimeValues();

        int maxNameLen = 0;
        for (int i = 0; i != (int)Type._Count; ++i)
        {
            if (GetFlushed((Type)i) == 0)
                continue;
            maxNameLen = Math.Max(maxNameLen, ((Type)i).ToString().Length);
        }

        var sb = new StringBuilder();
        for (int i = 0; i != (int)Type._Count; ++i)
        {
            long value = GetFlushed((Type)i);
            if (value == 0)
                continue;

            if (sb.Length > 0)
                sb.Append('\n');

            string name = ((Type)i).ToString();
            sb.Append(name);
            sb.Append(' ', maxNameLen - name.Length);
            sb.Append(": ");
            sb.Append(GetCategory((Type)i) switch
            {
                Category.Memory => FormatMem(value),
                Category.Time => FormatTime(value),
                _ => FormatNum(value),
            });
        }

        return sb.ToString();
    }

    private void FetchRuntimeValues()
    {
        Interlocked.Exchange(ref _data[(int)Type.RtPeakWorkingSet], System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64);
        Interlocked.Exchange(ref _data[(int)Type.RtGcAllocatedBytes], (long)GC.GetTotalAllocatedBytes());
        Interlocked.Exchange(ref _data[(int)Type.RtGcCurrentBytes], GC.GetTotalMemory(false));
        Interlocked.Exchange(ref _data[(int)Type.RtGcGen0], GC.CollectionCount(0));
        Interlocked.Exchange(ref _data[(int)Type.RtGcGen1], GC.CollectionCount(1));
        Interlocked.Exchange(ref _data[(int)Type.RtGcGen2], GC.CollectionCount(2));
    }

    private long GetFlushed(Type c) => Interlocked.Read(ref _data[(int)c]);

    private static Category GetCategory(Type c) => c switch
    {
        Type.RtPeakWorkingSet or Type.RtGcAllocatedBytes or Type.RtGcCurrentBytes => Category.Memory,
        Type.TimeSetup or Type.TimeRender or Type.TimeCompose or Type.TimeTotal => Category.Time,
        _ => Category.Raw
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

    private static string FormatTime(long micros) => $"{micros / 1_000_000.0:F2}s";
}

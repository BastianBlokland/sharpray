using System;
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

    public Timestamp Elapsed => Timestamp.Now() - _start;

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
        TimeMeshBvhBuild,
        TimeSceneBvhBuild,
        TimeSetup,
        TimeRender,
        TimeCompose,
        TimeTotal,

        _Count
    }

    private enum Category { Raw, Memory, Time }

    private readonly long[] _data = new long[(int)Type._Count];
    private readonly ThreadLocal<long[]> _dataLocal = new ThreadLocal<long[]>(() => new long[(int)Type._Count]);

    private static readonly string[] _typeNames = Enum.GetNames<Type>();

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

    public void Dump(ref FormatWriter fmt)
    {
        Flush();
        FetchRuntimeValues();

        int nameLenMax = 0;
        for (int i = 0; i != (int)Type._Count; ++i)
        {
            if (GetFlushed((Type)i) != 0)
                nameLenMax = Math.Max(nameLenMax, _typeNames[i].Length);
        }

        for (int i = 0; i != (int)Type._Count; ++i)
        {
            long value = GetFlushed((Type)i);
            if (value == 0)
                continue;

            fmt.Separate(0);
            string name = _typeNames[i].PadRight(nameLenMax);
            switch (GetCategory((Type)i))
            {
                case Category.Memory: fmt.WriteLine($"{name}: {new FormatMem(value)}"); break;
                case Category.Time: fmt.WriteLine($"{name}: {Timestamp.FromMicros(value)}"); break;
                default: fmt.WriteLine($"{name}: {new FormatNum(value)}"); break;
            }
        }
    }

    private long GetFlushed(Type c) => Interlocked.Read(ref _data[(int)c]);

    private void FetchRuntimeValues()
    {
        Interlocked.Exchange(ref _data[(int)Type.RtPeakWorkingSet], System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64);
        Interlocked.Exchange(ref _data[(int)Type.RtGcAllocatedBytes], (long)GC.GetTotalAllocatedBytes());
        Interlocked.Exchange(ref _data[(int)Type.RtGcCurrentBytes], GC.GetTotalMemory(false));
        Interlocked.Exchange(ref _data[(int)Type.RtGcGen0], GC.CollectionCount(0));
        Interlocked.Exchange(ref _data[(int)Type.RtGcGen1], GC.CollectionCount(1));
        Interlocked.Exchange(ref _data[(int)Type.RtGcGen2], GC.CollectionCount(2));
    }

    private static Category GetCategory(Type c) => c switch
    {
        Type.RtPeakWorkingSet or Type.RtGcAllocatedBytes or Type.RtGcCurrentBytes => Category.Memory,
        Type.TimeMeshBvhBuild or Type.TimeSceneBvhBuild or Type.TimeSetup or Type.TimeRender or Type.TimeCompose or Type.TimeTotal => Category.Time,
        _ => Category.Raw
    };
}

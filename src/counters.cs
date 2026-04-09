using System;
using System.Threading;

readonly struct TimeScope : IDisposable
{
    private readonly Counters _counters;
    private readonly Counters.Type _type;
    private readonly Timestamp _start;

    internal TimeScope(Counters counters, Counters.Type type)
    {
        _counters = counters;
        _type = type;
        _start = Timestamp.Now();
    }

    public Timestamp Elapsed => Timestamp.Now() - _start;

    public void Dispose() => _counters.Bump(_type, Timestamp.Now() - _start);
}

class Counters : IDescribable
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
        SampleFogScatter,
        SceneObject,
        SceneBvhNodes,
        SceneBvhDepth,
        SceneTrace,
        SceneOcclude,
        MeshTriangle,
        MeshIntersect,
        MeshIntersectAny,
        BvhIntersectNode,
        BvhIntersectShape,
        BvhOverlapNode,
        BvhOverlapShape,
        FilterSample,
        DenoiseEarlyOut,
        DenoiseWeightMax,
        DenoiseMaxLum,
        DenoiseMaxLumBoost,
        DenoiseRejectMissing,
        DenoiseRejectNormal,
        DenoiseRejectDepth,
        OverlayLine,
        OverlayText,
        RtPeakWorkingSet,
        RtGcAllocatedBytes,
        RtGcCurrentBytes,
        RtGcGen0,
        RtGcGen1,
        RtGcGen2,
        TimeSetup,
        TimeObjLoad,
        TimeMeshBvhBuild,
        TimeSceneBvhBuild,
        TimeRender,
        TimeCompose,
        TimeTotal,

        _Count
    }

    private enum Reduction { Sum, Min, Max }
    private enum Category { Raw, Memory, Time, Decimal }

    private const float _decimalFactor = 1e4f;

    private static readonly Reduction[] _reductions = BuildReductions();
    private static readonly string[] _typeNames = Enum.GetNames<Type>();

    private readonly long[] _data = BuildStorage();
    private readonly ThreadLocal<long[]> _dataLocal = new ThreadLocal<long[]>(BuildStorage);

    public TimeScope TimeScope(Type c) => new TimeScope(this, c);

    public void Bump(Type c) => _dataLocal.Value![(int)c]++;
    public void Bump(Type c, long n) => _dataLocal.Value![(int)c] += n;
    public void Bump(Type c, Timestamp duration) => _dataLocal.Value![(int)c] += (long)duration.Micros;

    public void BumpMax(Type c, float value) => BumpMax(_dataLocal.Value!, c, value);

    // Directly retrieve the thread-local data array for direct access in hot loops.
    // Caller must not cache this across threads.
    public long[] GetLocalData() => _dataLocal.Value!;

    public void Flush()
    {
        if (!_dataLocal.IsValueCreated)
            return;

        long[] local = _dataLocal.Value!;
        for (int i = 0; i < local.Length; ++i)
        {
            long localVal = local[i];
            long initial = InitialValue(_reductions[i]);
            if (localVal == initial)
                continue;

            switch (_reductions[i])
            {
                case Reduction.Sum:
                    Interlocked.Add(ref _data[i], localVal);
                    break;
                case Reduction.Max:
                    {
                        long current = Interlocked.Read(ref _data[i]);
                        while (localVal > current)
                        {
                            long prev = Interlocked.CompareExchange(ref _data[i], localVal, current);
                            if (prev == current)
                                break;
                            current = prev;
                        }
                        break;
                    }
                case Reduction.Min:
                    {
                        long current = Interlocked.Read(ref _data[i]);
                        while (localVal < current)
                        {
                            long prev = Interlocked.CompareExchange(ref _data[i], localVal, current);
                            if (prev == current)
                                break;
                            current = prev;
                        }
                        break;
                    }
            }
            local[i] = initial; // Reset to initial value for next flush.
        }
    }

    public void Describe(FormatWriter fmt)
    {
        Flush();
        FetchRuntimeValues();

        int nameLenMax = 0;
        for (int i = 0; i != (int)Type._Count; ++i)
        {
            if (IsSet((Type)i))
                nameLenMax = Math.Max(nameLenMax, _typeNames[i].Length);
        }

        for (int i = 0; i != (int)Type._Count; ++i)
        {
            if (!IsSet((Type)i))
                continue;

            long value = GetFlushed((Type)i);
            fmt.Separate(0);
            string name = _typeNames[i].PadRight(nameLenMax);
            switch (GetCategory((Type)i))
            {
                case Category.Memory: fmt.WriteLine($"{name}: {new FormatMem(value)}"); break;
                case Category.Time: fmt.WriteLine($"{name}: {Timestamp.FromMicros(value)}"); break;
                case Category.Decimal: fmt.WriteLine($"{name}: {value / _decimalFactor:F4}"); break;
                default: fmt.WriteLine($"{name}: {new FormatNum(value)}"); break;
            }
        }
    }

    private long GetFlushed(Type c) => Interlocked.Read(ref _data[(int)c]);
    private bool IsSet(Type c) => GetFlushed(c) != InitialValue(_reductions[(int)c]);

    private void FetchRuntimeValues()
    {
        Interlocked.Exchange(ref _data[(int)Type.RtPeakWorkingSet], System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64);
        Interlocked.Exchange(ref _data[(int)Type.RtGcAllocatedBytes], (long)GC.GetTotalAllocatedBytes());
        Interlocked.Exchange(ref _data[(int)Type.RtGcCurrentBytes], GC.GetTotalMemory(false));
        Interlocked.Exchange(ref _data[(int)Type.RtGcGen0], GC.CollectionCount(0));
        Interlocked.Exchange(ref _data[(int)Type.RtGcGen1], GC.CollectionCount(1));
        Interlocked.Exchange(ref _data[(int)Type.RtGcGen2], GC.CollectionCount(2));
    }

    public static void BumpMax(long[] localData, Type c, float value)
    {
        long scaled = (long)(value * _decimalFactor);
        if (scaled > localData[(int)c])
            localData[(int)c] = scaled;
    }

    private static long InitialValue(Reduction reduction) => reduction switch
    {
        Reduction.Max => long.MinValue,
        Reduction.Min => long.MaxValue,
        _ => 0L
    };

    private static Reduction[] BuildReductions()
    {
        Reduction[] r = new Reduction[(int)Type._Count]; // Default: Sum.
        r[(int)Type.DenoiseWeightMax] = Reduction.Max;
        r[(int)Type.DenoiseMaxLum] = Reduction.Max;
        r[(int)Type.DenoiseMaxLumBoost] = Reduction.Max;
        return r;
    }

    private static long[] BuildStorage()
    {
        long[] d = new long[(int)Type._Count];
        for (int i = 0; i < d.Length; ++i)
        {
            d[i] = InitialValue(_reductions[i]);
        }
        return d;
    }

    private static Category GetCategory(Type c)
    {
        switch (c)
        {
            case Type.RtPeakWorkingSet:
            case Type.RtGcAllocatedBytes:
            case Type.RtGcCurrentBytes:
                return Category.Memory;
            case Type.TimeObjLoad:
            case Type.TimeMeshBvhBuild:
            case Type.TimeSceneBvhBuild:
            case Type.TimeSetup:
            case Type.TimeRender:
            case Type.TimeCompose:
            case Type.TimeTotal:
                return Category.Time;
            case Type.DenoiseWeightMax:
            case Type.DenoiseMaxLum:
            case Type.DenoiseMaxLumBoost:
                return Category.Decimal;
            default:
                return Category.Raw;
        }
    }
}

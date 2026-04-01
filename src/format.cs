using System;
using System.Globalization;

readonly struct FormatNum(long n) : ISpanFormattable
{
    public string ToString(string? format, IFormatProvider? provider) => FormatUtils.FormatNum(n);

    public override string ToString() => FormatUtils.FormatNum(n);

    public bool TryFormat(Span<char> dest, out int written, ReadOnlySpan<char> format, IFormatProvider? provider) =>
        FormatUtils.FormatNum(dest, out written, n);
}

readonly struct FormatMem(long n) : ISpanFormattable
{
    public string ToString(string? format, IFormatProvider? provider) => FormatUtils.FormatMem(n);

    public override string ToString() => FormatUtils.FormatMem(n);

    public bool TryFormat(Span<char> dest, out int written, ReadOnlySpan<char> format, IFormatProvider? provider) =>
        FormatUtils.FormatMem(dest, out written, n);
}

static class FormatUtils
{
    public static string FormatNum(long n)
    {
        Span<char> buf = stackalloc char[32];
        FormatNum(buf, out int written, n);
        return new string(buf[..written]);
    }

    public static bool FormatNum(Span<char> dst, out int written, long n)
    {
        written = 0;

        double scaled;
        string suffix;
        string fmt;
        if (n < 1_000L)
        {
            scaled = n; suffix = ""; fmt = "F0";
        }
        else if (n < 1_000_000L)
        {
            scaled = n / 1_000.0; suffix = "K"; fmt = "F1";
        }
        else if (n < 1_000_000_000L)
        {
            scaled = n / 1_000_000.0; suffix = "M"; fmt = "F1";
        }
        else
        {
            scaled = n / 1_000_000_000.0; suffix = "B"; fmt = "F2";
        }
        return scaled.TryFormat(dst, out written, fmt, CultureInfo.InvariantCulture)
            && Push(dst, ref written, suffix);
    }

    public static string FormatMem(long n)
    {
        Span<char> buf = stackalloc char[32];
        FormatMem(buf, out int written, n);
        return new string(buf[..written]);
    }

    public static bool FormatMem(Span<char> dst, out int written, long n)
    {
        written = 0;

        double scaled;
        string suffix;
        string fmt;
        if (n < 1_024L)
        {
            scaled = n; suffix = "B"; fmt = "F0";
        }
        else if (n < 1_024L * 1_024L)
        {
            scaled = n / 1_024.0; suffix = "KiB"; fmt = "F1";
        }
        else if (n < 1_024L * 1_024L * 1_024L)
        {
            scaled = n / (1_024.0 * 1_024.0); suffix = "MiB"; fmt = "F1";
        }
        else
        {
            scaled = n / (1_024.0 * 1_024.0 * 1_024.0); suffix = "GiB"; fmt = "F2";
        }
        return PushFormatted(dst, ref written, scaled, fmt) && Push(dst, ref written, suffix);
    }

    public static string FormatTime(double micros)
    {
        Span<char> buf = stackalloc char[32];
        FormatTime(buf, out int written, micros);
        return new string(buf[..written]);
    }

    public static bool FormatTime(Span<char> dst, out int written, double micros)
    {
        const double usPerMin = 60_000_000.0;
        const double usPerHour = 3_600_000_000.0;

        written = 0;
        if (micros >= usPerHour)
        {
            long hours = (long)(micros / usPerHour);
            long mins = (long)((micros % usPerHour) / usPerMin);
            return PushFormatted(dst, ref written, hours)
                && Push(dst, ref written, "h ")
                && PushFormatted(dst, ref written, mins)
                && Push(dst, ref written, "m");
        }
        if (micros >= usPerMin)
        {
            long mins = (long)(micros / usPerMin);
            double secs = (micros % usPerMin) / 1_000_000.0;
            return PushFormatted(dst, ref written, mins)
                && Push(dst, ref written, "m ")
                && PushFormatted(dst, ref written, secs, "F0")
                && Push(dst, ref written, "s");
        }
        return PushFormatted(dst, ref written, micros / 1_000_000.0, "F2") && Push(dst, ref written, "s");
    }

    public static string FormatSet<T>(Span<T> values, ReadOnlySpan<char> format = default)
        where T : ISpanFormattable
    {
        Span<char> buf = stackalloc char[128];
        FormatSet(buf, out int written, values, format);
        return new string(buf[..written]);
    }

    public static bool FormatSet<T>(Span<char> dst, out int written, Span<T> values, ReadOnlySpan<char> format = default)
        where T : ISpanFormattable
    {
        written = 0;
        if (!Push(dst, ref written, "("))
            return false;

        for (int i = 0; i != values.Length; ++i)
        {
            if (i > 0 && !Push(dst, ref written, ", "))
                return false;
            if (!PushFormatted(dst, ref written, values[i], format))
                return false;
        }
        return Push(dst, ref written, ")");
    }

    private static bool Push(Span<char> dst, ref int pos, ReadOnlySpan<char> src)
    {
        if (pos + src.Length > dst.Length)
            return false;
        src.CopyTo(dst[pos..]);
        pos += src.Length;
        return true;
    }

    private static bool PushFormatted<T>(Span<char> dst, ref int pos, T value, ReadOnlySpan<char> fmt = default)
        where T : ISpanFormattable
    {
        if (!value.TryFormat(dst[pos..], out int len, fmt, CultureInfo.InvariantCulture))
            return false;
        pos += len;
        return true;
    }
}

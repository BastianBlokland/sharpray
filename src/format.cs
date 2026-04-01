using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

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

struct FormatWriter
{
    [InterpolatedStringHandler]
    public struct Formatter
    {
        private StringBuilder _sb;

        public Formatter(int literalLength, int formattedCount, FormatWriter writer)
        {
            _sb = writer._sb;
            _sb.Append(' ', writer.Indent * 2);
        }

        public void AppendLiteral(string s) => _sb.Append(s);

        public void AppendFormatted<T>(T value)
        {
            if (value is ISpanFormattable spanFormattable)
            {
                Span<char> buffer = stackalloc char[64];
                spanFormattable.TryFormat(buffer, out int written, default, CultureInfo.InvariantCulture);
                _sb.Append(buffer[..written]);
            }
            else
            {
                _sb.Append(value);
            }
        }

        public void AppendFormatted<T>(T value, string format) where T : IFormattable
        {
            if (value is ISpanFormattable spanFormattable)
            {
                Span<char> buffer = stackalloc char[64];
                spanFormattable.TryFormat(buffer, out int written, format, CultureInfo.InvariantCulture);
                _sb.Append(buffer[..written]);
            }
            else
            {
                _sb.Append(value.ToString(format, CultureInfo.InvariantCulture));
            }
        }

        public void AppendFormatted<T>(T value, int alignment)
        {
            Span<char> buffer = stackalloc char[64];
            int length = 0;
            if (value is ISpanFormattable spanFormattable)
            {
                spanFormattable.TryFormat(buffer, out length, default, CultureInfo.InvariantCulture);
            }
            else if (value != null)
            {
                string? valueStr = value.ToString();
                if (valueStr is string str)
                {
                    str.CopyTo(buffer);
                    length = str.Length;
                }
            }
            int pad = Math.Abs(alignment) - length;
            if (alignment > 0)
                _sb.Append(' ', Math.Max(0, pad));
            _sb.Append(buffer[..length]);
            if (alignment < 0)
                _sb.Append(' ', Math.Max(0, pad));
        }
    }

    public int Indent { get; private set; }

    private StringBuilder _sb;

    public FormatWriter()
    {
        Indent = 0;
        _sb = new StringBuilder();
    }

    public FormatWriter(int indent)
    {
        Indent = indent;
        _sb = new StringBuilder();
    }

    public void Clear()
    {
        Indent = 0;
        _sb.Clear();
    }

    public void IndentPush() => ++Indent;
    public void IndentPop()
    {
        Debug.Assert(Indent > 0, "Indent underflow");
        --Indent;
    }

    public void Write(string text) => _sb.Append(text);
    public void Write([InterpolatedStringHandlerArgument("")] ref Formatter f) { }

    public void WriteLine(string text) => _sb.AppendLine(new string(' ', Indent * 2) + text);
    public void WriteLine([InterpolatedStringHandlerArgument("")] ref Formatter f) => _sb.AppendLine();

    public void EndLine() => _sb.AppendLine();

    public void Separate(int lines = 1)
    {
        int remainingNewlines = lines + ((_sb.Length == 0) ? 0 : 1);
        for (int i = _sb.Length; i-- != 0;)
        {
            switch (_sb[i])
            {
                case '\r':
                    continue;
                case '\n':
                    if (--remainingNewlines == 0)
                        return;
                    continue;
            }
            break;
        }
        while (remainingNewlines-- != 0)
            _sb.AppendLine();
    }

    public override string ToString() => _sb.ToString();
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

using System.Globalization;
using System.Text;
using Serilog.Events;

namespace MagenticWorkflowApp.Services;

public sealed class SerilogTeeTextWriter : TextWriter
{
    [ThreadStatic] private static bool reentrant;

    private readonly TextWriter inner;
    private readonly Serilog.ILogger sink;
    private readonly LogEventLevel level;
    private readonly StringBuilder buffer = new();
    private readonly object syncRoot = new();

    public SerilogTeeTextWriter(TextWriter inner, Serilog.ILogger sink, LogEventLevel level = LogEventLevel.Information)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        this.level = level;
    }

    public override Encoding Encoding => inner.Encoding;
    public override IFormatProvider FormatProvider => CultureInfo.InvariantCulture;

    public override void Write(char value)
    {
        inner.Write(value);
        lock (syncRoot)
        {
            if (value == '\n') FlushLineLocked();
            else if (value != '\r') buffer.Append(value);
        }
    }

    public override void Write(string? value)
    {
        inner.Write(value);
        if (string.IsNullOrEmpty(value)) return;
        lock (syncRoot)
        {
            foreach (var ch in value)
            {
                if (ch == '\n') FlushLineLocked();
                else if (ch != '\r') buffer.Append(ch);
            }
        }
    }

    public override void WriteLine(string? value)
    {
        inner.WriteLine(value);
        lock (syncRoot)
        {
            if (!string.IsNullOrEmpty(value)) buffer.Append(value);
            FlushLineLocked();
        }
    }

    public override void WriteLine() { WriteLine(string.Empty); }

    public override void Flush()
    {
        inner.Flush();
        lock (syncRoot)
        {
            if (buffer.Length > 0) FlushLineLocked();
        }
    }

    private void FlushLineLocked()
    {
        if (buffer.Length == 0) return;
        var line = buffer.ToString();
        buffer.Clear();
        if (reentrant) return;
        if (LooksLikeAnsi(line)) line = StripAnsi(line);
        if (string.IsNullOrWhiteSpace(line)) return;
        reentrant = true;
        try { sink.Write(level, "{ConsoleLine}", line); }
        finally { reentrant = false; }
    }

    private static bool LooksLikeAnsi(string s) => s.IndexOf('\x1b') >= 0;

    private static string StripAnsi(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\x1b' && i + 1 < s.Length && s[i + 1] == '[')
            {
                i += 2;
                while (i < s.Length && !(s[i] >= '@' && s[i] <= '~')) i++;
            }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }
}

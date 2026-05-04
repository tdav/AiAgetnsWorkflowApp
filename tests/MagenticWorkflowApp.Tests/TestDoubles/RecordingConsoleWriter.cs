using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MagenticWorkflowApp.Interfaces;

namespace MagenticWorkflowApp.Tests.TestDoubles;

public sealed class RecordingConsoleWriter : IConsoleWriter
{
    private readonly StringBuilder buffer = new();
    private readonly List<(string Text, ConsoleColor? Color)> entries = new();
    private readonly object syncRoot = new();

    public IReadOnlyList<(string Text, ConsoleColor? Color)> Entries
    {
        get { lock (syncRoot) { return entries.ToList(); } }
    }

    public string AllText
    {
        get { lock (syncRoot) { return buffer.ToString(); } }
    }

    public void Write(string text)
    {
        lock (syncRoot)
        {
            buffer.Append(text);
            entries.Add((text, null));
        }
    }

    public void WriteLine(string text)
    {
        lock (syncRoot)
        {
            buffer.AppendLine(text);
            entries.Add((text + Environment.NewLine, null));
        }
    }

    public void WriteWithColor(string text, ConsoleColor color)
    {
        lock (syncRoot)
        {
            buffer.Append(text);
            entries.Add((text, color));
        }
    }

    public void WriteLineWithColor(string text, ConsoleColor color)
    {
        lock (syncRoot)
        {
            buffer.AppendLine(text);
            entries.Add((text + Environment.NewLine, color));
        }
    }
}

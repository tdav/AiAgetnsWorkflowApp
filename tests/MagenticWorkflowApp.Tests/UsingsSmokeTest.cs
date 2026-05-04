using System;
using Xunit;

namespace MagenticWorkflowApp.Tests;

public class UsingsSmokeTest
{
    [Fact]
    public void TestProjectCompilesAndDiscoversTests()
    {
        Assert.True(true);
    }

    [Fact]
    public void RecordingConsoleWriter_CapturesText()
    {
        var w = new TestDoubles.RecordingConsoleWriter();
        w.Write("hello ");
        w.WriteWithColor("world", ConsoleColor.Cyan);
        Assert.Equal("hello world", w.AllText);
        Assert.Equal(2, w.Entries.Count);
        Assert.Equal(ConsoleColor.Cyan, w.Entries[1].Color);
    }
}

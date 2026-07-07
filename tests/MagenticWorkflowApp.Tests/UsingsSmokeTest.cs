using System;

namespace MagenticWorkflowApp.Tests;

public class UsingsSmokeTest
{
    [Test]
    public void TestProjectCompilesAndDiscoversTests()
    {
        true.Should().BeTrue();
    }

    [Test]
    public void RecordingConsoleWriter_CapturesText()
    {
        var w = new TestDoubles.RecordingConsoleWriter();
        w.Write("hello ");
        w.WriteWithColor("world", ConsoleColor.Cyan);
        w.AllText.Should().Be("hello world");
        w.Entries.Should().HaveCount(2);
        w.Entries[1].Color.Should().Be(ConsoleColor.Cyan);
    }
}

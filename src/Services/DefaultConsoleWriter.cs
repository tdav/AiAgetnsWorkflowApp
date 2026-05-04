using MagenticWorkflowApp.Interfaces;

namespace MagenticWorkflowApp.Services;

public sealed class DefaultConsoleWriter : IConsoleWriter
{
    private readonly object syncRoot = new();

    public void Write(string text)
    {
        try { Console.Write(text); }
        catch (IOException) { /* closed stream — ignore */ }
    }

    public void WriteLine(string text)
    {
        try { Console.WriteLine(text); }
        catch (IOException) { /* closed stream — ignore */ }
    }

    public void WriteWithColor(string text, ConsoleColor color)
    {
        lock (syncRoot)
        {
            try
            {
                Console.ForegroundColor = color;
                Console.Write(text);
            }
            catch (IOException) { }
            finally { Console.ResetColor(); }
        }
    }

    public void WriteLineWithColor(string text, ConsoleColor color)
    {
        lock (syncRoot)
        {
            try
            {
                Console.ForegroundColor = color;
                Console.WriteLine(text);
            }
            catch (IOException) { }
            finally { Console.ResetColor(); }
        }
    }
}

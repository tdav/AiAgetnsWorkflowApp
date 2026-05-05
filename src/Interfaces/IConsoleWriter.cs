namespace MagenticWorkflowApp.Interfaces;

public interface IConsoleWriter
{
    void Write(string text);
    void WriteLine(string text);
    void WriteWithColor(string text, ConsoleColor color);
    void WriteLineWithColor(string text, ConsoleColor color);
}

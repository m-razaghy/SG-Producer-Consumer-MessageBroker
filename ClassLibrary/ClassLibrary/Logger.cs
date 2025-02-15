public enum LogLevel
{
    Info,
    Warning,
    Error
}

public class Logger
{
    private readonly string _logFilePath;
    private static readonly object _lock = new object();

    public Logger(string logFilePath = "")
    {
        _logFilePath = logFilePath;
    }

    public void Log(LogLevel level, string message)
    {
        lock (_lock)
        {
            string dateTime = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss:FFFFFFF} ";

            Console.Write(dateTime);

            Console.ForegroundColor = level switch
            {
                LogLevel.Info => ConsoleColor.Blue,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                _ => ConsoleColor.White
            };

            Console.Write($"{level} ");
            Console.ResetColor();

            Console.WriteLine(message);

            string logMessage = $"{dateTime} [{level}] {message}";
            if (!string.IsNullOrEmpty(_logFilePath))
            {
                File.AppendAllText(_logFilePath, logMessage + Environment.NewLine);
            }
        }
    }
}
using System.Reflection;

public class Logger
{
    private static readonly object _lock = new object();
    private readonly int _daysToKeep;
    private readonly bool _isLogDisabled;
    private readonly int _maxLengthMessage;

    private const string LogDirectoryName = "Logs";
    private const string LogFilePrefix = "HASFOB";

    public Logger(Configuration config)
    {
        _daysToKeep = config.LogRetentionDays;
        _isLogDisabled = config.IsLogDisabled;

        _maxLengthMessage = config.MaxLengthLogMessage > 0 ? config.MaxLengthLogMessage : 200;
    }

    public void WriteLog(string message)
    {
        lock (_lock)
        {
            try
            {
                DelOldLogs();

                if (_isLogDisabled || string.IsNullOrWhiteSpace(message))
                    return;

                if (message.Length > _maxLengthMessage)
                    message = message.Substring(0, _maxLengthMessage);

                string logDir = GetLogDirectory();
                string logFilePath = GetCurrentLogFilePath(logDir);
                string logEntry = $"{DateTime.Now:HH:mm:ss} {message}";

                File.AppendAllText(logFilePath, logEntry + Environment.NewLine);
            }
            catch { }
        }
    }

    public void DelOldLogs()
    {
        try
        {
            string logDir = GetLogDirectory();
            if (!Directory.Exists(logDir))
                return;

            DateTime cutoffDate = DateTime.Now.AddDays(-_daysToKeep);

            var oldFiles = Directory.GetFiles(logDir, "*.log")
                .Where(file =>
                {
                    try
                    {
                        return new FileInfo(file).LastWriteTime < cutoffDate;
                    }
                    catch
                    {
                        return false;
                    }
                });

            foreach (string file in oldFiles)
            {
                try
                {
                    File.Delete(file);
                }
                catch { }
            }
        }
        catch { }
    }

    private string GetLogDirectory()
    {
        string exePath = Assembly.GetExecutingAssembly().Location;
        string exeDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
        string logDir = Path.Combine(exeDir, LogDirectoryName);

        if (!Directory.Exists(logDir))
            Directory.CreateDirectory(logDir);

        return logDir;
    }

    private string GetCurrentLogFilePath(string logDirectory)
    {
        string fileName = $"{DateTime.Now:dd_MM_yyyy}_{LogFilePrefix}.log";
        return Path.Combine(logDirectory, fileName);
    }
}
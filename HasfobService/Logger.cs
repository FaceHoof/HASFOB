using System.Reflection;

public class Logger
{
    private static readonly object _lock = new object();
    private readonly int _daysToKeep;
    const int maxLengthMessage = 200;
    private const string LogDirectoryName = "Logs";
    private const string LogFilePrefix = "HASFOB";

    public Logger(Configuration config)
    {
        _daysToKeep = config.LogRetentionDays;
    }

    public void WriteLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        lock (_lock)
        {
            try
            {
                if (message.Length > maxLengthMessage)
                    message = message.Substring(0, maxLengthMessage);

                string logDir = GetLogDirectory();
                string logFilePath = GetCurrentLogFilePath(logDir);
                string logEntry = $"{DateTime.Now:HH:mm:ss} {message}";

                File.AppendAllText(logFilePath, logEntry + Environment.NewLine);
                DelOldLogs();
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
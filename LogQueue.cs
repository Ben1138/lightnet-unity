using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

public enum ELogType
{
    Info,
    Warning,
    Error
}

public struct LogMessage
{
    public ELogType Type;
    public string Message;
}

public static class LogQueue
{
    static ConcurrentQueue<LogMessage> Logs = new ConcurrentQueue<LogMessage>();

    static void Log(ELogType type, string format, object[] args = null, [CallerFilePathAttribute] string caller = null, [CallerLineNumber] int lineNumber = 0)
    {
        caller = System.IO.Path.GetFileName(caller);
        args = args == null ? new object[0] : args;
        LogMessage msg;
        msg.Type = type;
        msg.Message = string.Format("[{0}:{1}]", caller, lineNumber) + string.Format(format, args);
        Logs.Enqueue(msg);
    }

    public static bool GetNext(out LogMessage nextLogMessage)
    {
        return Logs.TryDequeue(out nextLogMessage);
    }

    public static void LogInfo(string format, object[] args = null, [CallerFilePathAttribute] string caller = null, [CallerLineNumber] int lineNumber = 0)
    {
        Log(ELogType.Info, format, args, caller, lineNumber);
    }

    public static void LogWarning(string format, object[] args = null, [CallerFilePathAttribute] string caller = null, [CallerLineNumber] int lineNumber = 0)
    {
        Log(ELogType.Warning, format, args, caller, lineNumber);
    }

    public static void LogError(string format, object[] args = null, [CallerFilePathAttribute] string caller = null, [CallerLineNumber] int lineNumber = 0)
    {
        Log(ELogType.Error, format, args, caller, lineNumber);
    }
}
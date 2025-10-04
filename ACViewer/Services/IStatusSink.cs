using System;

namespace ACViewer.Services
{
    public enum StatusSeverity
    {
        Info,
        Warning,
        Error,
        Success,
        Debug
    }

    public interface IStatusSink
    {
        void Post(string message, StatusSeverity severity = StatusSeverity.Info);
    }
}

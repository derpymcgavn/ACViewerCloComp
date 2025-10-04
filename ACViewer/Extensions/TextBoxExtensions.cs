using System.Windows.Controls;
using ACViewer.View;
using ACViewer.Services;

namespace ACViewer
{
    public static class TextBoxExtensions
    {
        public static void WriteLine(this TextBox textBox, string line)
        {
            if (MainWindow.StatusSink != null)
                MainWindow.StatusSink.Post(line);
            else
                MainWindow.Instance?.AddStatusText(line);
        }
    }
}

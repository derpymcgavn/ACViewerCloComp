namespace ACViewer.Data.DatIntegration;

public sealed class DatFileHandle
{
    public string Path { get; }
    public object Inner { get; }

    public DatFileHandle(string path, object inner)
    {
        Path = path;
        Inner = inner;
    }
}

namespace ACViewer.Data.DatIntegration;

public interface IDatFileReader
{
    DatFileHandle Open(string path);
}

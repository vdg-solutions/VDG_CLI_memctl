namespace Memctl.CoreAbstractions.Ports;

public interface IEmbeddingEngine : IDisposable
{
    float[] Embed(string text);
}

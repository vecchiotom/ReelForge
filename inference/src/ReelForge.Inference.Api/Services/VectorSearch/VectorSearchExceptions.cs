namespace ReelForge.Inference.Api.Services.VectorSearch;

public class VectorSearchException : Exception
{
    public VectorSearchException(string message)
        : base(message)
    {
    }

    public VectorSearchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class IndexNotReadyException : VectorSearchException
{
    public IndexNotReadyException(string message)
        : base(message)
    {
    }

    public IndexNotReadyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

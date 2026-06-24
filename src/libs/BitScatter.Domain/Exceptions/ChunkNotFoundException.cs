namespace BitScatter.Domain.Exceptions;

public class ChunkNotFoundException : Exception
{
    public string StorageKey { get; }

    public ChunkNotFoundException(string storageKey)
        : base($"Chunk with storage key '{storageKey}' was not found.")
    {
        StorageKey = storageKey;
    }
}

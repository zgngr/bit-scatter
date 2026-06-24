namespace BitScatter.Application.DTOs;

public sealed class ChunkData : IDisposable
{
    public int Index { get; init; }
    public Stream Data { get; init; } = Stream.Null;
    public string Sha256Checksum { get; init; } = string.Empty;
    public long Size { get; init; }

    public void Dispose() => Data.Dispose();
}

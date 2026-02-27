namespace BitScatter.Application.Interfaces;

public interface IChecksumService
{
    Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken = default);

    string ComputeSha256(byte[] data);
}

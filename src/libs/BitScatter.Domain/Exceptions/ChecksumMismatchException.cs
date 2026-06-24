namespace BitScatter.Domain.Exceptions;

public class ChecksumMismatchException : Exception
{
    public string Expected { get; }
    public string Actual { get; }

    public ChecksumMismatchException(string expected, string actual)
        : base($"Checksum mismatch. Expected: {expected}, Actual: {actual}")
    {
        Expected = expected;
        Actual = actual;
    }

    public ChecksumMismatchException(string message, string expected, string actual)
        : base(message)
    {
        Expected = expected;
        Actual = actual;
    }
}

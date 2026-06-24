using System.Security.Cryptography;

namespace BitScatter.Application.Helpers;

public static class EncryptionHelper
{
    private const int Iterations = 100_000;
    private const int SaltSizeBytes = 16;
    private const int KeySizeBytes = 32; // AES-256
    private const int NonceSizeBytes = 12; // GCM Standard
    private const int TagSizeBytes = 16;   // GCM Standard

    public static byte[] GenerateSalt()
    {
        return RandomNumberGenerator.GetBytes(SaltSizeBytes);
    }

    public static byte[] GenerateKey()
    {
        return RandomNumberGenerator.GetBytes(KeySizeBytes);
    }

    public static byte[] GenerateNonce()
    {
        return RandomNumberGenerator.GetBytes(NonceSizeBytes);
    }

    public static byte[] DeriveKey(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySizeBytes);
    }

    public static (byte[] Ciphertext, byte[] Tag) Encrypt(byte[] plaintext, byte[] key, byte[] nonce)
    {
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSizeBytes];
        using var aesGcm = new AesGcm(key, TagSizeBytes);
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
        return (ciphertext, tag);
    }

    public static byte[] Decrypt(byte[] ciphertext, byte[] key, byte[] nonce, byte[] tag)
    {
        var plaintext = new byte[ciphertext.Length];
        using var aesGcm = new AesGcm(key, TagSizeBytes);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    public static byte[] PackChunkPayload(byte[] ciphertext, byte[] nonce, byte[] tag)
    {
        // Layout: [12-byte nonce/IV] + [16-byte GCM Tag] + [Ciphertext]
        var payload = new byte[NonceSizeBytes + TagSizeBytes + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSizeBytes);
        Buffer.BlockCopy(tag, 0, payload, NonceSizeBytes, TagSizeBytes);
        Buffer.BlockCopy(ciphertext, 0, payload, NonceSizeBytes + TagSizeBytes, ciphertext.Length);
        return payload;
    }

    public static (byte[] Ciphertext, byte[] Nonce, byte[] Tag) UnpackChunkPayload(byte[] payload)
    {
        if (payload.Length < NonceSizeBytes + TagSizeBytes)
            throw new CryptographicException("Payload is too small to be a valid encrypted chunk.");

        var nonce = new byte[NonceSizeBytes];
        var tag = new byte[TagSizeBytes];
        var ciphertextLength = payload.Length - NonceSizeBytes - TagSizeBytes;
        var ciphertext = new byte[ciphertextLength];

        Buffer.BlockCopy(payload, 0, nonce, 0, NonceSizeBytes);
        Buffer.BlockCopy(payload, NonceSizeBytes, tag, 0, TagSizeBytes);
        Buffer.BlockCopy(payload, NonceSizeBytes + TagSizeBytes, ciphertext, 0, ciphertextLength);

        return (ciphertext, nonce, tag);
    }
}

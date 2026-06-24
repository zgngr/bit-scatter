using System;
using System.Security.Cryptography;
using System.Text;
using BitScatter.Application.Helpers;
using Xunit;

namespace BitScatter.Application.Tests;

public class EncryptionHelperTests
{
    [Fact]
    public void DeriveKey_GeneratesDeterministicKey()
    {
        var password = "SuperSecretPassword123!";
        var salt = EncryptionHelper.GenerateSalt();

        var key1 = EncryptionHelper.DeriveKey(password, salt);
        var key2 = EncryptionHelper.DeriveKey(password, salt);

        Assert.Equal(32, key1.Length);
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void DeriveKey_DifferentSalts_GenerateDifferentKeys()
    {
        var password = "SuperSecretPassword123!";
        var salt1 = EncryptionHelper.GenerateSalt();
        var salt2 = EncryptionHelper.GenerateSalt();

        var key1 = EncryptionHelper.DeriveKey(password, salt1);
        var key2 = EncryptionHelper.DeriveKey(password, salt2);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void EncryptDecrypt_RoundtripsSuccessfully()
    {
        var plaintext = Encoding.UTF8.GetBytes("Hello BitScatter Encrypted World!");
        var key = EncryptionHelper.GenerateKey();
        var nonce = EncryptionHelper.GenerateNonce();

        var (ciphertext, tag) = EncryptionHelper.Encrypt(plaintext, key, nonce);
        var decrypted = EncryptionHelper.Decrypt(ciphertext, key, nonce, tag);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Decrypt_IncorrectKey_ThrowsCryptographicException()
    {
        var plaintext = Encoding.UTF8.GetBytes("Hello BitScatter Encrypted World!");
        var key1 = EncryptionHelper.GenerateKey();
        var key2 = EncryptionHelper.GenerateKey();
        var nonce = EncryptionHelper.GenerateNonce();

        var (ciphertext, tag) = EncryptionHelper.Encrypt(plaintext, key1, nonce);

        Assert.ThrowsAny<CryptographicException>(() =>
            EncryptionHelper.Decrypt(ciphertext, key2, nonce, tag));
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_ThrowsCryptographicException()
    {
        var plaintext = Encoding.UTF8.GetBytes("Hello BitScatter Encrypted World!");
        var key = EncryptionHelper.GenerateKey();
        var nonce = EncryptionHelper.GenerateNonce();

        var (ciphertext, tag) = EncryptionHelper.Encrypt(plaintext, key, nonce);

        // Tamper with one byte of ciphertext
        ciphertext[0] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() =>
            EncryptionHelper.Decrypt(ciphertext, key, nonce, tag));
    }

    [Fact]
    public void PackUnpackChunkPayload_RoundtripsSuccessfully()
    {
        var ciphertext = Encoding.UTF8.GetBytes("CiphertextData");
        var nonce = EncryptionHelper.GenerateNonce();
        var tag = new byte[16];
        RandomNumberGenerator.Fill(tag);

        var packed = EncryptionHelper.PackChunkPayload(ciphertext, nonce, tag);
        var (unpackedCipher, unpackedNonce, unpackedTag) = EncryptionHelper.UnpackChunkPayload(packed);

        Assert.Equal(ciphertext, unpackedCipher);
        Assert.Equal(nonce, unpackedNonce);
        Assert.Equal(tag, unpackedTag);
    }
}

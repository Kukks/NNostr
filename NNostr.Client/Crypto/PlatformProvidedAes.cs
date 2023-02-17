#if !NETSTANDARD
using System.Security.Cryptography;

namespace NNostr.Client.Crypto;

/// <summary>
/// Uses the native AES implementation provided by the OS.
/// </summary>
internal sealed class PlatformProvidedAes : IAesEncryption
{
    public ValueTask<byte[]> DecryptAsync(byte[] cipherText, byte[] key, byte[] iv)
    {
        // Create new AES instance.
        using Aes aes = Aes.Create();
        aes.Key = key;

        return ValueTask.FromResult(aes.DecryptCbc(cipherText, iv));
    }

    public ValueTask<(byte[] CipherText, byte[] Iv)> EncryptAsync(byte[] plainText, byte[] key)
    {
        // Create new AES instance. This also initializes the random IV.
        using Aes aes = Aes.Create();
        aes.Key = key;

        return ValueTask.FromResult((aes.EncryptCbc(plainText, aes.IV), aes.IV));
    }
}
#endif
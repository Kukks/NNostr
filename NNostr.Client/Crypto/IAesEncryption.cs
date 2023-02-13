namespace NNostr.Client.Crypto;

/// <summary>
/// Allows pluggable AES-256-CBC implementation.
/// </summary>
/// <remarks>
/// For example, when building for WebAssembly you have to provide your own implementation of AES-CBC-256.
/// </remarks>
public interface IAesEncryption
{
    ValueTask<(byte[] CipherText, byte[] Iv)> EncryptAsync(byte[] plainText, byte[] key);
    ValueTask<byte[]> DecryptAsync(byte[] cipherText, byte[] key, byte[] iv);
}
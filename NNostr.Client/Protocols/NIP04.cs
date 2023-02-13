using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

using NBitcoin.Secp256k1;

using NNostr.Client.Crypto;

namespace NNostr.Client;

/// <summary>
/// Implementation of the NIP-04, "Encrypted Direct Message".
/// </summary>
public static class NIP04
{
    private static IAesEncryption _platformAesImplementation = new PlatformProvidedAes();

    /// <summary>
    /// Decrypts given <paramref name="nostrEvent"/> encrypted with AES-CBC-256 using specified key.
    /// </summary>
    /// <param name="nostrEvent">The encrypted event.</param>
    /// <param name="privateKey">The receiver private key.</param>
    /// <param name="aes">The AES-256-CBC implementation to use in the decryption. If <c>null</c>, uses the native platform provided implementation.</param>
    /// <returns>Decrypted <see cref="NostrEvent.Content"/>.</returns>
    public static async ValueTask<string> DecryptNip04EventAsync(this NostrEvent nostrEvent, ECPrivKey privateKey, IAesEncryption? aes = null)
    {
        // By default, use native AES implementation.
        aes ??= _platformAesImplementation;

        if (nostrEvent.Kind != 4)
        {
            throw new ArgumentException("The event is not of kind 4", nameof(nostrEvent));
        }

        var receiverPubKeyStr = nostrEvent.Tags.FirstOrDefault(tag => tag.TagIdentifier == "p")?.Data?.First();
        if (receiverPubKeyStr is null)
        {
            throw new ArgumentException("The event did not specify a receiver public key", nameof(nostrEvent));
        }

        var ourPubKey = privateKey.CreateXOnlyPubKey();
        var ourPubKeyHex = ourPubKey.ToBytes().AsSpan().ToHex();
        var areWeSender = false;
        var receiverPubKey = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(receiverPubKeyStr));

        var receiverPubKeyHex = receiverPubKey.ToBytes().AsSpan().ToHex();
        var senderPubkKey = nostrEvent.GetPublicKey();
        if (nostrEvent.PublicKey == ourPubKeyHex)
        {
            areWeSender = true;
        }
        else if (receiverPubKeyHex == ourPubKeyHex)
        {
            areWeSender = false;
        }
        else
        {
            throw new ArgumentException("The public key does not match recipients of this event", nameof(privateKey));
        }

        if (!TryGetSharedPubkey(areWeSender ? receiverPubKey : senderPubkKey, privateKey, out var sharedKey))
            throw new CryptographicException("Failed to get a shared key.");

        var encrypted = nostrEvent.Content.Split("?iv=");
        var encryptedContentBytes = Convert.FromBase64String(encrypted[0]);

        byte[] decryptionKey = sharedKey.ToBytes().AsSpan(1).ToArray();
        byte[] iv = Convert.FromBase64String(encrypted[1]);

        byte[] plainTextContent = await aes.DecryptAsync(encryptedContentBytes, decryptionKey, iv);
        return Encoding.UTF8.GetString(plainTextContent);
    }
    /// <summary>
    /// Encrypts given <see cref="NostrEvent"/>'s <see cref="NostrEvent.Content"/> with AES-CBC-256 using the private key.
    /// </summary>
    /// <param name="nostrEvent">The event which content will be encrypted.</param>
    /// <param name="privateKey">The sender private key.</param>
    /// <param name="aes">The AES-256-CBC implementation to use in the encryption. If <c>null</c>, uses the native platform provided implementation.</param>
    public static async ValueTask EncryptNip04EventAsync(this NostrEvent nostrEvent, ECPrivKey privateKey, IAesEncryption? aes = null)
    {
        // By default, use native AES implementation.
        aes ??= _platformAesImplementation;

        if (nostrEvent.Kind != 4)
        {
            throw new ArgumentException("The event is not of kind 4", nameof(nostrEvent));
        }

        var receiverPubKeyStr = nostrEvent.Tags.FirstOrDefault(tag => tag.TagIdentifier == "p")?.Data?.First();
        if (receiverPubKeyStr is null)
        {
            throw new ArgumentException("The event did not specify a receiver public key", nameof(nostrEvent));
        }

        var ourPubKey = privateKey.CreateXOnlyPubKey();
        if (nostrEvent.PublicKey is null)
        {
            nostrEvent.PublicKey = ourPubKey.ToBytes().AsSpan().ToHex();
        }
        else if (nostrEvent.PublicKey != ourPubKey.ToBytes().AsSpan().ToHex())
        {
            throw new ArgumentException("Public key of the event does not match sender of this event", nameof(privateKey));
        }

        var receiverPubKey = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(receiverPubKeyStr));
        if (!TryGetSharedPubkey(receiverPubKey, privateKey, out var sharedKey))
            throw new CryptographicException("Failed to get shared key for event.");

        byte[] encryptionKey = sharedKey.ToBytes().AsSpan(1).ToArray();
        byte[] plainText = Encoding.UTF8.GetBytes(nostrEvent.Content);

        (byte[] cipherTextBytes, byte[] ivBytes) = await aes.EncryptAsync(plainText, encryptionKey);

        nostrEvent.Content = $"{Convert.ToBase64String(cipherTextBytes)}?iv={Convert.ToBase64String(ivBytes)}";
    }
    
    private static bool TryGetSharedPubkey(this ECXOnlyPubKey ecxOnlyPubKey, ECPrivKey key, 
        [NotNullWhen(true)] out ECPubKey? sharedPublicKey)
    {
        // 32 + 1 byte for the compression (0x02) prefix.
        Span<byte> input = stackalloc byte[33];
        input[0] = 0x02;
        ecxOnlyPubKey.WriteToSpan(input.Slice(1));

        bool success = Context.Instance.TryCreatePubKey(input, out var publicKey);
        sharedPublicKey = publicKey?.GetSharedPubkey(key);
        return success;
    } 
}
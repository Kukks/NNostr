using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

using NBitcoin.Secp256k1;

namespace NNostr.Client;

/// <summary>
/// Implementation of the NIP-04, "Encrypted Direct Message".
/// </summary>
public static class NIP04
{
    public static string DecryptNip04Event(this NostrEvent nostrEvent, ECPrivKey key)
    {
        if (nostrEvent.Kind != 4)
        {
            throw new ArgumentException("The event is not of kind 4", nameof(nostrEvent));
        }

        var receiverPubKeyStr = nostrEvent.Tags.FirstOrDefault(tag => tag.TagIdentifier == "p")?.Data?.First();
        if (receiverPubKeyStr is null)
        {
            throw new ArgumentException("The event did not specify a receiver public key", nameof(nostrEvent));
        }

        var ourPubKey = key.CreateXOnlyPubKey();
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
            throw new ArgumentException("The public key does not match recipients of this event", nameof(key));
        }

        if (!TryGetSharedPubkey(areWeSender ? receiverPubKey : senderPubkKey, key, out var sharedKey))
            throw new CryptographicException("Failed to get a shared key.");

        var encrypted = nostrEvent.Content.Split("?iv=");
        var encryptedContentBytes = Convert.FromBase64String(encrypted[0]);
        var ivBytes = Convert.FromBase64String(encrypted[1]);

        // Create AES instance.
        using Aes aes = Aes.Create();
        aes.Key = sharedKey.ToBytes().AsSpan(1).ToArray();
        aes.IV = ivBytes;

        byte[] decryptionBuffer = ArrayPool<byte>.Shared.Rent(encryptedContentBytes.Length);

        if (!aes.TryDecryptCbc(encryptedContentBytes, ivBytes, decryptionBuffer, out int bytesDecrypted))
            throw new CryptographicException("Failed to decrypt event.");

        string decryptedContent = Encoding.UTF8.GetString(decryptionBuffer.AsSpan(0, bytesDecrypted));

        ArrayPool<byte>.Shared.Return(decryptionBuffer);
        return decryptedContent;
    }
    
    public static void EncryptNip04Event(this NostrEvent nostrEvent, ECPrivKey key)
    {
        if (nostrEvent.Kind != 4)
        {
            throw new ArgumentException("The event is not of kind 4", nameof(nostrEvent));
        }

        var receiverPubKeyStr = nostrEvent.Tags.FirstOrDefault(tag => tag.TagIdentifier == "p")?.Data?.First();
        if (receiverPubKeyStr is null)
        {
            throw new ArgumentException("The event did not specify a receiver public key", nameof(nostrEvent));
        }

        var ourPubKey = key.CreateXOnlyPubKey();
        if (nostrEvent.PublicKey is null)
        {
            nostrEvent.PublicKey = ourPubKey.ToBytes().AsSpan().ToHex();
        }
        else if (nostrEvent.PublicKey != ourPubKey.ToBytes().AsSpan().ToHex())
        {
            throw new ArgumentException("Public key of the event does not match sender of this event", nameof(key));
        }

        var receiverPubKey = Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(receiverPubKeyStr));
        if (!TryGetSharedPubkey(receiverPubKey, key, out var sharedKey))
            throw new CryptographicException("Failed to get shared key for event.");

        // Create AES instance. Aes.Create also initializes the IV.
        using Aes aes = Aes.Create();
        aes.Key = sharedKey.ToBytes().AsSpan(1).ToArray();

        byte[] encodedContent = Encoding.UTF8.GetBytes(nostrEvent.Content);

        // Now rent a buffer for the AES-256-CBC encryption.
        int maxEncryptedLength = aes.GetCiphertextLengthCbc(encodedContent.Length);
        byte[] encryptionBuffer = ArrayPool<byte>.Shared.Rent(maxEncryptedLength);

        if (!aes.TryEncryptCbc(encodedContent, aes.IV, encryptionBuffer, out int encryptedBytesWritten))
            throw new CryptographicException("Failed to encrypt the event.");

        string encryptedContentBase64 = Convert.ToBase64String(encryptionBuffer.AsSpan(0, encryptedBytesWritten));
        nostrEvent.Content = $"{encryptedContentBase64}?iv={Convert.ToBase64String(aes.IV)}";

        // Return rented buffer.
        ArrayPool<byte>.Shared.Return(encryptionBuffer);
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
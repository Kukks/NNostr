using System.Text.Json;
using CSChaCha20;
using NBitcoin.Secp256k1;

namespace NNostr.Client.Protocols;

using System;
using System.Text;
using System.Security.Cryptography;
#if NETSTANDARD
using HKDF = HkdfStandard.Hkdf;
using RandomNumberGenerator = RND;
public class RND
{
    public static byte[] GetBytes(int size)
    {
        byte[] bytes = new byte[size];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return bytes;
    }
}

#endif
public class NIP44
{
    private const int MinPlaintextSize = 1;
    private const int MaxPlaintextSize = 65535;

    internal static int CalcPaddedLen(int len)
    {
        if (len < 1) 
            throw new ArgumentException("Expected a positive integer");

        if (len <= 32) return 32;

        int nextPower = 1 << (int)(Math.Floor(Math.Log(len - 1, 2)) + 1);
        int chunk = nextPower <= 256 ? 32 : nextPower / 8;

        return chunk * ((len - 1) / chunk + 1);
    }

    private static byte[] Pad(string plaintext)
    {
        byte[] unpadded = Encoding.UTF8.GetBytes(plaintext);
        
        if (unpadded.Length < MinPlaintextSize || unpadded.Length > MaxPlaintextSize)
            throw new Exception("Invalid plaintext length");

        byte[] prefix = BitConverter.GetBytes((ushort)unpadded.Length);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(prefix); // Convert to big endian
        byte[] suffix = new byte[CalcPaddedLen(unpadded.Length) - unpadded.Length];
        return [..prefix,..unpadded, ..suffix];
    }

    private static string Unpad(byte[] padded)
    {
        int unpaddedLen = BitConverter.ToUInt16(new byte[] { padded[1], padded[0] }, 0);
        byte[] unpadded = new byte[unpaddedLen];
        Buffer.BlockCopy(padded, 2, unpadded, 0, unpaddedLen);

        if (unpaddedLen == 0 || unpadded.Length != unpaddedLen || padded.Length != 2 + CalcPaddedLen(unpaddedLen))
            throw new Exception("Invalid padding");

        return Encoding.UTF8.GetString(unpadded);
    }

    private static (byte[] Nonce, byte[] Cipher, byte[] MAC) DecodePayload(string payload)
    {
        if (payload.StartsWith("#"))
        {
            throw new NotSupportedException("Encryption payload not supported");
        }
        byte[] data = Convert.FromBase64String(payload);
        if (data.Length < 99 || data.Length > 65603) throw new Exception("Invalid data size");

        if (data[0] != 2) throw new Exception("Unknown version");

        byte[] nonce = data[1..33];
        byte[] ciphertext = data[33..^32];
        byte[] mac = data[^32..];
        return (nonce, ciphertext, mac);
    }

    private static byte[] HmacAad(byte[] key, byte[] message, byte[] aad)
    {
        if (aad.Length != 32) throw new Exception("AAD associated data must be 32 bytes");

        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash([..aad, ..message]);
    }

    internal static byte[] GetConversationKey(ECPrivKey c, ECXOnlyPubKey publicKey)
    {
        byte[] sharedX = publicKey.TryGetSharedPubkey(c, out var sharedPubKey) ? sharedPubKey.ToXOnlyPubKey().ToBytes() : throw new Exception("Invalid public key");
        return HKDF.Extract(HashAlgorithmName.SHA256, sharedX, "nip44-v2"u8.ToArray());
    }

    internal static (byte[], byte[], byte[]) GetMessageKeys(byte[] conversationKey, byte[] nonce)
    {
        if (conversationKey.Length != 32) throw new Exception("Invalid conversation_key length");
        if (nonce.Length != 32) throw new Exception("Invalid nonce length");

        byte[] keys = HKDF.Expand(HashAlgorithmName.SHA256, conversationKey,  76,nonce);
        byte[] chachaKey = keys[0..32];
        byte[] chachaNonce = keys[32..44];
        byte[] hmacKey = keys[44..76];
        return (chachaKey, chachaNonce, hmacKey);
    }

    public static string Encrypt(ECPrivKey privKey, ECXOnlyPubKey pubKey,string plaintext, byte[]? nonce = null)
    {
        byte[] conversationKey = GetConversationKey(privKey, pubKey);
        nonce ??= RandomNumberGenerator.GetBytes(32);
        if(nonce.Length != 32) throw new Exception("Invalid nonce length");
        var (chachaKey, chachaNonce, hmacKey) = GetMessageKeys(conversationKey, nonce);
        byte[] padded = Pad(plaintext);
        
        // Use ChaCha20 encryption here
        using var chaCha = new ChaCha20(chachaKey, chachaNonce, 0);
        byte[] ciphertext = chaCha.EncryptBytes(padded);

        byte[] mac = HmacAad(hmacKey, ciphertext, nonce);
        byte[] payload = [2, ..nonce, ..ciphertext, ..mac];

        return Convert.ToBase64String(payload);
    }

    public static string Decrypt(ECPrivKey privKey, ECXOnlyPubKey pubKey, string payload)
    {
        byte[] conversationKey = GetConversationKey(privKey, pubKey);
        var (nonce, ciphertext, mac) = DecodePayload(payload);
        var (chachaKey, chachaNonce, hmacKey) = GetMessageKeys(conversationKey, nonce);

        byte[] calculatedMac = HmacAad(hmacKey, ciphertext, nonce);
        if (!calculatedMac.SequenceEqual(mac)) throw new Exception("Invalid MAC");

        // Use ChaCha20 decryption here
        using var chaCha = new ChaCha20(chachaKey, chachaNonce, 0);
        byte[] paddedPlaintext = chaCha.DecryptBytes(ciphertext);

        return Unpad(paddedPlaintext);
    }
    
    public static string Nip44Encrypt<T>(T data, ECPrivKey privateKey, ECXOnlyPubKey publicKey)
    {
        var json = JsonSerializer.Serialize(data);
        return NIP44.Encrypt(privateKey, publicKey, json);
    }

    public static T Nip44Decrypt<T>(string encryptedContent, ECPrivKey privateKey, ECXOnlyPubKey publicKey)
    {
        var decryptedJson = NIP44.Decrypt(privateKey, publicKey,encryptedContent);
        return JsonSerializer.Deserialize<T>(decryptedJson);
    }
}

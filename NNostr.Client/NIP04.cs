using System.Security.Cryptography;
using NBitcoin.Secp256k1;

namespace NNostr.Client;

public static class NIP04
{
    public static string DecryptNip04Event(this NostrEvent nostrEvent, ECPrivKey key)
    {
        if (nostrEvent.Kind != 4)
        {
            throw new ArgumentException( "event is not of kind 4", nameof(nostrEvent));
        }

        var receiverPubKeyStr = nostrEvent.Tags.FirstOrDefault(tag => tag.TagIdentifier == "p")?.Data?.First();
        if (receiverPubKeyStr is null)
        {
            throw new ArgumentException( "event did not specify a receiver pub key", nameof(nostrEvent));
        }

        var ourPubKey = key.CreateXOnlyPubKey();
        var ourPubKeyHex = ourPubKey.ToBytes().ToHex();
        var areWeSender = false;
        var receiverPubKey = Context.Instance.CreateXOnlyPubKey(receiverPubKeyStr.DecodHexData());
        
        var receiverPubKeyHex = receiverPubKey.ToBytes().ToHex();
        var senderPubkKey = nostrEvent.GetPublicKey();
        if (nostrEvent.PublicKey == ourPubKeyHex)
        {
            areWeSender = true;
        }else if (receiverPubKeyHex == ourPubKeyHex)
        {
            areWeSender = false;
        }
        else
        {
            throw new ArgumentException( "key does not match recipients of this event", nameof(key));
        }

        var sharedKey = GetSharedPubkey(areWeSender ? receiverPubKey : senderPubkKey, key).ToBytes().Skip(1).ToArray();
        var encrypted = nostrEvent.Content.Split("?iv=");
        var encryptedText = encrypted[0];
        var iv = encrypted[1];
        return Decrypt(encryptedText, iv, sharedKey);
    }
    
    public static void EncryptNip04Event(this NostrEvent nostrEvent, ECPrivKey key)
    {
        if (nostrEvent.Kind != 4)
        {
            throw new ArgumentException( "event is not of kind 4", nameof(nostrEvent));
        }

        var receiverPubKeyStr = nostrEvent.Tags.FirstOrDefault(tag => tag.TagIdentifier == "p")?.Data?.First();
        if (receiverPubKeyStr is null)
        {
            throw new ArgumentException( "event did not specify a receiver pub key", nameof(nostrEvent));
        }

        var ourPubKey = key.CreateXOnlyPubKey();
        if (nostrEvent.PublicKey == null)
        {
            nostrEvent.PublicKey = ourPubKey.ToBytes().ToHex();
        }else if (nostrEvent.PublicKey != ourPubKey.ToBytes().ToHex())
        {
            throw new ArgumentException( "key does not match sender of this event", nameof(key));
        }
        var receiverPubKey = Context.Instance.CreateXOnlyPubKey(receiverPubKeyStr.DecodHexData());
        var sharedKey = GetSharedPubkey(receiverPubKey, key).ToBytes().Skip(1).ToArray();

        var result = Encrypt(nostrEvent.Content, sharedKey);
        nostrEvent.Content = $"{result.cipherText}?iv={result.iv}";

    }
    
    private static byte[] posBytes =  "02".DecodHexData();
    private static ECPubKey? GetSharedPubkey(this ECXOnlyPubKey ecxOnlyPubKey, ECPrivKey key)
    {
        Context.Instance.TryCreatePubKey(posBytes.Concat(ecxOnlyPubKey.ToBytes()).ToArray(), out var mPubKey);
        return mPubKey?.GetSharedPubkey(key);
    }
    
    
    
    

    private static (string cipherText, string iv) Encrypt(string plainText, byte[] key)
    {
        byte[] cipherData;
        Aes aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        ICryptoTransform cipher = aes.CreateEncryptor(aes.Key, aes.IV);

        using (MemoryStream ms = new MemoryStream())
        {
            using (CryptoStream cs = new CryptoStream(ms, cipher, CryptoStreamMode.Write))
            {
                using (StreamWriter sw = new StreamWriter(cs))
                {
                    sw.Write(plainText);
                }
            }

            cipherData = ms.ToArray();
        }

        return (Convert.ToBase64String(cipherData), Convert.ToBase64String(aes.IV));
    }

    private static string Decrypt(string cipherText, string iv, byte[] key)
    {
        string plainText;
        Aes aes = Aes.Create();
        aes.Key = key;
        aes.IV = Convert.FromBase64String(iv);
        aes.Mode = CipherMode.CBC;
        ICryptoTransform decipher = aes.CreateDecryptor(aes.Key, aes.IV);

        using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(cipherText)))
        {
            using (CryptoStream cs = new CryptoStream(ms, decipher, CryptoStreamMode.Read))
            {
                using (StreamReader sr = new StreamReader(cs))
                {
                    plainText = sr.ReadToEnd();
                }
            }

            return plainText;
        }
    }
    
}
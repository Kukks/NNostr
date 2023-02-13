using NBitcoin.Secp256k1;

namespace NNostr.Client;

public static class NIP04
{
    public static IAesEncryptor Encryptor = new AesEncryptor();
    
    public static Task<string> DecryptNip04Event(this NostrEvent nostrEvent, ECPrivKey key)
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
        return Encryptor.Decrypt(encryptedText, iv, sharedKey);
    }
    
    public static async Task EncryptNip04Event(this NostrEvent nostrEvent, ECPrivKey key)
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

        var result = await Encryptor.Encrypt(nostrEvent.Content, sharedKey);
        nostrEvent.Content = $"{result.cipherText}?iv={result.iv}";

    }
    
    private static byte[] posBytes =  "02".DecodHexData();
    private static ECPubKey? GetSharedPubkey(this ECXOnlyPubKey ecxOnlyPubKey, ECPrivKey key)
    {
        Context.Instance.TryCreatePubKey(posBytes.Concat(ecxOnlyPubKey.ToBytes()).ToArray(), out var mPubKey);
        return mPubKey?.GetSharedPubkey(key);
    }
    
   
    
}

public interface IAesEncryptor
{
    Task<(string cipherText, string iv)> Encrypt(string plainText, byte[] key);
    Task<string> Decrypt(string cipherText, string iv, byte[] key);
}
using System.Security.Cryptography;
using System.Text.Json;
using NBitcoin.Secp256k1;
using NNostr.Client;
using NNostr.Client.Protocols;

#if NETSTANDARD
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

public class NIP59
{

    

   

    public static NostrEvent CreateRumor(NostrEvent evt)
    {
        return new NostrEvent()
        {
            Kind = evt.Kind,
            Content = evt.Content,
            CreatedAt = evt.CreatedAt,
            PublicKey = evt.PublicKey,
            Id = evt.Id,
            Tags = evt.Tags

        };
    }

    public static long NextInt64(Random random, long min, long max)
    {
        if (min >= max)
            throw new ArgumentOutOfRangeException(nameof(min), "min must be less than max.");

        ulong range = (ulong)(max - min);
        ulong ulongRand;

        do
        {
            byte[] buffer = new byte[8];
            random.NextBytes(buffer);
            ulongRand = BitConverter.ToUInt64(buffer, 0);
        } while (ulongRand >= ulong.MaxValue - (ulong.MaxValue % range + 1) % range);

        return (long)(ulongRand % range) + min;
    }



    private static DateTimeOffset RandomizedDate()
    {
        //up to two days in the past
        var max = DateTimeOffset.UtcNow;
        var min =max.AddDays(-2);
        
        #if NETSTANDARD
        var r = new Random();
        var num = NextInt64(r, min.ToUnixTimeSeconds(), max.ToUnixTimeSeconds());
        #else
        var num = Random.Shared.NextInt64(min.ToUnixTimeSeconds(), max.ToUnixTimeSeconds());
        #endif
       return DateTimeOffset.FromUnixTimeSeconds(num);
    }
    public static async Task<NostrEvent> CreateSeal(NostrEvent rumor, ECPrivKey senderPrivateKey, ECXOnlyPubKey recipientPublicKey, bool randomizeDate = true)
    {
        var encryptedRumor = NIP44.Nip44Encrypt(rumor, senderPrivateKey, recipientPublicKey);
        return await  new NostrEvent()
        {
            Kind = 13,
            Content = encryptedRumor,
            CreatedAt = randomizeDate ? RandomizedDate() : DateTimeOffset.UtcNow,
        }.ComputeIdAndSignAsync(senderPrivateKey);
    }

    public static async Task<NostrEvent> CreateWrap(NostrEvent seal, ECXOnlyPubKey recipientPublicKey, NostrEventTag[]? tags = null, bool randomizeDate = true)
    {
        var randomKey = ECPrivKey.Create(RandomNumberGenerator.GetBytes(32));
        var encryptedSeal = NIP44.Nip44Encrypt(seal, randomKey, recipientPublicKey);
        var tagsArray = tags?.ToList()?? [];
        if (!tagsArray.Any(tag => tag.TagIdentifier == "p" && tag.Data.Any(s => s == recipientPublicKey.ToHex())))
        {
            tagsArray = tagsArray.Prepend(new NostrEventTag()
            {
                TagIdentifier = "p",
                Data = [recipientPublicKey.ToHex()]
            }).ToList();
        }
        return await  new NostrEvent()
        {
            Kind = 1059,
            Content = encryptedSeal,
            Tags = tagsArray,
            CreatedAt = randomizeDate ? RandomizedDate() : DateTimeOffset.UtcNow,
        }.ComputeIdAndSignAsync(randomKey);
    }

    public static async Task<NostrEvent> Open(NostrEvent wrap, ECPrivKey receiverPrivateKey, bool allowDifferentAuthorInRumorAndSeal = false)
    {
        if(wrap.Kind != 1059)
            throw new InvalidOperationException("Not a wrap");
        var decryptedSeal = NIP44.Nip44Decrypt<NostrEvent>(wrap.Content, receiverPrivateKey, wrap.GetPublicKey());
        if(decryptedSeal.Kind != 13)
            throw new InvalidOperationException("Not a seal");
        if (!decryptedSeal.Verify())
        {
            throw new InvalidOperationException("Invalid signature");
        }
        var senderPublicKey = decryptedSeal.GetPublicKey();
        var rumor = NIP44.Nip44Decrypt<NostrEvent>(decryptedSeal.Content, receiverPrivateKey, senderPublicKey);
        if(!allowDifferentAuthorInRumorAndSeal && rumor.PublicKey != senderPublicKey.ToHex())
            throw new InvalidOperationException("Invalid rumor author");
        return rumor;
    }
}

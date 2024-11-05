using NBitcoin.Secp256k1;
using NNostr.Client;

public class NIP17
{
    public static async Task<NostrEvent> Create(NostrEvent dm, ECPrivKey senderPrivateKey,
        ECXOnlyPubKey receiverPublicKey, NostrEventTag[] tags)
    {
        if (dm.Kind != 14)
        {
            throw new Exception("Direct message must be a kind 14 event");
        }

        if (dm.Signature != null)
        {
            throw new Exception("Direct message must NOT be signed");
        }

        dm.CreatedAt ??= DateTimeOffset.UtcNow;
        dm.Id = dm.ComputeId();

        var rumor = NIP59.CreateRumor(dm);
        var seal = await NIP59.CreateSeal(rumor, senderPrivateKey, receiverPublicKey);
        var wrap = await NIP59.CreateWrap(seal, receiverPublicKey, tags);
        return wrap;
    }
    
    public static async Task<NostrEvent> Open(NostrEvent wrap,ECPrivKey receiverPrivateKey)
    {
        if (wrap.Kind != 1059)
        {
            throw new Exception("Direct message must be a kind 1059 event");
        }

        if (!wrap.Verify())
        {
            throw new Exception("Direct message must be signed");
        }
        
        var dm =  await NIP59.Open(wrap, receiverPrivateKey);
        if(dm.Kind != 14)
        {
            throw new Exception("Direct message must be a kind 14 event");
        }
        return dm;
    }
}
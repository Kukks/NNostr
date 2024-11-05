using System;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text.Json;
using NBitcoin.Secp256k1;
using NNostr.Client;
using NNostr.Client.Protocols;
using Xunit;

public class NIP59Tests
{
    [Fact]
    public async Task TestGiftWrapProcess()
    {
        ECPrivKey senderPrivateKey = "nsec1p0ht6p3wepe47sjrgesyn4m50m6avk2waqudu9rl324cg2c4ufesyp6rdg".FromNIP19Nsec();
        ECPrivKey recipientPrivateKey =
            "nsec1uyyrnx7cgfp40fcskcr2urqnzekc20fj0er6de0q8qvhx34ahazsvs9p36".FromNIP19Nsec();
        var recipientPublicKey = recipientPrivateKey.CreateXOnlyPubKey();

        // Step 1: Create the Rumor
        var rumorEvent = new NostrEvent()
        {
            Kind = 1,
            Content = "Are you going to the party tonight?",
            PublicKey = senderPrivateKey.CreateXOnlyPubKey().ToHex()
        };
        rumorEvent.Id = rumorEvent.ComputeId();

        var rumor = NIP59.CreateRumor(rumorEvent);

        // Step 2: Create the Seal
        var seal = await NIP59.CreateSeal(rumor, senderPrivateKey, recipientPublicKey);

        // Step 3: Create the Gift Wrap
        var wrap = await NIP59.CreateWrap(seal, recipientPublicKey);
        
        // Decrypting Gift Wrap
        var decryptedRumor =await  NIP59.Open(wrap, recipientPrivateKey);

        // Assertions
        Assert.Equal(rumorEvent.Content, decryptedRumor.Content);
        Assert.Equal(rumorEvent.Kind, decryptedRumor.Kind);
        Assert.Equal(rumorEvent.PublicKey, decryptedRumor.PublicKey);
        Assert.Equal(rumorEvent.Id, decryptedRumor.Id);
    }
}
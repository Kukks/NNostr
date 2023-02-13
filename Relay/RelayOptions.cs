using System;
using NBitcoin.DataEncoders;
using NBitcoin.Secp256k1;
using NNostr.Client;

namespace Relay;

public class RelayOptions
{
    //cost in sats per event
    public long EventCost { get; set; } = 0;

    //whether the cost is per byte of the event json
    public bool EventCostPerByte { get; set; } = false;

    //cost for a new pubkey to post events to relay
    public long PubKeyCost { get; set; } = 0;

    //priv key of admin bot
    public string? AdminKey { get; set; }

    public bool EnableNip09 { get; set; } = true;
    public bool EnableNip11 { get; set; } = true;
    public bool EnableNip16 { get; set; } = true;
    public bool EnableNip33 { get; set; } = true;
    public bool EnableNip20 { get; set; } = true;
    public int Nip13Difficulty { get; set; } = 0;

    public ECPrivKey? AdminPrivateKey
    {
        get
        {
            if (string.IsNullOrEmpty(AdminKey))
            {
                return null;
            }

            var key = Encoders.Hex.DecodeData(AdminKey);
            return NBitcoin.Secp256k1.ECPrivKey.TryCreate(key, out var privKey) ? privKey : null;
        }
    }

    public string? AdminPublicKey => AdminPrivateKey?.CreateXOnlyPubKey()?.ToBytes()?.AsSpan().ToHex();

    public Uri? BTCPayServerUri { get; set; }
    public string? BTCPayServerApiKey { get; set; }
    public string? BTCPayServerStoreId { get; set; }
    public bool EnableNip15 { get; set; } = true;
    public TimeSpan? Nip22BackwardLimit { get; set; } = TimeSpan.FromDays(1);
    public TimeSpan? Nip22ForwardLimit { get; set; } = TimeSpan.FromMinutes(15);
}
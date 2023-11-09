using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
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

    //priv key of relay admin
    public string? AdminKey { get; set; }

    public bool EnableNip09 { get; set; } = true;

    public bool EnableNip11 { get; set; } = true;

    public bool EnableNip16 { get; set; } = true;

    public bool EnableNip33 { get; set; } = true;

    public bool EnableNip20 { get; set; } = true;

    public int Nip13Difficulty { get; set; } = 0;
    public string? RelayName { get; set; }
    public string? RelayDescription { get; set; }
    public string? RelayAlternativeContact { get; set; }

    public int? PurgeAfterDays { get; set; } = 30;
    

    [JsonIgnore]
    public ECPrivKey? AdminPrivateKey
    {
        get
        {
            if (string.IsNullOrEmpty(AdminKey))
            {
                return null;
            }

            var key = Encoders.Hex.DecodeData(AdminKey);
            return ECPrivKey.TryCreate(key, out var privKey) ? privKey : null;
        }
    }

    [JsonIgnore] public string? AdminPublicKey => AdminPrivateKey?.CreateXOnlyPubKey().ToBytes().AsSpan().ToHex();

    public Uri? BTCPayServerUri { get; set; }

    public string? BTCPayServerApiKey { get; set; }
    public string? BTCPayServerWebhookSecret { get; set; }

    public string? BTCPayServerStoreId { get; set; }

    public bool EnableNip15 { get; set; } = true;

    public TimeSpan? Nip22BackwardLimit { get; set; } = TimeSpan.FromDays(1);

    public TimeSpan? Nip22ForwardLimit { get; set; } = TimeSpan.FromMinutes(15);

    public bool Validate(out List<string> errors)
    {
        errors = new List<string>();
        if (AdminPrivateKey is null)
        {
            errors.Add("AdminKey is null or invalid");
        }

        return errors.Count == 0;
    }
}
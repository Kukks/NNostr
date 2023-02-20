using System;
using System.Text.Json.Serialization;
using NBitcoin.DataEncoders;
using NBitcoin.Secp256k1;
using NNostr.Client;

namespace Relay;

public class RelayOptions
{
    //cost in sats per event
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long EventCost { get; set; } = 0;

    //whether the cost is per byte of the event json
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool EventCostPerByte { get; set; } = false;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    //cost for a new pubkey to post events to relay
    public long PubKeyCost { get; set; } = 0;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    //priv key of admin bot
    public string? AdminKey { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool EnableNip09 { get; set; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool EnableNip11 { get; set; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool EnableNip16 { get; set; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool EnableNip33 { get; set; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool EnableNip20 { get; set; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Nip13Difficulty { get; set; } = 0;

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

    [JsonIgnore] public string? AdminPublicKey => AdminPrivateKey?.CreateXOnlyPubKey()?.ToBytes()?.AsSpan().ToHex();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Uri? BTCPayServerUri { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? BTCPayServerApiKey { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? BTCPayServerStoreId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool EnableNip15 { get; set; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TimeSpan? Nip22BackwardLimit { get; set; } = TimeSpan.FromDays(1);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TimeSpan? Nip22ForwardLimit { get; set; } = TimeSpan.FromMinutes(15);
}
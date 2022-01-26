using System;
using NBitcoin.DataEncoders;
using NBitcoin.Secp256k1;
using NNostr.Client;

namespace Relay;

public class RelayOptions
{
    public long EventCost { get; set; } = 0;
    public bool EventCostPerByte { get; set; } = false;
    public long PubKeyCost { get; set; } = 0;
    public bool EnableNip09 { get; set; } = true;
    public bool EnableNip11 { get; set; } = true;

    public string? AdminKey { get; set; }

    public ECPrivKey? AdminPrivateKey
    {
        get
        {
            if (string.IsNullOrEmpty(AdminKey))
            {
                return null;
            }

            var key = Encoders.Hex.DecodeData(AdminKey);
            return ECPrivKey.TryCreateFromDer(key, out var privKey) ? privKey : null;
        }
    }

    public string? AdminPublicKey => AdminPrivateKey?.CreateXOnlyPubKey()?.ToBytes()?.ToHex();

    public Uri? BTCPayServerUri { get; set; }
    public string? BTCPayServerApiKey { get; set; }
    public string? BTCPayServerStoreId { get; set; }
}
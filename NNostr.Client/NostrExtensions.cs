using System.Linq;
using NBitcoin.Secp256k1;

namespace NNostr.Client
{
    public static class NostrExtensions
    {
        public static string ToJson(this NostrEvent nostrEvent)
        {
            return
                $"[0,\"{nostrEvent.PublicKey}\",{nostrEvent.CreatedAt?.ToUnixTimeSeconds()},{nostrEvent.Kind},[{string.Join(',', nostrEvent.Tags.Select(tag => tag.ToString()))}],\"{nostrEvent.Content}\"]";
        }

        public static string ComputeId(this NostrEvent nostrEvent)
        {
            return nostrEvent.ToJson().ComputeSha256Hash().ToHex();
        }

        public static string ComputeSignature(this NostrEvent nostrEvent, ECPrivKey priv)
        {
            return nostrEvent.ToJson().ComputeSignature(priv);
        }

        public static bool Verify(this NostrEvent nostrEvent)
        {
            var hash = nostrEvent.ToJson().ComputeSha256Hash();
            if (hash.ToHex() != nostrEvent.Id)
            {
                return false;
            }

            var pub = nostrEvent.GetPublicKey();
            if (!SecpSchnorrSignature.TryCreate(nostrEvent.Signature.DecodHexData(), out var sig))
            {
                return false;
            }

            return pub.SigVerifyBIP340(sig, hash);
        }

        public static ECXOnlyPubKey GetPublicKey(this NostrEvent nostrEvent)
        {
            return Context.Instance.CreateXOnlyPubKey(nostrEvent.PublicKey.DecodHexData());
        }

        public static ECPrivKey ParseKey(string key)
        {
            return ECPrivKey.Create(key.DecodHexData());
        }
        
        public static string ToString(this ECPrivKey key)
        {
            Span<byte> output = new Span<byte>(new byte[32]);
            key.WriteToSpan(output);
            return output.ToHex();
        }
    }
}
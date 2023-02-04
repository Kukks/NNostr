using System.Text;
using NBitcoin.Secp256k1;

namespace NNostr.Client
{
    public static class StringExtensions
    {
        public static string ComputeSchnorrSignature(this string rawData, ECPrivKey privKey)
        {
            var bytes = rawData.ComputeSha256Hash();
            Span<byte> buf = stackalloc byte[64];
            privKey.SignBIP340(bytes).WriteToSpan(buf);
            return ToHex(buf);
        }

        public static byte[] ComputeSha256Hash(this string rawData)
        {
            // Create a SHA256
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            // ComputeHash - returns byte array
            return sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
        }

        public static string ToHex(this Span<byte> bytes) 
        {
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
    public static class EnumeratorExtensions
    {
        public static IEnumerable<T> ToEnumerable<T>(this IEnumerator<T> enumerator)
        {
            while(enumerator.MoveNext())
                yield return enumerator.Current;
        }
    }
}
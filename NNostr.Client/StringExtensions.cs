using System.Text;

using NBitcoin.Secp256k1;

namespace NNostr.Client
{
    public static class StringExtensions
    {
        public static string ComputeBIP340Signature(this string rawData, ECPrivKey privKey)
        {
            Span<byte> buf = stackalloc byte[64];
            using var sha256 = System.Security.Cryptography.SHA256.Create();

            sha256.TryComputeHash(Encoding.UTF8.GetBytes(rawData), buf, out _);
            privKey.SignBIP340(buf[..32]).WriteToSpan(buf);

            return buf.ToHex();
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
            #if NETSTANDARD
                return string.Join(string.Empty, Array.ConvertAll(bytes.ToArray(), b => b.ToString("X2")));
            #else
            
                return Convert.ToHexString(bytes).ToLowerInvariant();
            #endif
        }
        public static Span<byte>  FromHex(this string hex)
        {
            return Convert.FromHexString(hex).AsSpan();
        }
    }
}
using System.Text;
using NBitcoin.Secp256k1;

namespace NNostr.Client
{
    public static class StringExtensions
    {
        public static byte[] DecodHexData(this string encoded)
        {
            if (encoded == null)
                throw new ArgumentNullException(nameof(encoded));
            if (encoded.Length % 2 == 1)
                throw new FormatException("Invalid Hex String");

            var result = new byte[encoded.Length / 2];
            for (int i = 0, j = 0; i < encoded.Length; i += 2, j++)
            {
                var a = IsDigit(encoded[i]);
                var b = IsDigit(encoded[i + 1]);
                if (a == -1 || b == -1)
                    throw new FormatException("Invalid Hex String");
                result[j] = (byte)(((uint)a << 4) | (uint)b);
            }

            return result;
        }

        public static int IsDigit(this char c)
        {
            if ('0' <= c && c <= '9')
            {
                return c - '0';
            }
            else if ('a' <= c && c <= 'f')
            {
                return c - 'a' + 10;
            }
            else if ('A' <= c && c <= 'F')
            {
                return c - 'A' + 10;
            }
            else
            {
                return -1;
            }
        }

        public static string ComputeSignature(this string rawData, ECPrivKey privKey)
        {
            var bytes = rawData.ComputeSha256Hash();
            var buf = new byte[64];
            privKey.SignBIP340(bytes).WriteToSpan(buf);
            return buf.ToHex();
        }

        public static byte[] ComputeSha256Hash(this string rawData)
        {
            // Create a SHA256   
            using var sha256Hash = System.Security.Cryptography.SHA256.Create();
            // ComputeHash - returns byte array  
            return sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
        }

        public static string ToHex(this byte[] bytes)
        {
            var builder = new StringBuilder();
            foreach (var t in bytes)
            {
                builder.Append(t.ToHex());
            }

            return builder.ToString();
        }

        private static string ToHex(this byte b)
        {
            return b.ToString("x2");
        }

        public static string ToHex(this Span<byte> bytes)
        {
            var builder = new StringBuilder();
            foreach (var t in bytes)
            {
                builder.Append(t.ToHex());
            }

            return builder.ToString();
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
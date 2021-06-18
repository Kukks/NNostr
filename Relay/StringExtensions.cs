using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Tasks;
using NBitcoin.Secp256k1;

namespace Relay
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
                result[j] = (byte) (((uint) a << 4) | (uint) b);
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
            return bytes.ToHex();
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
                builder.Append(t.ToString("x2"));
            }  
            return builder.ToString();
        }
        // from https://stackoverflow.com/a/65265024/275504
        /// <summary>
        /// Adds a key/value pair to the <see cref="ConcurrentDictionary{TKey,TValue}"/> by using the specified function 
        /// if the key does not already exist. Returns the new value, or the existing value if the key exists.
        /// </summary>
        public static async Task<TResult> GetOrAddAsync<TKey,TResult>(
            this ConcurrentDictionary<TKey,TResult> dict,
            TKey key, Func<TKey,Task<TResult>> asyncValueFactory)
        {
            if (dict.TryGetValue(key, out TResult resultingValue))
            {
                return resultingValue;
            }
            var newValue = await asyncValueFactory(key);
            return dict.GetOrAdd(key, newValue);
        }

    }
}
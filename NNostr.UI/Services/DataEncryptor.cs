using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BlazingPay.Abstractions.Services
{
    public class DataEncryptor
    {
        public static string Encrypt(string data, string? passphrase)
        {
            return Encrypt(Encoding.UTF8.GetBytes(data), passphrase);
        }

        
        public static string Encrypt(byte[] data, string? passphrase)
        {
            using Aes aes = Aes.Create();
            aes.Padding = PaddingMode.PKCS7;
            aes.KeySize = 128; // in bits
            aes.Key = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(passphrase));
            using MemoryStream msEncrypt = new MemoryStream();
            using (CryptoStream csEncrypt =
                new CryptoStream(msEncrypt, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                csEncrypt.Write(data, 0, data.Length);
            }

            var resultBytes = aes.IV.Concat(msEncrypt.ToArray()).ToArray();
            return BitConverter.ToString(resultBytes).Replace("-", "");
        }

        public static string Decrypt(string data, string passphrase)
        {
            int NumberChars = data.Length;
            byte[] bytes = new byte[NumberChars / 2];
            for (int i = 0; i < NumberChars; i += 2)
                bytes[i / 2] = Convert.ToByte(data.Substring(i, 2), 16);
            var dataB = bytes;
            using Aes aes = Aes.Create();
            aes.Padding = PaddingMode.PKCS7;
            aes.KeySize = 128; // in bits
            aes.Key = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(passphrase));
            aes.IV = dataB.Take(aes.IV.Length).ToArray();
            using MemoryStream ms = new MemoryStream();
            using (CryptoStream cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
            {
                cs.Write(dataB, aes.IV.Length, dataB.Length - aes.IV.Length);
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }
    }
}
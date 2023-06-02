#if NETSTANDARD
using System.Security.Cryptography;
using System.Text;
using NNostr.Client.Crypto;

namespace NNostr.Client;
public class NetStandardAesEncryptor : IAesEncryption
{
    public ValueTask<(byte[] CipherText, byte[] Iv)> EncryptAsync(byte[] plainText, byte[] key)
    {
        byte[] cipherData;
        var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        var cipher = aes.CreateEncryptor(aes.Key, aes.IV);

        using (var ms = new MemoryStream())
        {
            using (var cs = new CryptoStream(ms, cipher, CryptoStreamMode.Write))
            {
                using (var sw = new StreamWriter(cs))
                {
                    sw.Write(plainText);
                }
            }

            cipherData = ms.ToArray();
        }

        return new ValueTask<(byte[] CipherText, byte[] Iv)>( (cipherData, aes.IV) );
    }

    public ValueTask<byte[]> DecryptAsync(byte[] cipherText, byte[] iv, byte[] key)
    {
        var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        var decipher = aes.CreateDecryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream(cipherText);
        string plainText;
        using (var cs = new CryptoStream(ms, decipher, CryptoStreamMode.Read))
        {
            using (var sr = new StreamReader(cs))
            {
                plainText = sr.ReadToEnd();
            }
        }

        return new ValueTask<byte[]>(Encoding.UTF8.GetBytes(plainText));
    }
}
#endif
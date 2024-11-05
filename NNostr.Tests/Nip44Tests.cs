using System;
using System.Linq;
using System.Security.Cryptography;
using NBitcoin.Secp256k1;
using Xunit;

namespace NNostr.Client.Protocols.Tests
{
    public class NIP44Tests
    {
        [Theory]
        [InlineData("fffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364139",
            "0000000000000000000000000000000000000000000000000000000000000002",
            "8b6392dbf2ec6a2b2d5b1477fc2be84d63ef254b667cadd31bd3f444c44ae6ba")]
        [InlineData("0000000000000000000000000000000000000000000000000000000000000002",
            "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdeb",
            "be234f46f60a250bef52a5ee34c758800c4ca8e5030bf4cc1a31d37ba2104d43")]
        [InlineData("0000000000000000000000000000000000000000000000000000000000000001",
            "79be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798",
            "3b4610cb7189beb9cc29eb3716ecc6102f1247e8f3101a03a1787d8908aeb54e")]
        public void TestGetConversationKey(string sec1, string pub2, string expectedConversationKey)
        {
            var privateKey = ECPrivKey.Create(Convert.FromHexString(sec1));
            var publicKey = ECXOnlyPubKey.Create(Convert.FromHexString(pub2));
            byte[] conversationKey = NIP44.GetConversationKey(privateKey, publicKey);

            Assert.Equal(expectedConversationKey, Convert.ToHexString(conversationKey),
                StringComparer.InvariantCultureIgnoreCase);
        }

        [Theory]
        [InlineData(16, 32)]
        [InlineData(32, 32)]
        [InlineData(33, 64)]
        [InlineData(37, 64)]
        [InlineData(45, 64)]
        [InlineData(49, 64)]
        [InlineData(64, 64)]
        [InlineData(65, 96)]
        [InlineData(100, 128)]
        [InlineData(111, 128)]
        [InlineData(200, 224)]
        [InlineData(250, 256)]
        [InlineData(320, 320)]
        [InlineData(383, 384)]
        [InlineData(384, 384)]
        [InlineData(400, 448)]
        [InlineData(500, 512)]
        [InlineData(512, 512)]
        [InlineData(515, 640)]
        [InlineData(700, 768)]
        [InlineData(800, 896)]
        [InlineData(900, 1024)]
        [InlineData(1020, 1024)]
        [InlineData(65536, 65536)]
        public void TestCalcPaddedLen(int unpaddedLen, int expectedPaddedLen)
        {
            int paddedLen = NIP44.CalcPaddedLen(unpaddedLen);
            Assert.Equal(expectedPaddedLen, paddedLen);
        }

        [Theory]
        [InlineData("0000000000000000000000000000000000000000000000000000000000000001", "0000000000000000000000000000000000000000000000000000000000000002", "0000000000000000000000000000000000000000000000000000000000000001", "a", "AgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABee0G5VSK0/9YypIObAtDKfYEAjD35uVkHyB0F4DwrcNaCXlCWZKaArsGrY6M9wnuTMxWfp1RTN9Xga8no+kF5Vsb")]
        [InlineData("0000000000000000000000000000000000000000000000000000000000000002", "0000000000000000000000000000000000000000000000000000000000000001", "f00000000000000000000000000000f00000000000000000000000000000000f", "🍕🫃", "AvAAAAAAAAAAAAAAAAAAAPAAAAAAAAAAAAAAAAAAAAAPSKSK6is9ngkX2+cSq85Th16oRTISAOfhStnixqZziKMDvB0QQzgFZdjLTPicCJaV8nDITO+QfaQ61+KbWQIOO2Yj")]
        [InlineData("5c0c523f52a5b6fad39ed2403092df8cebc36318b39383bca6c00808626fab3a", "4b22aa260e4acb7021e32f38a6cdf4b673c6a277755bfce287e370c924dc936d", "b635236c42db20f021bb8d1cdff5ca75dd1a0cc72ea742ad750f33010b24f73b", "表ポあA鷗ŒéＢ逍Üßªąñ丂㐀𠀀", "ArY1I2xC2yDwIbuNHN/1ynXdGgzHLqdCrXUPMwELJPc7s7JqlCMJBAIIjfkpHReBPXeoMCyuClwgbT419jUWU1PwaNl4FEQYKCDKVJz+97Mp3K+Q2YGa77B6gpxB/lr1QgoqpDf7wDVrDmOqGoiPjWDqy8KzLueKDcm9BVP8xeTJIxs=")]
        public void TestEncryptDecrypt(string sec1, string sec2, string nonce, string plaintext,
            string expectedCiphertext)
        {
            var privateKeyA = ECPrivKey.Create(Convert.FromHexString(sec1));
            var privateKeyB = ECPrivKey.Create(Convert.FromHexString(sec2));
            var nonceBytes = Convert.FromHexString(nonce);
            string encryptedPayload =
                NIP44.Encrypt(privateKeyA, privateKeyB.CreateXOnlyPubKey(), plaintext, nonceBytes);
            Assert.Equal(expectedCiphertext, encryptedPayload);

            string decryptedText = NIP44.Decrypt(privateKeyA, privateKeyB.CreateXOnlyPubKey(), encryptedPayload);
            Assert.Equal(plaintext, decryptedText);
        }

        [Theory]
        [InlineData("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef")]
        [InlineData("0000000000000000000000000000000000000000000000000000000000000000", "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef")]
        [InlineData("fffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364139", "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")]
        [InlineData("fffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364141", "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef")]

        public void TestGetConversationKeyInvalidCases(string sec1, string pub2)
        {
            Assert.ThrowsAny<Exception>(() =>
            {
                var privateKey = ECPrivKey.Create(Convert.FromHexString(sec1));
                var publicKey = ECXOnlyPubKey.Create(Convert.FromHexString(pub2));

                NIP44.GetConversationKey(privateKey, publicKey);
            });
        }

        [Theory]
        [InlineData(
            "Agn/l3ULCEAS4V7LhGFM6IGA17jsDUaFCKhrbXDANholyySBfeh+EN8wNB9gaLlg4j6wdBYh+3oK+mnxWu3NKRbSvQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        public void TestDecryptInvalidMac(string ciphertext)
        {
            Assert.ThrowsAny<Exception>(() =>
            {
                var privateKeyA =
                    ECPrivKey.Create(
                        Convert.FromHexString("d5633530f5bcfebceb5584cfbbf718a30df0751b729dd9a789b9f30c0587d74e"));
                var publicKeyB =
                    ECXOnlyPubKey.Create(
                        Convert.FromHexString("b74e6a341fb134127272b795a08b59250e5fa45a82a2eb4095e4ce9ed5f5e214"));
                NIP44.Decrypt(privateKeyA, publicKeyB, ciphertext);
            });
        }
       
               [Theory]
               [InlineData("")]
               [InlineData(
            "Anq2XbuLvCuONcr7V0UxTh8FAyWoZNEdBHXvdbNmDZHB573MI7R7rrTYftpqmvUpahmBC2sngmI14/L0HjOZ7lWGJlzdh6luiOnGPc46cGxf08MRC4CIuxx3i2Lm0KqgJ7vA")]
        public void TestDecryptInvalidPadding(string ciphertext)
        {
            Assert.ThrowsAny<Exception>(() =>
            {
                var privateKeyA =
                    ECPrivKey.Create(
                        Convert.FromHexString("d5633530f5bcfebceb5584cfbbf718a30df0751b729dd9a789b9f30c0587d74e"));
                var publicKeyB =
                    ECXOnlyPubKey.Create(
                        Convert.FromHexString("b74e6a341fb134127272b795a08b59250e5fa45a82a2eb4095e4ce9ed5f5e214"));


                NIP44.Decrypt(privateKeyA, publicKeyB, ciphertext);
            });
        }
    }
}
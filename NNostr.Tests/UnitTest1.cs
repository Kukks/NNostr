using System;
using System.Text;
using Xunit;

namespace NNostr.Tests
{
    public class UnitTest1
    {
        [Fact]
        public void Test1()
        {
            NBitcoin.Secp256k1.ECPrivKey.TryCreate(UTF8Encoding.UTF8.GetBytes("0000000000000000000000000000000000000000000000000000000000000003")
                , out var key);
            key.CreatePubKey().ToXOnlyPubKey().ToBytes()
        }
    }
}
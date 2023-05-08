using NNostr.Client.Protocols;
using Shouldly;
using System.Linq;
using System.Threading.Tasks;
using NNostr.Client;
using Xunit;

namespace NNostr.Tests
{
    public class Nip5Tests
    {
        [Theory]
        [InlineData("kukks@kukks.org", "22aa81510ee63fe2b16cae16e0921f78e9ba9882e2868e7e63ad6d08ae9b5954")]
        [InlineData("kori@no.str.cr", "1ad34e8aa265df5bd6106b4535a6a82528141efd800beb35b6413d7a8298741f")]
        public async Task CanGetNip5(string nip5, string pubKey)
        {
            NIP05.Parse(nip5).ShouldSatisfyAllConditions(
                x => x.ShouldNotBeNull()
            );

            var result = await NIP05.Validate(pubKey, nip5);
            result.ShouldSatisfyAllConditions(
                x => x.ShouldNotBeNull()    
            );
        }
    }
    public class Nip19Tests
    {
        [Fact]
        public async Task CanHandleNip19()
        {
          var npub = "npub10elfcs4fr0l0r8af98jlmgdh9c8tcxjvz9qkw038js35mp4dma8qzvjptg";
            var nsec = "nsec1vl029mgpspedva04g90vltkh6fvh240zqtv9k0t9af8935ke9laqsnlfe5";
            
            Assert.Equal("67dea2ed018072d675f5415ecfaed7d2597555e202d85b3d65ea4e58d2d92ffa",
                nsec.FromNIP19Nsec().ToHex());
            
            Assert.Equal("7e7e9c42a91bfef19fa929e5fda1b72e0ebc1a4c1141673e2794234d86addf4e",
                npub.FromNIP19Npub().ToHex());
            
            Assert.Equal(npub, npub.FromNIP19Npub().ToNIP19());
            Assert.Equal(nsec, nsec.FromNIP19Nsec().ToNIP19());

            var ournprofile =new NIP19.NosteProfileNote()
            {
                PubKey = "3bf0c63fcb93463407af97a5e5ee64fa883d107ef9e558472c4eb9aaaefa459d",
                Relays = new[] {"wss://r.x.com", "wss://djbas.sadkb.com"}
            };
            
            var nprofile =
                "nprofile1qqsrhuxx8l9ex335q7he0f09aej04zpazpl0ne2cgukyawd24mayt8gpp4mhxue69uhhytnc9e3k7mgpz4mhxue69uhkg6nzv9ejuumpv34kytnrdaksjlyr9p";

Assert.Equal(nprofile, 
    ournprofile.ToNIP19());            
            
            var note = Assert.IsType<NIP19.NosteProfileNote>(nprofile.FromNIP19Note());
            Assert.Equal("3bf0c63fcb93463407af97a5e5ee64fa883d107ef9e558472c4eb9aaaefa459d", note.PubKey);
            Assert.Equal(2, note.Relays.Length);
            Assert.True(note.Relays.Contains("wss://r.x.com"));
            Assert.True(note.Relays.Contains("wss://djbas.sadkb.com"));

        }
        
    }
}

using NNostr.Client.Protocols;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace NNostr.Tests
{
    public class Nip5Tests
    {
        [Theory]
        [InlineData("kori@nostress.cc", "1ad34e8aa265df5bd6106b4535a6a82528141efd800beb35b6413d7a8298741f")]
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
}

using System.Text.Json;

namespace NNostr.Client.Protocols
{
    /// <summary>
    /// https://github.com/nostr-protocol/nips/blob/master/05.md
    /// </summary>
    /// <param name="Username"></param>
    /// <param name="Host"></param>
    public class NIP05
    {
        public string Username { get; set; }
        public string Host { get; set; }

        public NIP05(string username, string host)
        {
            Username = username;
            Host = host;
        }

        public Uri Url => new("https://" + Host + "/.well-known/nostr.json?name=" + Username);

        public Uri SiteUrl => new("https://" + Host);

        public static NIP05? Parse(string nip05)
        {
            string[] parts = nip05.Split('@');
            if (parts.Length != 2)
            {
                return null;
            }
            return new NIP05(parts[0], parts[1]);
        }

        public static async Task<NIP05?> Validate(string pubkey, string nip05_str)
        {
            var nip05 = NIP05.Parse(nip05_str);
            if (nip05 == null)
            {
                return null;
            } 

            if (nip05.Url == null)
            {
                return null;
            }

            using var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(nip05.Url);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var stream = await response.Content.ReadAsStreamAsync();
            var decoded = await JsonSerializer.DeserializeAsync<NIP05Response>(stream);

            if (decoded?.Names == null || !decoded.Names.ContainsKey(nip05.Username))
            {
                return null;
            }

            if (decoded.Names[nip05.Username] != pubkey)
            {
                return null;
            }

            return nip05;
        }
    }
}
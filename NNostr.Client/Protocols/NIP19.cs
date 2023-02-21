using System.Text;
using LNURL;
using NBitcoin.Secp256k1;

namespace NNostr.Client.Protocols;

public static class NIP19
{
    public static ECXOnlyPubKey FromNIP19Npub(this string npub)
    {
        Bech32Engine.Decode(npub, out var hrp, out var data);
        if (hrp != "npub")
            throw new ArgumentException("Invalid NIP19 npub");
        return NostrExtensions.ParsePubKey(data);
    }

    public static string ToNIP19(this ECXOnlyPubKey key)
    {
        var data = key.ToBytes();
        return Bech32Engine.Encode("npub", data);
    }

    public static ECPrivKey FromNIP19Nsec(this string nsec)
    {
        Bech32Engine.Decode(nsec, out var hrp, out var data);
        if (hrp != "nsec")
            throw new ArgumentException("Invalid NIP19 nsec");
        return NostrExtensions.ParseKey(data);
    }

    public static string ToNIP19(this ECPrivKey key)
    {
        var data = key.ToBytes();
        return Bech32Engine.Encode("nsec", data);
    }

    public static NostrNote? FromNIP19Note(this string note)
    {
        Bech32Engine.Decode(note, out var hrp, out var data);
        foreach (var noteParser in NoteParsers)
        {
            if (noteParser.TryParse(hrp, data, out var n))
                return n;
        }

        return null;
    }

    private static BaseNostrNoteParser[] NoteParsers =
    {
        new NostrProfileNoteParser(),
        new NostrEventNoteParser(),
        new NostrAddressNoteParser(),
        new NostrRelayNoteParser()
    };

    public interface NostrNote
    {
    }

    public abstract class BaseNostrNoteParser
    {
        public abstract bool TryParse(string hrp, byte[] data, out NostrNote? note);
    }

    abstract class BaseNostrNoteParser<T> : BaseNostrNoteParser where T : class, NostrNote
    {
        protected abstract string Hrp { get; }

        bool TryParse(string hrp, byte[] data, out T? note)
        {
            note = null;
            if (hrp != Hrp)
                return false;
            try
            {
                note = Parse(data);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public abstract T? Parse(byte[] data);

        public override bool TryParse(string hrp, byte[] data, out NostrNote? note)
        {
            var res = TryParse(hrp, data, out var noteTyped);
            note = noteTyped;
            return res;
        }
    }

    public class NosteProfileNote : NostrNote
    {
        public string PubKey { get; set; }
        public string[] Relays { get; set; }
    }

    class NostrProfileNoteParser : BaseNostrNoteParser<NosteProfileNote>
    {
        protected override string Hrp => "nprofile";

        public override NosteProfileNote? Parse(byte[] data)
        {
            var tlv = ParseTLV(data);
            return new NosteProfileNote()
            {
                PubKey = tlv.Single(pair => pair.Key == 0x00).Value.AsSpan().ToHex(),
                Relays = tlv.Where(pair => pair.Key == 0x01).Select(pair => Encoding.ASCII.GetString(pair.Value))
                    .ToArray()
            };
        }
    }

    public class NostrEventNote : NostrNote
    {
        public string EventId { get; set; }
        public string[] Relays { get; set; }
    }

    class NostrEventNoteParser : BaseNostrNoteParser<NostrEventNote>
    {
        protected override string Hrp => "nevent";

        public override NostrEventNote? Parse(byte[] data)
        {
            var tlv = ParseTLV(data);
            return new NostrEventNote()
            {
                EventId = tlv.Single(pair => pair.Key == 0x00).Value.AsSpan().ToHex(),
                Relays = tlv.Where(pair => pair.Key == 0x01).Select(pair => Encoding.ASCII.GetString(pair.Value))
                    .ToArray()
            };
        }
    }

    public class NostrRelayNote : NostrNote
    {
        public string Relay { get; set; }
    }

    class NostrRelayNoteParser : BaseNostrNoteParser<NostrRelayNote>
    {
        string Relay { get; set; }
        protected override string Hrp => "nrelay";

        public override NostrRelayNote? Parse(byte[] data)
        {
            var tlv = ParseTLV(data);
            return new NostrRelayNote()
            {
                Relay = tlv.Single(pair => pair.Key == 0x00).Value.AsSpan().ToHex()
            };
        }
    }

    public class NostrAddressNote : NostrNote
    {
        public string Identifier { get; set; }
        public string[] Relays { get; set; }
        public string Author { get; set; }
        public uint Kind { get; set; }
    }

    class NostrAddressNoteParser : BaseNostrNoteParser<NostrAddressNote>
    {
        protected override string Hrp => "naddr";

        public override NostrAddressNote? Parse(byte[] data)
        {
            var tlv = ParseTLV(data);
            return new NostrAddressNote()
            {
                Identifier = tlv.Single(pair => pair.Key == 0x00).Value.AsSpan().ToHex(),
                Author = tlv.Single(pair => pair.Key == 0x02).Value.AsSpan().ToHex(),
                Kind = tlv.Single(pair => pair.Key == 0x03).Value
                    .Aggregate<byte, uint>(0, (current, t) => (current << 8) | t),
                Relays = tlv.Where(pair => pair.Key == 0x01).Select(pair => Encoding.ASCII.GetString(pair.Value))
                    .ToArray()
            };
        }
    }


    private static List<KeyValuePair<byte, byte[]>> ParseTLV(byte[] tlvData)
    {
        var result = new List<KeyValuePair<byte, byte[]>>();
        var pos = 0;
        while (pos < tlvData.Length)
        {
            var tag = tlvData[pos++];
            int length = tlvData[pos++];

            // handle extended length encoding
            if ((length & 0x80) != 0)
            {
                int lengthBytes = length & 0x7F;
                length = 0;
                for (int i = 0; i < lengthBytes; i++)
                {
                    length = (length << 8) + tlvData[pos++];
                }
            }

            var value = new byte[length];
            Array.Copy(tlvData, pos, value, 0, length);
            result.Add(new KeyValuePair<byte, byte[]>(tag, value));
            pos += length;
        }

        return result;
    }
}
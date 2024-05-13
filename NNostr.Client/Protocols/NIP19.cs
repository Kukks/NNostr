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
        string ToNIP19();
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
        public string ToNIP19()
        {
            var tlvData = new List<(byte, byte[])>
            {
                (NostrProfileNoteParser.PubKeyKey, Convert.FromHexString(PubKey)),
            };
            tlvData.AddRange(Relays.Select(relay => (NostrProfileNoteParser.RelayKey, Encoding.ASCII.GetBytes(relay))));
            var tlv = BuildTLV(tlvData);
            return Bech32Engine.Encode(NostrProfileNoteParser.HRP, tlv);
        }
    }

    class NostrProfileNoteParser : BaseNostrNoteParser<NosteProfileNote>
    {
        protected override string Hrp => HRP;
        
        public const string HRP = "nprofile";

        
        public const byte PubKeyKey = 0x00;
        public const byte RelayKey = 0x01;
        public override NosteProfileNote? Parse(byte[] data)
        {
            var tlv = ParseTLV(data);
            return new NosteProfileNote()
            {
                PubKey = tlv.Single(pair => pair.Key == PubKeyKey).Value.AsSpan().ToHex(),
                Relays = tlv.Where(pair => pair.Key == RelayKey).Select(pair => Encoding.ASCII.GetString(pair.Value))
                    .ToArray()
            };
        }
    }

    public class NostrEventNote : NostrNote
    {
        public string EventId { get; set; }
        public string[] Relays { get; set; }
        public string ToNIP19()
        {
            var tlvData = new List<(byte, byte[])>
            {
                (NostrEventNoteParser.EventIdKey, Convert.FromHexString(EventId)),
            };
            tlvData.AddRange(Relays.Select(relay => (NostrEventNoteParser.RelayKey, Encoding.ASCII.GetBytes(relay))));
            var tlv = BuildTLV(tlvData);
            return Bech32Engine.Encode(NostrEventNoteParser.HRP, tlv);
        }
    }

    class NostrEventNoteParser : BaseNostrNoteParser<NostrEventNote>
    {
        protected override string Hrp => HRP;
        public const string HRP = "nevent";
        
        public const byte EventIdKey = 0x00;
        public const byte RelayKey = 0x01;

        public override NostrEventNote? Parse(byte[] data)
        {
            var tlv = ParseTLV(data);
            return new NostrEventNote()
            {
                EventId = tlv.Single(pair => pair.Key == EventIdKey).Value.AsSpan().ToHex(),
                Relays = tlv.Where(pair => pair.Key == RelayKey).Select(pair => Encoding.ASCII.GetString(pair.Value))
                    .ToArray()
            };
        }
    }

    public class NostrRelayNote : NostrNote
    {
        public string Relay { get; set; }

        public string ToNIP19()
        {
            var tlvData = new List<(byte, byte[])>
            {
                (NostrRelayNoteParser.RelayKey, Encoding.UTF8.GetBytes(Relay))
            };
            var tlv = BuildTLV(tlvData);
            return Bech32Engine.Encode(NostrRelayNoteParser.HRP, tlv);
        }
    }

    class NostrRelayNoteParser : BaseNostrNoteParser<NostrRelayNote>
    {
        public const byte RelayKey = 0x00;
        protected override string Hrp => HRP;
        public const string HRP = "nrelay";

        public override NostrRelayNote? Parse(byte[] data)
        {
            var tlv = ParseTLV(data);
            return new NostrRelayNote()
            {
                Relay = Encoding.UTF8.GetString(tlv.Single(pair => pair.Key == RelayKey).Value.AsSpan())
            };
        }
    }

    public class NostrAddressNote : NostrNote
    {
        public string Identifier { get; set; }
        public string[] Relays { get; set; }
        public string Author { get; set; }
        public uint Kind { get; set; }
        public string ToNIP19()
        {
            var tlvData = new List<(byte, byte[])>
            {
                (NostrAddressNoteParser.IdentifierKey, Convert.FromHexString(Identifier)),
                (NostrAddressNoteParser.AuthorKey, Convert.FromHexString(Author)),
                (NostrAddressNoteParser.KindKey, BitConverter.GetBytes(Kind).Reverse().ToArray()),
                
            };
            tlvData.AddRange(Relays.Select(relay => (NostrAddressNoteParser.RelayKey, Encoding.ASCII.GetBytes(relay))));
            var tlv = BuildTLV(tlvData);
            return Bech32Engine.Encode(NostrAddressNoteParser.HRP, tlv);
        }
    }

    class NostrAddressNoteParser : BaseNostrNoteParser<NostrAddressNote>
    {
        public const string HRP = "naddr";
        protected override string Hrp => HRP;
public const byte IdentifierKey = 0x00;
        public const byte RelayKey = 0x01;
        public const byte AuthorKey = 0x02;
        public const byte KindKey = 0x03;
        
        public override NostrAddressNote? Parse(byte[] data)
        {
            var tlv = ParseTLV(data);
            return new NostrAddressNote()
            {
                Identifier = tlv.Single(pair => pair.Key == IdentifierKey).Value.AsSpan().ToHex(),
                Author = tlv.Single(pair => pair.Key == AuthorKey).Value.AsSpan().ToHex(),
                Kind = tlv.Single(pair => pair.Key == KindKey).Value
                    .Aggregate<byte, uint>(0, (current, t) => (current << 8) | t),
                Relays = tlv.Where(pair => pair.Key == RelayKey).Select(pair => Encoding.ASCII.GetString(pair.Value))
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
    
    private static byte[] BuildTLV(List<(byte, byte[])> tlvList)
    {
        var result = new List<byte>();

        foreach (var item in tlvList)
        {
            var tag = item.Item1;
            var value = item.Item2;
            var length = value.Length;

            // handle extended length encoding
            var lengthBytes = length > 127 ? (byte)Math.Ceiling(length / 256.0) : (byte)0;
            if (lengthBytes > 0)
            {
                length = (int)Math.Pow(256, lengthBytes) + length;
            }

            result.Add(tag);
            if (lengthBytes > 0)
            {
                result.Add((byte)(0x80 | lengthBytes));
                for (int i = lengthBytes - 1; i >= 0; i--)
                {
                    result.Add((byte)(length / (int)Math.Pow(256, i)));
                }
            }
            else
            {
                result.Add((byte)length);
            }

            result.AddRange(value);
        }

        return result.ToArray();
    }
}
/* Copyright (c) 2017 Guillaume Bonnot and Palekhov Ilia
 * Based on the work of Pieter Wuille
 * Special Thanks to adiabat
 *                  
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */

using System.Diagnostics;

namespace LNURL;

internal static class Bech32Engine
{
    // charset is the sequence of ascii characters that make up the bech32
    // alphabet.  Each character represents a 5-bit squashed byte.
    // q = 0b00000, p = 0b00001, z = 0b00010, and so on.

    private const string charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";

    // used for polymod
    private static readonly uint[] generator = {0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3};

    // icharset is a mapping of 8-bit ascii characters to the charset
    // positions.  Both uppercase and lowercase ascii are mapped to the 5-bit
    // position values.
    private static readonly short[] icharset =
    {
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        15, -1, 10, 17, 21, 20, 26, 30, 7, 5, -1, -1, -1, -1, -1, -1,
        -1, 29, -1, 24, 13, 25, 9, 8, 23, -1, 18, 22, 31, 27, 19, -1,
        1, 0, 3, 16, 11, 28, 12, 14, 6, 4, 2, -1, -1, -1, -1, -1,
        -1, 29, -1, 24, 13, 25, 9, 8, 23, -1, 18, 22, 31, 27, 19, -1,
        1, 0, 3, 16, 11, 28, 12, 14, 6, 4, 2, -1, -1, -1, -1, -1
    };

    // PolyMod takes a byte slice and returns the 32-bit BCH checksum.
    // Note that the input bytes to PolyMod need to be squashed to 5-bits tall
    // before being used in this function.  And this function will not error,
    // but instead return an unsuable checksum, if you give it full-height bytes.
    public static uint PolyMod(byte[] values)
    {
        uint chk = 1;
        foreach (var value in values)
        {
            var top = chk >> 25;
            chk = ((chk & 0x1ffffff) << 5) ^ value;
            for (var i = 0; i < 5; ++i)
                if (((top >> i) & 1) == 1)
                    chk ^= generator[i];
        }

        return chk;
    }


    // on error, data == null
    public static void Decode(string encoded, out string hrp, out byte[] data)
    {
        byte[] squashed;
        DecodeSquashed(encoded, out hrp, out squashed);
        if (squashed == null)
        {
            data = null;
            return;
        }

        data = Bytes5to8(squashed);
    }

    // on error, data == null
    private static void DecodeSquashed(string adr, out string hrp, out byte[] data)
    {
        adr = CheckAndFormat(adr);
        if (adr == null)
        {
            data = null;
            hrp = null;
            return;
        }

        // find the last "1" and split there
        var splitLoc = adr.LastIndexOf("1");
        if (splitLoc == -1)
        {
            Debug.WriteLine("1 separator not present in address");
            data = null;
            hrp = null;
            return;
        }

        // hrp comes before the split
        hrp = adr.Substring(0, splitLoc);

        // get squashed data
        var squashed = StringToSquashedBytes(adr.Substring(splitLoc + 1));
        if (squashed == null)
        {
            data = null;
            return;
        }

        // make sure checksum works
        if (!VerifyChecksum(hrp, squashed))
        {
            Debug.WriteLine("Checksum invalid");
            data = null;
            return;
        }

        // chop off checksum to return only payload
        var length = squashed.Length - 6;
        data = new byte[length];
        Array.Copy(squashed, 0, data, 0, length);
    }

    // on error, return null
    private static string CheckAndFormat(string adr)
    {
        // make an all lowercase and all uppercase version of the input string
        var lowAdr = adr.ToLower();
        var highAdr = adr.ToUpper();

        // if there's mixed case, that's not OK
        if (adr != lowAdr && adr != highAdr)
        {
            Debug.WriteLine("mixed case address");
            return null;
        }

        // default to lowercase
        return lowAdr;
    }

    private static bool VerifyChecksum(string hrp, byte[] data)
    {
        var values = HRPExpand(hrp).Concat(data).ToArray();
        var checksum = PolyMod(values);
        // make sure it's 1 (from the LSB flip in CreateChecksum
        return checksum == 1;
    }

    // on error, return null
    private static byte[] StringToSquashedBytes(string input)
    {
        var squashed = new byte[input.Length];

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            var buffer = icharset[c];
            if (buffer == -1)
            {
                Debug.WriteLine("contains invalid character " + c);
                return null;
            }

            squashed[i] = (byte) buffer;
        }

        return squashed;
    }

    // we encode the data and the human readable prefix
    public static string Encode(string hrp, byte[] data)
    {
        var base5 = Bytes8to5(data);
        if (base5 == null)
            return string.Empty;
        return EncodeSquashed(hrp, base5);
    }

    // on error, return null
    private static string EncodeSquashed(string hrp, byte[] data)
    {
        var checksum = CreateChecksum(hrp, data);
        var combined = data.Concat(checksum).ToArray();

        // Should be squashed, return empty string if it's not.
        var encoded = SquashedBytesToString(combined);
        if (encoded == null)
            return null;
        return hrp + "1" + encoded;
    }

    private static byte[] CreateChecksum(string hrp, byte[] data)
    {
        var values = HRPExpand(hrp).Concat(data).ToArray();
        // put 6 zero bytes on at the end
        values = values.Concat(new byte[6]).ToArray();
        //get checksum for whole slice

        // flip the LSB of the checksum data after creating it
        var checksum = PolyMod(values) ^ 1;

        var ret = new byte[6];
        for (var i = 0; i < 6; i++)
            // note that this is NOT the same as converting 8 to 5
            // this is it's own expansion to 6 bytes from 4, chopping
            // off the MSBs.
            ret[i] = (byte) ((checksum >> (5 * (5 - i))) & 0x1f);

        return ret;
    }

    // HRPExpand turns the human redable part into 5bit-bytes for later processing
    private static byte[] HRPExpand(string input)
    {
        var output = new byte[input.Length * 2 + 1];

        // first half is the input string shifted down 5 bits.
        // not much is going on there in terms of data / entropy
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            output[i] = (byte) (c >> 5);
        }

        // then there's a 0 byte separator
        // don't need to set 0 byte in the middle, as it starts out that way

        // second half is the input string, with the top 3 bits zeroed.
        // most of the data / entropy will live here.
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            output[i + input.Length + 1] = (byte) (c & 0x1f);
        }

        return output;
    }

    private static string SquashedBytesToString(byte[] input)
    {
        var s = string.Empty;
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if ((c & 0xe0) != 0)
            {
                Debug.WriteLine("high bits set at position {0}: {1}", i, c);
                return null;
            }

            s += charset[c];
        }

        return s;
    }

    private static byte[] Bytes8to5(byte[] data)
    {
        return ByteSquasher(data, 8, 5);
    }

    private static byte[] Bytes5to8(byte[] data)
    {
        return ByteSquasher(data, 5, 8);
    }

    // ByteSquasher squashes full-width (8-bit) bytes into "squashed" 5-bit bytes,
    // and vice versa.  It can operate on other widths but in this package only
    // goes 5 to 8 and back again.  It can return null if the squashed input
    // you give it isn't actually squashed, or if there is padding (trailing q characters)
    // when going from 5 to 8
    private static byte[] ByteSquasher(byte[] input, int inputWidth, int outputWidth)
    {
        var bitstash = 0;
        var accumulator = 0;
        var output = new List<byte>();
        var maxOutputValue = (1 << outputWidth) - 1;

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c >> inputWidth != 0)
            {
                Debug.WriteLine("byte {0} ({1}) high bits set", i, c);
                return null;
            }

            accumulator = (accumulator << inputWidth) | c;
            bitstash += inputWidth;
            while (bitstash >= outputWidth)
            {
                bitstash -= outputWidth;
                output.Add((byte) ((accumulator >> bitstash) & maxOutputValue));
            }
        }

        // pad if going from 8 to 5
        if (inputWidth == 8 && outputWidth == 5)
        {
            if (bitstash != 0) output.Add((byte) ((accumulator << (outputWidth - bitstash)) & maxOutputValue));
        }
        else if (bitstash >= inputWidth || ((accumulator << (outputWidth - bitstash)) & maxOutputValue) != 0)
        {
            // no pad from 5 to 8 allowed
            Debug.WriteLine("invalid padding from {0} to {1} bits", inputWidth, outputWidth);
            return null;
        }

        return output.ToArray();
    }
}
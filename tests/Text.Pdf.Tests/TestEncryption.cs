using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// Builds an AES-128 (V4/R4) encrypted document from the specification, the way a producer of the
/// PDF/A era wrote one: an MD5-derived file key, RC4 <c>/O</c> and <c>/U</c>, and AES-128 with a key
/// per object.
/// </summary>
/// <remarks>
/// This is deliberately a second implementation, not a call into the library: if the reader and the
/// fixture shared the key-derivation code, a wrong Algorithm 2 would agree with itself and every
/// test would still pass. Only the encrypt direction is written here, which is also what an
/// independent producer would have.
/// </remarks>
internal static class TestEncryption
{
    /// <summary>The password that opens the fixture as a user.</summary>
    public const string UserPassword = "user-pass";

    /// <summary>The password that opens the fixture as its owner.</summary>
    public const string OwnerPassword = "owner-pass";

    /// <summary>The text the fixture's page draws, which a decrypted copy must still show.</summary>
    public const string ContentText = "AES-128 fixture";

    /// <summary>The title the fixture's information dictionary carries, encrypted like any string.</summary>
    public const string Title = "Encrypted by hand";

    /// <summary>
    /// The <c>/P</c> value the fixture declares: printing and copying are allowed, changing and
    /// annotating are not. The reserved bits are the ones the specification fixes.
    /// </summary>
    public const int Permissions = unchecked((int)(0xFFFFF0C0u | 0x14u));

    /// <summary>The 32-byte padding string of Algorithm 2.</summary>
    private static readonly byte[] Padding =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
        0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
        0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    ];

    /// <summary>A one-page document encrypted with AES-128, an information title and no compression.</summary>
    public static byte[] Aes128()
    {
        var identifier = Encoding.ASCII.GetBytes("0123456789abcdef");

        var user = Pad(UserPassword);
        var owner = Pad(OwnerPassword);

        var o = ComputeO(owner, user);
        var key = FileKey(user, o, Permissions, identifier);
        var u = ComputeU(key, identifier);

        var content = Encoding.Latin1.GetBytes("BT /F1 24 Tf 72 720 Td (" + ContentText + ") Tj ET");
        var encryptedContent = Encrypt(key, 5, 0, content, [.. Enumerable0(16)]);
        var encryptedTitle = Encrypt(key, 6, 0, Encoding.Latin1.GetBytes(Title), [.. Enumerable0(16)]);

        // The page's font needs a /ToUnicode map for the text probe to read the marker back, and the
        // CMap is a stream like any other, so it is encrypted with the object's own key.
        var encryptedToUnicode = Encrypt(key, 8, 0, Encoding.Latin1.GetBytes(ToUnicode(ContentText)), [.. Enumerable0(16)]);

        var bodies = new List<byte[]>
        {
            Ascii("<</Type/Catalog/Pages 2 0 R>>"),
            Ascii("<</Type/Pages/Count 1/Kids[4 0 R]>>"),
            Ascii("<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding/ToUnicode 8 0 R>>"),
            Ascii("<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Resources<</Font<</F1 3 0 R>>>>/Contents 5 0 R>>"),
            Stream(encryptedContent),
            Ascii("<</Title<" + Convert.ToHexString(encryptedTitle) + ">>>"),
            Ascii("<</Filter/Standard/V 4/R 4/Length 128"
                + "/CF<</StdCF<</CFM/AESV2/Length 16>>>>/StmF/StdCF/StrF/StdCF"
                + "/O<" + Convert.ToHexString(o) + ">"
                + "/U<" + Convert.ToHexString(u) + ">"
                + "/P " + Permissions.ToString(CultureInfo.InvariantCulture) + ">>"),
            Stream(encryptedToUnicode),
        };

        return Document(
            bodies,
            "/Info 6 0 R/ID[<" + Convert.ToHexString(identifier) + "><" + Convert.ToHexString(identifier) + ">]/Encrypt 7 0 R");
    }

    /// <summary>Algorithm 3: the <c>/O</c> entry, in RC4 whatever the crypt filters say.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5351",
        Justification = "The fixture implements what revision 4 of the standard security handler specifies, which is MD5.")]
    private static byte[] ComputeO(byte[] owner, byte[] user)
    {
        var hash = MD5.HashData(owner);

        for (var round = 0; round < 50; round++)
        {
            hash = MD5.HashData(hash);
        }

        var value = Rc4(hash, user);

        for (var round = 1; round <= 19; round++)
        {
            value = Rc4(Xor(hash, (byte)round), value);
        }

        return value;
    }

    /// <summary>Algorithm 2: the file key an R4 document derives from the padded user password.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5351",
        Justification = "The fixture implements what revision 4 of the standard security handler specifies, which is MD5.")]
    private static byte[] FileKey(byte[] user, byte[] owner, int permissions, byte[] identifier)
    {
        var data = new byte[32 + 32 + 4 + identifier.Length];
        user.CopyTo(data, 0);
        owner.AsSpan(0, 32).CopyTo(data.AsSpan(32));
        BitConverter.GetBytes(permissions).CopyTo(data, 64);
        identifier.CopyTo(data, 68);

        var hash = MD5.HashData(data);

        for (var round = 0; round < 50; round++)
        {
            hash = MD5.HashData(hash);
        }

        return hash;
    }

    /// <summary>Algorithm 5: the <c>/U</c> entry, sixteen bytes then sixteen that do not matter.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5351",
        Justification = "The fixture implements what revision 4 of the standard security handler specifies, which is MD5.")]
    private static byte[] ComputeU(byte[] key, byte[] identifier)
    {
        var data = new byte[32 + identifier.Length];
        Padding.CopyTo(data, 0);
        identifier.CopyTo(data, 32);

        var value = Rc4(key, MD5.HashData(data));

        for (var round = 1; round <= 19; round++)
        {
            value = Rc4(Xor(key, (byte)round), value);
        }

        var u = new byte[32];
        value.CopyTo(u, 0);
        data.AsSpan(0, 16).CopyTo(u.AsSpan(16));

        return u;
    }

    /// <summary>Algorithm 1 and the AES-128 crypt filter: an initialisation vector then ciphertext.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5351",
        Justification = "The fixture implements what revision 4 of the standard security handler specifies, which is MD5.")]
    private static byte[] Encrypt(byte[] fileKey, int number, int generation, byte[] data, byte[] iv)
    {
        var material = new byte[fileKey.Length + 5 + 4];
        fileKey.CopyTo(material, 0);
        material[16] = (byte)number;
        material[17] = (byte)(number >> 8);
        material[18] = (byte)(number >> 16);
        material[19] = (byte)generation;
        material[20] = (byte)(generation >> 8);
        "sAlT"u8.CopyTo(material.AsSpan(21));

        var objectKey = MD5.HashData(material);

        using var aes = Aes.Create();
        aes.Key = objectKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var cipher = encryptor.TransformFinalBlock(data, 0, data.Length);

        var result = new byte[16 + cipher.Length];
        iv.CopyTo(result, 0);
        cipher.CopyTo(result, 16);

        return result;
    }

    private static byte[] Rc4(byte[] key, byte[] data)
    {
        var state = new byte[256];
        for (var index = 0; index < 256; index++)
        {
            state[index] = (byte)index;
        }

        var j = 0;
        for (var index = 0; index < 256; index++)
        {
            j = (j + state[index] + key[index % key.Length]) & 0xFF;
            (state[index], state[j]) = (state[j], state[index]);
        }

        var result = new byte[data.Length];
        var a = 0;
        var b = 0;

        for (var index = 0; index < data.Length; index++)
        {
            a = (a + 1) & 0xFF;
            b = (b + state[a]) & 0xFF;
            (state[a], state[b]) = (state[b], state[a]);
            result[index] = (byte)(data[index] ^ state[(state[a] + state[b]) & 0xFF]);
        }

        return result;
    }

    private static byte[] Xor(byte[] key, byte value)
    {
        var copy = new byte[key.Length];
        for (var index = 0; index < copy.Length; index++)
        {
            copy[index] = (byte)(key[index] ^ value);
        }

        return copy;
    }

    private static byte[] Pad(string password)
    {
        var padded = new byte[32];
        var bytes = Encoding.ASCII.GetBytes(password);

        bytes.CopyTo(padded, 0);

        for (var index = bytes.Length; index < 32; index++)
        {
            padded[index] = Padding[index - bytes.Length];
        }

        return padded;
    }

    private static IEnumerable<byte> Enumerable0(int count)
    {
        for (var index = 0; index < count; index++)
        {
            yield return (byte)index;
        }
    }

    private static byte[] Ascii(string text) => Encoding.Latin1.GetBytes(text);

    /// <summary>A one-byte CMap mapping every character the fixture draws to itself.</summary>
    private static string ToUnicode(string text)
    {
        var codes = new SortedSet<int>();
        foreach (var character in text)
        {
            codes.Add(character);
        }

        var builder = new StringBuilder();
        builder.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n")
            .Append("/CMapName /Fixture def\n/CMapType 2 def\n")
            .Append("1 begincodespacerange\n<00> <FF>\nendcodespacerange\n")
            .Append(codes.Count.ToString(CultureInfo.InvariantCulture))
            .Append(" beginbfchar\n");

        foreach (var code in codes)
        {
            builder.Append('<').Append(code.ToString("X2", CultureInfo.InvariantCulture))
                .Append("> <").Append(code.ToString("X4", CultureInfo.InvariantCulture)).Append(">\n");
        }

        builder.Append("endbfchar\nendcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");

        return builder.ToString();
    }

    private static byte[] Stream(byte[] data)
    {
        var prefix = Ascii("<</Length " + data.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n");
        var result = new byte[prefix.Length + data.Length + "\nendstream".Length];

        prefix.CopyTo(result, 0);
        data.CopyTo(result, prefix.Length);
        Ascii("\nendstream").CopyTo(result, prefix.Length + data.Length);

        return result;
    }

    /// <summary>Writes the objects with a classic cross-reference table, as a producer would.</summary>
    private static byte[] Document(List<byte[]> bodies, string trailerExtra)
    {
        using var output = new MemoryStream();
        var offsets = new List<long>();

        Write(output, "%PDF-1.7\n");
        Write(output, "%\u00e2\u00e3\u00cf\u00d3\n");

        for (var index = 0; index < bodies.Count; index++)
        {
            offsets.Add(output.Length);
            Write(output, (index + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
            output.Write(bodies[index]);
            Write(output, "\nendobj\n");
        }

        var xref = output.Length;
        var size = bodies.Count + 1;

        Write(output, "xref\n0 " + size.ToString(CultureInfo.InvariantCulture) + "\n");
        Write(output, "0000000000 65535 f \n");

        foreach (var offset in offsets)
        {
            Write(output, offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        Write(output, "trailer\n<</Size " + size.ToString(CultureInfo.InvariantCulture) + "/Root 1 0 R"
            + trailerExtra + ">>\nstartxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");

        return output.ToArray();
    }

    private static void Write(Stream output, string text) => output.Write(Encoding.Latin1.GetBytes(text));
}

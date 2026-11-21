using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// The standard security handler: password validation, key derivation and per-object encryption, for
/// the two revisions real documents carry — AES-256 (V5/R6) and AES-128 (V4/R4 with an
/// <c>/AESV2</c> crypt filter).
/// </summary>
/// <remarks>
/// <para>
/// The two revisions share a shape and nothing else. R4 derives a file key from the user password
/// with MD5 and validates it through the RC4-based <c>/U</c> entry; R6 derives it with the hardened
/// SHA-256 algorithm of ISO 32000-2 and validates it through the full 32-byte <c>/U</c> hash. Both
/// then encrypt strings and streams with AES in CBC mode, a random 16-byte initialisation vector
/// written in front of the ciphertext, and PKCS#7 padding — but R4 derives a key per object from the
/// object number and generation, where R6 uses the file key as it is.
/// </para>
/// <para>
/// The RC4 path is here only for the <c>/O</c> and <c>/U</c> entries, which R4 still defines in
/// terms of RC4 whatever the crypt filters say. RC4-encrypted content, revision 2, 3 and 5, and
/// public-key encryption are refused rather than half-supported.
/// </para>
/// </remarks>
internal sealed class PdfStandardSecurity
{
    /// <summary>The 32-byte padding string of Algorithm 2, which every password is padded with.</summary>
    private static readonly byte[] PasswordPadding =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
        0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
        0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    ];

    /// <summary>The permission bits of <c>/P</c>, which are bits 3 to 6 and 9 to 12, counted from 1.</summary>
    private const uint PermissionBits = 0x0F3C;

    /// <summary>The bits <c>/P</c> carries besides the permissions: two reserved ones and the upper twenty.</summary>
    private const uint FixedPermissionBits = 0xFFFFF0C0;

    /// <summary>The largest password the specification defines, in bytes.</summary>
    private const int MaxPasswordBytes = 127;

    private readonly byte[] _fileKey;
    private readonly int _revision;
    private readonly int _keyLength;

    private PdfStandardSecurity(
        byte[] fileKey,
        int revision,
        int keyLength,
        PdfPermissions permissions,
        bool openedWithOwnerPassword,
        bool encryptsStrings,
        bool encryptsStreams)
    {
        _fileKey = fileKey;
        _revision = revision;
        _keyLength = keyLength;
        Permissions = permissions;
        OpenedWithOwnerPassword = openedWithOwnerPassword;
        EncryptsStrings = encryptsStrings;
        EncryptsStreams = encryptsStreams;
    }

    /// <summary>The revision of the handler: 4 or 6.</summary>
    public int Revision => _revision;

    /// <summary>The permissions <c>/P</c> declares.</summary>
    public PdfPermissions Permissions { get; }

    /// <summary>Whether the password matched <c>/O</c>, which grants everything.</summary>
    public bool OpenedWithOwnerPassword { get; }

    /// <summary>Whether strings are encrypted, which a <c>/StrF /Identity</c> filter turns off.</summary>
    public bool EncryptsStrings { get; }

    /// <summary>Whether streams are encrypted, which a <c>/StmF /Identity</c> filter turns off.</summary>
    public bool EncryptsStreams { get; }

    /// <summary>
    /// Reads <paramref name="encrypt"/>, validates <paramref name="password"/> against it and derives
    /// the file key.
    /// </summary>
    /// <exception cref="PdfEncryptionException">
    /// The password is wrong, the handler is not one of the two revisions supported, or the
    /// dictionary is malformed.
    /// </exception>
    public static PdfStandardSecurity ForReading(PdfFile file, PdfDictionary encrypt, string password)
    {
        var version = Integer(file, encrypt, "V", -1);
        var revision = Integer(file, encrypt, "R", -1);

        var filter = file.Resolve(encrypt["Filter"]) as PdfName;

        if (filter is null)
        {
            throw Malformed("the /Encrypt dictionary names no /Filter");
        }

        if (!string.Equals(filter.Value, "Standard", StringComparison.Ordinal))
        {
            throw new PdfEncryptionException(
                $"the document uses the '{filter.Value}' security handler; public-key encryption wraps the file key for a certificate rather than a password, and only the standard handler is supported");
        }

        var owner = Bytes(file, encrypt, "O")
            ?? throw Malformed("the /Encrypt dictionary has no /O entry");
        var user = Bytes(file, encrypt, "U")
            ?? throw Malformed("the /Encrypt dictionary has no /U entry");

        var raw = Integer(file, encrypt, "P", unchecked((int)FixedPermissionBits) | unchecked((int)PdfPermissions.All));
        var permissions = (PdfPermissions)((uint)raw & PermissionBits);
        var encryptMetadata = file.Resolve(encrypt["EncryptMetadata"]) is not PdfBoolean flag || flag.Value;

        return (version, revision) switch
        {
            (5, 6) => ReadAes256(file, encrypt, password, owner, user, permissions),
            (4, 4) => ReadAes128(file, encrypt, password, owner, user, raw, permissions, encryptMetadata),
            (5, 5) => throw new PdfEncryptionException(
                "the document uses AES-256 with revision 5, which ISO 32000-2 withdrew before publication; only revision 6 (V5/R6) and AES-128 revision 4 (V4/R4) are supported"),
            _ => throw new PdfEncryptionException(
                $"the document uses the standard security handler with V={version}/R={revision}; only AES-256 (V5/R6) and AES-128 (V4/R4) are supported, and RC4 and public-key encryption are out of scope"),
        };
    }

    /// <summary>
    /// Builds the <c>/Encrypt</c> dictionary of a new AES-256 document and the handler that encrypts
    /// its objects.
    /// </summary>
    /// <param name="userPassword">The password a reader needs to open the document.</param>
    /// <param name="ownerPassword">The password that also grants every permission.</param>
    /// <param name="permissions">What the user password permits.</param>
    /// <param name="dictionary">The dictionary to write as an indirect object.</param>
    /// <param name="identifier">The 16 bytes of the first element of the trailer's <c>/ID</c>.</param>
    public static PdfStandardSecurity ForWriting(
        string userPassword,
        string ownerPassword,
        PdfPermissions permissions,
        out PdfDictionary dictionary,
        out byte[] identifier)
    {
        var user = Password(userPassword);
        var owner = Password(ownerPassword);

        var fileKey = RandomNumberGenerator.GetBytes(32);
        identifier = RandomNumberGenerator.GetBytes(16);

        var userValidationSalt = RandomNumberGenerator.GetBytes(8);
        var userKeySalt = RandomNumberGenerator.GetBytes(8);
        var u = Concat(
            Hash2B(user, userValidationSalt, []),
            userValidationSalt,
            userKeySalt);
        var ue = AesWithoutPadding(Hash2B(user, userKeySalt, []), new byte[16], fileKey, decrypt: false);

        var ownerValidationSalt = RandomNumberGenerator.GetBytes(8);
        var ownerKeySalt = RandomNumberGenerator.GetBytes(8);
        var o = Concat(
            Hash2B(owner, ownerValidationSalt, u),
            ownerValidationSalt,
            ownerKeySalt);
        var oe = AesWithoutPadding(Hash2B(owner, ownerKeySalt, u), new byte[16], fileKey, decrypt: false);

        var raw = unchecked((int)(FixedPermissionBits | (uint)permissions));
        var perms = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(perms, raw);
        perms[4] = 0xFF;
        perms[5] = 0xFF;
        perms[6] = 0xFF;
        perms[7] = 0xFF;
        perms[8] = (byte)'T';
        perms[9] = (byte)'a';
        perms[10] = (byte)'d';
        perms[11] = (byte)'b';
        RandomNumberGenerator.Fill(perms.AsSpan(12, 4));

        var cryptFilter = new PdfDictionary();
        cryptFilter.Entries["CFM"] = new PdfName("AESV3");
        cryptFilter.Entries["AuthEvent"] = new PdfName("DocOpen");
        cryptFilter.Entries["Length"] = new PdfNumber(32);

        var filters = new PdfDictionary();
        filters.Entries["StdCF"] = cryptFilter;

        dictionary = new PdfDictionary();
        dictionary.Entries["Filter"] = new PdfName("Standard");
        dictionary.Entries["V"] = new PdfNumber(5);
        dictionary.Entries["R"] = new PdfNumber(6);
        dictionary.Entries["Length"] = new PdfNumber(256);
        dictionary.Entries["CF"] = filters;
        dictionary.Entries["StmF"] = new PdfName("StdCF");
        dictionary.Entries["StrF"] = new PdfName("StdCF");
        dictionary.Entries["O"] = new PdfString(o);
        dictionary.Entries["U"] = new PdfString(u);
        dictionary.Entries["OE"] = new PdfString(oe);
        dictionary.Entries["UE"] = new PdfString(ue);
        dictionary.Entries["P"] = new PdfNumber(raw);
        dictionary.Entries["Perms"] = new PdfString(AesEcbWithoutPadding(fileKey, perms));
        dictionary.Entries["EncryptMetadata"] = new PdfBoolean(true);

        return new PdfStandardSecurity(fileKey, 6, 32, permissions, false, true, true);
    }

    /// <summary>Decrypts the bytes of a string or stream belonging to an object.</summary>
    /// <exception cref="PdfFormatException">The bytes are too short to carry an initialisation vector.</exception>
    public byte[] DecryptObject(int number, int generation, byte[] data) => Unwrap(number, generation, data, decrypt: true);

    /// <summary>Encrypts the bytes of a string or stream belonging to an object.</summary>
    public byte[] EncryptObject(int number, int generation, byte[] data) => Unwrap(number, generation, data, decrypt: false);

    private byte[] Unwrap(int number, int generation, byte[] data, bool decrypt)
    {
        if (data.Length == 0)
        {
            // An empty string stays empty: there is no initialisation vector to carry.
            return data;
        }

        var key = _revision >= 6 ? _fileKey : ObjectKey(number, generation);

        if (!decrypt)
        {
            var iv = RandomNumberGenerator.GetBytes(16);
            var encrypted = AesCbc(key, iv, data, decrypt: false);

            var result = new byte[16 + encrypted.Length];
            iv.CopyTo(result, 0);
            encrypted.CopyTo(result, 16);

            return result;
        }

        if (data.Length < 32 || data.Length % 16 != 0)
        {
            throw new PdfFormatException(
                $"an encrypted value of {data.Length} bytes cannot be an initialisation vector followed by a 16-byte block");
        }

        return AesCbc(key, data.AsSpan(0, 16).ToArray(), data.AsSpan(16).ToArray(), decrypt: true);
    }

    /// <summary>The per-object key of Algorithm 1, which R4 derives and R6 does not have.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5351",
        Justification = "MD5 is what revision 4 of the standard security handler specifies; the format cannot be read with anything else.")]
    private byte[] ObjectKey(int number, int generation)
    {
        var data = new byte[_keyLength + 5 + 4];
        _fileKey.CopyTo(data, 0);

        data[_keyLength] = (byte)number;
        data[_keyLength + 1] = (byte)(number >> 8);
        data[_keyLength + 2] = (byte)(number >> 16);
        data[_keyLength + 3] = (byte)generation;
        data[_keyLength + 4] = (byte)(generation >> 8);

        "sAlT"u8.CopyTo(data.AsSpan(_keyLength + 5));

        var hash = MD5.HashData(data);

        return hash.AsSpan(0, Math.Min(_keyLength + 5, 16)).ToArray();
    }

    private static PdfStandardSecurity ReadAes256(
        PdfFile file,
        PdfDictionary encrypt,
        string password,
        byte[] owner,
        byte[] user,
        PdfPermissions permissions)
    {
        if (user.Length < 48 || owner.Length < 48)
        {
            throw Malformed("an AES-256 document needs 48-byte /U and /O entries, with a validation salt and a key salt after the hash");
        }

        var key = PdfBytes(file, encrypt, "UE");
        var ownerKey = PdfBytes(file, encrypt, "OE");

        if (key.Length < 32 || ownerKey.Length < 32)
        {
            throw Malformed("an AES-256 document needs 32-byte /UE and /OE entries");
        }

        var bytes = Password(password);
        var zero = new byte[16];

        var validation = Hash2B(bytes, user.AsSpan(32, 8), []);
        if (CryptographicOperations.FixedTimeEquals(validation, user.AsSpan(0, 32)))
        {
            return new PdfStandardSecurity(
                AesWithoutPadding(Hash2B(bytes, user.AsSpan(40, 8), []), zero, key.AsSpan(0, 32).ToArray(), decrypt: true),
                6,
                32,
                permissions,
                openedWithOwnerPassword: false,
                encryptsStrings: true,
                encryptsStreams: true);
        }

        var ownerValidation = Hash2B(bytes, owner.AsSpan(32, 8), user.AsSpan(0, 48));
        if (CryptographicOperations.FixedTimeEquals(ownerValidation, owner.AsSpan(0, 32)))
        {
            return new PdfStandardSecurity(
                AesWithoutPadding(Hash2B(bytes, owner.AsSpan(40, 8), user.AsSpan(0, 48)), zero, ownerKey.AsSpan(0, 32).ToArray(), decrypt: true),
                6,
                32,
                permissions,
                openedWithOwnerPassword: true,
                encryptsStrings: true,
                encryptsStreams: true);
        }

        throw WrongPassword();
    }

    private static PdfStandardSecurity ReadAes128(
        PdfFile file,
        PdfDictionary encrypt,
        string password,
        byte[] owner,
        byte[] user,
        int raw,
        PdfPermissions permissions,
        bool encryptMetadata)
    {
        if (owner.Length < 32 || user.Length < 16)
        {
            throw Malformed("an AES-128 document needs a 32-byte /O entry and at least 16 bytes in /U");
        }

        var strings = IsAes(file, encrypt, "StrF");
        var streams = IsAes(file, encrypt, "StmF");
        var identifier = file.Identifier;
        var length = 16;
        var padded = Pad(Password(password));

        var key = FileKey(padded, owner, raw, identifier, encryptMetadata, length);
        if (CryptographicOperations.FixedTimeEquals(ComputeU(key, identifier), user.AsSpan(0, 16)))
        {
            return new PdfStandardSecurity(key, 4, length, permissions, false, strings, streams);
        }

        // The owner path recovers the padded user password from /O, then derives the file key from it.
        var recovered = RecoverUserPassword(padded, owner, length);

        key = FileKey(recovered, owner, raw, identifier, encryptMetadata, length);
        if (CryptographicOperations.FixedTimeEquals(ComputeU(key, identifier), user.AsSpan(0, 16)))
        {
            return new PdfStandardSecurity(key, 4, length, permissions, true, strings, streams);
        }

        throw WrongPassword();
    }

    /// <summary>Whether the named crypt filter is AES, refusing an RC4 one rather than misreading it.</summary>
    private static bool IsAes(PdfFile file, PdfDictionary encrypt, string key)
    {
        var name = file.Resolve(encrypt[key]) as PdfName;
        if (name is null || string.Equals(name.Value, "Identity", StringComparison.Ordinal))
        {
            return false;
        }

        var filters = file.ResolveDictionary(encrypt["CF"]);
        var filter = filters is null ? null : file.ResolveDictionary(filters[name.Value]);
        var method = filter is null ? null : file.Resolve(filter["CFM"]) as PdfName;

        if (method is null)
        {
            throw Malformed($"the document names the crypt filter '{name.Value}' but does not define it");
        }

        if (string.Equals(method.Value, "AESV2", StringComparison.Ordinal) ||
            string.Equals(method.Value, "AESV3", StringComparison.Ordinal))
        {
            return true;
        }

        throw new PdfEncryptionException(
            $"the document encrypts with the crypt filter '{name.Value}' (/CFM /{method.Value}); only AES (/AESV2 and /AESV3) is supported, and RC4 is out of scope");
    }

    /// <summary>Algorithm 2: the file key an R4 document derives from a padded user password.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5351",
        Justification = "MD5 is what revision 4 of the standard security handler specifies; the format cannot be read with anything else.")]
    private static byte[] FileKey(
        byte[] paddedPassword,
        byte[] owner,
        int raw,
        byte[] identifier,
        bool encryptMetadata,
        int length)
    {
        var data = new byte[32 + 32 + 4 + identifier.Length + (encryptMetadata ? 0 : 4)];
        paddedPassword.CopyTo(data, 0);
        owner.AsSpan(0, 32).CopyTo(data.AsSpan(32));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(64), raw);
        identifier.CopyTo(data, 68);

        if (!encryptMetadata)
        {
            data.AsSpan(68 + identifier.Length, 4).Fill(0xFF);
        }

        var hash = MD5.HashData(data);

        for (var round = 0; round < 50; round++)
        {
            hash = MD5.HashData(hash.AsSpan(0, length));
        }

        return hash.AsSpan(0, length).ToArray();
    }

    /// <summary>Algorithm 5: the <c>/U</c> an R4 document derives from its file key.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5351",
        Justification = "MD5 is what revision 4 of the standard security handler specifies; the format cannot be read with anything else.")]
    private static byte[] ComputeU(byte[] key, byte[] identifier)
    {
        var data = new byte[32 + identifier.Length];
        PasswordPadding.CopyTo(data, 0);
        identifier.CopyTo(data, 32);

        var value = Rc4(key, MD5.HashData(data));

        for (var round = 1; round <= 19; round++)
        {
            value = Rc4(Xor(key, (byte)round), value);
        }

        return value;
    }

    /// <summary>Algorithm 3, backwards: the padded user password an R4 <c>/O</c> hides.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5351",
        Justification = "MD5 is what revision 4 of the standard security handler specifies; the format cannot be read with anything else.")]
    private static byte[] RecoverUserPassword(byte[] paddedOwner, byte[] owner, int length)
    {
        var key = MD5.HashData(paddedOwner);

        for (var round = 0; round < 50; round++)
        {
            key = MD5.HashData(key.AsSpan(0, length));
        }

        var value = owner.AsSpan(0, 32).ToArray();

        for (var round = 19; round >= 1; round--)
        {
            value = Rc4(Xor(key.AsSpan(0, length).ToArray(), (byte)round), value);
        }

        return Rc4(key.AsSpan(0, length).ToArray(), value);
    }

    /// <summary>
    /// Algorithm 2.B of ISO 32000-2: the hardened hash R6 uses for the <c>/U</c> hash, the
    /// <c>/O</c> hash and the two key-unwrapping keys.
    /// </summary>
    /// <param name="password">The password, as UTF-8.</param>
    /// <param name="salt">The 8-byte validation or key salt from <c>/U</c> or <c>/O</c>.</param>
    /// <param name="user">The 48 bytes of <c>/U</c> for the owner hash, empty otherwise.</param>
    private static byte[] Hash2B(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> user)
    {
        var initial = new byte[password.Length + salt.Length + user.Length];
        password.CopyTo(initial);
        salt.CopyTo(initial.AsSpan(password.Length));
        user.CopyTo(initial.AsSpan(password.Length + salt.Length));

        var key = SHA256.HashData(initial);
        var block = new byte[password.Length + key.Length + user.Length];
        var repeated = new byte[block.Length * 64];
        byte[] encrypted = [];
        var rounds = 0;

        // The loop runs at least 64 times; after that, it stops on the first round whose last byte
        // is small enough, which is the work factor the specification leaves to the implementation.
        while (rounds < 64 || encrypted[^1] > rounds - 32)
        {
            password.CopyTo(block);
            key.CopyTo(block.AsSpan(password.Length));
            user.CopyTo(block.AsSpan(password.Length + key.Length));

            for (var round = 0; round < 64; round++)
            {
                block.CopyTo(repeated, round * block.Length);
            }

            encrypted = AesWithoutPadding(key.AsSpan(0, 16).ToArray(), key.AsSpan(16, 16).ToArray(), repeated, decrypt: false);

            var selector = 0;
            for (var index = 0; index < 16; index++)
            {
                selector += encrypted[index];
            }

            key = (selector % 3) switch
            {
                0 => SHA256.HashData(encrypted),
                1 => SHA384.HashData(encrypted),
                _ => SHA512.HashData(encrypted),
            };

            block = new byte[password.Length + key.Length + user.Length];
            repeated = new byte[block.Length * 64];
            rounds++;
        }

        return key.AsSpan(0, 32).ToArray();
    }

    /// <summary>AES in CBC mode with PKCS#7 padding, with a key of 16 or 32 bytes.</summary>
    private static byte[] AesCbc(byte[] key, byte[] iv, byte[] data, bool decrypt)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        if (!decrypt)
        {
            using var encryptor = aes.CreateEncryptor();

            return encryptor.TransformFinalBlock(data, 0, data.Length);
        }

        using var decryptor = aes.CreateDecryptor();

        try
        {
            return decryptor.TransformFinalBlock(data, 0, data.Length);
        }
        catch (CryptographicException exception)
        {
            // A wrong key shows up as bad padding; the object is unreadable rather than the file.
            throw new PdfFormatException($"an encrypted value could not be decrypted: {exception.Message}");
        }
    }

    /// <summary>AES in CBC mode without padding, for the fixed-size values R6 works on.</summary>
    private static byte[] AesWithoutPadding(byte[] key, byte[] iv, byte[] data, bool decrypt)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;

        using var transform = decrypt ? aes.CreateDecryptor() : aes.CreateEncryptor();

        return transform.TransformFinalBlock(data, 0, data.Length);
    }

    /// <summary>
    /// AES in ECB mode without padding, for <c>/Perms</c>.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2, Algorithm 10 mandates ECB for this one value: <c>/Perms</c> is a single 16-byte
    /// block, so there is no block repetition for ECB to leak. The dedicated <c>EncryptEcb</c> API
    /// says that at the call site instead of configuring a cipher mode.
    /// </remarks>
    private static byte[] AesEcbWithoutPadding(byte[] key, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = key;

        return aes.EncryptEcb(data, PaddingMode.None);
    }

    /// <summary>RC4, which R4 still uses for the <c>/O</c> and <c>/U</c> entries.</summary>
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
        for (var index = 0; index < key.Length; index++)
        {
            copy[index] = (byte)(key[index] ^ value);
        }

        return copy;
    }

    /// <summary>Pads a password with the standard padding string, to exactly 32 bytes.</summary>
    private static byte[] Pad(byte[] password)
    {
        var padded = new byte[32];
        var count = Math.Min(password.Length, 32);

        password.AsSpan(0, count).CopyTo(padded);

        for (var index = count; index < 32; index++)
        {
            padded[index] = PasswordPadding[index - count];
        }

        return padded;
    }

    /// <summary>The bytes of a password, as UTF-8, cut at the 127 bytes the specification allows.</summary>
    private static byte[] Password(string password)
    {
        var bytes = Encoding.UTF8.GetBytes(password);

        return bytes.Length <= MaxPasswordBytes ? bytes : bytes.AsSpan(0, MaxPasswordBytes).ToArray();
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var total = 0;
        foreach (var part in parts)
        {
            total += part.Length;
        }

        var result = new byte[total];
        var offset = 0;

        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    private static int Integer(PdfFile file, PdfDictionary dictionary, string key, int fallback)
        => file.Resolve(dictionary[key]) is PdfNumber number && number.IsInteger ? (int)number.AsInteger : fallback;

    private static byte[]? Bytes(PdfFile file, PdfDictionary dictionary, string key)
        => file.Resolve(dictionary[key]) is PdfString text ? text.Bytes : null;

    private static byte[] PdfBytes(PdfFile file, PdfDictionary dictionary, string key)
        => Bytes(file, dictionary, key) ?? [];

    private static PdfEncryptionException WrongPassword()
        => new("the password does not open the document; it matches neither the user password nor the owner password");

    private static PdfEncryptionException Malformed(string message)
        => new($"the /Encrypt dictionary cannot be read: {message}");
}

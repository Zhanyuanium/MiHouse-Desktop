using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using MiHouse.Core.Crypto;

namespace MiHouse.Core.Cloud;

/// <summary>
/// Builds Mi Home cloud request signatures and decrypts cloud responses.
/// </summary>
/// <remarks>
/// The algorithm is a port of <c>micloud</c> (<c>micloud/miutils.py</c>):
/// <c>gen_nonce</c> and <c>generate_enc_params</c>. Field order is significant everywhere,
/// which is why forms are carried as ordered key/value lists instead of dictionaries.
/// </remarks>
public sealed class MiHomeRequestSigner
{
    private const int _rc4DiscardBytes = 1024;
    private readonly string? _ssecurity;
    private readonly Func<string> _nonceFactory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<byte[]> _randomSource;

    /// <summary>
    /// Creates a signer without an ssecurity value; use the overloads that take one for
    /// <see cref="SignForm(string, IReadOnlyList{KeyValuePair{string, string}}, out string)"/>.
    /// </summary>
    public MiHomeRequestSigner()
        : this(null, null, null, null)
    {
    }

    /// <summary>
    /// Creates a signer bound to an account's <c>ssecurity</c> value.
    /// </summary>
    public MiHomeRequestSigner(string ssecurity)
        : this(ssecurity, null, null, null)
    {
    }

    internal MiHomeRequestSigner(Func<DateTimeOffset> clock, Func<byte[]> randomSource)
        : this(null, null, clock, randomSource)
    {
    }

    internal MiHomeRequestSigner(
        string? ssecurity,
        Func<string>? nonceFactory,
        Func<DateTimeOffset>? clock,
        Func<byte[]>? randomSource)
    {
        _ssecurity = ssecurity;
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
        _randomSource = randomSource ?? (static () => RandomNumberGenerator.GetBytes(8));
        _nonceFactory = nonceFactory ?? CreateNonce;
    }

    /// <summary>
    /// Generates a request nonce: eight random bytes followed by the minimal big-endian
    /// bytes of <c>epochMillis / 60000</c>, Base64 encoded.
    /// </summary>
    public string CreateNonce()
    {
        Span<byte> buffer = stackalloc byte[16];
        var random = _randomSource();
        if (random.Length < 8)
        {
            throw new InvalidOperationException("The random byte source must return at least 8 bytes.");
        }

        random.AsSpan(0, 8).CopyTo(buffer);

        var minutes = Math.Max(0, _clock().ToUnixTimeMilliseconds() / 60_000);
        var length = WriteMinimalBigEndian(minutes, buffer[8..]);

        return Convert.ToBase64String(buffer[..(8 + length)]);
    }

    /// <summary>
    /// Returns <c>Base64(SHA256(Base64Decode(ssecurity) || Base64Decode(nonce)))</c>.
    /// </summary>
    public string SignedNonce(string ssecurity, string nonce)
    {
        var secret = Convert.FromBase64String(ssecurity);
        var nonceBytes = Convert.FromBase64String(nonce);

        var input = new byte[secret.Length + nonceBytes.Length];
        secret.CopyTo(input, 0);
        nonceBytes.CopyTo(input, secret.Length);

        return Convert.ToBase64String(SHA256.HashData(input));
    }

    /// <summary>
    /// Signs a form with the ssecurity value supplied to the constructor.
    /// </summary>
    /// <exception cref="InvalidOperationException">The signer has no ssecurity value.</exception>
    public IReadOnlyList<KeyValuePair<string, string>> SignForm(
        string absoluteUrl,
        IReadOnlyList<KeyValuePair<string, string>> fields,
        out string nonce)
    {
        var ssecurity = _ssecurity
            ?? throw new InvalidOperationException(
                "This signer was created without an ssecurity value; use the overload that takes one.");

        return SignForm(ssecurity, absoluteUrl, fields, out nonce);
    }

    /// <summary>
    /// Signs a form the way <c>generate_enc_params</c> does: hash the plaintext fields into
    /// <c>rc4_hash__</c>, RC4-encrypt every value including that hash, sign the ciphertext,
    /// then append the plaintext <c>ssecurity</c> and <c>_nonce</c>.
    /// </summary>
    /// <returns>
    /// The fields in wire order: encrypted input fields, <c>rc4_hash__</c>, <c>signature</c>,
    /// <c>ssecurity</c> and <c>_nonce</c>.
    /// </returns>
    public IReadOnlyList<KeyValuePair<string, string>> SignForm(
        string ssecurity,
        string absoluteUrl,
        IReadOnlyList<KeyValuePair<string, string>> fields,
        out string nonce)
    {
        ArgumentNullException.ThrowIfNull(ssecurity);
        ArgumentNullException.ThrowIfNull(absoluteUrl);
        ArgumentNullException.ThrowIfNull(fields);

        nonce = _nonceFactory();
        var signedNonce = SignedNonce(ssecurity, nonce);

        var signed = new List<KeyValuePair<string, string>>(fields.Count + 4);
        signed.AddRange(fields);

        // rc4_hash__ hashes the plaintext fields before encryption, then joins the list itself.
        signed.Add(new KeyValuePair<string, string>(
            "rc4_hash__",
            ComputeSignature(absoluteUrl, fields, signedNonce)));

        var key = Convert.FromBase64String(signedNonce);
        for (var index = 0; index < signed.Count; index++)
        {
            var encrypted = Rc4.Transform(
                key,
                Encoding.UTF8.GetBytes(signed[index].Value),
                _rc4DiscardBytes);

            signed[index] = new KeyValuePair<string, string>(
                signed[index].Key,
                Convert.ToBase64String(encrypted));
        }

        // The final signature covers the ciphertext values, in the same order.
        signed.Add(new KeyValuePair<string, string>(
            "signature",
            ComputeSignature(absoluteUrl, signed, signedNonce)));
        signed.Add(new KeyValuePair<string, string>("ssecurity", ssecurity));
        signed.Add(new KeyValuePair<string, string>("_nonce", nonce));

        return signed;
    }

    /// <summary>
    /// Decrypts a Base64 RC4 response body with the key derived from the request nonce.
    /// </summary>
    public string DecryptResponse(string ssecurity, string requestNonce, string body)
    {
        ArgumentNullException.ThrowIfNull(ssecurity);
        ArgumentNullException.ThrowIfNull(requestNonce);
        ArgumentNullException.ThrowIfNull(body);

        var key = Convert.FromBase64String(SignedNonce(ssecurity, requestNonce));
        var cipher = Convert.FromBase64String(body);
        var plaintext = Rc4.Transform(key, cipher, _rc4DiscardBytes);

        return Encoding.UTF8.GetString(plaintext);
    }

    /// <summary>
    /// Builds the string whose SHA-1 is the request signature:
    /// <c>POST&amp;{path}&amp;{k=v joined by &amp;}&amp;{signedNonce}</c>.
    /// </summary>
    internal static string BuildSignatureString(
        string absoluteUrl,
        IReadOnlyList<KeyValuePair<string, string>> fields,
        string signedNonce)
    {
        var builder = new StringBuilder("POST&");
        builder.Append(PathForSignature(absoluteUrl));

        if (fields.Count > 0)
        {
            builder.Append('&');
            for (var index = 0; index < fields.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append('&');
                }

                builder.Append(fields[index].Key).Append('=').Append(fields[index].Value);
            }
        }

        builder.Append('&').Append(signedNonce);
        return builder.ToString();
    }

    /// <summary>
    /// Mirrors <c>url.split("com")[1].replace("/app/", "/")</c>, for example
    /// <c>https://api.io.mi.com/app/home/device_list</c> becomes <c>/home/device_list</c>.
    /// </summary>
    internal static string PathForSignature(string absoluteUrl)
    {
        var hostEnd = absoluteUrl.IndexOf("com", StringComparison.Ordinal);
        if (hostEnd < 0)
        {
            throw new ArgumentException("The request URL must contain a 'com' host suffix.", nameof(absoluteUrl));
        }

        return absoluteUrl[(hostEnd + 3)..].Replace("/app/", "/", StringComparison.Ordinal);
    }

    private static string ComputeSignature(
        string absoluteUrl,
        IReadOnlyList<KeyValuePair<string, string>> fields,
        string signedNonce)
    {
        var payload = Encoding.UTF8.GetBytes(BuildSignatureString(absoluteUrl, fields, signedNonce));
        return Convert.ToBase64String(SHA1.HashData(payload));
    }

    private static int WriteMinimalBigEndian(long value, Span<byte> destination)
    {
        if (value <= 0)
        {
            return 0;
        }

        var length = 1;
        while (length < 8 && (value >> (length * 8)) != 0)
        {
            length++;
        }

        for (var index = 0; index < length; index++)
        {
            destination[index] = (byte)(value >> ((length - 1 - index) * 8));
        }

        return length;
    }
}

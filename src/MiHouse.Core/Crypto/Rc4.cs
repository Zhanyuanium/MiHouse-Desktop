namespace MiHouse.Core.Crypto;

/// <summary>
/// RC4 stream cipher, as required by the Mi Home cloud request signature.
/// .NET does not ship one, so it is implemented here and covered by known-answer tests.
/// </summary>
public static class Rc4
{
    /// <summary>
    /// Applies RC4 to <paramref name="data"/>.
    /// </summary>
    /// <param name="key">Key material, 1 to 256 bytes.</param>
    /// <param name="data">Input bytes; encryption and decryption are the same operation.</param>
    /// <param name="discardBytes">Number of keystream bytes to drop before use.</param>
    /// <returns>The transformed bytes.</returns>
    public static byte[] Transform(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data, int discardBytes = 0)
    {
        if (key.Length is 0 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(key), "RC4 keys must contain 1 to 256 bytes.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(discardBytes);

        Span<byte> state = stackalloc byte[256];
        for (var index = 0; index < state.Length; index++)
        {
            state[index] = (byte)index;
        }

        var j = 0;
        for (var index = 0; index < state.Length; index++)
        {
            j = (j + state[index] + key[index % key.Length]) & 0xFF;
            (state[index], state[j]) = (state[j], state[index]);
        }

        var i = 0;
        j = 0;

        for (var skipped = 0; skipped < discardBytes; skipped++)
        {
            i = (i + 1) & 0xFF;
            j = (j + state[i]) & 0xFF;
            (state[i], state[j]) = (state[j], state[i]);
        }

        var output = new byte[data.Length];
        for (var index = 0; index < data.Length; index++)
        {
            i = (i + 1) & 0xFF;
            j = (j + state[i]) & 0xFF;
            (state[i], state[j]) = (state[j], state[i]);
            output[index] = (byte)(data[index] ^ state[(state[i] + state[j]) & 0xFF]);
        }

        return output;
    }
}

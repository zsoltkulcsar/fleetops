using System.Security.Cryptography;

namespace FleetOps.Api.Auth;

internal static class Totp
{
    public static bool Verify(string secret, string code, DateTimeOffset now)
    {
        if (code.Length != 6 || !code.All(char.IsDigit)) return false;
        var key = Decode(secret);
        var step = now.ToUnixTimeSeconds() / 30;
        Span<byte> counter = stackalloc byte[8];
        for (var candidate = step - 1; candidate <= step + 1; candidate++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, candidate);
            var hash = HMACSHA1.HashData(key, counter);
            var offset = hash[^1] & 0x0f;
            var value = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
            var expected = (value % 1_000_000).ToString("D6");
            if (CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(expected), System.Text.Encoding.ASCII.GetBytes(code)))
                return true;
        }
        return false;
    }

    private static byte[] Decode(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var buffer = 0;
        var output = new List<byte>();
        foreach (var character in value.TrimEnd('=').ToUpperInvariant())
        {
            var index = alphabet.IndexOf(character);
            if (index < 0) return [];
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)(buffer >> bits));
                buffer &= (1 << bits) - 1;
            }
        }
        return output.ToArray();
    }
}

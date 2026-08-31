using System.Security.Cryptography;

namespace Aptechka.Domain.Identity;

public static class UlidGenerator
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Create(DateTimeOffset timestamp)
    {
        Span<byte> bytes = stackalloc byte[16];
        var milliseconds = timestamp.ToUnixTimeMilliseconds();

        if (milliseconds is < 0 or > 0x0000FFFFFFFFFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp));
        }

        bytes[0] = (byte)(milliseconds >> 40);
        bytes[1] = (byte)(milliseconds >> 32);
        bytes[2] = (byte)(milliseconds >> 24);
        bytes[3] = (byte)(milliseconds >> 16);
        bytes[4] = (byte)(milliseconds >> 8);
        bytes[5] = (byte)milliseconds;
        RandomNumberGenerator.Fill(bytes[6..]);

        Span<char> output = stackalloc char[26];
        for (var characterIndex = 0; characterIndex < output.Length; characterIndex++)
        {
            var value = 0;
            for (var bitIndex = 0; bitIndex < 5; bitIndex++)
            {
                value <<= 1;
                var sourceBit = characterIndex * 5 + bitIndex - 2;
                if (sourceBit < 0)
                {
                    continue;
                }

                var sourceByte = sourceBit / 8;
                var bitInByte = 7 - sourceBit % 8;
                value |= (bytes[sourceByte] >> bitInByte) & 1;
            }

            output[characterIndex] = Alphabet[value];
        }

        return new string(output);
    }

    public static bool IsValid(string? value)
    {
        if (value is not { Length: 26 } || value[0] > '7')
        {
            return false;
        }

        return value.All(static character => Alphabet.Contains(character));
    }
}

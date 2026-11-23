using System.Security.Cryptography;
using System.Text;

namespace LineLanguageLearn.Learning.Infrastructure;

public static class StableHash
{
    public static ulong Of(string value) =>
        BitConverter.ToUInt64(SHA256.HashData(Encoding.UTF8.GetBytes(value)), 0);
}

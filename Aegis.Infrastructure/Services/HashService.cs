using System.Security.Cryptography;
using System.Text;
using Aegis.Core.Interfaces;

namespace Aegis.Infrastructure.Services;

public class HashService : IHashService
{
    public string ComputeHash(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}

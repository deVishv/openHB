using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OpenHisabKitab.Host.Application;

/// <summary>PBKDF2 with SHA-256, stored as pbkdf2-sha256$iterations$salt$hash in base64.</summary>
public static class PasswordHasher
{
    const string Scheme = "pbkdf2-sha256";
    const int Iterations = 600_000;
    const int SaltBytes = 16;
    const int HashBytes = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, Iterations);
        return string.Create(CultureInfo.InvariantCulture,
            $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
    }

    /// <summary>False for a wrong password or a stored value that cannot be read; never throws for bad input.</summary>
    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme) return false;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) || iterations < 1) return false;
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException) { return false; }
        if (expected.Length != HashBytes) return false;
        return CryptographicOperations.FixedTimeEquals(Derive(password, salt, iterations), expected);
    }

    static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, HashBytes);
}

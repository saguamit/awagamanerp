using Awagaman.Api.Models;
using System.Security.Cryptography;
using System.Text;

namespace Awagaman.Api.DataAccess;

public static class AuthSecurity
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100000;

    public static (string Hash, string Salt) HashPassword(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(SaltSize);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(password ?? string.Empty, saltBytes, Iterations, HashAlgorithmName.SHA256, HashSize);
        return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
    }

    public static bool VerifyPassword(string password, string hash, string salt)
    {
        if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(salt)) return false;
        try
        {
            var saltBytes = Convert.FromBase64String(salt);
            var expectedHash = Convert.FromBase64String(hash);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(password ?? string.Empty, saltBytes, Iterations, HashAlgorithmName.SHA256, expectedHash.Length);
            return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
        }
        catch
        {
            return false;
        }
    }

    public static string EncryptPasswordPreview(string password, string secret)
    {
        var plainBytes = Encoding.UTF8.GetBytes(password ?? string.Empty);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(DeriveSecretKey(secret)))
        {
            aes.Encrypt(nonce, plainBytes, cipher, tag);
        }

        var payload = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, payload, nonce.Length + tag.Length, cipher.Length);
        return Convert.ToBase64String(payload);
    }

    public static string DecryptPasswordPreview(string encryptedValue, string secret)
    {
        if (string.IsNullOrWhiteSpace(encryptedValue))
        {
            return string.Empty;
        }

        var payload = Convert.FromBase64String(encryptedValue);
        if (payload.Length < 28)
        {
            return string.Empty;
        }

        var nonce = new byte[12];
        var tag = new byte[16];
        var cipher = new byte[payload.Length - nonce.Length - tag.Length];
        Buffer.BlockCopy(payload, 0, nonce, 0, nonce.Length);
        Buffer.BlockCopy(payload, nonce.Length, tag, 0, tag.Length);
        Buffer.BlockCopy(payload, nonce.Length + tag.Length, cipher, 0, cipher.Length);
        var plain = new byte[cipher.Length];
        using (var aes = new AesGcm(DeriveSecretKey(secret)))
        {
            aes.Decrypt(nonce, cipher, tag, plain);
        }

        return Encoding.UTF8.GetString(plain);
    }

    public static string CreateToken(AuthenticatedUser user, string secret, TimeSpan lifetime)
    {
        var expiresUtc = DateTime.UtcNow.Add(lifetime);
        var payload = string.Join("|",
            user.Id.ToString(),
            Escape(user.Username),
            Escape(user.FullName),
            Escape(user.Role),
            user.IsActive ? "1" : "0",
            expiresUtc.Ticks.ToString());
        var signature = Sign(payload, secret);
        return Base64UrlEncode(Encoding.UTF8.GetBytes(payload)) + "." + Base64UrlEncode(signature);
    }

    public static bool TryValidateToken(string token, string secret, out AuthenticatedUser user)
    {
        user = null;
        if (string.IsNullOrWhiteSpace(token)) return false;
        var parts = token.Split('.');
        if (parts.Length != 2) return false;

        try
        {
            var payloadBytes = Base64UrlDecode(parts[0]);
            var signatureBytes = Base64UrlDecode(parts[1]);
            var payload = Encoding.UTF8.GetString(payloadBytes);
            var expectedSignature = Sign(payload, secret);
            if (!CryptographicOperations.FixedTimeEquals(signatureBytes, expectedSignature)) return false;

            var values = payload.Split('|');
            if (values.Length != 6) return false;
            var expiresUtc = new DateTime(long.Parse(values[5]), DateTimeKind.Utc);
            if (expiresUtc <= DateTime.UtcNow) return false;

            user = new AuthenticatedUser
            {
                Id = int.Parse(values[0]),
                Username = Unescape(values[1]),
                FullName = Unescape(values[2]),
                Role = Unescape(values[3]),
                IsActive = values[4] == "1",
                ExpiresUtc = expiresUtc
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Sign(string payload, string secret)
    {
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? string.Empty)))
        {
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(payload ?? string.Empty));
        }
    }

    private static byte[] DeriveSecretKey(string secret)
    {
        using (var sha = SHA256.Create())
        {
            return sha.ComputeHash(Encoding.UTF8.GetBytes(secret ?? "AwagamanERP-Password-Preview"));
        }
    }

    private static string Escape(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
    private static string Unescape(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value ?? string.Empty));

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}

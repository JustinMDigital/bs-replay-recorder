using System;
using System.Security.Cryptography;
using System.Text;

namespace BSAutoReplayRecorder.Core.Obs;

public static class ObsAuthentication
{
    public static string CreateAuthentication(string password, string salt, string challenge)
    {
        if (password == null)
        {
            throw new ArgumentNullException(nameof(password));
        }

        if (salt == null)
        {
            throw new ArgumentNullException(nameof(salt));
        }

        if (challenge == null)
        {
            throw new ArgumentNullException(nameof(challenge));
        }

        var secret = Sha256Base64(password + salt);
        return Sha256Base64(secret + challenge);
    }

    private static string Sha256Base64(string value)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(sha256.ComputeHash(bytes));
    }
}


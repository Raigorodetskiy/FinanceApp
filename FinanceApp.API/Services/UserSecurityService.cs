using System.Security.Cryptography;
using System.Text;
using FinanceApp.Core.Models;
using Microsoft.AspNetCore.Identity;

namespace FinanceApp.API.Services;

public interface IUserSecurityService
{
    string NormalizeIdentifier(string value);
    bool TryNormalizeEmail(string email, out string normalizedEmail, out string trimmedEmail);
    bool TryValidateAndNormalizeUsername(string username, out string normalizedUsername, out string trimmedUsername, out string error);
    string HashPassword(User user, string password);
    PasswordVerificationResult VerifyAndUpgradePassword(User user, string password, out string? upgradedHash);
}

public sealed class UserSecurityService : IUserSecurityService
{
    private static readonly char[] UsernameAllowedSpecialChars = ['.', '_', '-'];
    private readonly IPasswordHasher<User> _passwordHasher;

    public UserSecurityService(IPasswordHasher<User> passwordHasher)
    {
        _passwordHasher = passwordHasher;
    }

    public string NormalizeIdentifier(string value) => value.Trim().ToUpperInvariant();

    public bool TryNormalizeEmail(string email, out string normalizedEmail, out string trimmedEmail)
    {
        trimmedEmail = email.Trim();
        normalizedEmail = string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedEmail))
        {
            return false;
        }

        try
        {
            var parsed = new System.Net.Mail.MailAddress(trimmedEmail);
            trimmedEmail = parsed.Address;
            normalizedEmail = NormalizeIdentifier(trimmedEmail);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryValidateAndNormalizeUsername(string username, out string normalizedUsername, out string trimmedUsername, out string error)
    {
        trimmedUsername = username.Trim();
        normalizedUsername = string.Empty;

        if (trimmedUsername.Length < 3 || trimmedUsername.Length > 32)
        {
            error = "Логин должен содержать от 3 до 32 символов.";
            return false;
        }

        if (trimmedUsername.Contains('@'))
        {
            error = "Логин не должен содержать символ @.";
            return false;
        }

        foreach (var ch in trimmedUsername)
        {
            if (char.IsLetterOrDigit(ch) || UsernameAllowedSpecialChars.Contains(ch))
            {
                continue;
            }

            error = "Логин может содержать только буквы, цифры и символы . _ -.";
            return false;
        }

        normalizedUsername = NormalizeIdentifier(trimmedUsername);
        error = string.Empty;
        return true;
    }

    public string HashPassword(User user, string password) => _passwordHasher.HashPassword(user, password);

    public PasswordVerificationResult VerifyAndUpgradePassword(User user, string password, out string? upgradedHash)
    {
        upgradedHash = null;
        var storedHash = user.PasswordHash?.Trim();
        if (string.IsNullOrWhiteSpace(storedHash))
        {
            return PasswordVerificationResult.Failed;
        }

        if (IsLikelyLegacySha256(storedHash))
        {
            var legacyVerified = VerifyLegacySha256(storedHash, password);
            if (!legacyVerified)
            {
                return PasswordVerificationResult.Failed;
            }

            upgradedHash = HashPassword(user, password);
            return PasswordVerificationResult.SuccessRehashNeeded;
        }

        PasswordVerificationResult result;
        try
        {
            result = _passwordHasher.VerifyHashedPassword(user, storedHash, password);
        }
        catch (FormatException)
        {
            return PasswordVerificationResult.Failed;
        }
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            upgradedHash = HashPassword(user, password);
        }

        return result;
    }

    private static bool IsLikelyLegacySha256(string hash)
    {
        if (hash.Length != 64)
        {
            return false;
        }

        foreach (var c in hash)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool VerifyLegacySha256(string storedHash, string password)
    {
        var candidateHex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant();
        var storedBytes = Encoding.ASCII.GetBytes(storedHash);
        var candidateBytes = Encoding.ASCII.GetBytes(candidateHex);
        return CryptographicOperations.FixedTimeEquals(storedBytes, candidateBytes);
    }
}

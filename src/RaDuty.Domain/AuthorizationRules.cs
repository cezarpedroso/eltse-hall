namespace RaDuty.Domain;

public static class AuthorizationRules
{
    public static bool IsInApplicationRole(IEnumerable<string> roleClaims, params string[] allowedRoles) =>
        roleClaims.Any(role => allowedRoles.Contains(role, StringComparer.Ordinal));

    public static bool IsAllowedSchoolEmail(string? value, string? allowedDomain)
    {
        var domain = allowedDomain?.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(domain)) return false;
        var email = value?.Trim();
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || email.Any(char.IsWhiteSpace)) return false;
        var separator = email.LastIndexOf('@');
        return separator > 0 && separator < email.Length - 1
            && string.Equals(email[(separator + 1)..], domain, StringComparison.OrdinalIgnoreCase);
    }
}

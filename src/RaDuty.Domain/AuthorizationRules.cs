namespace RaDuty.Domain;

public static class AuthorizationRules
{
    public static bool IsApprovedGroupMember(IEnumerable<string> groupClaims, string? approvedGroupId) =>
        !string.IsNullOrWhiteSpace(approvedGroupId) && groupClaims.Contains(approvedGroupId, StringComparer.Ordinal);

    public static bool IsInApplicationRole(IEnumerable<string> roleClaims, params string[] allowedRoles) =>
        roleClaims.Any(role => allowedRoles.Contains(role, StringComparer.Ordinal));
}

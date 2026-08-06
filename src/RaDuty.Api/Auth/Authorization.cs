using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using RaDuty.Application;
using RaDuty.Domain;

namespace RaDuty.Api.Auth;

public sealed class ApprovedGroupRequirement : IAuthorizationRequirement;

public sealed class ApprovedGroupHandler(IConfiguration configuration) : AuthorizationHandler<ApprovedGroupRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ApprovedGroupRequirement requirement)
    {
        var approvedGroup = configuration["Authorization:ApprovedGroupId"];
        if (AuthorizationRules.IsApprovedGroupMember(context.User.FindAll("groups").Select(x => x.Value), approvedGroup))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public sealed class ClaimsCurrentIdentityAccessor(IHttpContextAccessor contextAccessor) : ICurrentIdentityAccessor
{
    public CurrentIdentity GetRequired()
    {
        var principal = contextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true) throw new AppException(401, "UNAUTHENTICATED", "Sign in is required.");
        var oid = principal.FindFirstValue("oid") ?? principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
            ?? throw new AppException(403, "MISSING_OBJECT_ID", "The identity token does not contain a Microsoft Entra object ID.");
        var email = principal.FindFirstValue("preferred_username") ?? principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        var first = principal.FindFirstValue("given_name") ?? principal.FindFirstValue(ClaimTypes.GivenName) ?? "Residence";
        var last = principal.FindFirstValue("family_name") ?? principal.FindFirstValue(ClaimTypes.Surname) ?? "Life User";
        return new CurrentIdentity(oid, email, first, last, principal.IsInRole("HallDirector"), principal.IsInRole("Admin"),
            principal.FindAll("groups").Select(x => x.Value).ToArray());
    }
}

public sealed class DevelopmentAuthenticationOptions : AuthenticationSchemeOptions;

public sealed class DevelopmentAuthenticationHandler(
    IOptionsMonitor<DevelopmentAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration) : AuthenticationHandler<DevelopmentAuthenticationOptions>(options, logger, encoder)
{
    public const string AuthScheme = "Development";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var requested = Request.Headers["X-Dev-User"].FirstOrDefault();
        var isDirector = string.Equals(requested, "director", StringComparison.OrdinalIgnoreCase);
        var isAdmin = string.Equals(requested, "admin", StringComparison.OrdinalIgnoreCase);
        var groupId = configuration["Authorization:ApprovedGroupId"] ?? "development-approved-group";
        var claims = new List<Claim>
        {
            new("oid", isAdmin ? "dev-admin" : isDirector ? "dev-director" : "dev-ra-001"),
            new("preferred_username", isAdmin ? "admin@university.edu" : isDirector ? "mreyes@university.edu" : "jlee@university.edu"),
            new("given_name", isAdmin ? "Residence" : isDirector ? "Marisol" : "Jordan"),
            new("family_name", isAdmin ? "Administrator" : isDirector ? "Reyes" : "Lee"),
            new("groups", groupId),
            new(ClaimTypes.Role, isAdmin ? "Admin" : isDirector ? "HallDirector" : "ResidentAssistant")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthScheme));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, AuthScheme)));
    }
}

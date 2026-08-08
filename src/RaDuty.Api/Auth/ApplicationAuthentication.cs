using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaDuty.Application;
using RaDuty.Domain;
using RaDuty.Infrastructure;

namespace RaDuty.Api.Auth;

public static class ApplicationPrincipalFactory
{
    public const string SecurityStampClaim = "security_stamp";
    public const string PasswordChangeRequiredClaim = "password_change_required";

    public static ClaimsPrincipal Create(AuthenticatedAccountDto account)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.UserId.ToString()),
            new(ClaimTypes.Name, account.SchoolEmail),
            new(ClaimTypes.Email, account.SchoolEmail),
            new(ClaimTypes.GivenName, account.FirstName),
            new(ClaimTypes.Surname, account.LastName),
            new(ClaimTypes.Role, account.Role.ToString()),
            new(SecurityStampClaim, account.SecurityStamp),
            new(PasswordChangeRequiredClaim, account.MustChangePassword ? "true" : "false")
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}

public sealed class ClaimsCurrentIdentityAccessor(IHttpContextAccessor contextAccessor) : ICurrentIdentityAccessor
{
    public CurrentIdentity GetRequired()
    {
        var principal = contextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            throw new AppException(401, "UNAUTHENTICATED", "Sign in is required.");
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            throw new AppException(401, "INVALID_SESSION", "Your session is invalid. Sign in again.");
        return new CurrentIdentity(userId);
    }
}

public sealed class ApplicationCookieEvents(RaDutyDbContext db, IConfiguration configuration)
    : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var userIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var securityStamp = context.Principal?.FindFirstValue(ApplicationPrincipalFactory.SecurityStampClaim);
        if (!Guid.TryParse(userIdValue, out var userId) || string.IsNullOrWhiteSpace(securityStamp))
        {
            await RejectAsync(context);
            return;
        }

        var account = await db.Users.AsNoTracking().Include(x => x.User).ThenInclude(x => x.HallMemberships)
            .ThenInclude(x => x.ResidenceHall).SingleOrDefaultAsync(x => x.UserId == userId, context.HttpContext.RequestAborted);
        var membership = account?.User.HallMemberships.SingleOrDefault(x => x.IsActive && x.ResidenceHall.IsActive);
        var allowedDomain = configuration["Authentication:AllowedEmailDomain"];
        if (account is null || membership is null || !account.User.IsActive
            || !string.Equals(account.SecurityStamp, securityStamp, StringComparison.Ordinal)
            || !AuthorizationRules.IsAllowedSchoolEmail(account.User.SchoolEmail, allowedDomain))
        {
            await RejectAsync(context);
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}

public sealed class ResidenceLifeAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler fallback = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && context.User.FindFirstValue(
                ApplicationPrincipalFactory.PasswordChangeRequiredClaim) == "true")
        {
            await WriteProblemAsync(context, 403, "PASSWORD_CHANGE_REQUIRED",
                "Change your temporary password before using Eltse Hall.");
            return;
        }
        await fallback.HandleAsync(next, context, policy, authorizeResult);
    }

    public static Task WriteProblemAsync(HttpContext context, int status, string code, string title)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://eltse-hall.example/problems/{code.ToLowerInvariant()}",
            Instance = context.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        return context.Response.WriteAsJsonAsync(problem);
    }
}

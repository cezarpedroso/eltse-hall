using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RaDuty.Api.Auth;
using RaDuty.Application;

namespace RaDuty.Api.Controllers;

[ApiController, Route("api/auth")]
public sealed class AuthController(IAccountService accounts, IAntiforgery antiforgery,
    ICurrentIdentityAccessor identityAccessor) : ControllerBase
{
    [AllowAnonymous, HttpGet("csrf")]
    public ActionResult<object> Csrf()
    {
        Response.Headers.CacheControl = "no-store";
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = tokens.RequestToken });
    }

    [AllowAnonymous, HttpPost("login"), EnableRateLimiting("authentication")]
    public async Task<ActionResult<LoginResult>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var account = await accounts.AuthenticateAsync(request, cancellationToken);
        await IssueSessionAsync(account, request.RememberMe);
        return Ok(new LoginResult(account.MustChangePassword));
    }

    [AllowAnonymous, HttpPost("bootstrap"), EnableRateLimiting("bootstrap")]
    public async Task<ActionResult<LoginResult>> Bootstrap(BootstrapAdminRequest request,
        [FromHeader(Name = "X-Bootstrap-Token")] string? bootstrapToken, CancellationToken cancellationToken)
    {
        var account = await accounts.BootstrapAdminAsync(request, bootstrapToken, cancellationToken);
        await IssueSessionAsync(account, false);
        return Ok(new LoginResult(account.MustChangePassword));
    }

    [Authorize, HttpPost("change-password"), EnableRateLimiting("authentication")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var identity = identityAccessor.GetRequired();
        var existingSession = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var account = await accounts.ChangePasswordAsync(identity.UserId, request, cancellationToken);
        await IssueSessionAsync(account, existingSession.Properties?.IsPersistent == true);
        return NoContent();
    }

    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    private Task IssueSessionAsync(AuthenticatedAccountDto account, bool persistent) => HttpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        ApplicationPrincipalFactory.Create(account),
        new AuthenticationProperties
        {
            IsPersistent = persistent,
            AllowRefresh = false,
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.Add(persistent ? TimeSpan.FromDays(30) : TimeSpan.FromHours(8))
        });
}

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using RaDuty.Api.Auth;
using RaDuty.Application;
using RaDuty.Domain;

namespace RaDuty.Tests;

public sealed class LocalAuthenticationTests
{
    [Theory]
    [InlineData("ra@wmpenn.edu", true)]
    [InlineData("DIRECTOR@WMPENN.EDU", true)]
    [InlineData(" ra@wmpenn.edu ", true)]
    [InlineData("ra@other.edu", false)]
    [InlineData("ra@wmpenn.edu.example.com", false)]
    [InlineData("wmpenn.edu", false)]
    [InlineData("ra @wmpenn.edu", false)]
    [InlineData(null, false)]
    public void School_email_domain_match_is_exact_and_case_insensitive(string? email, bool expected)
    {
        Assert.Equal(expected, AuthorizationRules.IsAllowedSchoolEmail(email, "@wmpenn.edu"));
    }

    [Fact]
    public void Session_principal_contains_only_server_owned_identity_and_role_claims()
    {
        var userId = Guid.NewGuid();
        var principal = ApplicationPrincipalFactory.Create(new AuthenticatedAccountDto(userId,
            "ra@wmpenn.edu", "Jordan", "Lee", HallRole.ResidentAssistant, "stamp", false));

        Assert.Equal(userId.ToString(), principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("ra@wmpenn.edu", principal.FindFirstValue(ClaimTypes.Email));
        Assert.True(principal.IsInRole("ResidentAssistant"));
        Assert.Equal("false", principal.FindFirstValue(ApplicationPrincipalFactory.PasswordChangeRequiredClaim));
    }

    [Fact]
    public async Task Temporary_password_session_returns_a_clear_problem_code()
    {
        var principal = ApplicationPrincipalFactory.Create(new AuthenticatedAccountDto(Guid.NewGuid(),
            "ra@wmpenn.edu", "Jordan", "Lee", HallRole.ResidentAssistant, "stamp", true));
        var http = new DefaultHttpContext { User = principal };
        http.Response.Body = new MemoryStream();
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
            .RequireClaim(ApplicationPrincipalFactory.PasswordChangeRequiredClaim, "false").Build();
        var handler = new ResidenceLifeAuthorizationResultHandler();

        await handler.HandleAsync(_ => Task.CompletedTask, http, policy, PolicyAuthorizationResult.Forbid());

        http.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(http.Response.Body);
        Assert.Equal(StatusCodes.Status403Forbidden, http.Response.StatusCode);
        Assert.Equal("PASSWORD_CHANGE_REQUIRED", body.RootElement.GetProperty("code").GetString());
    }
}

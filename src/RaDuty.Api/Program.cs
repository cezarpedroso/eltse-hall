using System.Threading.RateLimiting;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RaDuty.Api.Auth;
using RaDuty.Api.Middleware;
using RaDuty.Application;
using RaDuty.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var isDevelopment = builder.Environment.IsDevelopment();

builder.Services.AddScoped<ApplicationCookieEvents>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = isDevelopment ? ".EltseHall.Session" : "__Host-EltseHall.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.Path = "/";
        options.Cookie.SameSite = isDevelopment ? SameSiteMode.None : SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = false;
        options.EventsType = typeof(ApplicationCookieEvents);
        options.Events.OnRedirectToLogin = context => ResidenceLifeAuthorizationResultHandler.WriteProblemAsync(
            context.HttpContext, 401, "UNAUTHENTICATED", "Sign in is required.");
        options.Events.OnRedirectToAccessDenied = context => ResidenceLifeAuthorizationResultHandler.WriteProblemAsync(
            context.HttpContext, 403, "FORBIDDEN", "You do not have permission to perform this action.");
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AuthorizedResidenceLifeUser", policy => RequireResidenceLifeAccess(policy));
    options.AddPolicy("ResidentAssistantOrDirector", policy =>
    {
        RequireResidenceLifeAccess(policy);
        policy.RequireRole("ResidentAssistant", "RA", "HallDirector", "Admin");
    });
    options.AddPolicy("HallDirectorOnly", policy =>
    {
        RequireResidenceLifeAccess(policy);
        policy.RequireRole("HallDirector", "Admin");
    });
    options.FallbackPolicy = options.GetPolicy("AuthorizedResidenceLifeUser");
});
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ResidenceLifeAuthorizationResultHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentIdentityAccessor, ClaimsCurrentIdentityAccessor>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = isDevelopment ? ".EltseHall.Antiforgery" : "__Host-EltseHall.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Path = "/";
    options.Cookie.SameSite = isDevelopment ? SameSiteMode.None : SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});
builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy => policy
    .WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? ["https://localhost:5173"])
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("authentication", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("bootstrap", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddFixedWindowLimiter("assignments", limiter =>
    {
        limiter.PermitLimit = 20;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("roster-imports", limiter =>
    {
        limiter.PermitLimit = 12;
        limiter.Window = TimeSpan.FromHours(1);
        limiter.QueueLimit = 0;
    });
});

var app = builder.Build();
app.UseMiddleware<ApiExceptionMiddleware>();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers.ContentSecurityPolicy = context.Request.Path.StartsWithSegments("/api")
        || context.Request.Path.StartsWithSegments("/health")
        || context.Request.Path.StartsWithSegments("/openapi")
        ? "default-src 'none'; frame-ancestors 'none'"
        : "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapOpenApi("/openapi/{documentName}.json").RequireAuthorization("HallDirectorOnly");
app.MapControllers();
app.MapFallbackToFile("index.html").AllowAnonymous();

if (app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("SeedData:Enabled"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<RaDutyDbContext>();
    await db.Database.MigrateAsync();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationAccount>>();
    var initialPassword = builder.Configuration["DevelopmentAccounts:InitialPassword"]
        ?? throw new InvalidOperationException("DevelopmentAccounts:InitialPassword is required when seed data is enabled.");
    await DevelopmentSeed.InitializeAsync(db, passwordHasher, initialPassword);
}

app.Run();

static void RequireResidenceLifeAccess(AuthorizationPolicyBuilder policy) =>
    policy.RequireAuthenticatedUser()
        .RequireClaim(ApplicationPrincipalFactory.PasswordChangeRequiredClaim, "false");

public partial class Program;

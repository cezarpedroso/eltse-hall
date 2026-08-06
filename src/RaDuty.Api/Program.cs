using System.Threading.RateLimiting;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using RaDuty.Api.Auth;
using RaDuty.Api.Middleware;
using RaDuty.Application;
using RaDuty.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var useDevelopmentAuth = builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("DevelopmentAuth:Enabled");

if (useDevelopmentAuth)
{
    builder.Services.AddAuthentication(DevelopmentAuthenticationHandler.AuthScheme)
        .AddScheme<DevelopmentAuthenticationOptions, DevelopmentAuthenticationHandler>(DevelopmentAuthenticationHandler.AuthScheme, _ => { });
}
else
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AuthorizedResidenceLifeUser", policy => policy.RequireAuthenticatedUser().AddRequirements(new ApprovedGroupRequirement()));
    options.AddPolicy("ResidentAssistantOrDirector", policy => policy.RequireAuthenticatedUser().AddRequirements(new ApprovedGroupRequirement())
        .RequireRole("ResidentAssistant", "RA", "HallDirector", "Admin"));
    options.AddPolicy("HallDirectorOnly", policy => policy.RequireAuthenticatedUser().AddRequirements(new ApprovedGroupRequirement()).RequireRole("HallDirector", "Admin"));
    options.FallbackPolicy = options.GetPolicy("AuthorizedResidenceLifeUser");
});
builder.Services.AddSingleton<IAuthorizationHandler, ApprovedGroupHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentIdentityAccessor, ClaimsCurrentIdentityAccessor>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy => policy
    .WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? ["https://localhost:5173"])
    .AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(options =>
{
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
    context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapOpenApi("/openapi/{documentName}.json").RequireAuthorization("HallDirectorOnly");
app.MapControllers();

if (app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("SeedData:Enabled"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<RaDutyDbContext>();
    await db.Database.MigrateAsync();
    await DevelopmentSeed.InitializeAsync(db);
}

app.Run();

public partial class Program;

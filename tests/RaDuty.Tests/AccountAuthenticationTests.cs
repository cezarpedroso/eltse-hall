using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaDuty.Application;
using RaDuty.Domain;
using RaDuty.Infrastructure;

namespace RaDuty.Tests;

public sealed class AccountAuthenticationTests
{
    [Fact]
    public async Task A_provisioned_wmpenn_account_can_authenticate_and_password_is_hashed()
    {
        await using var provider = Services();
        await SeedAccountAsync(provider, "ra@wmpenn.edu", "A long private passphrase 2026");
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountService>();
        var db = scope.ServiceProvider.GetRequiredService<RaDutyDbContext>();

        var authenticated = await service.AuthenticateAsync(
            new LoginRequest(" RA@WMPENN.EDU ", "A long private passphrase 2026"), CancellationToken.None);

        Assert.Equal("ra@wmpenn.edu", authenticated.SchoolEmail);
        var stored = await db.Users.SingleAsync();
        Assert.NotEqual("A long private passphrase 2026", stored.PasswordHash);
        Assert.True(stored.PasswordHash!.Length > 40);
    }

    [Fact]
    public async Task External_and_unknown_emails_receive_the_same_generic_failure()
    {
        await using var provider = Services();
        await SeedAccountAsync(provider, "ra@wmpenn.edu", "A long private passphrase 2026");
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountService>();

        var external = await Assert.ThrowsAsync<AppException>(() => service.AuthenticateAsync(
            new LoginRequest("person@gmail.com", "A long private passphrase 2026"), CancellationToken.None));
        var unknown = await Assert.ThrowsAsync<AppException>(() => service.AuthenticateAsync(
            new LoginRequest("unknown@wmpenn.edu", "A long private passphrase 2026"), CancellationToken.None));

        Assert.Equal((external.StatusCode, external.Code, external.Message),
            (unknown.StatusCode, unknown.Code, unknown.Message));
        Assert.Equal("INVALID_CREDENTIALS", external.Code);
    }

    [Fact]
    public async Task Five_failed_passwords_lock_the_account_for_fifteen_minutes()
    {
        await using var provider = Services();
        await SeedAccountAsync(provider, "ra@wmpenn.edu", "A long private passphrase 2026");
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountService>();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failure = await Assert.ThrowsAsync<AppException>(() => service.AuthenticateAsync(
                new LoginRequest("ra@wmpenn.edu", "This is the wrong password"), CancellationToken.None));
            Assert.Equal("INVALID_CREDENTIALS", failure.Code);
        }

        var locked = await Assert.ThrowsAsync<AppException>(() => service.AuthenticateAsync(
            new LoginRequest("ra@wmpenn.edu", "A long private passphrase 2026"), CancellationToken.None));
        Assert.Equal(429, locked.StatusCode);
        Assert.Equal("SIGN_IN_TEMPORARILY_LOCKED", locked.Code);
    }

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<RaDutyDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddIdentityCore<ApplicationAccount>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 15;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<RaDutyDbContext>()
            .AddDefaultTokenProviders();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Authentication:AllowedEmailDomain"] = "wmpenn.edu" }).Build());
        services.AddScoped<ICurrentUserService, UnusedCurrentUserService>();
        services.AddScoped<IAccountService, AccountService>();
        return services.BuildServiceProvider();
    }

    private static async Task SeedAccountAsync(ServiceProvider provider, string email, string password)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RaDutyDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationAccount>>();
        var hall = new ResidenceHall { Name = "Eltse Hall" };
        var user = new User
        {
            SchoolEmail = email,
            FirstName = "Jordan",
            LastName = "Lee",
            Role = HallRole.ResidentAssistant
        };
        user.HallMemberships.Add(new HallMembership
        {
            ResidenceHall = hall,
            User = user,
            HallRole = HallRole.ResidentAssistant
        });
        db.StaffUsers.Add(user);
        await db.SaveChangesAsync();
        var account = new ApplicationAccount
        {
            Id = user.Id,
            UserId = user.Id,
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            LockoutEnabled = true,
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
        var result = await manager.CreateAsync(account, password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(x => x.Description)));
    }

    private sealed class UnusedCurrentUserService : ICurrentUserService
    {
        public Task<CurrentUserDto> GetAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

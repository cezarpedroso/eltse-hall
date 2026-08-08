using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaDuty.Application;

namespace RaDuty.Infrastructure;

public static class ServiceRegistration
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<RaDutyDbContext>(options => options.UseSqlServer(
            configuration.GetConnectionString("RaDuty") ?? throw new InvalidOperationException("ConnectionStrings:RaDuty is required."),
            sql => sql.EnableRetryOnFailure()));
        services.AddIdentityCore<ApplicationAccount>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 15;
                options.Password.RequiredUniqueChars = 1;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<RaDutyDbContext>()
            .AddDefaultTokenProviders();
        services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210_000);
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IScheduleService, ScheduleService>();
        services.AddScoped<IDormCheckService, DormCheckService>();
        services.AddScoped<IDormCheckPhotoService, DormCheckPhotoService>();
        services.AddScoped<IDormRosterImportService, DormRosterImportService>();
        services.AddScoped<IDormResidentManagementService, DormResidentManagementService>();
        services.AddSingleton<ISchedulePdfService, SchedulePdfService>();
        services.AddSingleton<IDormCheckPdfService, DormCheckPdfService>();
        return services;
    }
}

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
        services.AddScoped<ICurrentUserService, CurrentUserService>();
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
